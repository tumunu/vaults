using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Stripe;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Cosmos;
using VaultsFunctions.Core.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker.Extensions.ServiceBus;
using Azure.Messaging.ServiceBus;
using VaultsFunctions.Core.Services;
using System.Text.Json;

namespace VaultsFunctions.Functions.Payments
{
    public class StripeWebhookFunction
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;
        private readonly TelemetryClient _telemetryClient;
        private readonly ServiceBusClient _serviceBusClient;
        private readonly IGraphInvitationService _invitationService;

        public StripeWebhookFunction(IConfiguration configuration, CosmosClient cosmosClient, TelemetryConfiguration telemetryConfiguration, ServiceBusClient serviceBusClient, IGraphInvitationService invitationService)
        {
            _configuration = configuration;
            StripeConfiguration.ApiKey = _configuration["StripeSecretKey"];

            _cosmosClient = cosmosClient;
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");

            _telemetryClient = new TelemetryClient(telemetryConfiguration);
            _serviceBusClient = serviceBusClient;
            _invitationService = invitationService;
        }

        [Function("StripeWebhook")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "stripe/webhook")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<StripeWebhookFunction>();
            log.LogInformation("Stripe webhook function received a request.");
            _telemetryClient.TrackEvent("StripeWebhookReceived");

            try
            {
                string json = await new StreamReader(req.Body).ReadToEndAsync();
                string stripeSignatureHeader = req.Headers.GetValues("Stripe-Signature").FirstOrDefault();

                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    stripeSignatureHeader,
                    _configuration["StripeWebhookSecret"]
                );

                log.LogInformation($"Stripe Event Type: {stripeEvent.Type}");
                _telemetryClient.TrackEvent($"StripeEventProcessed_{stripeEvent.Type}", new Dictionary<string, string> { { "EventType", stripeEvent.Type } });

                string tenantId = null;
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                    case "customer.created":
                    case "customer.subscription.created":
                    case "customer.subscription.updated":
                    case "invoice.payment_succeeded":
                        // Extract tenantId from metadata
                        if (stripeEvent.Data.Object is IHasMetadata hasMetadata && hasMetadata.Metadata.TryGetValue("tenantId", out var metadataTenantId))
                        {
                            tenantId = metadataTenantId;
                        }
                        else if (stripeEvent.Data.Object is Customer customer && customer.Metadata.TryGetValue("tenantId", out metadataTenantId))
                        {
                            tenantId = metadataTenantId;
                        }
                        else if (stripeEvent.Data.Object is Subscription subscription && subscription.Metadata.TryGetValue("tenantId", out metadataTenantId))
                        {
                            tenantId = metadataTenantId;
                        }
                        else if (stripeEvent.Data.Object is Invoice invoice && invoice.Subscription != null)
                        {
                            // For invoices, fetch subscription to get metadata
                            var subscriptionService = new SubscriptionService();
                            var stripeSubscription = await subscriptionService.GetAsync(invoice.Subscription.Id);
                            if (stripeSubscription.Metadata.TryGetValue("tenantId", out metadataTenantId))
                            {
                                tenantId = metadataTenantId;
                            }
                        }
                        else if (stripeEvent.Data.Object is Stripe.Checkout.Session session && session.Metadata != null)
                        {
                            // For payment link checkout sessions
                            if (session.Metadata.TryGetValue("tenantId", out metadataTenantId))
                            {
                                tenantId = metadataTenantId;
                            }
                        }

                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            await UpdateTenantStatusFromStripeEvent(tenantId, stripeEvent, context);
                            _telemetryClient.TrackEvent("TenantStatusUpdated", new Dictionary<string, string> { { "TenantId", tenantId }, { "EventType", stripeEvent.Type } });
                            
