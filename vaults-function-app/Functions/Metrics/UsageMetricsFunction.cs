using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Helpers;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Configuration;

namespace VaultsFunctions.Functions.Metrics
{
    public class UsageMetricsFunction
    {
        private readonly ITenantStatsService _statsService;
        private readonly ILogger<UsageMetricsFunction> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly IConfiguration _configuration;

        public UsageMetricsFunction(
            ITenantStatsService statsService,
            ILogger<UsageMetricsFunction> logger,
            TelemetryClient telemetryClient,
            IConfiguration configuration)
        {
            _statsService = statsService;
            _logger = logger;
            _telemetryClient = telemetryClient;
            _configuration = configuration;
        }

        [Function("GetUsageMetrics")]
        public async Task<HttpResponseData> GetUsageMetrics(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "metrics/usage")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("Usage metrics function triggered");
            _telemetryClient.TrackEvent("UsageMetricsRequested");

            try
            {
                // Add CORS headers
                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response, _configuration);

                // Parse query parameters
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = queryParams["tenantId"];
                var startDateStr = queryParams["startDate"];
                var endDateStr = queryParams["endDate"];

                if (string.IsNullOrEmpty(tenantId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "tenantId parameter is required" });
                    return response;
                }

                // Parse optional date parameters
                DateTimeOffset? startDate = null;
                DateTimeOffset? endDate = null;

                if (!string.IsNullOrEmpty(startDateStr))
                {
                    if (DateTimeOffset.TryParse(startDateStr, out var parsedStart))
                        startDate = parsedStart;
                    else
                    {
                        response.StatusCode = HttpStatusCode.BadRequest;
                        await response.WriteAsJsonAsync(new { error = "Invalid startDate format. Use ISO 8601 format." });
                        return response;
                    }
                }

                if (!string.IsNullOrEmpty(endDateStr))
                {
                    if (DateTimeOffset.TryParse(endDateStr, out var parsedEnd))
                        endDate = parsedEnd;
                    else
                    {
                        response.StatusCode = HttpStatusCode.BadRequest;
                        await response.WriteAsJsonAsync(new { error = "Invalid endDate format. Use ISO 8601 format." });
                        return response;
                    }
                }

                _logger.LogInformation("Fetching usage metrics for tenant {TenantId}", tenantId);

                // Get usage metrics
                var metrics = await _statsService.GetUsageMetricsAsync(tenantId, startDate, endDate);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(metrics);

                _telemetryClient.TrackEvent("UsageMetricsReturned", new Dictionary<string, string>
                {
                    { "TenantId", tenantId },
                    { "ActiveSeats", metrics.Seats.Active.ToString() },
                    { "TotalInteractions", metrics.Interactions.Total.ToString() },
                    { "HasCustomDateRange", (startDate.HasValue || endDate.HasValue).ToString() }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving usage metrics");
                _telemetryClient.TrackException(ex);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse, _configuration);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error retrieving usage metrics" });
                return errorResponse;
            }
        }

        [Function("GetTenantOverview")]
        public async Task<HttpResponseData> GetTenantOverview(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "metrics/overview")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("Tenant overview function triggered");
            _telemetryClient.TrackEvent("TenantOverviewRequested");

            try
            {
                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response, _configuration);

                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = queryParams["tenantId"];

                if (string.IsNullOrEmpty(tenantId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "tenantId parameter is required" });
                    return response;
                }

                _logger.LogInformation("Fetching tenant overview for tenant {TenantId}", tenantId);

                // Get current period (last 30 days) and previous period for comparison
                var currentEnd = DateTimeOffset.UtcNow;
                var currentStart = currentEnd.AddDays(-30);
                var previousStart = currentStart.AddDays(-30);

                var currentMetrics = await _statsService.GetUsageMetricsAsync(tenantId, currentStart, currentEnd);
                var previousMetrics = await _statsService.GetUsageMetricsAsync(tenantId, previousStart, currentStart);

                var overview = new
                {
                    tenantId = tenantId,
                    lastUpdated = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    current = new
                    {
                        period = currentMetrics.Period,
                        activeUsers = currentMetrics.Seats.Active,
                        totalInteractions = currentMetrics.Interactions.Total,
                        averageDaily = currentMetrics.Interactions.DailyAverage,
                        topApps = GetTopApps(currentMetrics.Apps, 3)
                    },
                    trends = new
                    {
                        userGrowth = CalculateGrowth(currentMetrics.Seats.Active, previousMetrics.Seats.Active),
                        interactionGrowth = CalculateGrowth(currentMetrics.Interactions.Total, previousMetrics.Interactions.Total),
                        conversationGrowth = CalculateGrowth(currentMetrics.Conversations.Threads, previousMetrics.Conversations.Threads)
                    },
                    healthScore = CalculateHealthScore(currentMetrics)
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(overview);

                _telemetryClient.TrackEvent("TenantOverviewReturned", new Dictionary<string, string>
                {
                    { "TenantId", tenantId },
                    { "ActiveUsers", currentMetrics.Seats.Active.ToString() },
                    { "HealthScore", overview.healthScore.ToString() }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving tenant overview");
                _telemetryClient.TrackException(ex);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse, _configuration);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error retrieving tenant overview" });
                return errorResponse;
            }
        }

        private Dictionary<string, int> GetTopApps(Dictionary<string, int> apps, int count)
        {
            var topApps = new Dictionary<string, int>();
            
            foreach (var app in apps.OrderByDescending(x => x.Value).Take(count))
            {
                topApps[app.Key] = app.Value;
            }

            return topApps;
        }

        private double CalculateGrowth(int current, int previous)
        {
            if (previous == 0) return current > 0 ? 100.0 : 0.0;
            return Math.Round(((double)(current - previous) / previous) * 100, 1);
        }

        private int CalculateHealthScore(Core.Models.TenantUsageMetrics metrics)
        {
            // Simple health score based on engagement metrics
            int score = 50; // Base score

            // Active users factor (0-30 points)
            if (metrics.Seats.Active > 0)
            {
                var utilizationRate = metrics.Seats.UtilizationRate;
                score += Math.Min(30, (int)(utilizationRate * 0.3));
            }

            // Activity level factor (0-20 points)
            if (metrics.Interactions.DailyAverage > 0)
            {
                // Assume healthy daily average is 2+ interactions per active user
                var healthyAverage = metrics.Seats.Active * 2;
                var activityRatio = Math.Min(1.0, metrics.Interactions.DailyAverage / Math.Max(1, healthyAverage));
                score += (int)(activityRatio * 20);
            }

            return Math.Min(100, Math.Max(0, score));
        }
    }
}