using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public class ConversationThread
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("userName")]
        public string UserName { get; set; }

        [JsonProperty("userEmail")]
        public string UserEmail { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("preview")]
        public string Preview { get; set; }

        [JsonProperty("messageCount")]
        public int MessageCount { get; set; }

        [JsonProperty("lastActivity")]
        public DateTime LastActivity { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; } // 'active' | 'archived' | 'flagged'

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }
    }
}
