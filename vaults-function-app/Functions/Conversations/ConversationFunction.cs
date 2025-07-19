using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using VaultsFunctions.Core;
using VaultsFunctions.Core.Helpers;
using System.Net;
using System.Collections.Generic;
using System.Linq;

namespace VaultsFunctions.Functions.Conversations
{
    public class ConversationFunction
    {
        private readonly ILogger<ConversationFunction> _logger;
        private readonly IConfiguration _configuration;
        private readonly Container _conversationsContainer;

        public ConversationFunction(
            IConfiguration configuration,
            CosmosClient cosmosClient,
            ILogger<ConversationFunction> logger)
        {
            _configuration = configuration;
            _logger = logger;
            var database = cosmosClient.GetDatabase(Constants.Databases.MainDatabase);
            _conversationsContainer = database.GetContainer(Constants.Databases.ProcessedConversationsContainer);
        }

        [Function("ConversationFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = Constants.ApiRoutes.Conversations)] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("Conversation function processed a request.");

            var response = req.CreateResponse();
            
            // Handle CORS
            bool isOptionsRequest = req.Method == "OPTIONS";
            CorsHelper.AddCorsHeaders(response, _configuration, isOptionsRequest);
            if (isOptionsRequest)
            {
                return response;
            }

            // Input validation
            var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string tenantId = queryParams["tenantId"];
            string conversationId = queryParams["conversationId"];

            if (string.IsNullOrEmpty(tenantId))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteAsJsonAsync(new 
                { 
                    error = "Please provide tenantId parameter." 
                });
                return response;
            }

            // If conversationId is provided, return single conversation
            if (!string.IsNullOrEmpty(conversationId))
            {
                return await GetSingleConversation(tenantId, conversationId, response);
            }

            // Otherwise, return list of conversations with pagination
            return await GetConversationsList(tenantId, queryParams, response);
        }

        private async Task<HttpResponseData> GetSingleConversation(string tenantId, string conversationId, HttpResponseData response)
        {
            try
            {
                // Query Cosmos DB for the specific conversation
                var cosmosResponse = await _conversationsContainer.ReadItemAsync<dynamic>(
                    conversationId, 
                    new PartitionKey(tenantId)
                );

                if (cosmosResponse.Resource == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteAsJsonAsync(new
                    {
                        error = $"Conversation '{conversationId}' not found for tenant '{tenantId}'."
                    });
                    return response;
                }

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync((object)cosmosResponse.Resource);
                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning(ex, "Conversation not found: {ConversationId}, Tenant: {TenantId}", 
                    conversationId, tenantId);
                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteAsJsonAsync(new 
                { 
                    error = $"Conversation '{conversationId}' not found for tenant '{tenantId}'." 
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversation: {ConversationId}, Tenant: {TenantId}", 
                    conversationId, tenantId);
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new 
                { 
                    error = "An internal server error occurred while retrieving the conversation." 
                });
                return response;
            }
        }

        private async Task<HttpResponseData> GetConversationsList(string tenantId, System.Collections.Specialized.NameValueCollection queryParams, HttpResponseData response)
        {
            try
            {
                // Parse pagination parameters
                int top = 20; // Default page size
                int skip = 0;
                string orderBy = "CreatedAt desc"; // Default ordering

                if (int.TryParse(queryParams["top"], out int parsedTop) && parsedTop > 0 && parsedTop <= 100)
                {
                    top = parsedTop;
                }

                if (int.TryParse(queryParams["skip"], out int parsedSkip) && parsedSkip >= 0)
                {
                    skip = parsedSkip;
                }

                if (!string.IsNullOrEmpty(queryParams["orderBy"]))
                {
                    orderBy = queryParams["orderBy"];
                }

                // Build query - ensure we're querying by partition key (tenantId)
                string sql = $"SELECT * FROM c WHERE c.tenantId = @tenantId ORDER BY c.{orderBy.Replace(" desc", "").Replace(" asc", "")} {(orderBy.Contains("desc") ? "DESC" : "ASC")} OFFSET @skip LIMIT @top";
                
                var queryDef = new QueryDefinition(sql)
                    .WithParameter("@tenantId", tenantId)
                    .WithParameter("@skip", skip)
                    .WithParameter("@top", top);

                var results = new List<dynamic>();
                using var feedIterator = _conversationsContainer.GetItemQueryIterator<dynamic>(queryDef);

                while (feedIterator.HasMoreResults)
                {
                    var feedResponse = await feedIterator.ReadNextAsync();
                    results.AddRange(feedResponse);
                }

                // Count total items for pagination info
                string countSql = "SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId";
                var countQueryDef = new QueryDefinition(countSql).WithParameter("@tenantId", tenantId);
                
                int totalCount = 0;
                using var countIterator = _conversationsContainer.GetItemQueryIterator<int>(countQueryDef);
                if (countIterator.HasMoreResults)
                {
                    var countResponse = await countIterator.ReadNextAsync();
                    totalCount = countResponse.FirstOrDefault();
                }

                var paginatedResponse = new
                {
                    data = results,
                    pagination = new
                    {
                        total = totalCount,
                        skip = skip,
                        top = top,
                        hasMore = skip + top < totalCount
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(paginatedResponse);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving conversations list for tenant: {TenantId}", tenantId);
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new 
                { 
                    error = "An internal server error occurred while retrieving conversations." 
                });
                return response;
            }
        }
    }
}
