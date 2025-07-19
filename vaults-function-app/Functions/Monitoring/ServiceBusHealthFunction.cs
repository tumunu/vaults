using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Helpers;
using VaultsFunctions.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace VaultsFunctions.Functions.Monitoring
{
    public class ServiceBusHealthFunction
    {
        private readonly IServiceBusMonitoringService _monitoringService;
        private readonly ILogger<ServiceBusHealthFunction> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly IConfiguration _configuration;
        private readonly IFeatureFlags _featureFlags;

        public ServiceBusHealthFunction(
            IServiceBusMonitoringService monitoringService,
            ILogger<ServiceBusHealthFunction> logger,
            TelemetryClient telemetryClient,
            IConfiguration configuration,
            IFeatureFlags featureFlags)
        {
            _monitoringService = monitoringService;
            _logger = logger;
            _telemetryClient = telemetryClient;
            _configuration = configuration;
            _featureFlags = featureFlags;
        }

        [Function("GetServiceBusHealth")]
        public async Task<HttpResponseData> GetServiceBusHealth(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/servicebus/health")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("Service Bus health check requested");
            
            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            // Check if monitoring is enabled
            if (!_featureFlags.IsServiceBusMonitoringEnabled)
            {
                response.StatusCode = HttpStatusCode.ServiceUnavailable;
                await response.WriteAsJsonAsync(new 
                { 
                    status = "disabled",
                    message = "Service Bus monitoring is not enabled",
                    feature = "ServiceBusMonitoring",
                    enabled = false
                });
                return response;
            }

            _telemetryClient.TrackEvent("ServiceBusHealthRequested");

            try
            {
                // Check health of invite-queue
                var inviteQueueMetrics = await _monitoringService.GetQueueMetricsAsync("invite-queue");
                var isHealthy = await _monitoringService.IsQueueHealthyAsync("invite-queue");

                var healthStatus = new
                {
                    timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    overall = new
                    {
                        status = isHealthy ? "healthy" : "unhealthy",
                        available = inviteQueueMetrics.IsAvailable,
                        monitoringEnabled = _featureFlags.IsServiceBusMonitoringEnabled
                    },
                    queues = new
                    {
                        inviteQueue = new
                        {
                            name = "invite-queue",
                            status = isHealthy ? "healthy" : "unhealthy",
                            available = inviteQueueMetrics.IsAvailable,
                            metrics = inviteQueueMetrics.IsAvailable ? new
                            {
                                activeMessages = inviteQueueMetrics.ActiveMessageCount,
                                deadLetterMessages = inviteQueueMetrics.DeadLetterMessageCount,
                                totalMessages = inviteQueueMetrics.TotalMessageCount,
                                sizeInBytes = inviteQueueMetrics.SizeInBytes,
                                maxSizeInMB = inviteQueueMetrics.MaxSizeInMegabytes,
                                maxDeliveryCount = inviteQueueMetrics.MaxDeliveryCount,
                                createdAt = inviteQueueMetrics.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                lastAccessed = inviteQueueMetrics.AccessedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                            } : null
                        }
                    },
                    features = new
                    {
                        serviceBusMonitoring = _featureFlags.IsServiceBusMonitoringEnabled,
                        deadLetterProcessing = _featureFlags.IsDeadLetterQueueProcessingEnabled,
                        advancedTelemetry = _featureFlags.IsAdvancedTelemetryEnabled
                    }
                };

                response.StatusCode = isHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
                await response.WriteAsJsonAsync(healthStatus);

                _telemetryClient.TrackEvent("ServiceBusHealthReturned", new Dictionary<string, string>
                {
                    { "IsHealthy", isHealthy.ToString() },
                    { "ActiveMessages", inviteQueueMetrics.ActiveMessageCount.ToString() },
                    { "DeadLetterMessages", inviteQueueMetrics.DeadLetterMessageCount.ToString() },
                    { "MonitoringEnabled", _featureFlags.IsServiceBusMonitoringEnabled.ToString() }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Service Bus health");
                _telemetryClient.TrackException(ex);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse, _configuration);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error retrieving Service Bus health" });
                return errorResponse;
            }
        }

        [Function("MonitorServiceBusHealth")]
        public async Task MonitorServiceBusHealth(
            [TimerTrigger("0 */2 * * * *")] TimerInfo timer,
            FunctionContext context)
        {
            // Check if Service Bus monitoring is enabled
            if (!_featureFlags.IsServiceBusMonitoringEnabled)
            {
                _logger.LogDebug("Service Bus monitoring is disabled by feature flag");
                return;
            }

            _logger.LogInformation("Service Bus health monitoring triggered");
            _telemetryClient.TrackEvent("ServiceBusHealthMonitorTriggered");

            try
            {
                var isHealthy = await _monitoringService.IsQueueHealthyAsync("invite-queue");
                
                if (!isHealthy)
                {
                    var metrics = await _monitoringService.GetQueueMetricsAsync("invite-queue");
                    await _monitoringService.AlertOnQueueIssuesAsync("invite-queue", metrics);
                }

                _logger.LogInformation("Service Bus health monitoring completed. Queue healthy: {IsHealthy}", isHealthy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Service Bus health monitoring");
                _telemetryClient.TrackException(ex);
            }
        }

        [Function("GetQueueMetrics")]
        public async Task<HttpResponseData> GetQueueMetrics(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "admin/servicebus/metrics")] HttpRequestData req,
            FunctionContext context)
        {
            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            // Check if monitoring is enabled
            if (!_featureFlags.IsServiceBusMonitoringEnabled)
            {
                response.StatusCode = HttpStatusCode.ServiceUnavailable;
                await response.WriteAsJsonAsync(new 
                { 
                    error = "Service Bus monitoring is not enabled",
                    feature = "ServiceBusMonitoring",
                    enabled = false,
                    userThreshold = _featureFlags.ServiceBusMonitoringThreshold
                });
                return response;
            }

            _logger.LogInformation("Queue metrics requested");
            _telemetryClient.TrackEvent("QueueMetricsRequested");

            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var queueName = queryParams["queue"] ?? "invite-queue";

                var metrics = await _monitoringService.GetQueueMetricsAsync(queueName);

                if (!metrics.IsAvailable)
                {
                    response.StatusCode = HttpStatusCode.ServiceUnavailable;
                    await response.WriteAsJsonAsync(new { error = $"Queue '{queueName}' is not available or connection failed" });
                    return response;
                }

                var metricsResponse = new
                {
                    queueName = metrics.QueueName,
                    timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    messages = new
                    {
                        active = metrics.ActiveMessageCount,
                        deadLetter = metrics.DeadLetterMessageCount,
                        scheduled = metrics.ScheduledMessageCount,
                        total = metrics.TotalMessageCount
                    },
                    size = new
                    {
                        currentBytes = metrics.SizeInBytes,
                        maxMegabytes = metrics.MaxSizeInMegabytes,
                        utilizationPercent = Math.Round((double)metrics.SizeInBytes / (metrics.MaxSizeInMegabytes * 1024 * 1024) * 100, 2)
                    },
                    configuration = new
                    {
                        maxDeliveryCount = metrics.MaxDeliveryCount
                    },
                    timestamps = new
                    {
                        created = metrics.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        updated = metrics.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        accessed = metrics.AccessedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    },
                    featureStatus = new
                    {
                        monitoringEnabled = _featureFlags.IsServiceBusMonitoringEnabled,
                        deadLetterEnabled = _featureFlags.IsDeadLetterQueueProcessingEnabled
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(metricsResponse);

                _telemetryClient.TrackEvent("QueueMetricsReturned", new Dictionary<string, string>
                {
                    { "QueueName", queueName },
                    { "ActiveMessages", metrics.ActiveMessageCount.ToString() },
                    { "DeadLetterMessages", metrics.DeadLetterMessageCount.ToString() },
                    { "MonitoringEnabled", _featureFlags.IsServiceBusMonitoringEnabled.ToString() }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving queue metrics");
                _telemetryClient.TrackException(ex);

                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse, _configuration);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error retrieving queue metrics" });
                return errorResponse;
            }
        }
    }
}