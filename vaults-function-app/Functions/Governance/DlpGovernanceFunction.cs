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
    public class DlpGovernanceFunction
    {
        private readonly ILogger<DlpGovernanceFunction> _logger;
        private readonly PurviewDlpService _purviewDlpService;
        private readonly IConfiguration _configuration;

        public DlpGovernanceFunction(
            ILogger<DlpGovernanceFunction> logger,
            PurviewDlpService purviewDlpService,
            IConfiguration configuration)
        {
            _logger = logger;
            _purviewDlpService = purviewDlpService;
            _configuration = configuration;
        }

        /// <summary>
        /// Get current DLP policies and their governance configurations
        /// GET /api/governance/dlp/policies
        /// </summary>
        [Function("GetDlpPolicies")]
        public async Task<HttpResponseData> GetDlpPolicies(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/dlp/policies")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing DLP policies request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = query["tenantId"] ?? "default-tenant";

                // Retrieve DLP policies from Purview
                var dlpPolicies = await _purviewDlpService.GetDlpPoliciesAsync(tenantId);

                // Format response with governance context
                var responseData = new
                {
                    tenantId,
                    totalPolicies = dlpPolicies.Count,
                    policies = dlpPolicies.Select(policy => new
                    {
                        id = policy.Id,
                        name = policy.Name,
                        description = policy.Description,
                        state = policy.State,
                        priority = policy.Priority,
                        locations = policy.Locations,
                        rules = policy.Rules.Select(rule => new
                        {
                            id = rule.Id,
                            name = rule.Name,
                            conditions = rule.Conditions,
                            actions = rule.Actions,
                            governanceActions = GetGovernanceActionsForRule(rule)
                        }).ToList(),
                        governanceEnabled = true, // Vaults governance layer
                        riskLevel = CalculatePolicyRiskLevel(policy)
                    }).ToList(),
                    governanceCapabilities = new
                    {
                        realTimeEnforcement = true,
                        copilotIntegration = true,
                        riskScoring = true,
                        approvalWorkflows = true,
                        enhancedMonitoring = true
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Successfully retrieved {dlpPolicies.Count} DLP policies for tenant {tenantId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving DLP policies");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to retrieve DLP policies",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Subscribe to real-time DLP violation events
        /// POST /api/governance/dlp/subscribe
        /// </summary>
        [Function("SubscribeDlpViolations")]
        public async Task<HttpResponseData> SubscribeDlpViolations(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/dlp/subscribe")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing DLP violation subscription request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var subscriptionRequest = JsonConvert.DeserializeObject<DlpSubscriptionRequest>(requestBody);

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

                // Create DLP violation subscription
                var subscriptionSuccess = await _purviewDlpService.SubscribeToDlpViolationsAsync(
                    subscriptionRequest.WebhookUrl, subscriptionRequest.TenantId);

                if (subscriptionSuccess)
                {
                    response.StatusCode = HttpStatusCode.Created;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        message = "Successfully subscribed to DLP violation events",
                        webhookUrl = subscriptionRequest.WebhookUrl,
                        tenantId = subscriptionRequest.TenantId ?? "default-tenant",
                        governanceFeatures = new
                        {
                            realTimeProcessing = true,
                            riskAssessment = true,
                            automaticActions = true,
                            copilotIntegration = true
                        },
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }));
                }
                else
                {
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Failed to create DLP subscription",
                        message = "Unable to subscribe to DLP violation events"
                    }));
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating DLP violation subscription");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to create DLP subscription",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Webhook endpoint for receiving real-time DLP violation events
        /// POST /api/governance/dlp/webhook
        /// </summary>
        [Function("DlpViolationWebhook")]
        public async Task<HttpResponseData> DlpViolationWebhook(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/dlp/webhook")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing DLP violation webhook notification");

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
                var webhookNotification = JsonConvert.DeserializeObject<DlpWebhookNotification>(requestBody);
                
                if (webhookNotification?.Value != null)
                {
                    var processedViolations = new List<object>();

                    foreach (var notification in webhookNotification.Value)
                    {
                        var processedViolation = await ProcessDlpViolationNotificationAsync(notification);
                        processedViolations.Add(processedViolation);
                    }

                    _logger.LogInformation($"Processed {webhookNotification.Value.Count} DLP violation notifications");

                    // Return processing summary
                    response.StatusCode = HttpStatusCode.OK;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        message = "DLP violations processed successfully",
                        processedCount = processedViolations.Count,
                        violations = processedViolations,
                        timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    }, Formatting.Indented));
                }
                else
                {
                    response.StatusCode = HttpStatusCode.OK;
                    await response.WriteStringAsync("No violations to process");
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing DLP violation webhook");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                await errorResponse.WriteStringAsync($"Webhook processing failed: {ex.Message}");
                return errorResponse;
            }
        }

        /// <summary>
        /// Manual DLP violation risk assessment
        /// POST /api/governance/dlp/assess-risk
        /// </summary>
        [Function("AssessDlpViolationRisk")]
        public async Task<HttpResponseData> AssessDlpViolationRisk(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/dlp/assess-risk")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing DLP violation risk assessment request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var violationEvent = JsonConvert.DeserializeObject<DlpViolationEvent>(requestBody);

                if (violationEvent == null)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid request",
                        message = "DLP violation event data is required"
                    }));
                    return response;
                }

                // Process violation and determine governance actions
                var governanceResponse = await _purviewDlpService.ProcessDlpViolationAsync(violationEvent);

                // Apply governance actions if requested
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var applyActions = bool.TryParse(query["applyActions"], out bool apply) && apply;

                if (applyActions)
                {
                    var actionsApplied = await _purviewDlpService.ApplyGovernanceActionsAsync(
                        governanceResponse, violationEvent);
                    
                    governanceResponse.ActionsApplied = actionsApplied;
                }

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    violationId = governanceResponse.ViolationId,
                    riskAssessment = new
                    {
                        riskLevel = governanceResponse.RiskLevel,
                        riskScore = governanceResponse.RiskScore,
                        processedAt = governanceResponse.ProcessedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    },
                    governanceActions = new
                    {
                        required = governanceResponse.ActionsRequired,
                        applied = applyActions ? governanceResponse.ActionsApplied : null
                    },
                    copilotImpact = new
                    {
                        accessRestricted = governanceResponse.ActionsRequired.Contains("BLOCK_COPILOT_ACCESS"),
                        responseFiltered = governanceResponse.ActionsRequired.Contains("RESTRICT_COPILOT_RESPONSE"),
                        approvalRequired = governanceResponse.ActionsRequired.Contains("REQUIRE_APPROVAL"),
                        enhancedMonitoring = governanceResponse.ActionsRequired.Contains("ENHANCED_MONITORING")
                    }
                }, Formatting.Indented));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assessing DLP violation risk");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to assess DLP violation risk",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Process individual DLP violation notifications
        /// </summary>
        private async Task<object> ProcessDlpViolationNotificationAsync(DlpNotification notification)
        {
            try
            {
                _logger.LogInformation($"Processing DLP violation notification: {notification.ResourceData?.Id}");

                // Extract DLP violation details
                var violationEvent = ExtractDlpViolationEvent(notification);
                
                if (violationEvent != null)
                {
                    // Assess risk and determine governance actions
                    var governanceResponse = await _purviewDlpService.ProcessDlpViolationAsync(violationEvent);
                    
                    // Apply automatic governance actions for high-risk violations
                    if (governanceResponse.RiskLevel == "HIGH")
                    {
                        await _purviewDlpService.ApplyGovernanceActionsAsync(governanceResponse, violationEvent);
                    }

                    return new
                    {
                        violationId = violationEvent.ViolationId,
                        userId = violationEvent.UserId,
                        riskLevel = governanceResponse.RiskLevel,
                        riskScore = governanceResponse.RiskScore,
                        actionsRequired = governanceResponse.ActionsRequired,
                        processed = true
                    };
                }

                return new
                {
                    notificationId = notification.ResourceData?.Id,
                    processed = false,
                    reason = "Unable to extract violation details"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing DLP violation notification: {notification.ResourceData?.Id}");
                return new
                {
                    notificationId = notification.ResourceData?.Id,
                    processed = false,
                    error = ex.Message
                };
            }
        }

        /// <summary>
        /// Extract DLP violation event from webhook notification
        /// </summary>
        private DlpViolationEvent ExtractDlpViolationEvent(DlpNotification notification)
        {
            try
            {
                if (notification.ResourceData?.Data == null)
                    return null;

                var data = JsonConvert.DeserializeObject<dynamic>(notification.ResourceData.Data);
                
                return new DlpViolationEvent
                {
                    ViolationId = notification.ResourceData.Id,
                    UserId = data?.UserId?.ToString(),
                    ResourceId = data?.ResourceId?.ToString(),
                    Location = data?.Location?.ToString(),
                    SensitivityLabel = data?.SensitivityLabel?.ToString(),
                    SensitiveInfoTypes = ExtractSensitiveInfoTypes(data?.SensitiveInfoTypes),
                    IsHighRiskUser = bool.TryParse(data?.IsHighRiskUser?.ToString(), out bool highRisk) ? highRisk : false,
                    IsExternalSharing = bool.TryParse(data?.IsExternalSharing?.ToString(), out bool external) ? external : false,
                    UserViolationCount = int.TryParse(data?.UserViolationCount?.ToString(), out int count) ? count : 1,
                    ViolationTime = DateTime.TryParse(data?.ViolationTime?.ToString(), out DateTime time) ? time : DateTime.UtcNow,
                    PolicyId = data?.PolicyId?.ToString(),
                    RuleId = data?.RuleId?.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract DLP violation event from notification");
                return null;
            }
        }

        /// <summary>
        /// Extract sensitive information types from notification data
        /// </summary>
        private List<string> ExtractSensitiveInfoTypes(dynamic sensitiveInfoTypesData)
        {
            var infoTypes = new List<string>();
            
            try
            {
                if (sensitiveInfoTypesData != null)
                {
                    var jsonString = sensitiveInfoTypesData.ToString();
                    var infoTypesList = JsonConvert.DeserializeObject<List<string>>(jsonString);
                    if (infoTypesList != null)
                    {
                        infoTypes.AddRange(infoTypesList);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract sensitive info types");
            }

            return infoTypes;
        }

        /// <summary>
        /// Get governance actions that would be applied for a specific DLP rule
        /// </summary>
        private List<string> GetGovernanceActionsForRule(DlpRuleInfo rule)
        {
            var actions = new List<string>();

            // Analyze rule conditions and map to governance actions
            if (rule.Conditions?.ToLower().Contains("credit card") == true ||
                rule.Conditions?.ToLower().Contains("social security") == true)
            {
                actions.Add("BLOCK_COPILOT_ACCESS");
                actions.Add("NOTIFY_SECURITY_TEAM");
            }

            if (rule.Conditions?.ToLower().Contains("confidential") == true)
            {
                actions.Add("RESTRICT_COPILOT_RESPONSE");
                actions.Add("LOG_ACCESS_ATTEMPT");
            }

            if (rule.Actions?.ToLower().Contains("external") == true)
            {
                actions.Add("REQUIRE_APPROVAL");
                actions.Add("ENHANCED_MONITORING");
            }

            // Default actions for all DLP rules
            if (actions.Count == 0)
            {
                actions.Add("LOG_ACCESS_ATTEMPT");
            }

            return actions;
        }

        /// <summary>
        /// Calculate overall risk level for a DLP policy
        /// </summary>
        private string CalculatePolicyRiskLevel(DlpPolicyInfo policy)
        {
            int riskScore = 0;

            // Higher priority = higher risk
            riskScore += policy.Priority * 10;

            // More rules = higher complexity = higher risk
            riskScore += policy.Rules.Count * 5;

            // Location-based risk
            if (policy.Locations.Any(l => l.ToLower().Contains("sharepoint") || l.ToLower().Contains("onedrive")))
            {
                riskScore += 15;
            }

            return riskScore switch
            {
                >= 50 => "HIGH",
                >= 25 => "MEDIUM",
                >= 10 => "LOW",
                _ => "MINIMAL"
            };
        }
    }

    // Supporting classes for DLP webhook processing
    public class DlpSubscriptionRequest
    {
        public string WebhookUrl { get; set; }
        public string TenantId { get; set; }
    }

    public class DlpWebhookNotification
    {
        public List<DlpNotification> Value { get; set; } = new List<DlpNotification>();
    }

    public class DlpNotification
    {
        public string SubscriptionId { get; set; }
        public string ChangeType { get; set; }
        public string Resource { get; set; }
        public DlpResourceData ResourceData { get; set; }
    }

    public class DlpResourceData
    {
        public string Id { get; set; }
        public string Data { get; set; }
    }
}