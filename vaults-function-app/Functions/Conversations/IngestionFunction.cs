using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using VaultsFunctions.Core.Models;
using Microsoft.Azure.Cosmos;
using Azure.Storage.Blobs;
using System.Text;
using Newtonsoft.Json;
using System.Linq;
using System.Net.Http; // Needed for HttpClient
using System.Text.RegularExpressions;
using System.Web; // Needed for HttpUtility.ParseQueryString for URL manipulation
using System.Net; // Add this for HttpStatusCode

namespace VaultsFunctions.Functions.Conversations
{
    public class IngestionFunction
    {
        private readonly IConfiguration _configuration;
        private readonly Microsoft.Graph.GraphServiceClient _graphClient;
        private readonly CosmosClient _cosmosClient;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly Container _interactionsContainer;
        private readonly Container _tenantsContainer;

        // Recommended: Use a static HttpClient to avoid socket exhaustion
        private static readonly HttpClient _httpClient = new HttpClient();

        public IngestionFunction(IConfiguration configuration)
        {
            _configuration = configuration;

            // Initialize Graph Client with Managed Identity for Copilot data access
            var credential = new DefaultAzureCredential();
            string[] scopes = { "https://graph.microsoft.com/.default" };
            
            // IMPORTANT: For /beta endpoints, you should configure the GraphServiceClient
            // to use the beta base URL if you intend to use its strongly-typed methods
            // that might be available there. However, for getAllEnterpriseInteractions,
            // direct HttpClient calls are often more straightforward if the SDK doesn't
            // have a direct method for it.
            // For now, we'll keep the default GraphServiceClient as it's used elsewhere
            // (e.g., FetchAllUsers) which defaults to v1.0, and use raw HttpClient for
            // the problematic Copilot interaction endpoint.
            _graphClient = new Microsoft.Graph.GraphServiceClient(credential, scopes);

            // Initialize Cosmos DB Client
            var cosmosDbConnectionString = _configuration["COSMOS_DB_CONNECTION_STRING"];
            _cosmosClient = new CosmosClient(cosmosDbConnectionString);
            
            var database = _cosmosClient.GetDatabase("Vaults");
            _interactionsContainer = database.GetContainer("Interactions");
            _tenantsContainer = database.GetContainer("Tenants");

            // Initialize Blob Storage Client
            var storageAccountConnectionString = _configuration["CUSTOMER_BLOB_STORAGE_CONNECTION_STRING"] 
                ?? _configuration["AzureWebJobsStorage"];
            
            if (string.IsNullOrEmpty(_configuration["CUSTOMER_BLOB_STORAGE_CONNECTION_STRING"]))
            {
                var storageAccountName = _configuration["AzureWebJobsStorageAccountName"];
                if (!string.IsNullOrEmpty(storageAccountName))
                {
                    _blobServiceClient = new BlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"), credential);
                }
                else
                {
                    // Fallback to AzureWebJobsStorage if CUSTOMER_BLOB_STORAGE_CONNECTION_STRING is not set
                    // and AzureWebJobsStorageAccountName is also not set. This implies
                    // AzureWebJobsStorage should be a full connection string.
                    _blobServiceClient = new BlobServiceClient(_configuration["AzureWebJobsStorage"]);
                }
            }
            else
            {
                _blobServiceClient = new BlobServiceClient(storageAccountConnectionString);
            }
        }

