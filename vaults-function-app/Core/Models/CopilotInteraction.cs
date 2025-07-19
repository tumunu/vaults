using System;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public class CopilotInteraction
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; } // Added for partition key

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("userPrincipalName")]
        public string UserPrincipalName { get; set; }

        [JsonProperty("threadId")]
        public string ThreadId { get; set; }

        [JsonProperty("createdDateTime")]
        public DateTimeOffset CreatedDateTime { get; set; }

        [JsonProperty("prompt")]
        public string UserPrompt { get; set; }

        [JsonProperty("response")]
        public string AiResponse { get; set; }

        [JsonProperty("appContext")]
        public string AppContext { get; set; } // e.g., Teams, Word, Excel

        // Additional properties for enrichment (to be populated later)
        [JsonProperty("userRoles")]
        public string[] UserRoles { get; set; }

        [JsonProperty("sensitivityLabels")]
        public string[] SensitivityLabels { get; set; }

        [JsonProperty("hasPii")]
        public bool HasPii { get; set; }

        [JsonProperty("conversationId")]
        public string ConversationId { get; set; } // For threading messages

        [JsonProperty("sessionId")]
        public string SessionId { get; set; } // For session tracking

        [JsonProperty("isProcessed")] // Added explicit processed flag
        public bool IsProcessed { get; set; } = false;

        [JsonProperty("processedDateTime")]
        public DateTimeOffset? ProcessedDateTime { get; set; } // Changed to nullable

        [JsonProperty("isExported")]
        public bool IsExported { get; set; } = false;

        [JsonProperty("exportedDateTime")]
        public DateTimeOffset? ExportedDateTime { get; set; }
    }
}
