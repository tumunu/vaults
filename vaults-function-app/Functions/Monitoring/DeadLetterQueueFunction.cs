using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights;
using VaultsFunctions.Core.Models;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Configuration;
using Microsoft.Azure.Cosmos;

namespace VaultsFunctions.Functions.Monitoring
{
    public class DeadLetterQueueFunction
    {
        private readonly ILogger<DeadLetterQueueFunction> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;
        private readonly IFeatureFlags _featureFlags;

        public DeadLetterQueueFunction(
            ILogger<DeadLetterQueueFunction> logger,
            TelemetryClient telemetryClient,
            CosmosClient cosmosClient,
            IFeatureFlags featureFlags)
        {
            _logger = logger;
            _telemetryClient = telemetryClient;
            _cosmosClient = cosmosClient;
            _featureFlags = featureFlags;
            
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");
        }

        [Function("ProcessDeadLetterQueue")]
        public async Task ProcessDeadLetterQueue(
            [ServiceBusTrigger("invite-queue/$deadletterqueue", Connection = "ServiceBusConnection")] 
            string messageBody,
            FunctionContext context)
        {
            // Check if dead letter processing is enabled
            if (!_featureFlags.IsDeadLetterQueueProcessingEnabled)
            {
                _logger.LogInformation("Dead letter queue processing is disabled by feature flag");
                return;
            }

            _logger.LogError("Processing dead letter message from invite-queue");
            _telemetryClient.TrackEvent("DeadLetterMessageReceived");

            try
            {
                if (string.IsNullOrEmpty(messageBody))
                {
                    _logger.LogError("Empty dead letter message body");
                    return;
                }

                _logger.LogError("Dead letter message content: {MessageBody}", messageBody);

                // Try to parse the failed invitation request
                var invitationRequest = System.Text.Json.JsonSerializer.Deserialize<InvitationRequest>(messageBody);
                
                if (invitationRequest != null && invitationRequest.IsValid())
                {
                    await RecordFailedInvitationAsync(invitationRequest, "Dead letter queue - max delivery count exceeded");
                    
                    _telemetryClient.TrackEvent("DeadLetterInvitationRecorded", new Dictionary<string, string>
                    {
                        { "TenantId", invitationRequest.TenantId },
                        { "AdminEmail", invitationRequest.AdminEmail },
                        { "Reason", "MaxDeliveryCountExceeded" }
                    });
                }
                else
                {
                    _logger.LogError("Invalid invitation request in dead letter queue: {MessageBody}", messageBody);
                    _telemetryClient.TrackEvent("DeadLetterInvalidMessage");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing dead letter message: {MessageBody}", messageBody);
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "ProcessingType", "DeadLetterQueue" },
                    { "MessageBody", messageBody }
                });
            }
        }

        [Function("MonitorQueueHealth")]
        public Task MonitorQueueHealth(
            [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
            FunctionContext context)
        {
            // Check if Service Bus monitoring is enabled
            if (!_featureFlags.IsServiceBusMonitoringEnabled)
            {
                _logger.LogDebug("Service Bus monitoring is disabled by feature flag");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Monitoring Service Bus queue health");
            _telemetryClient.TrackEvent("QueueHealthMonitorTriggered");

            try
            {
                // This would typically query Service Bus management API for queue metrics
                // For now, we'll log a health check and track via telemetry
                
                var healthMetrics = new Dictionary<string, string>
                {
                    { "QueueName", "invite-queue" },
                    { "MonitorTime", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") },
                    { "Status", "Monitoring" }
                };

                _telemetryClient.TrackEvent("QueueHealthCheck", healthMetrics);
                
                // TODO: Add actual queue metrics when Service Bus Management API is configured
                _logger.LogInformation("Queue health monitoring completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during queue health monitoring");
                _telemetryClient.TrackException(ex);
            }
            
            return Task.CompletedTask;
        }

        private async Task RecordFailedInvitationAsync(InvitationRequest request, string reason)
        {
            try
            {
                // Get or create tenant status
                var tenantStatus = await GetTenantStatusAsync(request.TenantId) ?? 
                    new TenantStatus { Id = request.TenantId };

                // Update with failure information
                tenantStatus.AdminEmail = request.AdminEmail;
                tenantStatus.InvitationState = InvitationState.Failed.ToString();
                tenantStatus.LastInvitationError = reason;
                tenantStatus.InvitationRetryCount++;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                // Save to Cosmos
                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(request.TenantId));

                _logger.LogInformation("Recorded failed invitation for tenant {TenantId}: {Reason}", 
                    request.TenantId, reason);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording failed invitation for tenant {TenantId}", request.TenantId);
            }
        }

        private async Task<TenantStatus> GetTenantStatusAsync(string tenantId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _tenantsContainer.GetItemQueryIterator<TenantStatus>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    return response.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not retrieve tenant status: {TenantId}", tenantId);
            }

            return null;
        }
    }
}