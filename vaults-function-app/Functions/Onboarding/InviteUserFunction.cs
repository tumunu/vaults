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

namespace VaultsFunctions.Functions.Onboarding
{
    public class InviteUserFunction
    {
        private readonly IGraphInvitationService _invitationService;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;
        private readonly ILogger<InviteUserFunction> _logger;
        private readonly TelemetryClient _telemetryClient;

        public InviteUserFunction(
            IGraphInvitationService invitationService,
            CosmosClient cosmosClient,
            ILogger<InviteUserFunction> logger,
            TelemetryClient telemetryClient)
        {
            _invitationService = invitationService;
            _cosmosClient = cosmosClient;
            _logger = logger;
            _telemetryClient = telemetryClient;
            
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");
        }

        [Function("InviteUser")]
        public async Task<HttpResponseData> InviteUserHttp(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "invite/user")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("InviteUser HTTP function triggered");
            _telemetryClient.TrackEvent("InviteUserHttpTriggered");

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var invitationRequest = JsonSerializer.Deserialize<InvitationRequest>(requestBody);

                if (invitationRequest == null || !invitationRequest.IsValid())
                {
                    var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "Invalid invitation request" });
                    return badRequestResponse;
                }

                // Process directly instead of queuing for now
                var result = await ProcessInvitationAsync(invitationRequest);

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new 
                { 
                    success = result.State != InvitationState.Failed,
                    state = result.State.ToString(),
                    inviteId = result.GraphInviteId,
                    userId = result.UserId,
                    error = result.ErrorMessage
                });
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invitation HTTP request");
                _telemetryClient.TrackException(ex);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = "Internal server error" });
                return errorResponse;
            }
        }

        [Function("ProcessInvitationQueue")]
        public async Task ProcessInvitationQueue(
            [ServiceBusTrigger("invite-queue", Connection = "ServiceBusConnection")] 
            string messageBody,
            FunctionContext context)
        {
            using var scope = _logger.BeginScope("ProcessInvitationQueue");
            _logger.LogInformation("Processing invitation from Service Bus queue");
            _telemetryClient.TrackEvent("ProcessInvitationQueueTriggered");

            try
            {
                if (string.IsNullOrEmpty(messageBody))
                {
                    _logger.LogError("Empty message body received from queue");
                    return;
                }

                var invitationRequest = JsonSerializer.Deserialize<InvitationRequest>(messageBody);
                
                if (invitationRequest == null || !invitationRequest.IsValid())
                {
                    _logger.LogError("Invalid invitation request from queue: {MessageBody}", messageBody);
                    _telemetryClient.TrackEvent("InvalidInvitationRequest");
                    return; // Don't retry invalid requests
                }

                // Check for duplicate processing using Cosmos DB
                var tenantStatus = await GetTenantStatusAsync(invitationRequest.TenantId);
                if (tenantStatus?.InvitationState == InvitationState.Sent.ToString() || 
                    tenantStatus?.InvitationState == InvitationState.Completed.ToString())
                {
                    _logger.LogInformation("Skipping duplicate invitation processing for tenant {TenantId}", invitationRequest.TenantId);
                    _telemetryClient.TrackEvent("InvitationSkipped_AlreadyProcessed", new Dictionary<string, string>
                    {
                        { "TenantId", invitationRequest.TenantId },
                        { "AdminEmail", invitationRequest.AdminEmail }
                    });
                    return;
                }

                using var inviteScope = _logger.BeginScope("Invite {Email} for {TenantId}", 
                    invitationRequest.AdminEmail, invitationRequest.TenantId);

                _logger.LogInformation("InviteStarted");
                _telemetryClient.TrackEvent("QueueInviteStarted", new Dictionary<string, string>
                {
                    { "TenantId", invitationRequest.TenantId },
                    { "AdminEmail", invitationRequest.AdminEmail }
                });

                var result = await ProcessInvitationAsync(invitationRequest);

                _logger.LogInformation("Queue Invite{State}", result.State);
                _telemetryClient.TrackEvent($"QueueInvite{result.State}", new Dictionary<string, string>
                {
                    { "TenantId", invitationRequest.TenantId },
                    { "AdminEmail", invitationRequest.AdminEmail },
                    { "InviteId", result.GraphInviteId },
                    { "UserId", result.UserId },
                    { "Error", result.ErrorMessage }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invitation from Service Bus queue: {MessageBody}", messageBody);
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "ProcessingType", "ServiceBusQueue" },
                    { "MessageBody", messageBody }
                });
                throw; // Re-throw to trigger Service Bus retry
            }
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