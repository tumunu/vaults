using System.Collections.Generic;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public class TenantUsageMetrics
    {
        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("period")]
        public UsagePeriod Period { get; set; }

        [JsonProperty("seats")]
        public SeatUsage Seats { get; set; }

        [JsonProperty("interactions")]
        public InteractionUsage Interactions { get; set; }

        [JsonProperty("apps")]
        public Dictionary<string, int> Apps { get; set; } = new Dictionary<string, int>();

        [JsonProperty("conversations")]
        public ConversationUsage Conversations { get; set; }

        [JsonProperty("activity")]
        public ActivityMetrics Activity { get; set; }
    }

    public class UsagePeriod
    {
        [JsonProperty("start")]
        public string Start { get; set; }

        [JsonProperty("end")]
        public string End { get; set; }
    }

    public class SeatUsage
    {
        [JsonProperty("active")]
        public int Active { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("licensed")]
        public int Licensed { get; set; }

        [JsonProperty("utilizationRate")]
        public double UtilizationRate => Licensed > 0 ? (double)Active / Licensed * 100 : 0;
    }

    public class InteractionUsage
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("dailyAverage")]
        public double DailyAverage { get; set; }

        [JsonProperty("growthRate")]
        public double GrowthRate { get; set; }
    }

    public class ConversationUsage
    {
        [JsonProperty("threads")]
        public int Threads { get; set; }

        [JsonProperty("averageLength")]
        public double AverageLength { get; set; }

        [JsonProperty("totalMessages")]
        public int TotalMessages { get; set; }
    }

    public class ActivityMetrics
    {
        [JsonProperty("dailyActivity")]
        public Dictionary<string, int> DailyActivity { get; set; } = new Dictionary<string, int>();

        [JsonProperty("peakHours")]
        public Dictionary<int, int> PeakHours { get; set; } = new Dictionary<int, int>();
    }
}