using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Newtonsoft.Json;
using VaultsFunctions.Core.Models;

namespace VaultsFunctions.Core.Services
{
    public class PurviewAuditService
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<PurviewAuditService> _logger;
        private readonly HttpClient _httpClient;
        private readonly TokenCredential _credential;

        public PurviewAuditService(IConfiguration configuration, ILogger<PurviewAuditService> logger)
        {
            _logger = logger;
            
            // Configure Graph client with required scopes for Purview Audit APIs
            var scopes = new[] { 
                "https://graph.microsoft.com/.default" // Uses all granted application permissions
            };

            // Use system-assigned managed identity (production standard)
            var managedIdentityEnabled = configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            
            if (managedIdentityEnabled)
            {
                _logger.LogInformation("PurviewAuditService: Using system-assigned ManagedIdentityCredential");
                _credential = new ManagedIdentityCredential();
            }
            else
            {
                _logger.LogWarning("PurviewAuditService: Using ClientSecretCredential (development only)");
                var tenantId = configuration["AZURE_TENANT_ID"];
                var clientId = configuration["AZURE_CLIENT_ID"];
                var clientSecret = configuration["AZURE_CLIENT_SECRET"];
                _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }

            _graphServiceClient = new GraphServiceClient(_credential, scopes);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Retrieve Copilot audit logs from Microsoft Purview
        /// https://learn.microsoft.com/en-us/purview/audit-copilot
        /// </summary>
        public async Task<List<AuditLogEntry>> GetCopilotAuditLogsAsync(
            DateTime startTime, 
            DateTime endTime, 
            string tenantId = null,
            int maxResults = 1000)
        {
            try
            {
                _logger.LogInformation($"Retrieving Copilot audit logs from {startTime:yyyy-MM-dd} to {endTime:yyyy-MM-dd}");

                // Search for Copilot-related audit log entries
                var auditLogSearchRequest = new AuditLogQuery
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    RecordTypes = new List<string> 
                    { 
                        "CopilotInteraction",           // Microsoft 365 Copilot interactions
                        "CopilotChat",                  // Copilot chat interactions
                        "SecurityCopilot",              // Security Copilot interactions
                        "PowerPlatformCopilot",         // Power Platform Copilot
                        "AiInteraction"                 // General AI interaction events
                    },
                    Operations = new List<string>
                    {
                        "CopilotInteraction",
                        "PromptSubmission",
                        "ResponseGenerated",
                        "FileAccessed",
                        "SensitiveDataAccessed"
                    }
                };

                // Use Microsoft Graph Security API to search audit logs
                var searchResponse = await SearchAuditLogsAsync(auditLogSearchRequest);
                
                _logger.LogInformation($"Retrieved {searchResponse.Count} Copilot audit log entries");
                
                return searchResponse;
            }
            catch (ServiceException ex)
            {
                _logger.LogError(ex, $"Microsoft Graph API error retrieving Copilot audit logs: {ex.Error?.Code} - {ex.Error?.Message}");
                throw new InvalidOperationException($"Failed to retrieve Copilot audit logs: {ex.Error?.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error retrieving Copilot audit logs");
                throw;
            }
        }

        /// <summary>
        /// Search audit logs using Microsoft Graph Security API
        /// </summary>
        private async Task<List<AuditLogEntry>> SearchAuditLogsAsync(AuditLogQuery query)
        {
            var auditEntries = new List<AuditLogEntry>();

            try
            {
                // Microsoft Graph endpoint for audit log search
                // https://graph.microsoft.com/v1.0/security/auditLog/queries
                var requestBody = new
                {
                    displayName = "Vaults Audit Search",
                    filterStartDateTime = query.StartTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    filterEndDateTime = query.EndTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    recordTypes = query.RecordTypes,
                    operations = query.Operations
                };

                var json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // Get access token for Graph API
                var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
                var accessToken = await _credential.GetTokenAsync(tokenRequestContext, default);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken.Token}");

                // Submit audit log search query
                var response = await _httpClient.PostAsync(
                    "https://graph.microsoft.com/v1.0/security/auditLog/queries", 
                    content);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var searchResult = JsonConvert.DeserializeObject<AuditSearchResponse>(responseContent);
                    
                    // Poll for search results (audit log searches are asynchronous)
                    if (!string.IsNullOrEmpty(searchResult?.Id))
                    {
                        auditEntries = await PollAuditSearchResultsAsync(searchResult.Id);
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Audit log search failed: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"Audit log search failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching audit logs via Graph API");
                throw;
            }

