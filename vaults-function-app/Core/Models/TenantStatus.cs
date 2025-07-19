using System;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public class TenantStatus
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("lastSyncTime")]
        public DateTimeOffset? LastSyncTime { get; set; }

        [JsonProperty("totalInteractionsProcessed")]
        public long TotalInteractionsProcessed { get; set; }

        [JsonProperty("lastFailureMessage")]
        public string LastFailureMessage { get; set; }

        [JsonProperty("lastMonitoringRun")]
        public DateTimeOffset? LastMonitoringRun { get; set; }

        // Stripe related properties
        [JsonProperty("stripeCustomerId")]
        public string StripeCustomerId { get; set; }

        [JsonProperty("stripeSubscriptionId")]
        public string StripeSubscriptionId { get; set; }

        [JsonProperty("stripeSubscriptionStatus")]
        public string StripeSubscriptionStatus { get; set; }

        [JsonProperty("stripeCurrentPeriodEnd")]
        public DateTimeOffset? StripeCurrentPeriodEnd { get; set; }

        [JsonProperty("lastInvoiceId")]
        public string LastInvoiceId { get; set; }

        [JsonProperty("lastInvoiceAmount")]
        public long? LastInvoiceAmount { get; set; } // Amount in cents

        [JsonProperty("lastInvoiceStatus")]
        public string LastInvoiceStatus { get; set; }

        // Onboarding related properties
        [JsonProperty("azureAdAppId")]
        public string AzureAdAppId { get; set; }

        [JsonProperty("azureStorageAccountName")]
        public string AzureStorageAccountName { get; set; }

        [JsonProperty("azureStorageContainerName")]
        public string AzureStorageContainerName { get; set; }

        [JsonProperty("retentionPolicy")]
        public string RetentionPolicy { get; set; }

        [JsonProperty("customRetentionDays")]
        public int? CustomRetentionDays { get; set; }

        [JsonProperty("exportSchedule")]
        public string ExportSchedule { get; set; }

        [JsonProperty("exportTime")]
        public string ExportTime { get; set; }

        [JsonProperty("onboardingComplete")]
        public bool OnboardingComplete { get; set; } = false;

        [JsonProperty("updatedAt")]
        public DateTimeOffset UpdatedAt { get; set; }

        // B2B Invitation tracking properties
        [JsonProperty("adminEmail")]
        public string AdminEmail { get; set; }

        [JsonProperty("invitationState")]
        public string InvitationState { get; set; } // Pending, Sent, Completed, Failed, Skipped

        [JsonProperty("invitationDateUtc")]
        public DateTimeOffset? InvitationDateUtc { get; set; }

        [JsonProperty("invitedBy")]
        public string InvitedBy { get; set; }

        [JsonProperty("graphInviteId")]
        public string GraphInviteId { get; set; }

        [JsonProperty("graphStatus")]
        public string GraphStatus { get; set; }

        [JsonProperty("invitationRetryCount")]
        public int InvitationRetryCount { get; set; } = 0;

        [JsonProperty("lastInvitationError")]
        public string LastInvitationError { get; set; }

        // Seat-based billing properties
        [JsonProperty("purchasedSeats")]
        public int PurchasedSeats { get; set; } = 0;

        [JsonProperty("activeSeats")]
        public int ActiveSeats { get; set; } = 0;

        [JsonProperty("maxSeats")]
        public int MaxSeats { get; set; } = 0;

        [JsonProperty("isEnterprise")]
        public bool IsEnterprise { get; set; } = false;

        [JsonProperty("seatPriceId")]
        public string SeatPriceId { get; set; }

        [JsonProperty("currentPaymentLinkId")]
        public string CurrentPaymentLinkId { get; set; }

        [JsonProperty("lastSeatUpdate")]
        public DateTimeOffset? LastSeatUpdate { get; set; }
    }
}
