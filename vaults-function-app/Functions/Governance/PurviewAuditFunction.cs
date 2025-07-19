using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using Newtonsoft.Json;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Helpers;

namespace VaultsFunctions.Functions.Governance
{
    public class PurviewAuditFunction
    {
        private readonly ILogger<PurviewAuditFunction> _logger;
        private readonly PurviewAuditService _purviewAuditService;
        private readonly IConfiguration _configuration;

        public PurviewAuditFunction(
            ILogger<PurviewAuditFunction> logger,
            PurviewAuditService purviewAuditService,
            IConfiguration configuration)
        {
            _logger = logger;
            _purviewAuditService = purviewAuditService;
            _configuration = configuration;
        }

        /// <summary>
        /// Retrieve Copilot audit logs from Microsoft Purview
        /// GET /api/governance/purview/audit-logs
        /// </summary>
        [Function("GetPurviewAuditLogs")]
        public async Task<HttpResponseData> GetPurviewAuditLogs(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/purview/audit-logs")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing Purview audit logs request");

                // Add CORS headers
                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = query["tenantId"] ?? "default-tenant";
                var startTimeStr = query["startTime"];
                var endTimeStr = query["endTime"];
                var maxResultsStr = query["maxResults"] ?? "1000";

                // Validate and parse date parameters
                if (!DateTime.TryParse(startTimeStr, out DateTime startTime))
                {
                    startTime = DateTime.UtcNow.AddDays(-7); // Default: last 7 days
                }

                if (!DateTime.TryParse(endTimeStr, out DateTime endTime))
                {
                    endTime = DateTime.UtcNow;
                }

                if (!int.TryParse(maxResultsStr, out int maxResults))
                {
                    maxResults = 1000;
                }

                // Retrieve audit logs from Purview
                var auditLogs = await _purviewAuditService.GetCopilotAuditLogsAsync(
                    startTime, endTime, tenantId, maxResults);

                // Format response
                var responseData = new
                {
                    tenantId,
                    startTime = startTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    endTime = endTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    totalRecords = auditLogs.Count,
                    auditLogs = auditLogs.Select(log => new
                    {
                        id = log.Id,
                        creationTime = log.CreationTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        operation = log.Operation,
                        recordType = log.RecordType,
                        userPrincipalName = log.UserPrincipalName,
                        clientIP = log.ClientIP,
                        userAgent = log.UserAgent,
                        copilotEvent = log.CopilotEventData != null ? new
                        {
                            appName = log.CopilotEventData.AppName,
                            promptText = log.CopilotEventData.PromptText,
                            responseText = log.CopilotEventData.ResponseText,
                            resourcesAccessed = log.CopilotEventData.ResourcesAccessed,
                            sensitivityLabels = log.CopilotEventData.SensitivityLabels,
                            isJailbreakAttempt = log.CopilotEventData.IsJailbreakAttempt,
                            modelTransparency = log.CopilotEventData.ModelTransparency,
                            pluginsUsed = log.CopilotEventData.PluginsUsed
                        } : null
                    }).ToList()
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Successfully retrieved {auditLogs.Count} Purview audit log entries for tenant {tenantId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Purview audit logs");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to retrieve Purview audit logs",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Set up real-time audit log streaming from Purview
        /// POST /api/governance/purview/subscribe
        /// </summary>
        [Function("SubscribePurviewAuditLogs")]
        public async Task<HttpResponseData> SubscribePurviewAuditLogs(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/purview/subscribe")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing Purview audit log subscription request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var subscriptionRequest = JsonConvert.DeserializeObject<PurviewSubscriptionRequest>(requestBody);

                if (subscriptionRequest == null || string.IsNullOrEmpty(subscriptionRequest.WebhookUrl))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid subscription request",
                        message = "WebhookUrl is required"
                    }));
                    return response;
                }

                // Create subscription
                var subscriptionSuccess = await _purviewAuditService.SubscribeToRealTimeAuditLogsAsync(
                    subscriptionRequest.WebhookUrl, subscriptionRequest.TenantId);

