using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using VaultsFunctions.Core.Models;
using VaultsFunctions.Core.Services;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker.Extensions.ServiceBus;
using System.Collections.Generic;
using System.Linq;

namespace VaultsFunctions.Functions.Onboarding
{
    public class ResendInvitationFunction
    {
        private readonly IGraphInvitationService _invitationService;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;
        private readonly ILogger<ResendInvitationFunction> _logger;
        private readonly TelemetryClient _telemetryClient;

        // Exponential backoff configuration
        private static readonly TimeSpan[] RetryDelays = new[]
        {
            TimeSpan.FromSeconds(2),   // 2s
            TimeSpan.FromSeconds(4),   // 4s  
            TimeSpan.FromSeconds(8),   // 8s
            TimeSpan.FromSeconds(16),  // 16s
            TimeSpan.FromSeconds(32)   // 32s - max retry
        };

        public ResendInvitationFunction(
            IGraphInvitationService invitationService,
            CosmosClient cosmosClient,
            ILogger<ResendInvitationFunction> logger,
            TelemetryClient telemetryClient)
        {
            _invitationService = invitationService;
            _cosmosClient = cosmosClient;
            _logger = logger;
            _telemetryClient = telemetryClient;
            
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");
        }

        [Function("ResendInvitation")]
        public async Task<HttpResponseData> ResendInvitationHttp(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "invite/resend")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("ResendInvitation HTTP function triggered");
            _telemetryClient.TrackEvent("ResendInvitationHttpTriggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var resendRequest = System.Text.Json.JsonSerializer.Deserialize<ResendInvitationRequest>(requestBody);

                if (resendRequest == null || string.IsNullOrEmpty(resendRequest.TenantId))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "TenantId is required" });
                    return badRequestResponse;
                }

                // Get current tenant status
                var tenantStatus = await GetTenantStatusAsync(resendRequest.TenantId);
                if (tenantStatus == null)
                {
                    var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFoundResponse.WriteAsJsonAsync(new { success = false, error = "Tenant not found" });
                    return notFoundResponse;
                }

                if (string.IsNullOrEmpty(tenantStatus.AdminEmail))
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "No admin email configured for tenant" });
                    return badRequestResponse;
                }

                // Check retry limits
                if (tenantStatus.InvitationRetryCount >= RetryDelays.Length)
                {
                    var limitResponse = req.CreateResponse(HttpStatusCode.TooManyRequests);
                    await limitResponse.WriteAsJsonAsync(new { success = false, error = "Maximum retry attempts exceeded" });
                    return limitResponse;
                }

                // Process invitation directly
                var invitationRequest = new InvitationRequest
                {
                    TenantId = resendRequest.TenantId,
                    AdminEmail = tenantStatus.AdminEmail,
                    RedirectUrl = resendRequest.RedirectUrl,
                    InvitedBy = resendRequest.RequestedBy ?? "Manual"
                };

                // Process the invitation directly instead of queuing
                var result = await ProcessInvitationAsync(invitationRequest);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new 
                { 
                    success = result.State != InvitationState.Failed, 
                    state = result.State.ToString(),
                    message = result.State == InvitationState.Failed ? result.ErrorMessage : "Invitation processed",
                    retryCount = tenantStatus.InvitationRetryCount + 1,
                    maxRetries = RetryDelays.Length
                });

                _telemetryClient.TrackEvent("InvitationRetryQueued", new Dictionary<string, string>
                {
                    { "TenantId", resendRequest.TenantId },
                    { "RetryCount", (tenantStatus.InvitationRetryCount + 1).ToString() },
                    { "RequestedBy", resendRequest.RequestedBy ?? "Manual" }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing resend invitation request");
                _telemetryClient.TrackException(ex);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = "Internal server error" });
                return errorResponse;
            }
        }

        [Function("RetryFailedInvitations")]
        public async Task RetryFailedInvitations(
            [TimerTrigger("0 */15 * * * *")] // Every 15 minutes
            TimerInfo timerInfo,
            FunctionContext context)
        {
            _logger.LogInformation("RetryFailedInvitations timer function triggered");
            _telemetryClient.TrackEvent("RetryFailedInvitationsTriggered");

            try
            {
                // Find tenants with failed invitations that are eligible for retry
                var cutoffTime = DateTimeOffset.UtcNow.AddMinutes(-10); // Wait 10 minutes before retry
                
                var query = new QueryDefinition(@"
                    SELECT * FROM c 
                    WHERE c.invitationState = @failedState 
                    AND c.invitationRetryCount < @maxRetries
                    AND c.invitationDateUtc < @cutoffTime
                    AND c.adminEmail != null")
                    .WithParameter("@failedState", InvitationState.Failed.ToString())
                    .WithParameter("@maxRetries", RetryDelays.Length)
                    .WithParameter("@cutoffTime", cutoffTime);

                using var iterator = _tenantsContainer.GetItemQueryIterator<TenantStatus>(query);
                var retriesQueued = 0;

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    
                    foreach (var tenantStatus in response)
                    {
                        try
                        {
                            // Calculate retry delay based on attempt count
                            var retryCount = tenantStatus.InvitationRetryCount;
                            if (retryCount >= RetryDelays.Length) continue;

                            var lastAttempt = tenantStatus.InvitationDateUtc ?? DateTimeOffset.UtcNow;
                            var requiredDelay = RetryDelays[Math.Min(retryCount, RetryDelays.Length - 1)];
                            var nextRetryTime = lastAttempt.Add(requiredDelay);

                            // Check if enough time has passed for retry
                            if (DateTimeOffset.UtcNow < nextRetryTime)
                            {
                                _logger.LogDebug("Skipping retry for tenant {TenantId} - delay not elapsed", tenantStatus.Id);
                                continue;
                            }

                            // Process retry directly
                            var invitationRequest = new InvitationRequest
                            {
                                TenantId = tenantStatus.Id,
                                AdminEmail = tenantStatus.AdminEmail,
                                RedirectUrl = "https://myapplications.microsoft.com",
                                InvitedBy = "AutoRetry"
                            };

                            var result = await ProcessInvitationAsync(invitationRequest);
                            retriesQueued++;

                            _logger.LogInformation("Queued retry for tenant {TenantId}, attempt {RetryCount}", 
                                tenantStatus.Id, retryCount + 1);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error queuing retry for tenant {TenantId}", tenantStatus.Id);
                        }
                    }
                }

                _logger.LogInformation("Queued {RetryCount} invitation retries", retriesQueued);
                _telemetryClient.TrackEvent("RetryFailedInvitationsCompleted", new Dictionary<string, string>
                {
                    { "RetriesQueued", retriesQueued.ToString() }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RetryFailedInvitations timer function");
                _telemetryClient.TrackException(ex);
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

        private async Task<InvitationResult> ProcessInvitationAsync(InvitationRequest request)
        {
            try
            {
                // Get or create tenant status
                var tenantStatus = await GetTenantStatusAsync(request.TenantId) ?? 
                    new TenantStatus { Id = request.TenantId };

                // Update tenant with invitation attempt
                tenantStatus.AdminEmail = request.AdminEmail;
                tenantStatus.InvitationState = InvitationState.Pending.ToString();
                tenantStatus.InvitationDateUtc = DateTimeOffset.UtcNow;
                tenantStatus.InvitedBy = request.InvitedBy ?? "System";
                tenantStatus.InvitationRetryCount++;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                // Send invitation
                var result = await _invitationService.InviteAsync(request.AdminEmail, request.RedirectUrl);

                // Update tenant status with result
                tenantStatus.InvitationState = result.State.ToString();
                tenantStatus.GraphInviteId = result.GraphInviteId;
                tenantStatus.GraphStatus = result.UserId;
                tenantStatus.LastInvitationError = result.ErrorMessage;

                // Save to Cosmos
                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(request.TenantId));

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invitation for tenant {TenantId}", request.TenantId);
                return InvitationResult.Failed($"Processing error: {ex.Message}");
            }
        }
    }

    public class ResendInvitationRequest
    {
        public string TenantId { get; set; }
        public string RedirectUrl { get; set; }
        public string RequestedBy { get; set; }
    }
}