        [Function("IngestionFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<IngestionFunction>();
            log.LogInformation("Copilot Vaults ingestion function started.");

            var response = req.CreateResponse();
            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = queryParams["tenantId"] ?? "default-tenant";
                
                var tenantStatus = await GetTenantStatus(tenantId);
                var lastSyncTime = tenantStatus?.LastSyncTime ?? DateTimeOffset.UtcNow.AddDays(-1);
                
                log.LogInformation($"Processing interactions for tenant {tenantId} since {lastSyncTime}");

                var processedInteractions = new List<CopilotInteraction>();
                long currentInteractionsProcessed = tenantStatus?.TotalInteractionsProcessed ?? 0;
                string lastFailureMessage = null;

                // Ensure the Graph client's base URL is compatible for the FetchAllUsers call
                // If GraphServiceClient was initialized with a specific beta URL, this won't be needed for users.
                // Assuming FetchAllUsers works with v1.0, no change needed here.
                var userIds = await FetchAllUsers(log);
                log.LogInformation($"Found {userIds.Count} users to process for tenant {tenantId}.");

                foreach (var userId in userIds)
                {
                    log.LogInformation($"Fetching Copilot interactions for user {userId}...");
                    try
                    {
                        // THIS IS THE MODIFIED CALL
                        var interactions = await FetchCopilotInteractions(userId, lastSyncTime, log);
                        
                        if (interactions != null && interactions.Any())
                        {
                            // Group interactions by sessionId to form conversations
                            var conversations = interactions
                                .GroupBy(i => i.SessionId)
                                .Select(group => new
                                {
                                    SessionId = group.Key,
                                    UserPrompt = group.FirstOrDefault(i => i.InteractionType == AiInteractionType.UserPrompt),
                                    AiResponse = group.FirstOrDefault(i => i.InteractionType == AiInteractionType.AiResponse)
                                })
                                .Where(c => c.UserPrompt != null && c.AiResponse != null)
                                .ToList();

                            var userInteractions = new List<CopilotInteraction>();
                            foreach (var conversation in conversations)
                            {
                                var copilotInteraction = new CopilotInteraction
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    TenantId = tenantId,
                                    UserId = userId,
                                    SessionId = conversation.SessionId,
                                    UserPrompt = conversation.UserPrompt?.Body?.Content ?? string.Empty,
                                    AiResponse = conversation.AiResponse?.Body?.Content ?? string.Empty,
                                    CreatedDateTime = conversation.UserPrompt?.CreatedDateTime ?? DateTimeOffset.UtcNow,
                                    IsProcessed = true,
                                    ProcessedDateTime = DateTimeOffset.UtcNow,
                                    IsExported = false
                                };

                                // PII Detection
                                copilotInteraction.HasPii = DetectPii(copilotInteraction.UserPrompt) || DetectPii(copilotInteraction.AiResponse);

                                userInteractions.Add(copilotInteraction);
                            }

                            processedInteractions.AddRange(userInteractions);

                            if (userInteractions.Any())
                            {
                                await PersistInteractions(userInteractions, tenantId, log);
                                currentInteractionsProcessed += userInteractions.Count;
                                log.LogInformation($"Successfully processed {userInteractions.Count} interactions for user {userId}");
                            }
                        }
                        else
                        {
                            log.LogInformation($"No interactions to process for user {userId}");
                        }
                    }
                    catch (Exception userGraphEx)
                    {
                        log.LogError(userGraphEx, $"Unable to fetch from Graph API for user {userId}. Continuing with next user.");
                        lastFailureMessage = $"Failed to fetch interactions for user {userId}: {userGraphEx.Message}";
                    }
                }

                await UpdateTenantStatus(tenantId, DateTimeOffset.UtcNow, currentInteractionsProcessed, lastFailureMessage);
                log.LogInformation($"Finished processing all users for tenant {tenantId}. Total interactions processed: {currentInteractionsProcessed}");

                await response.WriteAsJsonAsync(new
                {
                    Success = true,
                    TenantId = tenantId,
                    UsersProcessed = userIds.Count,
                    InteractionsProcessed = currentInteractionsProcessed,
                    LastSyncTime = DateTimeOffset.UtcNow,
                    ProcessedAt = DateTimeOffset.UtcNow,
                    LastFailureMessage = lastFailureMessage
                });
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error in Copilot Vaults ingestion function");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Error: {ex.Message}");
                return response;
            }
        }

        private bool DetectPii(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            string piiPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b|" + // Email
                                @"\b(?:\d{3}[-.\s]?\d{3}[-.\s]?\d{4}|\(\d{3}\)\s*\d{3}[-.\s]?\d{4})\b|" + // Phone
                                @"\b\d{3}-\d{2}-\d{4}\b|" + // SSN
                                @"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b"; // IP Address

            return Regex.IsMatch(content, piiPattern, RegexOptions.IgnoreCase);
        }

        private async Task<List<string>> FetchAllUsers(ILogger log)
        {
            var allUserIds = new List<string>();
            string nextLink = null;
            int retryCount = 0;
            const int maxRetries = 5;
            const int pageSize = 999;

            do
            {
                try
                {
                    log.LogInformation($"Attempting to fetch users from Graph API. Page: {nextLink ?? "initial"}");

                    // This uses the strongly-typed GraphServiceClient which defaults to v1.0
                    // and typically works well for /users endpoint.
                    var response = await _graphClient.Users.GetAsync((requestConfiguration) =>
                    {
                        requestConfiguration.QueryParameters.Top = pageSize;
                        requestConfiguration.QueryParameters.Select = new string[] { "id" };
                    });

                    if (response?.Value != null)
                    {
                        allUserIds.AddRange(response.Value.Select(u => u.Id).Where(id => !string.IsNullOrEmpty(id)));
                        nextLink = response.OdataNextLink;
                        log.LogInformation($"Fetched {response.Value.Count} users. NextLink: {nextLink}");
                    }
                    else
                    {
                        nextLink = null;
                    }

                    retryCount = 0;
                }
                catch (ServiceException serviceEx) when ((int)serviceEx.ResponseStatusCode == (int)System.Net.HttpStatusCode.TooManyRequests ||
                                                          (int)serviceEx.ResponseStatusCode == (int)System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    retryCount++;
                    if (retryCount <= maxRetries)
                    {
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                        log.LogWarning($"Graph API rate limit hit during user fetch. Retrying in {delay.TotalSeconds} seconds. Retry count: {retryCount}");
                        await Task.Delay(delay);
                    }
                    else
                    {
                        log.LogError(serviceEx, $"Max retries reached for Graph API user fetch.");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to fetch users from Graph API.");
                    throw;
                }
            } while (!string.IsNullOrEmpty(nextLink));

            return allUserIds;
        }

        private async Task<List<AiInteraction>> FetchCopilotInteractions(string userId, DateTimeOffset since, ILogger log)
        {
            var allInteractions = new List<AiInteraction>();
            string nextLink = null;
            int retryCount = 0;
            const int maxRetries = 5;
            const int pageSize = 100; // Recommended $top value per documentation for this API

            // Obtain a token using the same credential as GraphServiceClient
            // This ensures consistent authentication and allows the credential to manage token caching.
            var tokenRequestContext = new Azure.Core.TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
            // The GraphServiceClient already initialized with DefaultAzureCredential will have this setup
            // You can also get it from the GraphServiceClient's RequestAdapter if you want to be sure it's the same
            // For simplicity, we directly get it from a new DefaultAzureCredential instance here,
            // assuming it handles its own caching. Or, ideally, the GraphServiceClient's token provider
            // could be exposed or passed.
            var accessToken = await new DefaultAzureCredential().GetTokenAsync(tokenRequestContext);


            do
            {
                try
                {
                    string requestUri;
                    if (!string.IsNullOrEmpty(nextLink))
                    {
                        // Use nextLink directly for subsequent pages
                        requestUri = nextLink; 
                    }
                    else
                    {
                        // Construct the initial URL for the beta endpoint
                        var baseUrl = "https://graph.microsoft.com/beta";
                        var path = $"/copilot/users/{userId}/interactionHistory/getAllEnterpriseInteractions";
                        // Ensure filter value is URL-encoded
                        var sinceIso = Uri.EscapeDataString(since.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                        requestUri = $"{baseUrl}{path}?$top={pageSize}&$filter=createdDateTime gt {sinceIso}";
                    }

                    log.LogInformation($"Attempting to fetch Copilot interactions from Graph API for user {userId} via URL: {requestUri}");

                    // Create the HTTP request
                    using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);

                    // Add authentication header using the token
                    httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken.Token);
                    
                    // Add ConsistencyLevel header - often required for newer Graph endpoints or eventually consistent data
                    httpRequest.Headers.Add("ConsistencyLevel", "eventual");

                    // Execute the request using the static HttpClient
                    var httpResponse = await _httpClient.SendAsync(httpRequest);

                    // Throw an exception for non-success status codes (4xx or 5xx)
                    httpResponse.EnsureSuccessStatusCode(); 

                    // Read the content and deserialize
                    var json = await httpResponse.Content.ReadAsStringAsync();
                    var response = JsonConvert.DeserializeObject<CopilotInteractionResponse>(json);

                    if (response?.Value != null)
                    {
                        // Filter interactions to only include those after the last sync time
                        // This double-checks the API filter and ensures no older data creeps in.
                        var filteredInteractions = response.Value.Where(i => i.CreatedDateTime >= since).ToList();
                        allInteractions.AddRange(filteredInteractions);
                        
                        nextLink = response.OdataNextLink;
                        log.LogInformation($"Fetched {filteredInteractions.Count} interactions for user {userId}. NextLink: {nextLink}");
                    }
                    else
                    {
                        nextLink = null;
                    }

                    retryCount = 0; // Reset retry count on success
                }
                catch (HttpRequestException httpEx)
                {
                    // Catch specific HTTP exceptions to handle rate limiting (429) or service unavailable (503)
                    if (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                        httpEx.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                    {
                        retryCount++;
                        if (retryCount <= maxRetries)
                        {
                            // Implement exponential backoff
                            var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                            log.LogWarning($"Graph API rate limit hit or service unavailable for user {userId}. Retrying in {delay.TotalSeconds} seconds. Retry count: {retryCount}. Error: {httpEx.Message}");
                            await Task.Delay(delay);
                        }
                        else
                        {
                            log.LogError(httpEx, $"Max retries reached for Graph API call for user {userId}.");
                            throw; // Re-throw if max retries reached
                        }
                    }
                    else
                    {
                        // For other HTTP errors, log and re-throw
                        log.LogError(httpEx, $"Failed to fetch Copilot interactions from Graph API for user {userId}. HTTP Status: {httpEx.StatusCode}.");
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    // Catch any other unexpected exceptions
                    log.LogError(ex, $"An unexpected error occurred while fetching Copilot interactions from Graph API for user {userId}.");
                    throw;
                }
            } while (!string.IsNullOrEmpty(nextLink));

            return allInteractions;
        }

        private async Task PersistInteractions(List<CopilotInteraction> interactions, string tenantId, ILogger log)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient($"{tenantId.ToLower()}-copilot-exports");
            await containerClient.CreateIfNotExistsAsync();

            foreach (var interaction in interactions)
            {
                try
                {
                    // Persist to Cosmos DB
                    await _interactionsContainer.UpsertItemAsync(
                        interaction,
                        new PartitionKey(interaction.TenantId)
                    );

                    // Export to Blob Storage
                    var date = interaction.CreatedDateTime.ToString("yyyy-MM-dd");
                    var blobPath = $"{tenantId}/exports/{date}/conversation-{interaction.Id}.json";
                    var blobClient = containerClient.GetBlobClient(blobPath);

                    var json = JsonConvert.SerializeObject(interaction, Formatting.Indented);
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
                    
                    await blobClient.UploadAsync(stream, overwrite: true);
                    
                    // Update Cosmos DB record to mark as exported
                    interaction.IsExported = true;
                    interaction.ExportedDateTime = DateTimeOffset.UtcNow;
                    
                    await _interactionsContainer.UpsertItemAsync(
                        interaction,
                        new PartitionKey(interaction.TenantId)
                    );

                    log.LogInformation($"Persisted interaction {interaction.Id} for tenant {tenantId}");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"Failed to persist interaction {interaction.Id}");
                    // Decide if you want to re-throw or handle per interaction.
                    // Re-throwing here would stop the whole batch. Perhaps log and continue for other interactions.
                    // For now, it re-throws as per original logic.
                    throw;
                }
            }
        }

        private async Task<TenantStatus> GetTenantStatus(string tenantId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _tenantsContainer.GetItemQueryIterator<TenantStatus>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    return response.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not retrieve tenant status: {ex.Message}");
                // In a real function, you might want to log this to ILogger instead of Console
            }

            return null;
        }

        private async Task UpdateTenantStatus(string tenantId, DateTimeOffset lastSyncTime, long totalInteractionsProcessed, string lastFailureMessage)
        {
            try
            {
                var tenantStatus = await GetTenantStatus(tenantId) ?? new TenantStatus { Id = tenantId };

                tenantStatus.LastSyncTime = lastSyncTime;
                tenantStatus.TotalInteractionsProcessed = totalInteractionsProcessed;
                tenantStatus.LastFailureMessage = lastFailureMessage;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not update tenant status: {ex.Message}");
                // In a real function, you might want to log this to ILogger instead of Console
            }
        }
    }

    // Response model for Copilot interactions API
    public class CopilotInteractionResponse
    {
        [JsonProperty("@odata.context")]
        public string OdataContext { get; set; }
        
        [JsonProperty("@odata.count")]
        public int? OdataCount { get; set; }
        
        [JsonProperty("@odata.nextLink")]
        public string OdataNextLink { get; set; }
        
        [JsonProperty("value")]
        public List<AiInteraction> Value { get; set; }
    }

    // Models for AI Interaction based on the Graph API response
    public class AiInteraction
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }
        
        [JsonProperty("requestId")]
        public string RequestId { get; set; }
        
        [JsonProperty("appClass")]
        public string AppClass { get; set; }
        
        [JsonProperty("interactionType")]
        public AiInteractionType InteractionType { get; set; }
        
        [JsonProperty("conversationType")]
        public string ConversationType { get; set; }
        
        [JsonProperty("etag")]
        public string Etag { get; set; }
        
        [JsonProperty("createdDateTime")]
        public DateTimeOffset CreatedDateTime { get; set; }
        
        [JsonProperty("locale")]
        public string Locale { get; set; }
        
        [JsonProperty("contexts")]
        public List<AiContext> Contexts { get; set; }
        
        [JsonProperty("from")]
        public ChatMessageFromIdentitySet From { get; set; }
        
        [JsonProperty("body")]
        public ItemBody Body { get; set; }
        
        [JsonProperty("attachments")]
        public List<ChatMessageAttachment> Attachments { get; set; }
        
        [JsonProperty("links")]
        public List<LinkInfo> Links { get; set; }
        
        [JsonProperty("mentions")]
        public List<ChatMessageMention> Mentions { get; set; }
    }

    public enum AiInteractionType
    {
        [JsonProperty("userPrompt")]
        UserPrompt,
        
        [JsonProperty("aiResponse")]
        AiResponse
    }

    public class AiContext
    {
        [JsonProperty("contextReference")]
        public string ContextReference { get; set; }
        
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
        
        [JsonProperty("contextType")]
        public string ContextType { get; set; }
    }

    public class LinkInfo
    {
        [JsonProperty("linkUrl")]
        public string LinkUrl { get; set; }
        
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
        
        [JsonProperty("linkType")]
        public string LinkType { get; set; }
    }
}