            return auditEntries;
        }

        /// <summary>
        /// Poll audit search results until completion
        /// </summary>
        private async Task<List<AuditLogEntry>> PollAuditSearchResultsAsync(string searchId)
        {
            var auditEntries = new List<AuditLogEntry>();
            var maxPollingAttempts = 30; // Max 5 minutes of polling
            var pollingInterval = TimeSpan.FromSeconds(10);

            for (int attempt = 0; attempt < maxPollingAttempts; attempt++)
            {
                try
                {
                    var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
                    var accessToken = await _credential.GetTokenAsync(tokenRequestContext, default);

                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken.Token}");

                    var response = await _httpClient.GetAsync(
                        $"https://graph.microsoft.com/v1.0/security/auditLog/queries/{searchId}");

                    if (response.IsSuccessStatusCode)
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        var searchStatus = JsonConvert.DeserializeObject<AuditSearchStatus>(content);

                        if (searchStatus?.Status == "completed")
                        {
                            // Retrieve the actual audit log records
                            auditEntries = await GetAuditSearchRecordsAsync(searchId);
                            break;
                        }
                        else if (searchStatus?.Status == "failed")
                        {
                            _logger.LogError($"Audit log search failed: {searchStatus.Error}");
                            break;
                        }
                        
                        // Still running, wait and retry
                        _logger.LogInformation($"Audit search in progress, attempt {attempt + 1}/{maxPollingAttempts}");
                        await Task.Delay(pollingInterval);
                    }
                    else
                    {
                        _logger.LogWarning($"Polling attempt {attempt + 1} failed: {response.StatusCode}");
                        await Task.Delay(pollingInterval);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Polling attempt {attempt + 1} encountered error");
                    await Task.Delay(pollingInterval);
                }
            }

            return auditEntries;
        }

        /// <summary>
        /// Retrieve audit records from completed search
        /// </summary>
        private async Task<List<AuditLogEntry>> GetAuditSearchRecordsAsync(string searchId)
        {
            var auditEntries = new List<AuditLogEntry>();

            try
            {
                var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
                var accessToken = await _credential.GetTokenAsync(tokenRequestContext, default);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken.Token}");

                var response = await _httpClient.GetAsync(
                    $"https://graph.microsoft.com/v1.0/security/auditLog/queries/{searchId}/records");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var recordsResponse = JsonConvert.DeserializeObject<AuditRecordsResponse>(content);

                    if (recordsResponse?.Value != null)
                    {
                        foreach (var record in recordsResponse.Value)
                        {
                            auditEntries.Add(new AuditLogEntry
                            {
                                Id = record.Id,
                                CreationTime = DateTime.Parse(record.CreationTime),
                                Operation = record.Operation,
                                RecordType = record.RecordType,
                                UserPrincipalName = record.UserId,
                                ClientIP = record.ClientIP,
                                UserAgent = record.UserAgent,
                                AuditData = record.AuditData,
                                // Copilot-specific fields
                                CopilotEventData = ExtractCopilotEventData(record.AuditData)
                            });
                        }
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to retrieve audit records: {response.StatusCode} - {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audit search records");
            }

            return auditEntries;
        }

        /// <summary>
        /// Extract Copilot-specific event data from audit log
        /// </summary>
        private CopilotEventData ExtractCopilotEventData(string auditDataJson)
        {
            try
            {
                if (string.IsNullOrEmpty(auditDataJson))
                    return null;

                var auditData = JsonConvert.DeserializeObject<dynamic>(auditDataJson);
                
                return new CopilotEventData
                {
                    AppName = auditData?.AppName?.ToString(),
                    PromptText = auditData?.Prompt?.ToString(),
                    ResponseText = auditData?.Response?.ToString(),
                    ResourcesAccessed = auditData?.ResourcesAccessed?.ToString(),
                    SensitivityLabels = auditData?.SensitivityLabels?.ToString(),
                    IsJailbreakAttempt = bool.TryParse(auditData?.IsJailbreakAttempt?.ToString(), out bool jailbreak) ? jailbreak : false,
                    ModelTransparency = auditData?.ModelTransparency?.ToString(),
                    PluginsUsed = auditData?.PluginsUsed?.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract Copilot event data from audit log");
                return null;
            }
        }

        /// <summary>
        /// Real-time audit log streaming (webhook-based)
        /// </summary>
        public async Task<bool> SubscribeToRealTimeAuditLogsAsync(string webhookUrl, string tenantId = null)
        {
            try
            {
                _logger.LogInformation($"Setting up real-time audit log subscription for webhook: {webhookUrl}");

                // Create Microsoft Graph subscription for audit log events
                var subscription = new Subscription
                {
                    ChangeType = "created",
                    NotificationUrl = webhookUrl,
                    Resource = "security/auditLog/queries",
                    ExpirationDateTime = DateTimeOffset.UtcNow.AddHours(24), // 24-hour subscription
                    ClientState = Guid.NewGuid().ToString() // Validation token
                };

                var createdSubscription = await _graphServiceClient.Subscriptions.PostAsync(subscription);
                
                if (createdSubscription != null)
                {
                    _logger.LogInformation($"Successfully created audit log subscription: {createdSubscription.Id}");
                    return true;
                }

                return false;
            }
            catch (ServiceException ex)
            {
                _logger.LogError(ex, $"Failed to create audit log subscription: {ex.Error?.Code} - {ex.Error?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating audit log subscription");
                return false;
            }
        }
    }

    // Supporting classes for audit log operations
    public class AuditLogQuery
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<string> RecordTypes { get; set; } = new List<string>();
        public List<string> Operations { get; set; } = new List<string>();
    }

    public class AuditSearchResponse
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string DisplayName { get; set; }
    }

    public class AuditSearchStatus
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
    }

    public class AuditRecordsResponse
    {
        public List<AuditRecord> Value { get; set; } = new List<AuditRecord>();
    }

    public class AuditRecord
    {
        public string Id { get; set; }
        public string CreationTime { get; set; }
        public string Operation { get; set; }
        public string RecordType { get; set; }
        public string UserId { get; set; }
        public string ClientIP { get; set; }
        public string UserAgent { get; set; }
        public string AuditData { get; set; }
    }

    public class AuditLogEntry
    {
        public string Id { get; set; }
        public DateTime CreationTime { get; set; }
        public string Operation { get; set; }
        public string RecordType { get; set; }
        public string UserPrincipalName { get; set; }
        public string ClientIP { get; set; }
        public string UserAgent { get; set; }
        public string AuditData { get; set; }
        public CopilotEventData CopilotEventData { get; set; }
    }

    public class CopilotEventData
    {
        public string AppName { get; set; }
        public string PromptText { get; set; }
        public string ResponseText { get; set; }
        public string ResourcesAccessed { get; set; }
        public string SensitivityLabels { get; set; }
        public bool IsJailbreakAttempt { get; set; }
        public string ModelTransparency { get; set; }
        public string PluginsUsed { get; set; }
    }
}