                if (subscriptionSuccess)
                {
                    response.StatusCode = HttpStatusCode.Created;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        message = "Successfully subscribed to Purview audit log events",
                        webhookUrl = subscriptionRequest.WebhookUrl,
                        tenantId = subscriptionRequest.TenantId ?? "default-tenant",
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }));
                }
                else
                {
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Failed to create subscription",
                        message = "Unable to subscribe to Purview audit log events"
                    }));
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating Purview audit log subscription");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to create subscription",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Webhook endpoint for receiving real-time audit log events from Purview
        /// POST /api/governance/purview/webhook
        /// </summary>
        [Function("PurviewAuditWebhook")]
        public async Task<HttpResponseData> PurviewAuditWebhook(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/purview/webhook")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing Purview audit log webhook notification");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Read webhook payload
                var requestBody = await req.ReadAsStringAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync("Empty webhook payload");
                    return response;
                }

                // Parse webhook notification
                var webhookNotification = JsonConvert.DeserializeObject<PurviewWebhookNotification>(requestBody);
                
                if (webhookNotification?.Value != null)
                {
                    foreach (var notification in webhookNotification.Value)
                    {
                        await ProcessAuditLogNotificationAsync(notification);
                    }

                    _logger.LogInformation($"Processed {webhookNotification.Value.Count} audit log notifications");
                }

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync("Webhook processed successfully");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing Purview audit webhook");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                await errorResponse.WriteStringAsync($"Webhook processing failed: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// Process individual audit log notifications for governance actions
        /// </summary>
        private async Task ProcessAuditLogNotificationAsync(AuditNotification notification)
        {
            try
            {
                _logger.LogInformation($"Processing audit notification: {notification.ResourceData?.Id}");

                // Extract Copilot event details
                if (notification.ResourceData?.AuditData != null)
                {
                    var auditData = JsonConvert.DeserializeObject<dynamic>(notification.ResourceData.AuditData);
                    
                    // Check for governance triggers
                    await CheckGovernanceTriggers(auditData, notification);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing audit notification: {notification.ResourceData?.Id}");
            }
        }

        /// <summary>
        /// Check audit events against governance rules and trigger actions
        /// </summary>
        private async Task CheckGovernanceTriggers(dynamic auditData, AuditNotification notification)
        {
            try
            {
                // Governance Rule 1: Jailbreak Attempt Detection
                if (bool.TryParse(auditData?.IsJailbreakAttempt?.ToString(), out bool isJailbreak) && isJailbreak)
                {
                    _logger.LogWarning($"Jailbreak attempt detected: User {notification.ResourceData?.UserId}");
                    // TODO: Trigger security alert workflow
                }

                // Governance Rule 2: Sensitive Data Access
                var sensitivityLabels = auditData?.SensitivityLabels?.ToString();
                if (!string.IsNullOrEmpty(sensitivityLabels) && 
                    (sensitivityLabels.Contains("Confidential") || sensitivityLabels.Contains("Restricted")))
                {
                    _logger.LogWarning($"Sensitive data accessed via Copilot: User {notification.ResourceData?.UserId}");
                    // TODO: Trigger data protection workflow
                }

                // Governance Rule 3: Unusual Resource Access Patterns
                var resourcesAccessed = auditData?.ResourcesAccessed?.ToString();
                if (!string.IsNullOrEmpty(resourcesAccessed))
                {
                    var resourceCount = resourcesAccessed.Split(',').Length;
                    if (resourceCount > 10) // Threshold for unusual access
                    {
                        _logger.LogWarning($"Unusual resource access pattern: User {notification.ResourceData?.UserId} accessed {resourceCount} resources");
                        // TODO: Trigger anomaly detection workflow
                    }
                }

                // Governance Rule 4: External Plugin Usage
                var pluginsUsed = auditData?.PluginsUsed?.ToString();
                if (!string.IsNullOrEmpty(pluginsUsed))
                {
                    _logger.LogInformation($"External plugins used: {pluginsUsed} by User {notification.ResourceData?.UserId}");
                    // TODO: Validate against approved plugin list
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking governance triggers");
            }
        }
    }

    // Supporting classes for webhook processing
    public class PurviewSubscriptionRequest
    {
        public string WebhookUrl { get; set; }
        public string TenantId { get; set; }
    }

    public class PurviewWebhookNotification
    {
        public List<AuditNotification> Value { get; set; } = new List<AuditNotification>();
    }

    public class AuditNotification
    {
        public string SubscriptionId { get; set; }
        public string ChangeType { get; set; }
        public string Resource { get; set; }
        public AuditResourceData ResourceData { get; set; }
    }

    public class AuditResourceData
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string Operation { get; set; }
        public string RecordType { get; set; }
        public string AuditData { get; set; }
    }
}