                            // Queue invitation after successful payment
                            if (stripeEvent.Type == "invoice.payment_succeeded")
                            {
                                await QueueInvitationAfterPayment(tenantId, stripeEvent, context);
                            }
                        }
                        else
                        {
                            log.LogWarning($"Stripe event {stripeEvent.Type} received but tenantId could not be extracted from metadata.");
                            _telemetryClient.TrackEvent("TenantIdExtractionFailed", new Dictionary<string, string> { { "EventType", stripeEvent.Type } });
                        }
                        break;
                    // Handle other event types as needed
                    default:
                        log.LogInformation($"Unhandled Stripe event type: {stripeEvent.Type}");
                        _telemetryClient.TrackEvent("UnhandledStripeEventType", new Dictionary<string, string> { { "EventType", stripeEvent.Type } });
                        break;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe webhook error: {e.Message}");
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteStringAsync($"Stripe webhook error: {e.Message}");
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing Stripe webhook");
                var response = req.CreateResponse(HttpStatusCode.InternalServerError);
                await response.WriteStringAsync("Internal server error");
                return response;
            }
            finally
            {
                _telemetryClient.Flush();
            }
        }

        private async Task UpdateTenantStatusFromStripeEvent(
            string tenantId, 
            Event stripeEvent,
            FunctionContext context)
        {
            var log = context.GetLogger<StripeWebhookFunction>();
            var tenantStatus = await GetTenantStatus(tenantId) ?? new TenantStatus { Id = tenantId };

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
                    if (session.Metadata != null && session.Metadata.TryGetValue("seats", out var seatsStr) && int.TryParse(seatsStr, out var seats))
                    {
                        tenantStatus.PurchasedSeats = seats;
                        tenantStatus.MaxSeats = seats;
                        tenantStatus.LastSeatUpdate = DateTimeOffset.UtcNow;
                        log.LogInformation($"Tenant {tenantId}: Payment completed for {seats} seats");
                        _telemetryClient.TrackEvent("SeatsPurchased", new Dictionary<string, string> { { "TenantId", tenantId }, { "Seats", seats.ToString() } });
                    }
                    break;
                case "customer.created":
                    var customer = stripeEvent.Data.Object as Customer;
                    tenantStatus.StripeCustomerId = customer.Id;
                    log.LogInformation($"Tenant {tenantId}: Stripe Customer created: {customer.Id}");
                    _telemetryClient.TrackEvent("StripeCustomerCreated", new Dictionary<string, string> { { "TenantId", tenantId }, { "CustomerId", customer.Id } });
                    break;
                case "customer.subscription.created":
                case "customer.subscription.updated":
                    var subscription = stripeEvent.Data.Object as Subscription;
                    tenantStatus.StripeSubscriptionId = subscription.Id;
                    tenantStatus.StripeSubscriptionStatus = subscription.Status;
                    tenantStatus.StripeCurrentPeriodEnd = subscription.CurrentPeriodEnd;
                    log.LogInformation($"Tenant {tenantId}: Subscription {subscription.Id} status: {subscription.Status}");
                    _telemetryClient.TrackEvent("StripeSubscriptionUpdated", new Dictionary<string, string> { { "TenantId", tenantId }, { "SubscriptionId", subscription.Id }, { "Status", subscription.Status } });
                    break;
                case "invoice.payment_succeeded":
                    var invoice = stripeEvent.Data.Object as Invoice;
                    tenantStatus.LastInvoiceId = invoice.Id;
                    tenantStatus.LastInvoiceAmount = invoice.AmountDue;
                    tenantStatus.LastInvoiceStatus = invoice.Status;
                    log.LogInformation($"Tenant {tenantId}: Invoice {invoice.Id} payment succeeded. Amount: {invoice.AmountDue}");
                    _telemetryClient.TrackEvent("StripeInvoicePaymentSucceeded", new Dictionary<string, string> { { "TenantId", tenantId }, { "InvoiceId", invoice.Id }, { "Amount", invoice.AmountDue.ToString() } });
                    break;
            }
            tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;
            await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));
        }

        private async Task<TenantStatus> GetTenantStatus(string tenantId)
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
                Console.WriteLine($"Could not retrieve tenant status: {ex.Message}");
                _telemetryClient.TrackException(ex, new Dictionary<string, string> { { "ErrorType", "GetTenantStatusError" }, { "TenantId", tenantId } });
            }

            return null;
        }

        private async Task QueueInvitationAfterPayment(
            string tenantId, 
            Event stripeEvent,
            FunctionContext context)
        {
            var log = context.GetLogger<StripeWebhookFunction>();
            
            try
            {
                // Only process live mode payments to avoid test invitations
                if (stripeEvent.Livemode != true)
                {
                    log.LogInformation("Skipping invitation for test mode payment: {TenantId}", tenantId);
                    return;
                }

                // Get tenant status to extract admin email
                var tenantStatus = await GetTenantStatus(tenantId);
                if (tenantStatus == null)
                {
                    log.LogWarning("No tenant status found for invitation queuing: {TenantId}", tenantId);
                    return;
                }

                // Check if we have an admin email from onboarding
                string adminEmail = null;
                if (!string.IsNullOrEmpty(tenantStatus.AdminEmail))
                {
                    adminEmail = tenantStatus.AdminEmail;
                }
                else if (stripeEvent.Data.Object is Invoice invoice)
                {
                    // Try to extract email from customer
                    var customerService = new CustomerService();
                    var customer = await customerService.GetAsync(invoice.CustomerId);
                    adminEmail = customer?.Email;
                }

                if (string.IsNullOrEmpty(adminEmail))
                {
                    log.LogWarning("No admin email found for invitation: {TenantId}", tenantId);
                    _telemetryClient.TrackEvent("InvitationSkipped_NoEmail", new Dictionary<string, string> { { "TenantId", tenantId } });
                    return;
                }

                // Create invitation request
                var invitationRequest = new InvitationRequest
                {
                    TenantId = tenantId,
                    AdminEmail = adminEmail,
                    RedirectUrl = _configuration["DashboardUrl"] ?? "https://myapplications.microsoft.com",
                    InvitedBy = "StripeWebhook"
                };

                // Try to queue for processing, fallback to direct processing
                try
                {
                    if (_serviceBusClient != null)
                    {
                        var messageBody = System.Text.Json.JsonSerializer.Serialize(invitationRequest);
                        var sender = _serviceBusClient.CreateSender("invite-queue");
                        var message = new ServiceBusMessage(messageBody);
                        await sender.SendMessageAsync(message);
                        
                        log.LogInformation("Invitation queued for tenant {TenantId}, email: {AdminEmail}", tenantId, adminEmail);
                    }
                    else
                    {
                        throw new InvalidOperationException("Service Bus client not available");
                    }
                }
                catch (Exception queueEx)
                {
                    log.LogWarning(queueEx, "Failed to queue invitation, processing directly for tenant {TenantId}", tenantId);
                    
                    // Fallback: Process invitation directly
                    await ProcessInvitationDirectly(invitationRequest, context);
                }

                _telemetryClient.TrackEvent("InvitationQueued", new Dictionary<string, string> 
                { 
                    { "TenantId", tenantId }, 
                    { "AdminEmail", adminEmail },
                    { "InvoiceId", stripeEvent.Data.Object is Invoice inv ? inv.Id : "unknown" }
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error queuing invitation after payment: {TenantId}", tenantId);
                _telemetryClient.TrackException(ex, new Dictionary<string, string> { { "TenantId", tenantId } });
            }
        }

        private async Task ProcessInvitationDirectly(InvitationRequest invitationRequest, FunctionContext context)
        {
            var log = context.GetLogger<StripeWebhookFunction>();
            
            try
            {
                log.LogInformation("Processing invitation directly for tenant {TenantId}, email {AdminEmail}", 
                    invitationRequest.TenantId, invitationRequest.AdminEmail);

                // Check for duplicate invitation using Cosmos DB
                var tenantStatus = await GetTenantStatus(invitationRequest.TenantId);
                
                if (tenantStatus?.InvitationState == InvitationState.Sent.ToString() || 
                    tenantStatus?.InvitationState == InvitationState.Completed.ToString())
                {
                    log.LogInformation("Skipping duplicate invitation for tenant {TenantId}", invitationRequest.TenantId);
                    _telemetryClient.TrackEvent("InvitationSkipped_Duplicate", new Dictionary<string, string> 
                    { 
                        { "TenantId", invitationRequest.TenantId }, 
                        { "AdminEmail", invitationRequest.AdminEmail } 
                    });
                    return;
                }

                // Update tenant status to pending
                if (tenantStatus == null)
                {
                    tenantStatus = new TenantStatus { Id = invitationRequest.TenantId };
                }

                tenantStatus.AdminEmail = invitationRequest.AdminEmail;
                tenantStatus.InvitationState = InvitationState.Pending.ToString();
                tenantStatus.InvitationDateUtc = DateTimeOffset.UtcNow;
                tenantStatus.InvitedBy = invitationRequest.InvitedBy;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                // Send invitation
                var result = await _invitationService.InviteAsync(invitationRequest.AdminEmail, invitationRequest.RedirectUrl);

                // Update tenant status with result
                tenantStatus.InvitationState = result.State.ToString();
                tenantStatus.GraphInviteId = result.GraphInviteId;
                tenantStatus.GraphStatus = result.UserId;
                tenantStatus.LastInvitationError = result.ErrorMessage;

                // Save to Cosmos
                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(invitationRequest.TenantId));

                log.LogInformation("Direct invitation processed for tenant {TenantId}: {State}", 
                    invitationRequest.TenantId, result.State);
                
                _telemetryClient.TrackEvent($"DirectInvite{result.State}", new Dictionary<string, string>
                {
                    { "TenantId", invitationRequest.TenantId },
                    { "AdminEmail", invitationRequest.AdminEmail },
                    { "InviteId", result.GraphInviteId },
                    { "Error", result.ErrorMessage }
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error processing direct invitation for tenant {TenantId}", invitationRequest.TenantId);
                _telemetryClient.TrackException(ex, new Dictionary<string, string> 
                { 
                    { "TenantId", invitationRequest.TenantId },
                    { "ProcessingType", "Direct" }
                });
            }
        }
    }
}
