using System;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public enum ConversationInteractionType
    {
        UserPrompt,
        AiResponse
    }

    public class ConversationInteractionBody
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("contentType")]
        public string ContentType { get; set; } = "text/plain";
    }

    public class ConversationInteraction
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        [JsonProperty("interactionType")]
        public ConversationInteractionType InteractionType { get; set; }

        [JsonProperty("body")]
        public ConversationInteractionBody Body { get; set; }

        [JsonProperty("createdDateTime")]
        public DateTimeOffset CreatedDateTime { get; set; }
    }
}