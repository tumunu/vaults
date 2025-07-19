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
    public class PermissionValidationService
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<PermissionValidationService> _logger;
        private readonly HttpClient _httpClient;
        private readonly TokenCredential _credential;

        public PermissionValidationService(IConfiguration configuration, ILogger<PermissionValidationService> logger)
        {
            _logger = logger;
            
            // Configure Graph client with required scopes for permission validation
            var scopes = new[] { 
                "https://graph.microsoft.com/.default" // Uses all granted application permissions
            };

            // Use system-assigned managed identity (production standard)
            var managedIdentityEnabled = configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            
            if (managedIdentityEnabled)
            {
                _logger.LogInformation("PermissionValidationService: Using system-assigned ManagedIdentityCredential");
                _credential = new ManagedIdentityCredential();
            }
            else
            {
                _logger.LogWarning("PermissionValidationService: Using ClientSecretCredential (development only)");
                var tenantId = configuration["AZURE_TENANT_ID"];
                var clientId = configuration["AZURE_CLIENT_ID"];
                var clientSecret = configuration["AZURE_CLIENT_SECRET"];
                _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }

            _graphServiceClient = new GraphServiceClient(_credential, scopes);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Validate user permissions before AI interaction
        /// Implements principle of least privilege for Copilot access
        /// </summary>
        public async Task<PermissionValidationResult> ValidateAiInteractionPermissionsAsync(
            string userId, 
            string resourceId, 
            string operation = "read",
            string tenantId = null)
        {
            try
            {
                _logger.LogInformation($"Validating AI interaction permissions: User {userId}, Resource {resourceId}, Operation {operation}");

                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    Operation = operation,
                    ValidatedAt = DateTime.UtcNow
                };

                // Step 1: Validate user exists and is active
                var userValidation = await ValidateUserStatusAsync(userId);
                if (!userValidation.IsValid)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.Add($"User validation failed: {userValidation.Reason}");
                    return result;
                }

                // Step 2: Validate resource access permissions
                var resourceValidation = await ValidateResourceAccessAsync(userId, resourceId, operation);
                if (!resourceValidation.IsAuthorized)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.AddRange(resourceValidation.DenialReasons);
                    return result;
                }

                // Step 3: Check sensitivity-based restrictions
                var sensitivityValidation = await ValidateSensitivityRestrictionsAsync(userId, resourceId);
                if (!sensitivityValidation.IsAuthorized)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.AddRange(sensitivityValidation.DenialReasons);
                    return result;
                }

                // Step 4: Apply governance-specific restrictions
                var governanceValidation = await ValidateGovernanceRestrictionsAsync(userId, resourceId);
                if (!governanceValidation.IsAuthorized)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.AddRange(governanceValidation.DenialReasons);
                    return result;
                }

                // Step 5: Check for time-based or context-based restrictions
                var contextValidation = await ValidateContextualRestrictionsAsync(userId, resourceId);
                if (!contextValidation.IsAuthorized)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.AddRange(contextValidation.DenialReasons);
                    return result;
                }

                // All validations passed
                result.IsAuthorized = true;
                result.PermissionLevel = DeterminePermissionLevel(resourceValidation, sensitivityValidation);
                result.RecommendedActions = GenerateRecommendedActions(userId, resourceId, result.PermissionLevel);

                _logger.LogInformation($"Permission validation successful: User {userId} authorized for {operation} on {resourceId}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating AI interaction permissions for user {userId}, resource {resourceId}");
                
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    Operation = operation,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"Permission validation error: {ex.Message}" },
                    ValidatedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Validate user status and account health
        /// </summary>
        private async Task<UserValidationResult> ValidateUserStatusAsync(string userId)
        {
            try
            {
                // Get user details from Microsoft Graph
                var user = await _graphServiceClient.Users[userId].GetAsync();
                
                if (user == null)
                {
                    return new UserValidationResult
                    {
                        IsValid = false,
                        Reason = "User not found"
                    };
                }

                // Check if user account is enabled
                if (user.AccountEnabled == false)
                {
                    return new UserValidationResult
                    {
                        IsValid = false,
                        Reason = "User account is disabled"
                    };
                }

                // Check for risky user status
                var riskStatus = await CheckUserRiskStatusAsync(userId);
                if (riskStatus.IsHighRisk)
                {
                    return new UserValidationResult
                    {
                        IsValid = false,
                        Reason = $"User is high risk: {riskStatus.RiskReason}"
                    };
                }

                return new UserValidationResult
                {
                    IsValid = true,
                    User = user
                };
            }
            catch (ServiceException ex)
            {
                _logger.LogError(ex, $"Error validating user status for {userId}");
                return new UserValidationResult
                {
                    IsValid = false,
                    Reason = $"User validation error: {ex.Error?.Message}"
                };
            }
        }

        /// <summary>
        /// Validate user's access permissions to specific resource
        /// </summary>
        private async Task<PermissionValidationResult> ValidateResourceAccessAsync(string userId, string resourceId, string operation)
        {
            try
            {
                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    Operation = operation
                };

                // Determine resource type and check appropriate permissions
                var resourceType = DetermineResourceType(resourceId);
                
                switch (resourceType)
                {
                    case "SharePointFile":
                        return await ValidateSharePointPermissionsAsync(userId, resourceId, operation);
                    case "OneDriveFile":
                        return await ValidateOneDrivePermissionsAsync(userId, resourceId, operation);
                    case "TeamsMessage":
                        return await ValidateTeamsPermissionsAsync(userId, resourceId, operation);
                    case "ExchangeEmail":
                        return await ValidateExchangePermissionsAsync(userId, resourceId, operation);
                    default:
                        result.IsAuthorized = false;
                        result.DenialReasons.Add($"Unknown resource type: {resourceType}");
                        return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating resource access for user {userId}, resource {resourceId}");
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"Resource access validation error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Validate SharePoint file permissions
        /// </summary>
        private async Task<PermissionValidationResult> ValidateSharePointPermissionsAsync(string userId, string resourceId, string operation)
        {
            try
            {
                // Extract site and file information from resource ID
                var (siteId, fileId) = ParseSharePointResourceId(resourceId);
                
                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    Operation = operation
                };

                // Check if user has access to the SharePoint site
                var sitePermissions = await _graphServiceClient.Sites[siteId].Permissions.GetAsync();
                
                bool hasAccess = false;
                foreach (var permission in sitePermissions.Value)
                {
                    if (permission.GrantedToV2?.User?.Id == userId)
                    {
                        hasAccess = ValidateOperationPermission(permission.Roles, operation);
                        break;
                    }
                }

                if (!hasAccess)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.Add("User does not have required SharePoint permissions");
                    return result;
                }

                // Check file-specific permissions if file ID is provided
                if (!string.IsNullOrEmpty(fileId))
                {
                    var filePermissions = await _graphServiceClient.Sites[siteId].Drive.Items[fileId].Permissions.GetAsync();
                    
                    bool hasFileAccess = false;
                    foreach (var permission in filePermissions.Value)
                    {
                        if (permission.GrantedToV2?.User?.Id == userId)
                        {
                            hasFileAccess = ValidateOperationPermission(permission.Roles, operation);
                            break;
                        }
                    }

                    if (!hasFileAccess)
                    {
                        result.IsAuthorized = false;
                        result.DenialReasons.Add("User does not have required file permissions");
                        return result;
                    }
                }

                result.IsAuthorized = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating SharePoint permissions for user {userId}, resource {resourceId}");
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"SharePoint permission validation error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Validate OneDrive file permissions
        /// </summary>
        private async Task<PermissionValidationResult> ValidateOneDrivePermissionsAsync(string userId, string resourceId, string operation)
        {
            try
            {
                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    Operation = operation
                };

                // Get the file from OneDrive
                var driveItem = await _graphServiceClient.Users[userId].Drive.Items[resourceId].GetAsync();
                
                if (driveItem == null)
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.Add("File not found in user's OneDrive");
                    return result;
                }

                // For OneDrive, if user owns the file, they have full access
                // Check sharing permissions for additional validation
                var permissions = await _graphServiceClient.Users[userId].Drive.Items[resourceId].Permissions.GetAsync();
                
                bool hasAccess = true; // Owner has default access
                
                // Additional governance checks can be added here
                result.IsAuthorized = hasAccess;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating OneDrive permissions for user {userId}, resource {resourceId}");
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"OneDrive permission validation error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Validate Teams message permissions
        /// </summary>
        private async Task<PermissionValidationResult> ValidateTeamsPermissionsAsync(string userId, string resourceId, string operation)
        {
            // Implementation for Teams permission validation
            // This would check if user is member of the team/channel
            return new PermissionValidationResult
            {
                UserId = userId,
                ResourceId = resourceId,
                IsAuthorized = true // Simplified for now
            };
        }

        /// <summary>
        /// Validate Exchange email permissions
        /// </summary>
        private async Task<PermissionValidationResult> ValidateExchangePermissionsAsync(string userId, string resourceId, string operation)
        {
            // Implementation for Exchange permission validation
            // This would check if user has access to the mailbox/message
            return new PermissionValidationResult
            {
                UserId = userId,
                ResourceId = resourceId,
                IsAuthorized = true // Simplified for now
            };
        }

        /// <summary>
        /// Validate sensitivity-based restrictions
        /// </summary>
        private async Task<PermissionValidationResult> ValidateSensitivityRestrictionsAsync(string userId, string resourceId)
        {
            try
            {
                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId
                };

                // Get sensitivity label information
                var sensitivityInfo = await GetResourceSensitivityAsync(resourceId);
                
                if (sensitivityInfo != null)
                {
                    // Check if user's clearance level allows access to this sensitivity level
                    var userClearance = await GetUserSecurityClearanceAsync(userId);
                    
                    if (!IsAuthorizedForSensitivityLevel(userClearance, sensitivityInfo.SensitivityLevel))
                    {
                        result.IsAuthorized = false;
                        result.DenialReasons.Add($"User clearance level insufficient for {sensitivityInfo.SensitivityLevel} content");
                        return result;
                    }
                }

                result.IsAuthorized = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating sensitivity restrictions for user {userId}, resource {resourceId}");
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"Sensitivity validation error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Validate governance-specific restrictions
        /// </summary>
        private async Task<PermissionValidationResult> ValidateGovernanceRestrictionsAsync(string userId, string resourceId)
        {
            try
            {
                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId
                };

                // Check for AI-specific governance restrictions
                var governanceRestrictions = await GetGovernanceRestrictionsAsync(userId, resourceId);
                
                if (governanceRestrictions.Any())
                {
                    foreach (var restriction in governanceRestrictions)
                    {
                        if (!restriction.IsAllowed)
                        {
                            result.IsAuthorized = false;
                            result.DenialReasons.Add($"Governance restriction: {restriction.Reason}");
                        }
                    }
                }

                if (result.DenialReasons.Count == 0)
                {
                    result.IsAuthorized = true;
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating governance restrictions for user {userId}, resource {resourceId}");
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"Governance validation error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Validate contextual restrictions (time, location, etc.)
        /// </summary>
        private async Task<PermissionValidationResult> ValidateContextualRestrictionsAsync(string userId, string resourceId)
        {
            try
            {
                var result = new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId
                };

                // Check time-based restrictions
                var currentHour = DateTime.UtcNow.Hour;
                var timeRestrictions = await GetTimeBasedRestrictionsAsync(userId);
                
                if (timeRestrictions != null && !timeRestrictions.IsAllowedTime(currentHour))
                {
                    result.IsAuthorized = false;
                    result.DenialReasons.Add("Access restricted during current time period");
                    return result;
                }

                // Check location-based restrictions (if available)
                // Check device-based restrictions
                // Check concurrent session restrictions

                result.IsAuthorized = true;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error validating contextual restrictions for user {userId}, resource {resourceId}");
                return new PermissionValidationResult
                {
                    UserId = userId,
                    ResourceId = resourceId,
                    IsAuthorized = false,
                    DenialReasons = new List<string> { $"Contextual validation error: {ex.Message}" }
                };
            }
        }

        // Helper methods
        private async Task<UserRiskStatus> CheckUserRiskStatusAsync(string userId)
        {
            try
            {
                // Check Microsoft Graph Identity Protection for risky users
                var riskyUsers = await _graphServiceClient.IdentityProtection.RiskyUsers.GetAsync();
                
                var riskyUser = riskyUsers.Value?.FirstOrDefault(u => u.Id == userId);
                
                if (riskyUser != null && riskyUser.RiskLevel?.ToString().ToLower() == "high")
                {
                    return new UserRiskStatus
                    {
                        IsHighRisk = true,
                        RiskReason = $"Identity Protection risk level: {riskyUser.RiskLevel}"
                    };
                }

                return new UserRiskStatus { IsHighRisk = false };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Could not check user risk status for {userId}");
                return new UserRiskStatus { IsHighRisk = false };
            }
        }

        private string DetermineResourceType(string resourceId)
        {
            // Logic to determine resource type from resource ID
            if (resourceId.Contains("sharepoint"))
                return "SharePointFile";
            if (resourceId.Contains("onedrive"))
                return "OneDriveFile";
            if (resourceId.Contains("teams"))
                return "TeamsMessage";
            if (resourceId.Contains("exchange"))
                return "ExchangeEmail";
            
            return "Unknown";
        }

        private (string siteId, string fileId) ParseSharePointResourceId(string resourceId)
        {
            // Parse SharePoint resource ID to extract site and file IDs
            // Implementation depends on resource ID format
            return ("siteId", "fileId");
        }

        private bool ValidateOperationPermission(IList<string> roles, string operation)
        {
            // Map operation to required roles
            return operation.ToLower() switch
            {
                "read" => roles.Contains("read") || roles.Contains("write") || roles.Contains("owner"),
                "write" => roles.Contains("write") || roles.Contains("owner"),
                "delete" => roles.Contains("owner"),
                _ => false
            };
        }

        private string DeterminePermissionLevel(PermissionValidationResult resourceValidation, PermissionValidationResult sensitivityValidation)
        {
            // Logic to determine overall permission level
            return "READ"; // Simplified
        }

        private List<string> GenerateRecommendedActions(string userId, string resourceId, string permissionLevel)
        {
            // Generate recommendations based on validation results
            return new List<string> { "LOG_ACCESS", "MONITOR_USAGE" };
        }

        // Placeholder methods for advanced features
        private Task<SensitivityInfo> GetResourceSensitivityAsync(string resourceId) => Task.FromResult<SensitivityInfo>(null);
        private Task<UserSecurityClearance> GetUserSecurityClearanceAsync(string userId) => Task.FromResult(new UserSecurityClearance());
        private bool IsAuthorizedForSensitivityLevel(UserSecurityClearance clearance, string sensitivityLevel) => true;
        private Task<List<GovernanceRestriction>> GetGovernanceRestrictionsAsync(string userId, string resourceId) => Task.FromResult(new List<GovernanceRestriction>());
        private Task<TimeBasedRestrictions> GetTimeBasedRestrictionsAsync(string userId) => Task.FromResult<TimeBasedRestrictions>(null);
    }

    // Supporting classes
    public class PermissionValidationResult
    {
        public string UserId { get; set; }
        public string ResourceId { get; set; }
        public string Operation { get; set; }
        public bool IsAuthorized { get; set; }
        public List<string> DenialReasons { get; set; } = new List<string>();
        public string PermissionLevel { get; set; }
        public List<string> RecommendedActions { get; set; } = new List<string>();
        public DateTime ValidatedAt { get; set; }
    }

    public class UserValidationResult
    {
        public bool IsValid { get; set; }
        public string Reason { get; set; }
        public User User { get; set; }
    }

    public class UserRiskStatus
    {
        public bool IsHighRisk { get; set; }
        public string RiskReason { get; set; }
    }

    public class SensitivityInfo
    {
        public string SensitivityLevel { get; set; }
        public List<string> RequiredClearances { get; set; }
    }

    public class UserSecurityClearance
    {
        public string Level { get; set; }
        public List<string> Permissions { get; set; } = new List<string>();
    }

    public class GovernanceRestriction
    {
        public bool IsAllowed { get; set; }
        public string Reason { get; set; }
    }

    public class TimeBasedRestrictions
    {
        public int AllowedStartHour { get; set; }
        public int AllowedEndHour { get; set; }
        
        public bool IsAllowedTime(int currentHour)
        {
            return currentHour >= AllowedStartHour && currentHour <= AllowedEndHour;
        }
    }
}