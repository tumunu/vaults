using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Stripe;
using Stripe.Checkout;
using System.Linq;
using System.Collections.Generic;
using VaultsFunctions.Core.Models; // Ensure this contains your TenantStatus class
using VaultsFunctions.Core.Attributes;

namespace VaultsFunctions.Functions.Payments
{
    /// <summary>
    /// Stripe API integration functions with proper separation of concerns:
    /// 
    /// ## Backend-Only Endpoints (Internal Billing)
    /// - /api/v1/stripe/seats/{tenantId} - Seat status management
    /// - /api/v1/stripe/seats/{tenantId}/reserve - Reserve seats
    /// - /api/v1/stripe/seats/{tenantId}/release - Release seats
    /// - /api/v1/stripe/billing/{tenantId} - Billing status
    /// 
    /// ## Frontend-Facing Endpoints (Stripe Checkout)
    /// - /api/v1/stripe/payment-links - Create/list payment links
    /// - /api/v1/stripe/payment-links/{id} - Get payment link details
    /// - /api/v1/stripe/payment-links/{id}/line-items - Get line items
    /// - /api/v1/stripe/checkout - Create checkout sessions
    /// </summary>
    public class StripeApiFunction
    {
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;
        private readonly string _stripeSecretKey;
        private readonly string _stripePerSeatPriceId;
        private readonly int _defaultSeatCount;

        public StripeApiFunction(CosmosClient cosmosClient)
        {
            _cosmosClient = cosmosClient;
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");
            _stripeSecretKey = Environment.GetEnvironmentVariable("StripeSecretKey") ?? throw new InvalidOperationException("StripeSecretKey environment variable not set.");
            _stripePerSeatPriceId = Environment.GetEnvironmentVariable("StripePerSeatPriceId") ?? throw new InvalidOperationException("StripePerSeatPriceId environment variable not set.");
            _defaultSeatCount = int.Parse(Environment.GetEnvironmentVariable("DefaultSeatCount") ?? "5");
            StripeConfiguration.ApiKey = _stripeSecretKey;
        }

        /// <summary>
        /// Frontend-facing endpoint to create payment links. Used by frontend applications.
        /// </summary>
        [Function("CreateStripePaymentLink")]
        public async Task<HttpResponseData> CreateStripePaymentLink(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/stripe/payment-links")] HttpRequestData req,
            FunctionContext executionContext)
        {
            var log = executionContext.GetLogger("CreateStripePaymentLink");
            log.LogInformation("Stripe payment link creation function received a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string tenantId = data?.tenantId;
            int seats = data?.seats ?? _defaultSeatCount;
            string successUrl = data?.successUrl;
            string cancelUrl = data?.cancelUrl;

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(tenantId) || seats <= 0)
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please pass tenantId and valid seat count in the request body.");
                return response;
            }

            try
            {
                // Create Payment Link with seat-based pricing
                var options = new PaymentLinkCreateOptions
                {
                    LineItems = new List<PaymentLinkLineItemOptions>
                    {
                        new PaymentLinkLineItemOptions
                        {
                            Price = _stripePerSeatPriceId,
                            Quantity = seats,
                        },
                    },
                    Metadata = new Dictionary<string, string> 
                    { 
                        { "tenantId", tenantId },
                        { "seats", seats.ToString() }
                    },
                    AfterCompletion = new PaymentLinkAfterCompletionOptions
                    {
                        Type = "redirect",
                        Redirect = new PaymentLinkAfterCompletionRedirectOptions
                        {
                            Url = successUrl ?? "https://app.vaults.com/success"
                        }
                    }
                };

                var service = new PaymentLinkService();
                var paymentLink = await service.CreateAsync(options);

                // Update tenant status with payment link info
                await UpdateTenantWithPaymentLink(tenantId, paymentLink.Id, seats, log);

                log.LogInformation($"Stripe payment link created for tenant {tenantId}: {paymentLink.Id} ({seats} seats)");

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { 
                    paymentLinkId = paymentLink.Id, 
                    url = paymentLink.Url,
                    seats = seats,
                    tenantId = tenantId
                });
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe error creating payment link: {e.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Stripe API error: {e.Message}");
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error creating Stripe payment link");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Internal backend-only endpoint for seat status. Used by backend services only.
        /// </summary>
        [Function("GetSeatStatus")]
        public async Task<HttpResponseData> GetSeatStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/stripe/seats/{tenantId}")] HttpRequestData req,
            string tenantId,
            FunctionContext context)
        {
            var log = context.GetLogger("GetSeatStatus");
            log.LogInformation($"Get seat status function received a request for tenant: {tenantId}");

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(tenantId))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a tenantId.");
                return response;
            }

