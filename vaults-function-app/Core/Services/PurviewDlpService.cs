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
    public class PurviewDlpService
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<PurviewDlpService> _logger;
        private readonly HttpClient _httpClient;
        private readonly TokenCredential _credential;

        public PurviewDlpService(IConfiguration configuration, ILogger<PurviewDlpService> logger)
        {
            _logger = logger;
            
            // Configure Graph client with required scopes for Purview DLP APIs
            var scopes = new[] { 
                "https://graph.microsoft.com/.default" // Uses all granted application permissions
            };

            // Use system-assigned managed identity (production standard)
            var managedIdentityEnabled = configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            
            if (managedIdentityEnabled)
            {
                _logger.LogInformation("PurviewDlpService: Using system-assigned ManagedIdentityCredential");
                _credential = new ManagedIdentityCredential();
            }
            else
            {
                _logger.LogWarning("PurviewDlpService: Using ClientSecretCredential (development only)");
                var tenantId = configuration["AZURE_TENANT_ID"];
                var clientId = configuration["AZURE_CLIENT_ID"];
                var clientSecret = configuration["AZURE_CLIENT_SECRET"];
                _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }

            _graphServiceClient = new GraphServiceClient(_credential, scopes);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Subscribe to real-time DLP policy violation events
        /// https://learn.microsoft.com/en-us/purview/dlp-overview-plan-for-dlp
        /// </summary>
        public async Task<bool> SubscribeToDlpViolationsAsync(string webhookUrl, string tenantId = null)
        {
            try
            {
                _logger.LogInformation($"Setting up DLP violation subscription for webhook: {webhookUrl}");

                // Create Microsoft Graph subscription for DLP policy violation events
                var subscription = new Subscription
                {
                    ChangeType = "created,updated",
                    NotificationUrl = webhookUrl,
                    Resource = "security/informationProtection/dataLossPreventionPolicies", // DLP policy violations
                    ExpirationDateTime = DateTimeOffset.UtcNow.AddHours(24), // 24-hour subscription
                    ClientState = Guid.NewGuid().ToString(), // Validation token
                    IncludeResourceData = true // Include DLP violation details
                };

                var createdSubscription = await _graphServiceClient.Subscriptions.PostAsync(subscription);
                
                if (createdSubscription != null)
                {
                    _logger.LogInformation($"Successfully created DLP violation subscription: {createdSubscription.Id}");
                    return true;
                }

                return false;
            }
            catch (ServiceException ex)
            {
                _logger.LogError(ex, $"Failed to create DLP violation subscription: {ex.Error?.Code} - {ex.Error?.Message}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error creating DLP violation subscription");
                return false;
            }
        }

        /// <summary>
        /// Get current DLP policies and their configurations
        /// </summary>
        public async Task<List<DlpPolicyInfo>> GetDlpPoliciesAsync(string tenantId = null)
        {
            try
            {
                _logger.LogInformation("Retrieving DLP policies from Microsoft Purview");

                var dlpPolicies = new List<DlpPolicyInfo>();

                // Get DLP policies via Microsoft Graph Security API
                var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
                var accessToken = await _credential.GetTokenAsync(tokenRequestContext, default);

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken.Token}");

                // Microsoft Graph endpoint for DLP policies
                var response = await _httpClient.GetAsync(
                    "https://graph.microsoft.com/v1.0/security/informationProtection/dataLossPreventionPolicies");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var policiesResponse = JsonConvert.DeserializeObject<DlpPoliciesResponse>(content);

                    if (policiesResponse?.Value != null)
                    {
                        foreach (var policy in policiesResponse.Value)
                        {
                            dlpPolicies.Add(new DlpPolicyInfo
                            {
                                Id = policy.Id,
                                Name = policy.Name,
                                Description = policy.Description,
                                State = policy.State,
                                Priority = policy.Priority,
                                Locations = policy.Locations,
                                Rules = policy.Rules?.Select(r => new DlpRuleInfo
                                {
                                    Id = r.Id,
                                    Name = r.Name,
                                    Conditions = r.Conditions,
                                    Actions = r.Actions
                                }).ToList() ?? new List<DlpRuleInfo>()
                            });
                        }
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Failed to retrieve DLP policies: {response.StatusCode} - {errorContent}");
                }

                _logger.LogInformation($"Retrieved {dlpPolicies.Count} DLP policies");
                return dlpPolicies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving DLP policies");
                throw;
            }
        }

        /// <summary>
        /// Process DLP violation event and determine governance actions
        /// </summary>
        public async Task<DlpGovernanceResponse> ProcessDlpViolationAsync(DlpViolationEvent dlpEvent)
        {
            try
            {
                _logger.LogInformation($"Processing DLP violation: {dlpEvent.ViolationId}");

                var governanceResponse = new DlpGovernanceResponse
                {
                    ViolationId = dlpEvent.ViolationId,
                    ProcessedAt = DateTime.UtcNow,
                    ActionsRequired = new List<string>(),
                    RiskScore = CalculateRiskScore(dlpEvent)
                };

                // Governance Rule 1: Sensitive Information Type Detection
                if (dlpEvent.SensitiveInfoTypes?.Any() == true)
                {
                    foreach (var infoType in dlpEvent.SensitiveInfoTypes)
                    {
                        switch (infoType.ToLower())
                        {
                            case "credit card number":
                            case "social security number":
                            case "bank account number":
                                governanceResponse.ActionsRequired.Add("BLOCK_COPILOT_ACCESS");
                                governanceResponse.ActionsRequired.Add("NOTIFY_SECURITY_TEAM");
                                governanceResponse.RiskScore += 30;
                                break;
                            case "email address":
                            case "phone number":
                                governanceResponse.ActionsRequired.Add("REQUIRE_APPROVAL");
                                governanceResponse.RiskScore += 10;
                                break;
                        }
                    }
                }

                // Governance Rule 2: Document Sensitivity Label
                if (!string.IsNullOrEmpty(dlpEvent.SensitivityLabel))
                {
                    switch (dlpEvent.SensitivityLabel.ToLower())
                    {
                        case "confidential":
                        case "highly confidential":
                            governanceResponse.ActionsRequired.Add("RESTRICT_COPILOT_RESPONSE");
                            governanceResponse.ActionsRequired.Add("LOG_ACCESS_ATTEMPT");
                            governanceResponse.RiskScore += 25;
                            break;
                        case "internal":
                            governanceResponse.ActionsRequired.Add("LOG_ACCESS_ATTEMPT");
                            governanceResponse.RiskScore += 10;
                            break;
                    }
                }

                // Governance Rule 3: Location-Based Risk Assessment
                if (dlpEvent.Location?.Contains("SharePoint") == true || dlpEvent.Location?.Contains("OneDrive") == true)
                {
                    governanceResponse.ActionsRequired.Add("VALIDATE_USER_PERMISSIONS");
                    governanceResponse.RiskScore += 5;
                }

                // Governance Rule 4: User Risk Profile
                if (dlpEvent.IsHighRiskUser)
                {
                    governanceResponse.ActionsRequired.Add("ENHANCED_MONITORING");
                    governanceResponse.ActionsRequired.Add("REQUIRE_APPROVAL");
                    governanceResponse.RiskScore += 20;
                }

                // Determine overall risk level
                governanceResponse.RiskLevel = governanceResponse.RiskScore switch
                {
                    >= 50 => "HIGH",
                    >= 25 => "MEDIUM",
                    >= 10 => "LOW",
                    _ => "MINIMAL"
                };

                _logger.LogInformation($"DLP violation processed: Risk Level = {governanceResponse.RiskLevel}, Score = {governanceResponse.RiskScore}");
                return governanceResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing DLP violation: {dlpEvent.ViolationId}");
                throw;
            }
        }

        /// <summary>
        /// Calculate risk score based on DLP violation characteristics
        /// </summary>
        private int CalculateRiskScore(DlpViolationEvent dlpEvent)
        {
            int baseScore = 0;

            // Base score for DLP violation
            baseScore += 15;

            // Time-based risk (after hours = higher risk)
            var currentHour = DateTime.UtcNow.Hour;
            if (currentHour < 6 || currentHour > 20) // Outside business hours
            {
                baseScore += 10;
            }

            // Frequency-based risk (multiple violations)
            if (dlpEvent.UserViolationCount > 3)
            {
                baseScore += (dlpEvent.UserViolationCount - 3) * 5; // +5 per additional violation
            }

            // External sharing risk
            if (dlpEvent.IsExternalSharing)
            {
                baseScore += 20;
            }

            return Math.Min(baseScore, 100); // Cap at 100
        }

        /// <summary>
        /// Apply governance actions based on DLP violation assessment
        /// </summary>
        public async Task<bool> ApplyGovernanceActionsAsync(DlpGovernanceResponse governanceResponse, DlpViolationEvent originalEvent)
        {
            try
            {
                _logger.LogInformation($"Applying governance actions for violation: {governanceResponse.ViolationId}");

                foreach (var action in governanceResponse.ActionsRequired)
                {
                    switch (action)
                    {
                        case "BLOCK_COPILOT_ACCESS":
                            await BlockCopilotAccessAsync(originalEvent.UserId, "DLP_VIOLATION");
                            break;
                        case "RESTRICT_COPILOT_RESPONSE":
                            await RestrictCopilotResponseAsync(originalEvent.UserId, originalEvent.ResourceId);
                            break;
                        case "REQUIRE_APPROVAL":
                            await CreateApprovalWorkflowAsync(originalEvent);
                            break;
                        case "NOTIFY_SECURITY_TEAM":
                            await NotifySecurityTeamAsync(governanceResponse, originalEvent);
                            break;
                        case "LOG_ACCESS_ATTEMPT":
                            await LogAccessAttemptAsync(originalEvent);
                            break;
                        case "VALIDATE_USER_PERMISSIONS":
                            await ValidateUserPermissionsAsync(originalEvent.UserId, originalEvent.ResourceId);
                            break;
                        case "ENHANCED_MONITORING":
                            await EnableEnhancedMonitoringAsync(originalEvent.UserId);
                            break;
                    }
                }

                _logger.LogInformation($"Successfully applied {governanceResponse.ActionsRequired.Count} governance actions");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error applying governance actions for violation: {governanceResponse.ViolationId}");
                return false;
            }
        }

        // Governance action implementations
        private async Task BlockCopilotAccessAsync(string userId, string reason)
        {
            _logger.LogWarning($"GOVERNANCE ACTION: Blocking Copilot access for user {userId}, reason: {reason}");
            // TODO: Implement Copilot access blocking logic
            await Task.CompletedTask;
        }

        private async Task RestrictCopilotResponseAsync(string userId, string resourceId)
        {
            _logger.LogInformation($"GOVERNANCE ACTION: Restricting Copilot response for user {userId}, resource: {resourceId}");
            // TODO: Implement response restriction logic
            await Task.CompletedTask;
        }

        private async Task CreateApprovalWorkflowAsync(DlpViolationEvent dlpEvent)
        {
            _logger.LogInformation($"GOVERNANCE ACTION: Creating approval workflow for violation: {dlpEvent.ViolationId}");
            // TODO: Implement approval workflow creation
            await Task.CompletedTask;
        }

        private async Task NotifySecurityTeamAsync(DlpGovernanceResponse response, DlpViolationEvent dlpEvent)
        {
            _logger.LogWarning($"GOVERNANCE ACTION: Notifying security team of HIGH RISK violation: {response.ViolationId}");
            // TODO: Implement security team notification
            await Task.CompletedTask;
        }

        private async Task LogAccessAttemptAsync(DlpViolationEvent dlpEvent)
        {
            _logger.LogInformation($"GOVERNANCE ACTION: Logging access attempt for user {dlpEvent.UserId}, resource: {dlpEvent.ResourceId}");
            // TODO: Implement enhanced access logging
            await Task.CompletedTask;
        }

        private async Task ValidateUserPermissionsAsync(string userId, string resourceId)
        {
            _logger.LogInformation($"GOVERNANCE ACTION: Validating permissions for user {userId}, resource: {resourceId}");
            // TODO: Implement permission validation
            await Task.CompletedTask;
        }

        private async Task EnableEnhancedMonitoringAsync(string userId)
        {
            _logger.LogInformation($"GOVERNANCE ACTION: Enabling enhanced monitoring for user {userId}");
            // TODO: Implement enhanced user monitoring
            await Task.CompletedTask;
        }
    }

    // Supporting classes for DLP operations
    public class DlpPoliciesResponse
    {
        public List<DlpPolicy> Value { get; set; } = new List<DlpPolicy>();
    }

    public class DlpPolicy
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
        public int Priority { get; set; }
        public List<string> Locations { get; set; } = new List<string>();
        public List<DlpRule> Rules { get; set; } = new List<DlpRule>();
    }

    public class DlpRule
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Conditions { get; set; }
        public string Actions { get; set; }
    }

    public class DlpPolicyInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
        public int Priority { get; set; }
        public List<string> Locations { get; set; } = new List<string>();
        public List<DlpRuleInfo> Rules { get; set; } = new List<DlpRuleInfo>();
    }

    public class DlpRuleInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Conditions { get; set; }
        public string Actions { get; set; }
    }

    public class DlpViolationEvent
    {
        public string ViolationId { get; set; }
        public string UserId { get; set; }
        public string ResourceId { get; set; }
        public string Location { get; set; }
        public string SensitivityLabel { get; set; }
        public List<string> SensitiveInfoTypes { get; set; } = new List<string>();
        public bool IsHighRiskUser { get; set; }
        public bool IsExternalSharing { get; set; }
        public int UserViolationCount { get; set; }
        public DateTime ViolationTime { get; set; }
        public string PolicyId { get; set; }
        public string RuleId { get; set; }
    }

    public class DlpGovernanceResponse
    {
        public string ViolationId { get; set; }
        public DateTime ProcessedAt { get; set; }
        public List<string> ActionsRequired { get; set; } = new List<string>();
        public int RiskScore { get; set; }
        public string RiskLevel { get; set; }
    }
}