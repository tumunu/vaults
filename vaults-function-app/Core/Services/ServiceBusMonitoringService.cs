using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Configuration;

namespace VaultsFunctions.Core.Services
{
    public interface IServiceBusMonitoringService
    {
        Task<ServiceBusQueueMetrics> GetQueueMetricsAsync(string queueName);
        Task<bool> IsQueueHealthyAsync(string queueName);
        Task AlertOnQueueIssuesAsync(string queueName, ServiceBusQueueMetrics metrics);
    }

    public class ServiceBusMonitoringService : IServiceBusMonitoringService
    {
        private readonly ServiceBusAdministrationClient _adminClient;
        private readonly ILogger<ServiceBusMonitoringService> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly IConfiguration _configuration;

        public ServiceBusMonitoringService(
            ILogger<ServiceBusMonitoringService> logger,
            TelemetryClient telemetryClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _telemetryClient = telemetryClient;
            _configuration = configuration;

            try
            {
                var connectionString = _configuration["ServiceBusConnection"];
                if (!string.IsNullOrEmpty(connectionString))
                {
                    _adminClient = new ServiceBusAdministrationClient(connectionString);
                }
                else
                {
                    _logger.LogWarning("ServiceBusConnection not configured - monitoring will be limited");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Service Bus Administration Client");
            }
        }

        public async Task<ServiceBusQueueMetrics> GetQueueMetricsAsync(string queueName)
        {
            try
            {
                if (_adminClient == null)
                {
                    _logger.LogWarning("Service Bus admin client not available");
                    return new ServiceBusQueueMetrics { IsAvailable = false };
                }

                var queueRuntimeInfo = await _adminClient.GetQueueRuntimePropertiesAsync(queueName);
                var queueProperties = await _adminClient.GetQueueAsync(queueName);

                var metrics = new ServiceBusQueueMetrics
                {
                    QueueName = queueName,
                    IsAvailable = true,
                    ActiveMessageCount = queueRuntimeInfo.Value.ActiveMessageCount,
                    DeadLetterMessageCount = queueRuntimeInfo.Value.DeadLetterMessageCount,
                    ScheduledMessageCount = queueRuntimeInfo.Value.ScheduledMessageCount,
                    TotalMessageCount = queueRuntimeInfo.Value.TotalMessageCount,
                    SizeInBytes = queueRuntimeInfo.Value.SizeInBytes,
                    MaxSizeInMegabytes = queueProperties.Value.MaxSizeInMegabytes,
                    MaxDeliveryCount = queueProperties.Value.MaxDeliveryCount,
                    CreatedAt = queueRuntimeInfo.Value.CreatedAt,
                    UpdatedAt = queueRuntimeInfo.Value.UpdatedAt,
                    AccessedAt = queueRuntimeInfo.Value.AccessedAt
                };

                _telemetryClient.TrackMetric("ServiceBus.ActiveMessages", metrics.ActiveMessageCount, 
                    new Dictionary<string, string> { { "QueueName", queueName } });
                _telemetryClient.TrackMetric("ServiceBus.DeadLetterMessages", metrics.DeadLetterMessageCount, 
                    new Dictionary<string, string> { { "QueueName", queueName } });

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue metrics for {QueueName}", queueName);
                _telemetryClient.TrackException(ex);
                return new ServiceBusQueueMetrics { IsAvailable = false };
            }
        }

        public async Task<bool> IsQueueHealthyAsync(string queueName)
        {
            try
            {
                var metrics = await GetQueueMetricsAsync(queueName);
                
                if (!metrics.IsAvailable)
                    return false;

                // Define health criteria
                var isHealthy = true;
                var healthIssues = new List<string>();

                // Check for excessive dead letter messages
                if (metrics.DeadLetterMessageCount > 10)
                {
                    isHealthy = false;
                    healthIssues.Add($"High dead letter count: {metrics.DeadLetterMessageCount}");
                }

                // Check for queue backing up (more than 100 active messages)
                if (metrics.ActiveMessageCount > 100)
                {
                    isHealthy = false;
                    healthIssues.Add($"Queue backing up: {metrics.ActiveMessageCount} active messages");
                }

                // Check if queue is approaching size limit (>80% full)
                var sizePercentage = (double)metrics.SizeInBytes / (metrics.MaxSizeInMegabytes * 1024 * 1024) * 100;
                if (sizePercentage > 80)
                {
                    isHealthy = false;
                    healthIssues.Add($"Queue size high: {sizePercentage:F1}% full");
                }

                if (!isHealthy)
                {
                    await AlertOnQueueIssuesAsync(queueName, metrics);
                    _logger.LogWarning("Queue {QueueName} health issues: {Issues}", queueName, string.Join(", ", healthIssues));
                }

                _telemetryClient.TrackEvent("QueueHealthCheck", new Dictionary<string, string>
                {
                    { "QueueName", queueName },
                    { "IsHealthy", isHealthy.ToString() },
                    { "Issues", string.Join(", ", healthIssues) }
                });

                return isHealthy;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking queue health for {QueueName}", queueName);
                return false;
            }
        }

        public Task AlertOnQueueIssuesAsync(string queueName, ServiceBusQueueMetrics metrics)
        {
            try
            {
                var alertData = new Dictionary<string, string>
                {
                    { "QueueName", queueName },
                    { "ActiveMessages", metrics.ActiveMessageCount.ToString() },
                    { "DeadLetterMessages", metrics.DeadLetterMessageCount.ToString() },
                    { "TotalMessages", metrics.TotalMessageCount.ToString() },
                    { "SizeInBytes", metrics.SizeInBytes.ToString() },
                    { "AlertTime", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
                };

                _telemetryClient.TrackEvent("ServiceBusQueueAlert", alertData);
                
                // Here you could also integrate with other alerting systems:
                // - Send email via SendGrid
                // - Post to Teams/Slack webhook
                // - Create Azure Monitor alert
                
                _logger.LogError("SERVICE BUS ALERT: Queue {QueueName} requires attention - " +
                    "Active: {ActiveMessages}, Dead Letter: {DeadLetterMessages}, Size: {SizeInBytes} bytes",
                    queueName, metrics.ActiveMessageCount, metrics.DeadLetterMessageCount, metrics.SizeInBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending queue alert for {QueueName}", queueName);
            }
            
            return Task.CompletedTask;
        }
    }

    public class ServiceBusQueueMetrics
    {
        public string QueueName { get; set; }
        public bool IsAvailable { get; set; }
        public long ActiveMessageCount { get; set; }
        public long DeadLetterMessageCount { get; set; }
        public long ScheduledMessageCount { get; set; }
        public long TotalMessageCount { get; set; }
        public long SizeInBytes { get; set; }
        public long MaxSizeInMegabytes { get; set; }
        public int MaxDeliveryCount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset AccessedAt { get; set; }
    }
}