using System;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public enum InvitationState
    {
        Pending,
        Sent,
        Completed,
        Failed,
        Skipped
    }

    public class InvitationResult
    {
        public InvitationState State { get; set; }
        public string GraphInviteId { get; set; }
        public string ErrorMessage { get; set; }
        public string UserId { get; set; }
        public DateTimeOffset Timestamp { get; set; }

        public static InvitationResult Sent(string graphInviteId)
        {
            return new InvitationResult
            {
                State = InvitationState.Sent,
                GraphInviteId = graphInviteId,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public static InvitationResult Skipped(string userId)
        {
            return new InvitationResult
            {
                State = InvitationState.Skipped,
                UserId = userId,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public static InvitationResult Failed(string errorMessage)
        {
            return new InvitationResult
            {
                State = InvitationState.Failed,
                ErrorMessage = errorMessage,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public static InvitationResult Completed(string userId)
        {
            return new InvitationResult
            {
                State = InvitationState.Completed,
                UserId = userId,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    public class InvitationRequest
    {
        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("adminEmail")]
        public string AdminEmail { get; set; }

        [JsonProperty("redirectUrl")]
        public string RedirectUrl { get; set; }

        [JsonProperty("invitedBy")]
        public string InvitedBy { get; set; }

        [JsonProperty("requestId")]
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("timestamp")]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        public bool IsValid()
        {
            return !string.IsNullOrEmpty(TenantId) && 
                   !string.IsNullOrEmpty(AdminEmail) && 
                   AdminEmail.Contains('@');
        }
    }
}