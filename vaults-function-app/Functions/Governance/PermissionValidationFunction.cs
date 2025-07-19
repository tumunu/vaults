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
    public class PermissionValidationFunction
    {
        private readonly ILogger<PermissionValidationFunction> _logger;
        private readonly PermissionValidationService _permissionValidationService;
        private readonly IConfiguration _configuration;

        public PermissionValidationFunction(
            ILogger<PermissionValidationFunction> logger,
            PermissionValidationService permissionValidationService,
            IConfiguration configuration)
        {
            _logger = logger;
            _permissionValidationService = permissionValidationService;
            _configuration = configuration;
        }

        /// <summary>
        /// Validate user permissions before AI interaction
        /// POST /api/governance/permissions/validate
        /// </summary>
        [Function("ValidateAiPermissions")]
        public async Task<HttpResponseData> ValidateAiPermissions(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/permissions/validate")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing AI permission validation request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var validationRequest = JsonConvert.DeserializeObject<PermissionValidationRequest>(requestBody);

                if (validationRequest == null || string.IsNullOrEmpty(validationRequest.UserId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid validation request",
                        message = "UserId is required"
                    }));
                    return response;
                }

                // Validate permissions
                var validationResult = await _permissionValidationService.ValidateAiInteractionPermissionsAsync(
                    validationRequest.UserId,
                    validationRequest.ResourceId,
                    validationRequest.Operation ?? "read",
                    validationRequest.TenantId
                );

                // Format response
                var responseData = new
                {
                    validationId = Guid.NewGuid().ToString(),
                    request = new
                    {
                        userId = validationRequest.UserId,
                        resourceId = validationRequest.ResourceId,
                        operation = validationRequest.Operation ?? "read",
                        tenantId = validationRequest.TenantId ?? "default-tenant"
                    },
                    validation = new
                    {
                        isAuthorized = validationResult.IsAuthorized,
                        permissionLevel = validationResult.PermissionLevel,
                        denialReasons = validationResult.DenialReasons,
                        recommendedActions = validationResult.RecommendedActions,
                        validatedAt = validationResult.ValidatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    },
                    governance = new
                    {
                        principleOfLeastPrivilege = true,
                        realTimeValidation = true,
                        contextualRestrictions = true,
                        sensitivityAware = true
                    },
                    copilotImpact = new
                    {
                        allowInteraction = validationResult.IsAuthorized,
                        restrictedResponse = !validationResult.IsAuthorized || validationResult.PermissionLevel == "RESTRICTED",
                        requiresApproval = validationResult.RecommendedActions?.Contains("REQUIRE_APPROVAL") == true,
                        enhancedLogging = validationResult.RecommendedActions?.Contains("LOG_ACCESS") == true
                    }
                };

                response.StatusCode = validationResult.IsAuthorized ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Permission validation completed: User {validationRequest.UserId}, Authorized: {validationResult.IsAuthorized}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing permission validation request");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Permission validation failed",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Batch validate permissions for multiple resources
        /// POST /api/governance/permissions/validate-batch
        /// </summary>
        [Function("ValidateAiPermissionsBatch")]
        public async Task<HttpResponseData> ValidateAiPermissionsBatch(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/permissions/validate-batch")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing batch AI permission validation request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var batchRequest = JsonConvert.DeserializeObject<BatchPermissionValidationRequest>(requestBody);

                if (batchRequest?.Requests == null || !batchRequest.Requests.Any())
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid batch validation request",
                        message = "At least one validation request is required"
                    }));
                    return response;
                }

                var batchResults = new List<object>();
                var batchId = Guid.NewGuid().ToString();

                // Process each validation request
                foreach (var validationRequest in batchRequest.Requests)
                {
                    try
                    {
                        var validationResult = await _permissionValidationService.ValidateAiInteractionPermissionsAsync(
                            validationRequest.UserId,
                            validationRequest.ResourceId,
                            validationRequest.Operation ?? "read",
                            validationRequest.TenantId
                        );

                        batchResults.Add(new
                        {
                            userId = validationRequest.UserId,
                            resourceId = validationRequest.ResourceId,
                            operation = validationRequest.Operation ?? "read",
                            isAuthorized = validationResult.IsAuthorized,
                            permissionLevel = validationResult.PermissionLevel,
                            denialReasons = validationResult.DenialReasons,
                            recommendedActions = validationResult.RecommendedActions,
                            validatedAt = validationResult.ValidatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error validating permissions for user {validationRequest.UserId}, resource {validationRequest.ResourceId}");
                        
                        batchResults.Add(new
                        {
                            userId = validationRequest.UserId,
                            resourceId = validationRequest.ResourceId,
                            operation = validationRequest.Operation ?? "read",
                            isAuthorized = false,
                            error = ex.Message,
                            validatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                        });
                    }
                }

                // Calculate batch statistics
                var authorizedCount = batchResults.Count(r => r.GetType().GetProperty("isAuthorized")?.GetValue(r) as bool? == true);
                var deniedCount = batchResults.Count - authorizedCount;

                var responseData = new
                {
                    batchId,
                    processedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    summary = new
                    {
                        totalRequests = batchResults.Count,
                        authorized = authorizedCount,
                        denied = deniedCount,
                        successRate = Math.Round((double)authorizedCount / batchResults.Count * 100, 2)
                    },
                    results = batchResults,
                    governance = new
                    {
                        batchProcessing = true,
                        consistentPolicyEnforcement = true,
                        auditTrail = true
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Batch permission validation completed: {authorizedCount}/{batchResults.Count} authorized");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch permission validation request");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Batch permission validation failed",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Get user's permission summary for governance dashboard
        /// GET /api/governance/permissions/user-summary
        /// </summary>
        [Function("GetUserPermissionSummary")]
        public async Task<HttpResponseData> GetUserPermissionSummary(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/permissions/user-summary")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing user permission summary request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var userId = query["userId"];
                var tenantId = query["tenantId"] ?? "default-tenant";

                if (string.IsNullOrEmpty(userId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid request",
                        message = "UserId query parameter is required"
                    }));
                    return response;
                }

                // Generate permission summary (this would typically query historical data)
                var permissionSummary = await GenerateUserPermissionSummaryAsync(userId, tenantId);

                var responseData = new
                {
                    userId,
                    tenantId,
                    generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    summary = permissionSummary,
                    governance = new
                    {
                        permissionAnalytics = true,
                        riskAssessment = true,
                        accessOptimization = true,
                        complianceMonitoring = true
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"User permission summary generated for user {userId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating user permission summary");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to generate permission summary",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Pre-authorize AI interaction (for improved performance)
        /// POST /api/governance/permissions/pre-authorize
        /// </summary>
        [Function("PreAuthorizeAiInteraction")]
        public async Task<HttpResponseData> PreAuthorizeAiInteraction(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/permissions/pre-authorize")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing AI interaction pre-authorization request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var preAuthRequest = JsonConvert.DeserializeObject<PreAuthorizationRequest>(requestBody);

                if (preAuthRequest == null || string.IsNullOrEmpty(preAuthRequest.UserId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid pre-authorization request",
                        message = "UserId is required"
                    }));
                    return response;
                }

                // Generate pre-authorization token
                var preAuthResult = await GeneratePreAuthorizationAsync(preAuthRequest);

                var responseData = new
                {
                    preAuthId = preAuthResult.PreAuthId,
                    userId = preAuthRequest.UserId,
                    tenantId = preAuthRequest.TenantId ?? "default-tenant",
                    authorization = new
                    {
                        token = preAuthResult.Token,
                        expiresAt = preAuthResult.ExpiresAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        maxInteractions = preAuthResult.MaxInteractions,
                        allowedOperations = preAuthResult.AllowedOperations
                    },
                    governance = new
                    {
                        preAuthorizationEnabled = true,
                        tokenBasedValidation = true,
                        sessionManagement = true,
                        auditTrail = true
                    },
                    usage = new
                    {
                        includeTokenInRequests = true,
                        validateBeforeInteraction = true,
                        monitorUsage = true
                    }
                };

                response.StatusCode = HttpStatusCode.Created;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Pre-authorization created for user {preAuthRequest.UserId}: {preAuthResult.PreAuthId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing pre-authorization request");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Pre-authorization failed",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Generate user permission summary
        /// </summary>
        private async Task<object> GenerateUserPermissionSummaryAsync(string userId, string tenantId)
        {
            // This would typically query historical permission validation data
            return new
            {
                userDetails = new
                {
                    userId,
                    displayName = "User Display Name", // Would be fetched from Graph
                    department = "IT Department",
                    securityClearance = "Standard"
                },
                permissionStats = new
                {
                    totalValidationsLast30Days = 125,
                    authorizedRequests = 118,
                    deniedRequests = 7,
                    authorizationRate = 94.4
                },
                riskProfile = new
                {
                    riskLevel = "LOW",
                    riskScore = 15,
                    lastRiskAssessment = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ")
                },
                accessPatterns = new
                {
                    mostAccessedResourceTypes = new[] { "SharePoint", "OneDrive", "Teams" },
                    peakAccessHours = new[] { 9, 10, 14, 15 },
                    averageResourcesPerSession = 3.2
                },
                governanceActions = new
                {
                    activeRestrictions = 0,
                    approvalRequests = 2,
                    enhancedMonitoring = false
                }
            };
        }

        /// <summary>
        /// Generate pre-authorization for AI interactions
        /// </summary>
        private async Task<PreAuthorizationResult> GeneratePreAuthorizationAsync(PreAuthorizationRequest request)
        {
            // Generate secure pre-authorization token
            var preAuthId = Guid.NewGuid().ToString();
            var token = GenerateSecureToken();
            
            return new PreAuthorizationResult
            {
                PreAuthId = preAuthId,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddHours(request.DurationHours ?? 1),
                MaxInteractions = request.MaxInteractions ?? 100,
                AllowedOperations = request.AllowedOperations ?? new List<string> { "read" }
            };
        }

        /// <summary>
        /// Generate secure token for pre-authorization
        /// </summary>
        private string GenerateSecureToken()
        {
            // Generate cryptographically secure token
            return Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        }
    }

    // Supporting classes for permission validation functions
    public class PermissionValidationRequest
    {
        public string UserId { get; set; }
        public string ResourceId { get; set; }
        public string Operation { get; set; }
        public string TenantId { get; set; }
    }

    public class BatchPermissionValidationRequest
    {
        public List<PermissionValidationRequest> Requests { get; set; } = new List<PermissionValidationRequest>();
    }

    public class PreAuthorizationRequest
    {
        public string UserId { get; set; }
        public string TenantId { get; set; }
        public int? DurationHours { get; set; }
        public int? MaxInteractions { get; set; }
        public List<string> AllowedOperations { get; set; }
    }

    public class PreAuthorizationResult
    {
        public string PreAuthId { get; set; }
        public string Token { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int MaxInteractions { get; set; }
        public List<string> AllowedOperations { get; set; }
    }
}