            try
            {
                var tenantStatus = await GetTenantStatus(tenantId);
                
                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    tenantId = tenantId,
                    purchasedSeats = tenantStatus?.PurchasedSeats ?? 0,
                    activeSeats = tenantStatus?.ActiveSeats ?? 0,
                    maxSeats = tenantStatus?.MaxSeats ?? 0,
                    isEnterprise = tenantStatus?.IsEnterprise ?? false,
                    lastSeatUpdate = tenantStatus?.LastSeatUpdate,
                    canAddUsers = (tenantStatus?.IsEnterprise == true) || (tenantStatus?.ActiveSeats < tenantStatus?.MaxSeats)
                });
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting seat status");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Internal backend-only endpoint to reserve a seat for a tenant. Used by backend services only.
        /// </summary>
        [Function("ReserveSeat")]
        public async Task<HttpResponseData> ReserveSeat(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/stripe/seats/{tenantId}/reserve")] HttpRequestData req,
            string tenantId,
            FunctionContext context)
        {
            var log = context.GetLogger("ReserveSeat");
            log.LogInformation($"Reserve seat function received a request for tenant: {tenantId}");

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(tenantId))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a tenantId.");
                return response;
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody) ?? new { };
                string userId = data?.userId;
                string userEmail = data?.userEmail;

                if (string.IsNullOrEmpty(userId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync("Please provide userId in the request body.");
                    return response;
                }

                var tenantStatus = await GetTenantStatus(tenantId);
                if (tenantStatus == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync($"Tenant {tenantId} not found.");
                    return response;
                }

                // Check if user can be added (enterprise = unlimited, otherwise check seat limits)
                if (!tenantStatus.IsEnterprise && tenantStatus.ActiveSeats >= tenantStatus.MaxSeats)
                {
                    response.StatusCode = HttpStatusCode.Conflict;
                    await response.WriteAsJsonAsync(new
                    {
                        error = "Seat limit reached",
                        message = $"Cannot reserve seat. Active seats ({tenantStatus.ActiveSeats}) at maximum ({tenantStatus.MaxSeats})",
                        tenantId = tenantId,
                        activeSeats = tenantStatus.ActiveSeats,
                        maxSeats = tenantStatus.MaxSeats
                    });
                    return response;
                }

                // Reserve the seat (increment active seat count)
                tenantStatus.ActiveSeats = tenantStatus.ActiveSeats + 1;
                tenantStatus.LastSeatUpdate = DateTimeOffset.UtcNow;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));

                log.LogInformation($"Seat reserved for tenant {tenantId}, userId {userId}. Active seats: {tenantStatus.ActiveSeats}");

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Seat reserved successfully",
                    tenantId = tenantId,
                    userId = userId,
                    userEmail = userEmail,
                    activeSeats = tenantStatus.ActiveSeats,
                    maxSeats = tenantStatus.MaxSeats,
                    reservedAt = tenantStatus.LastSeatUpdate
                });
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error reserving seat");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Internal backend-only endpoint to release a seat for a tenant. Used by backend services only.
        /// </summary>
        [Function("ReleaseSeat")]
        public async Task<HttpResponseData> ReleaseSeat(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "v1/stripe/seats/{tenantId}/release")] HttpRequestData req,
            string tenantId,
            FunctionContext context)
        {
            var log = context.GetLogger("ReleaseSeat");
            log.LogInformation($"Release seat function received a request for tenant: {tenantId}");

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(tenantId))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a tenantId.");
                return response;
            }

            try
            {
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody) ?? new { };
                string userId = data?.userId;
                string reason = data?.reason ?? "User removed";

                if (string.IsNullOrEmpty(userId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync("Please provide userId in the request body.");
                    return response;
                }

                var tenantStatus = await GetTenantStatus(tenantId);
                if (tenantStatus == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync($"Tenant {tenantId} not found.");
                    return response;
                }

                // Check if there are seats to release
                if (tenantStatus.ActiveSeats <= 0)
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new
                    {
                        error = "No active seats to release",
                        message = $"Tenant {tenantId} has no active seats to release",
                        tenantId = tenantId,
                        activeSeats = tenantStatus.ActiveSeats
                    });
                    return response;
                }

                // Release the seat (decrement active seat count)
                tenantStatus.ActiveSeats = Math.Max(0, tenantStatus.ActiveSeats - 1);
                tenantStatus.LastSeatUpdate = DateTimeOffset.UtcNow;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));

                log.LogInformation($"Seat released for tenant {tenantId}, userId {userId}. Active seats: {tenantStatus.ActiveSeats}. Reason: {reason}");

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Seat released successfully",
                    tenantId = tenantId,
                    userId = userId,
                    reason = reason,
                    activeSeats = tenantStatus.ActiveSeats,
                    maxSeats = tenantStatus.MaxSeats,
                    releasedAt = tenantStatus.LastSeatUpdate
                });
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error releasing seat");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Frontend-facing endpoint to create checkout sessions. Used by frontend applications.
        /// </summary>
        [Function("CreateStripeCheckoutSession")]
        public async Task<HttpResponseData> CreateStripeCheckoutSession(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "v1/stripe/checkout")] HttpRequestData req,
            FunctionContext executionContext)
        {
            var log = executionContext.GetLogger("CreateStripeCheckoutSession");
            log.LogInformation("Stripe checkout session creation function received a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string tenantId = data?.tenantId;
            string successUrl = data?.successUrl;
            string cancelUrl = data?.cancelUrl;

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(successUrl) || string.IsNullOrEmpty(cancelUrl))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please pass tenantId, successUrl, and cancelUrl in the request body.");
                return response;
            }

            try
            {
                // Retrieve or create a Stripe Customer for the tenant
                var tenantStatus = await GetTenantStatus(tenantId);
                string stripeCustomerId = tenantStatus?.StripeCustomerId;

                if (string.IsNullOrEmpty(stripeCustomerId))
                {
                    var customerService = new CustomerService();
                    var customer = await customerService.CreateAsync(new CustomerCreateOptions
                    {
                        Metadata = new Dictionary<string, string> { { "tenantId", tenantId } }
                    });
                    stripeCustomerId = customer.Id;

                    // Update tenant status with new Stripe Customer ID
                    await UpdateTenantStatus(tenantId, stripeCustomerId, log);
                }

                // Create a Checkout Session
                var options = new Stripe.Checkout.SessionCreateOptions
                {
                    Customer = stripeCustomerId,
                    PaymentMethodTypes = new List<string> { "card" },
                    Mode = "subscription",
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            Price = _stripePerSeatPriceId,
                            Quantity = 1,
                        },
                    },
                    SuccessUrl = successUrl,
                    CancelUrl = cancelUrl,
                    SubscriptionData = new SessionSubscriptionDataOptions
                    {
                        Metadata = new Dictionary<string, string> { { "tenantId", tenantId } }
                    }
                };

                var service = new Stripe.Checkout.SessionService();
                var session = await service.CreateAsync(options);

                log.LogInformation($"Stripe checkout session created for tenant {tenantId}: {session.Id}");

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { sessionId = session.Id, url = session.Url });
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe error creating checkout session: {e.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Stripe API error: {e.Message}");
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error creating Stripe checkout session");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Internal backend-only endpoint for billing status. Used by backend services only.
        /// </summary>
        [Function("GetBillingStatus")]
        public async Task<HttpResponseData> GetBillingStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "v1/stripe/billing/{tenantId}")] HttpRequestData req,
            string tenantId,
            FunctionContext context)
        {
            var log = context.GetLogger("GetBillingStatus");
            log.LogInformation($"Get billing status function received a request for tenant: {tenantId}");

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(tenantId))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a tenantId.");
                return response;
            }

            try
            {
                var tenantStatus = await GetTenantStatus(tenantId);
                if (tenantStatus == null)
                {
                    log.LogWarning($"Tenant status not found in Cosmos DB for tenantId: {tenantId}");
                    // Return default trial status instead of 404
                    response.StatusCode = HttpStatusCode.OK;
                    await response.WriteAsJsonAsync(new
                    {
                        tenantId = tenantId,
                        status = "trial",
                        isActive = true,
                        billingCycle = "monthly",
                        nextBillingDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
                        usage = new
                        {
                            currentPeriodStart = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd"),
                            currentPeriodEnd = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                            totalRequests = 0,
                            totalCost = 0.00
                        }
                    });
                    return response;
                }

                if (string.IsNullOrEmpty(tenantStatus.StripeCustomerId))
                {
                    log.LogWarning($"StripeCustomerId is null or empty for tenant {tenantId}. TenantStatus: {JsonConvert.SerializeObject(tenantStatus)}");
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync($"Stripe customer ID not associated with tenant {tenantId}. Please ensure onboarding is complete and a Stripe customer has been created.");
                    return response;
                }

                var customerService = new CustomerService();
                var customer = await customerService.GetAsync(tenantStatus.StripeCustomerId);

                var subscriptionService = new SubscriptionService();
                var subscriptions = await subscriptionService.ListAsync(new SubscriptionListOptions
                {
                    Customer = customer.Id,
                    Status = "active",
                    Limit = 1
                });

                Subscription activeSubscription = subscriptions.FirstOrDefault();

                // Retrieve invoices
                var invoiceService = new InvoiceService();
                var invoices = await invoiceService.ListAsync(new InvoiceListOptions
                {
                    Customer = customer.Id,
                    Limit = 5, // Get last 5 invoices
                    Status = "paid"
                });

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    customerEmail = customer.Email,
                    subscriptionStatus = activeSubscription?.Status,
                    currentPeriodEnd = activeSubscription?.CurrentPeriodEnd,
                    invoices = invoices.Data.Select(i => new { i.Id, i.AmountDue, i.Status, i.InvoicePdf, i.Created })
                });
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe error getting billing status: {e.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Stripe API error: {e.Message}");
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting billing status");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Frontend-facing endpoint to list payment links. Proxies to Stripe's Payment Links API.
        /// </summary>
        [Function("ListPaymentLinks")]
        public async Task<HttpResponseData> ListPaymentLinks(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/stripe/payment-links")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger("ListPaymentLinks");
            log.LogInformation("List payment links function received a request.");

            var response = req.CreateResponse();

            try
            {
                var service = new PaymentLinkService();
                var options = new PaymentLinkListOptions
                {
                    Limit = 10, // Limit to last 10 payment links
                    Active = true
                };

                var paymentLinks = await service.ListAsync(options);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    @object = "list",
                    data = paymentLinks.Data.Select(link => new
                    {
                        id = link.Id,
                        url = link.Url,
                        active = link.Active,
                        metadata = link.Metadata,
                        lineItems = link.LineItems?.Data?.Select(item => new
                        {
                            price = item.Price?.Id,
                            quantity = item.Quantity
                        })
                    }),
                    has_more = paymentLinks.HasMore
                });
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe error listing payment links: {e.Message}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Stripe API error: {e.Message}");
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error listing payment links");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Frontend-facing endpoint to get a specific payment link. Proxies to Stripe's Payment Links API.
        /// </summary>
        [Function("GetPaymentLink")]
        public async Task<HttpResponseData> GetPaymentLink(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/stripe/payment-links/{id}")] HttpRequestData req,
            string id,
            FunctionContext context)
        {
            var log = context.GetLogger("GetPaymentLink");
            log.LogInformation($"Get payment link function received a request for ID: {id}");

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(id))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a payment link ID.");
                return response;
            }

            try
            {
                var service = new PaymentLinkService();
                var paymentLink = await service.GetAsync(id);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    id = paymentLink.Id,
                    url = paymentLink.Url,
                    active = paymentLink.Active,
                    metadata = paymentLink.Metadata,
                    lineItems = paymentLink.LineItems?.Data?.Select(item => new
                    {
                        price = new
                        {
                            id = item.Price?.Id,
                            unit_amount = item.Price?.UnitAmount,
                            currency = item.Price?.Currency,
                            recurring = item.Price?.Recurring
                        },
                        quantity = item.Quantity
                    })
                });
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe error getting payment link: {e.Message}");
                if (e.StripeError?.Type == "invalid_request_error")
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync($"Payment link not found: {id}");
                }
                else
                {
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    await response.WriteStringAsync($"Stripe API error: {e.Message}");
                }
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting payment link");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        /// <summary>
        /// Frontend-facing endpoint to get line items for a payment link. Proxies to Stripe's Payment Links API.
        /// </summary>
        [Function("GetPaymentLinkLineItems")]
        public async Task<HttpResponseData> GetPaymentLinkLineItems(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "v1/stripe/payment-links/{id}/line-items")] HttpRequestData req,
            string id,
            FunctionContext context)
        {
            var log = context.GetLogger("GetPaymentLinkLineItems");
            log.LogInformation($"Get payment link line items function received a request for ID: {id}");

            var response = req.CreateResponse();

            if (string.IsNullOrEmpty(id))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Please provide a payment link ID.");
                return response;
            }

            try
            {
                var service = new PaymentLinkService();
                var lineItems = await service.ListLineItemsAsync(id);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new
                {
                    @object = "list",
                    data = lineItems.Data.Select(item => new
                    {
                        id = item.Id,
                        @object = item.Object,
                        amount_total = item.AmountTotal,
                        currency = item.Currency,
                        description = item.Description,
                        price = new
                        {
                            id = item.Price?.Id,
                            unit_amount = item.Price?.UnitAmount,
                            currency = item.Price?.Currency,
                            product = item.Price?.Product,
                            recurring = item.Price?.Recurring
                        },
                        quantity = item.Quantity
                    }),
                    has_more = lineItems.HasMore
                });
                return response;
            }
            catch (StripeException e)
            {
                log.LogError(e, $"Stripe error getting payment link line items: {e.Message}");
                if (e.StripeError?.Type == "invalid_request_error")
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    await response.WriteStringAsync($"Payment link not found: {id}");
                }
                else
                {
                    response.StatusCode = HttpStatusCode.InternalServerError;
                    await response.WriteStringAsync($"Stripe API error: {e.Message}");
                }
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting payment link line items");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Internal server error: {ex.Message}");
                return response;
            }
        }

        // Helper method to retrieve tenant status from Cosmos DB
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
                // Ideally, log error
                Console.WriteLine($"Could not retrieve tenant status: {ex.Message}");
            }

            return null;
        }

        // Helper method to update tenant status in Cosmos DB
        private async Task UpdateTenantStatus(string tenantId, string stripeCustomerId, ILogger log)
        {
            try
            {
                var tenantStatus = await GetTenantStatus(tenantId) ?? new TenantStatus { Id = tenantId };
                tenantStatus.StripeCustomerId = stripeCustomerId;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Could not update tenant status with Stripe Customer ID: {ex.Message}");
            }
        }

        // Helper method to update tenant with payment link info
        private async Task UpdateTenantWithPaymentLink(string tenantId, string paymentLinkId, int seats, ILogger log)
        {
            try
            {
                var tenantStatus = await GetTenantStatus(tenantId) ?? new TenantStatus { Id = tenantId };
                tenantStatus.CurrentPaymentLinkId = paymentLinkId;
                tenantStatus.SeatPriceId = _stripePerSeatPriceId;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Could not update tenant status with Payment Link ID: {ex.Message}");
            }
        }
    }
}
