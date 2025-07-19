using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Middleware;
using VaultsFunctions.Core.Attributes;
using System.Net;
using Newtonsoft.Json;

namespace VaultsFunctions.Functions.Copilot
{
    public class CopilotSecurityFunction
    {
        private readonly ILogger<CopilotSecurityFunction> _logger;
        private readonly GraphCopilotService _graphCopilotService;
        private readonly IConfiguration _configuration;

        public CopilotSecurityFunction(
            ILogger<CopilotSecurityFunction> logger,
            GraphCopilotService graphCopilotService,
            IConfiguration configuration)
        {
            _logger = logger;
            _graphCopilotService = graphCopilotService;
            _configuration = configuration;
        }

        [Function("CopilotSecurityAlerts")]
        public async Task<HttpResponseData> GetSecurityAlerts(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/copilot/security/alerts")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot security alerts request");

            try
            {
                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = "default-tenant"; // Function-level auth - use default tenant
                var severity = query["severity"]; // Optional filter by severity (low/medium/high)
                var alertType = query["alertType"]; // Optional filter by alert type
                var timeWindow = query["timeWindow"] ?? "7d"; // Default to 7 days (1d, 7d, 30d, 90d)
                var topStr = query["$top"] ?? query["top"] ?? "20"; // Pagination - number of items to return
                var skipStr = query["$skip"] ?? query["skip"] ?? "0"; // Pagination - number of items to skip

                // Validate time window parameter
                var validTimeWindows = new[] { "1d", "7d", "30d", "90d" };
                if (!Array.Exists(validTimeWindows, tw => tw.Equals(timeWindow, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Invalid timeWindow parameter: {TimeWindow}", timeWindow);
                    return await req.CreateBadRequestResponseAsync($"Invalid timeWindow. Supported values: {string.Join(", ", validTimeWindows)}");
                }

                // Validate severity parameter if provided
                if (!string.IsNullOrEmpty(severity))
                {
                    var validSeverities = new[] { "low", "medium", "high", "critical" };
                    if (!Array.Exists(validSeverities, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogWarning("Invalid severity parameter: {Severity}", severity);
                        return await req.CreateBadRequestResponseAsync($"Invalid severity. Supported values: {string.Join(", ", validSeverities)}");
                    }
                }

                // Validate and parse pagination parameters
                if (!int.TryParse(topStr, out var top) || top < 1 || top > 100)
                {
                    _logger.LogWarning("Invalid $top parameter: {Top}", topStr);
                    return await req.CreateBadRequestResponseAsync("Invalid $top parameter. Must be between 1 and 100.");
                }

                if (!int.TryParse(skipStr, out var skip) || skip < 0)
                {
                    _logger.LogWarning("Invalid $skip parameter: {Skip}", skipStr);
                    return await req.CreateBadRequestResponseAsync("Invalid $skip parameter. Must be 0 or greater.");
                }

                _logger.LogInformation("Fetching security alerts for tenant {TenantId}, timeWindow: {TimeWindow}, severity: {Severity}, alertType: {AlertType}, top: {Top}, skip: {Skip}", 
                    tenantId, timeWindow, severity, alertType, top, skip);

                // Get security alerts from Microsoft Graph
                var alerts = await _graphCopilotService.GetRecentAlertsAsync(tenantId);

                if (alerts == null)
                {
                    _logger.LogWarning("No security alerts data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No security alerts available");
                }

                // Add tenant context and metadata with pagination
                var response = new
                {
                    tenantId = tenantId,
                    retrievedAt = DateTimeOffset.UtcNow,
                    filters = new
                    {
                        severity = severity,
                        alertType = alertType,
                        timeWindow = timeWindow
                    },
                    pagination = new
                    {
                        top = top,
                        skip = skip,
                        hasMore = true, // This would be determined by actual data count
                        nextLink = skip + top < 1000 ? $"/api/v1/copilot/security/alerts?$top={top}&$skip={skip + top}" : null
                    },
                    data = alerts,
                    metadata = new
                    {
                        source = "Microsoft Graph Security API",
                        endpoint = "security/alerts_v2",
                        permissions = "SecurityEvents.Read.All",
                        description = "Security alerts related to Microsoft 365 Copilot usage",
                        apiVersion = "v1"
                    }
                };

                _logger.LogInformation("Successfully retrieved security alerts for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to security alerts");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving security alerts");
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to retrieve security alerts");
            }
        }

        [Function("CopilotRiskyUsers")]
        public async Task<HttpResponseData> GetRiskyUsers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/copilot/security/risky-users")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot risky users request");

            try
            {
                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = "default-tenant"; // Function-level auth - use default tenant
                var riskLevel = query["riskLevel"]; // Optional filter by risk level (low/medium/high)
                var timeWindow = query["timeWindow"] ?? "30d"; // Default to 30 days
                var topStr = query["$top"] ?? query["top"] ?? "20"; // Pagination - number of items to return
                var skipStr = query["$skip"] ?? query["skip"] ?? "0"; // Pagination - number of items to skip

                // Validate risk level parameter if provided
                if (!string.IsNullOrEmpty(riskLevel))
                {
                    var validRiskLevels = new[] { "low", "medium", "high", "hidden" };
                    if (!Array.Exists(validRiskLevels, rl => rl.Equals(riskLevel, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogWarning("Invalid riskLevel parameter: {RiskLevel}", riskLevel);
                        return await req.CreateBadRequestResponseAsync($"Invalid riskLevel. Supported values: {string.Join(", ", validRiskLevels)}");
                    }
                }

                // Validate time window parameter
                var validTimeWindows = new[] { "7d", "30d", "90d", "180d" };
                if (!Array.Exists(validTimeWindows, tw => tw.Equals(timeWindow, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Invalid timeWindow parameter: {TimeWindow}", timeWindow);
                    return await req.CreateBadRequestResponseAsync($"Invalid timeWindow. Supported values: {string.Join(", ", validTimeWindows)}");
                }

                // Validate and parse pagination parameters
                if (!int.TryParse(topStr, out var top) || top < 1 || top > 100)
                {
                    _logger.LogWarning("Invalid $top parameter: {Top}", topStr);
                    return await req.CreateBadRequestResponseAsync("Invalid $top parameter. Must be between 1 and 100.");
                }

                if (!int.TryParse(skipStr, out var skip) || skip < 0)
                {
                    _logger.LogWarning("Invalid $skip parameter: {Skip}", skipStr);
                    return await req.CreateBadRequestResponseAsync("Invalid $skip parameter. Must be 0 or greater.");
                }

                _logger.LogInformation("Fetching risky users for tenant {TenantId}, riskLevel: {RiskLevel}, timeWindow: {TimeWindow}, top: {Top}, skip: {Skip}", 
                    tenantId, riskLevel, timeWindow, top, skip);

                // Get risky users from Microsoft Graph
                var riskyUsers = await _graphCopilotService.GetHighRiskUsersAsync(tenantId);

                if (riskyUsers == null)
                {
                    _logger.LogWarning("No risky users data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No risky users data available");
                }

                // Add tenant context and metadata with pagination
                var response = new
                {
                    tenantId = tenantId,
                    retrievedAt = DateTimeOffset.UtcNow,
                    filters = new
                    {
                        riskLevel = riskLevel,
                        timeWindow = timeWindow
                    },
                    pagination = new
                    {
                        top = top,
                        skip = skip,
                        hasMore = true, // This would be determined by actual data count
                        nextLink = skip + top < 1000 ? $"/api/v1/copilot/security/risky-users?$top={top}&$skip={skip + top}" : null
                    },
                    data = riskyUsers,
                    metadata = new
                    {
                        source = "Microsoft Graph Identity Protection API",
                        endpoint = "identityProtection/riskyUsers",
                        permissions = "IdentityRiskEvent.Read.All",
                        description = "Users identified as high-risk in the context of Microsoft 365 Copilot usage",
                        apiVersion = "v1"
                    }
                };

                _logger.LogInformation("Successfully retrieved risky users for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to risky users");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving risky users");
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to retrieve risky users");
            }
        }

        [Function("CopilotComplianceViolations")]
        public async Task<HttpResponseData> GetComplianceViolations(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/copilot/compliance/violations")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot compliance violations request");

            try
            {
                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = "default-tenant"; // Function-level auth - use default tenant
                var severity = query["severity"]; // Optional filter by severity (low/medium/high/critical)
                var violationType = query["violationType"]; // Optional filter by violation type
                var timeWindow = query["timeWindow"] ?? "30d"; // Default to 30 days
                var topStr = query["$top"] ?? query["top"] ?? "20"; // Pagination - number of items to return
                var skipStr = query["$skip"] ?? query["skip"] ?? "0"; // Pagination - number of items to skip

                // Validate severity parameter if provided
                if (!string.IsNullOrEmpty(severity))
                {
                    var validSeverities = new[] { "low", "medium", "high", "critical" };
                    if (!Array.Exists(validSeverities, s => s.Equals(severity, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogWarning("Invalid severity parameter: {Severity}", severity);
                        return await req.CreateBadRequestResponseAsync($"Invalid severity. Supported values: {string.Join(", ", validSeverities)}");
                    }
                }

                // Validate time window parameter
                var validTimeWindows = new[] { "7d", "30d", "90d", "180d" };
                if (!Array.Exists(validTimeWindows, tw => tw.Equals(timeWindow, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Invalid timeWindow parameter: {TimeWindow}", timeWindow);
                    return await req.CreateBadRequestResponseAsync($"Invalid timeWindow. Supported values: {string.Join(", ", validTimeWindows)}");
                }

                // Validate and parse pagination parameters
                if (!int.TryParse(topStr, out var top) || top < 1 || top > 100)
                {
                    _logger.LogWarning("Invalid $top parameter: {Top}", topStr);
                    return await req.CreateBadRequestResponseAsync("Invalid $top parameter. Must be between 1 and 100.");
                }

                if (!int.TryParse(skipStr, out var skip) || skip < 0)
                {
                    _logger.LogWarning("Invalid $skip parameter: {Skip}", skipStr);
                    return await req.CreateBadRequestResponseAsync("Invalid $skip parameter. Must be 0 or greater.");
                }

                _logger.LogInformation("Fetching compliance violations for tenant {TenantId}, severity: {Severity}, violationType: {ViolationType}, timeWindow: {TimeWindow}, top: {Top}, skip: {Skip}", 
                    tenantId, severity, violationType, timeWindow, top, skip);

                // Get compliance violations from Microsoft Graph
                var violations = await _graphCopilotService.GetPolicyViolationsAsync(tenantId);

                if (violations == null)
                {
                    _logger.LogWarning("No compliance violations data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No compliance violations data available");
                }

                // Add tenant context and metadata with pagination
                var response = new
                {
                    tenantId = tenantId,
                    retrievedAt = DateTimeOffset.UtcNow,
                    filters = new
                    {
                        severity = severity,
                        violationType = violationType,
                        timeWindow = timeWindow
                    },
                    pagination = new
                    {
                        top = top,
                        skip = skip,
                        hasMore = true, // This would be determined by actual data count
                        nextLink = skip + top < 1000 ? $"/api/v1/copilot/compliance/violations?$top={top}&$skip={skip + top}" : null
                    },
                    data = violations,
                    metadata = new
                    {
                        source = "Microsoft Graph Compliance API",
                        endpoint = "compliance/complianceManagementPartner",
                        permissions = "InformationProtectionPolicy.Read.All",
                        description = "Policy violations and compliance issues related to Microsoft 365 Copilot usage",
                        apiVersion = "v1"
                    }
                };

                _logger.LogInformation("Successfully retrieved compliance violations for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to compliance violations");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving compliance violations");
                return await req.CreateInternalServerErrorResponseAsync("Failed to retrieve compliance violations", ex);
            }
        }
    }
}