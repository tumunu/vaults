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
    public class CopilotUsageSummaryFunction
    {
        private readonly ILogger<CopilotUsageSummaryFunction> _logger;
        private readonly GraphCopilotService _graphCopilotService;
        private readonly IConfiguration _configuration;

        public CopilotUsageSummaryFunction(
            ILogger<CopilotUsageSummaryFunction> logger,
            GraphCopilotService graphCopilotService,
            IConfiguration configuration)
        {
            _logger = logger;
            _graphCopilotService = graphCopilotService;
            _configuration = configuration;
        }

        [Function("CopilotUsageSummary")]
        public async Task<HttpResponseData> GetCopilotUsageSummary(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "copilot/usage/summary")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot usage summary request");

            try
            {
                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var period = query["period"] ?? "D7"; // Default to 7 days
                var tenantId = "default-tenant";

                _logger.LogInformation("Fetching Copilot usage summary for tenant {TenantId}, period {Period}", tenantId, period);

                // Validate period parameter
                var validPeriods = new[] { "D7", "D30", "D90", "D180" };
                if (!Array.Exists(validPeriods, p => p.Equals(period, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Invalid period parameter: {Period}", period);
                    return await req.CreateBadRequestResponseAsync($"Invalid period. Supported values: {string.Join(", ", validPeriods)}");
                }

                // Get usage summary from Microsoft Graph
                var usageSummary = await _graphCopilotService.GetCopilotUsageSummaryAsync(period);

                if (usageSummary == null)
                {
                    _logger.LogWarning("No usage summary data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No usage data available for the specified period");
                }

                // Add tenant context and metadata
                var response = new
                {
                    tenantId = tenantId,
                    period = period,
                    retrievedAt = DateTimeOffset.UtcNow,
                    data = usageSummary,
                    metadata = new
                    {
                        source = "Microsoft Graph Reports API",
                        endpoint = $"reports/getCopilotUsageUserSummary(period='{period}')",
                        permissions = "Reports.Read.All"
                    }
                };

                _logger.LogInformation("Successfully retrieved Copilot usage summary for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to Copilot usage summary");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Copilot usage summary");
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to retrieve usage summary");
            }
        }

        [Function("CopilotUserCount")]
        public async Task<HttpResponseData> GetCopilotUserCount(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "copilot/users/count")] HttpRequestData req)
        {
            _logger.LogInformation("Processing Copilot user count request");

            try
            {
                // Extract query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var period = query["period"] ?? "D7"; // Default to 7 days
                var tenantId = "default-tenant";

                _logger.LogInformation("Fetching Copilot user count for tenant {TenantId}, period {Period}", tenantId, period);

                // Validate period parameter
                var validPeriods = new[] { "D7", "D30", "D90", "D180" };
                if (!Array.Exists(validPeriods, p => p.Equals(period, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning("Invalid period parameter: {Period}", period);
                    return await req.CreateBadRequestResponseAsync($"Invalid period. Supported values: {string.Join(", ", validPeriods)}");
                }

                // Get user count from Microsoft Graph
                var userCount = await _graphCopilotService.GetCopilotUserCountAsync(period);

                if (userCount == null)
                {
                    _logger.LogWarning("No user count data available for tenant {TenantId}", tenantId);
                    return await req.CreateNotFoundResponseAsync("No user count data available for the specified period");
                }

                // Add tenant context and metadata
                var response = new
                {
                    tenantId = tenantId,
                    period = period,
                    retrievedAt = DateTimeOffset.UtcNow,
                    data = userCount,
                    metadata = new
                    {
                        source = "Microsoft Graph Reports API",
                        endpoint = $"reports/getCopilotUserCountSummary(period='{period}')",
                        permissions = "Reports.Read.All"
                    }
                };

                _logger.LogInformation("Successfully retrieved Copilot user count for tenant {TenantId}", tenantId);

                var httpResponse = req.CreateResponse(HttpStatusCode.OK);
                httpResponse.Headers.Add("Content-Type", "application/json");
                await httpResponse.WriteStringAsync(JsonConvert.SerializeObject(response, Formatting.Indented));

                return httpResponse;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Unauthorized access to Copilot user count");
                return await req.CreateUnauthorizedResponseAsync("Access denied. Check Microsoft Graph permissions.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Copilot user count");
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, "Failed to retrieve user count");
            }
        }
    }
}