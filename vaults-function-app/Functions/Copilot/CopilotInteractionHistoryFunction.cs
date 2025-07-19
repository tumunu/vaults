using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Middleware;
using System.Net;
using Newtonsoft.Json;

namespace VaultsFunctions.Functions.Copilot
{
    public class CopilotInteractionHistoryFunction
    {
        private readonly ILogger<CopilotInteractionHistoryFunction> _logger;
        private readonly GraphCopilotService _graphCopilotService;
        private readonly IConfiguration _configuration;

        public CopilotInteractionHistoryFunction(
            ILogger<CopilotInteractionHistoryFunction> logger,
            GraphCopilotService graphCopilotService,
            IConfiguration configuration)
        {
            _logger = logger;
            _graphCopilotService = graphCopilotService;
            _configuration = configuration;
        }

        [Function("CopilotInteractionHistory")]
        public async Task<HttpResponseData> GetInteractionHistory(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "copilot/interactions/history")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot interaction history request");

            try
            {
                // Apply authentication and tenant validation middleware
                var authResult = await AuthenticationMiddleware.ValidateRequestAsync(req, _configuration, _logger);
                if (!authResult.IsValid)
                {
                    return await req.CreateUnauthorizedResponseAsync(authResult.ErrorMessage);
                }

                // Validate required scopes for interaction history
                var requiredScopes = new[] { "Vaults.ReadUsage", "Vaults.Admin" };
                if (!AuthenticationMiddleware.HasRequiredScope(authResult.Claims, requiredScopes))
                {
                    _logger.LogWarning("Insufficient permissions for interaction history access");
                    return await req.CreateForbiddenResponseAsync("Insufficient permissions. Required scope: Vaults.ReadUsage");
                }

                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = authResult.TenantId;
                var topStr = query["top"];
                var filter = query["filter"];
                var userId = query["userId"]; // Optional filter by user

                // Validate and parse top parameter
                int? top = null;
                if (!string.IsNullOrEmpty(topStr))
                {
                    if (int.TryParse(topStr, out var topValue) && topValue > 0 && topValue <= 1000)
                    {
                        top = topValue;
                    }
                    else
                    {
                        _logger.LogWarning("Invalid top parameter: {Top}", topStr);
                        return await req.CreateBadRequestResponseAsync("Invalid 'top' parameter. Must be between 1 and 1000.");
                    }
                }

                _logger.LogInformation("Fetching interaction history for tenant {TenantId}, top: {Top}, filter: {Filter}", 
                    tenantId, top, filter);

                // Build filter if userId is provided
                var graphFilter = filter;
                if (!string.IsNullOrEmpty(userId))
                {
                    var userFilter = $"userId eq '{userId}'";
                    graphFilter = string.IsNullOrEmpty(graphFilter) ? userFilter : $"{graphFilter} and {userFilter}";
                }

                // Get interaction history from Microsoft Graph
                var interactionHistory = await _graphCopilotService.GetInteractionHistoryAsync(tenantId, top, graphFilter);

                if (interactionHistory == null)
                {
                    _logger.LogWarning("No interaction history data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No interaction history available");
                }

                // Add tenant context and metadata
                var response = new
                {
                    tenantId = tenantId,
                    retrievedAt = DateTimeOffset.UtcNow,
                    parameters = new
                    {
                        top = top,
                        filter = graphFilter,
                        userId = userId
                    },
                    data = interactionHistory,
                    metadata = new
                    {
                        source = "Microsoft Graph Copilot API",
                        endpoint = "copilot/users/getAllEnterpriseInteractions",
                        permissions = "AiEnterpriseInteraction.Read.All",
                        description = "Enterprise-wide Microsoft 365 Copilot interaction history and usage patterns"
                    }
                };

                _logger.LogInformation("Successfully retrieved interaction history for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to interaction history");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving interaction history");
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to retrieve interaction history");
            }
        }

        [Function("CopilotUsers")]
        public async Task<HttpResponseData> GetCopilotUsers(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "copilot/users")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot users request");

            try
            {
                // Apply authentication and tenant validation middleware
                var authResult = await AuthenticationMiddleware.ValidateRequestAsync(req, _configuration, _logger);
                if (!authResult.IsValid)
                {
                    return await req.CreateUnauthorizedResponseAsync(authResult.ErrorMessage);
                }

                // Validate required scopes for user data
                var requiredScopes = new[] { "Vaults.ReadUsage", "Vaults.Admin" };
                if (!AuthenticationMiddleware.HasRequiredScope(authResult.Claims, requiredScopes))
                {
                    _logger.LogWarning("Insufficient permissions for Copilot users access");
                    return await req.CreateForbiddenResponseAsync("Insufficient permissions. Required scope: Vaults.ReadUsage");
                }

                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = authResult.TenantId;
                var includeUsage = query["includeUsage"] == "true"; // Optional include usage details

                _logger.LogInformation("Fetching Copilot users for tenant {TenantId}, includeUsage: {IncludeUsage}", 
                    tenantId, includeUsage);

                // Get Copilot users from Microsoft Graph
                var copilotUsers = await _graphCopilotService.GetCopilotUsersAsync(tenantId);

                if (copilotUsers == null)
                {
                    _logger.LogWarning("No Copilot users data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No Copilot users data available");
                }

                // Add tenant context and metadata
                var response = new
                {
                    tenantId = tenantId,
                    retrievedAt = DateTimeOffset.UtcNow,
                    parameters = new
                    {
                        includeUsage = includeUsage
                    },
                    data = copilotUsers,
                    metadata = new
                    {
                        source = "Microsoft Graph Reports API",
                        endpoint = "reports/getCopilotUsageUserDetail(period='D7')",
                        permissions = "Reports.Read.All",
                        description = "Detailed information about users who have interacted with Microsoft 365 Copilot"
                    }
                };

                _logger.LogInformation("Successfully retrieved Copilot users for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to Copilot users");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Copilot users");
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to retrieve Copilot users");
            }
        }
    }
}