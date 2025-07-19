using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using VaultsFunctions.Core.Models;

namespace VaultsFunctions.Core.Services
{
    public interface ITenantStatsService
    {
        Task<TenantUsageMetrics> GetUsageMetricsAsync(string tenantId, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null);
    }

    public class TenantStatsService : ITenantStatsService
    {
        private readonly CosmosClient _cosmosClient;
        private readonly Container _interactionsContainer;
        private readonly Container _conversationsContainer;
        private readonly Container _tenantsContainer;
        private readonly ILogger<TenantStatsService> _logger;

        public TenantStatsService(CosmosClient cosmosClient, ILogger<TenantStatsService> logger)
        {
            _cosmosClient = cosmosClient;
            _logger = logger;
            
            var database = _cosmosClient.GetDatabase("Vaults");
            _interactionsContainer = database.GetContainer("CopilotInteractions");
            _conversationsContainer = database.GetContainer("ConversationThreads");
            _tenantsContainer = database.GetContainer("Tenants");
        }

        public async Task<TenantUsageMetrics> GetUsageMetricsAsync(string tenantId, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null)
        {
            try
            {
                // Default to last 30 days if no dates specified
                var periodEnd = endDate ?? DateTimeOffset.UtcNow;
                var periodStart = startDate ?? periodEnd.AddDays(-30);

                _logger.LogInformation("Calculating usage metrics for tenant {TenantId} from {StartDate} to {EndDate}", 
                    tenantId, periodStart, periodEnd);

                // Get interaction data
                _logger.LogInformation("Step 1: Getting interaction stats for tenant {TenantId}", tenantId);
                var interactionStats = await GetInteractionStatsAsync(tenantId, periodStart, periodEnd);
                _logger.LogInformation("Step 1 Complete: Found {InteractionCount} interactions, {UserCount} unique users", 
                    interactionStats.TotalInteractions, interactionStats.UniqueUsers);
                
                // Get conversation data
                _logger.LogInformation("Step 2: Getting conversation stats for tenant {TenantId}", tenantId);
                var conversationStats = await GetConversationStatsAsync(tenantId, periodStart, periodEnd);
                _logger.LogInformation("Step 2 Complete: Found {ThreadCount} conversation threads", conversationStats.ThreadCount);

                // Calculate previous period for comparison
                _logger.LogInformation("Step 3: Getting previous period interaction stats for tenant {TenantId}", tenantId);
                var previousPeriodStart = periodStart.AddDays(-30);
                var previousInteractionStats = await GetInteractionStatsAsync(tenantId, previousPeriodStart, periodStart);
                _logger.LogInformation("Step 3 Complete: Previous period had {PreviousInteractions} interactions", 
                    previousInteractionStats.TotalInteractions);

                _logger.LogInformation("Step 4: Building final metrics object for tenant {TenantId}", tenantId);
                
                // Get licensed seat count first
                _logger.LogInformation("Step 4a: Getting licensed seat count for tenant {TenantId}", tenantId);
                var licensedSeats = await GetLicensedSeatCountAsync(tenantId);
                _logger.LogInformation("Step 4a Complete: Licensed seats = {LicensedSeats}", licensedSeats);
                
                // Get daily activity
                _logger.LogInformation("Step 4b: Getting daily activity for tenant {TenantId}", tenantId);
                var dailyActivity = await GetDailyActivityAsync(tenantId, periodStart, periodEnd);
                _logger.LogInformation("Step 4b Complete: Daily activity has {DayCount} days of data", dailyActivity.Count);
                
                // Get peak hours
                _logger.LogInformation("Step 4c: Getting peak usage hours for tenant {TenantId}", tenantId);
                var peakHours = await GetPeakUsageHoursAsync(tenantId, periodStart, periodEnd);
                _logger.LogInformation("Step 4c Complete: Peak hours has {HourCount} hours of data", peakHours.Count);

                _logger.LogInformation("Step 5: Creating final TenantUsageMetrics object for tenant {TenantId}", tenantId);
                var result = new TenantUsageMetrics
                {
                    TenantId = tenantId,
                    Period = new UsagePeriod
                    {
                        Start = periodStart.ToString("yyyy-MM-dd"),
                        End = periodEnd.ToString("yyyy-MM-dd")
                    },
                    Seats = new SeatUsage
                    {
                        Active = interactionStats.UniqueUsers,
                        Total = interactionStats.UniqueUsers, // For now, active = total
                        Licensed = licensedSeats // Pre-calculated above
                    },
                    Interactions = new InteractionUsage
                    {
                        Total = interactionStats.TotalInteractions,
                        DailyAverage = CalculateDailyAverage(interactionStats.TotalInteractions, periodStart, periodEnd),
                        GrowthRate = CalculateGrowthRate(interactionStats.TotalInteractions, previousInteractionStats.TotalInteractions)
                    },
                    Apps = interactionStats.AppBreakdown,
                    Conversations = new ConversationUsage
                    {
                        Threads = conversationStats.ThreadCount,
                        AverageLength = conversationStats.AverageMessageLength,
                        TotalMessages = conversationStats.TotalMessages
                    },
                    Activity = new ActivityMetrics
                    {
                        DailyActivity = dailyActivity, // Pre-calculated above
                        PeakHours = peakHours // Pre-calculated above
                    }
                };
                
                _logger.LogInformation("Step 6: Successfully created usage metrics for tenant {TenantId} with {ActiveUsers} active users and {TotalInteractions} interactions", 
                    tenantId, interactionStats.UniqueUsers, interactionStats.TotalInteractions);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DETAILED ERROR: Failed to calculate usage metrics for tenant {TenantId}", tenantId);
                _logger.LogError("Exception Type: {ExceptionType}", ex.GetType().FullName);
                _logger.LogError("Exception Message: {Message}", ex.Message);
                _logger.LogError("Stack Trace: {StackTrace}", ex.StackTrace);
                
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner Exception Type: {InnerType}", ex.InnerException.GetType().FullName);
                    _logger.LogError("Inner Exception Message: {InnerMessage}", ex.InnerException.Message);
                    _logger.LogError("Inner Stack Trace: {InnerStackTrace}", ex.InnerException.StackTrace);
                }
                
                // Return default metrics on error
                return new TenantUsageMetrics
                {
                    TenantId = tenantId,
                    Period = new UsagePeriod
                    {
                        Start = (startDate ?? DateTimeOffset.UtcNow.AddDays(-30)).ToString("yyyy-MM-dd"),
                        End = (endDate ?? DateTimeOffset.UtcNow).ToString("yyyy-MM-dd")
                    },
                    Seats = new SeatUsage { Active = 0, Total = 0, Licensed = 0 },
                    Interactions = new InteractionUsage { Total = 0, DailyAverage = 0, GrowthRate = 0 },
                    Apps = new Dictionary<string, int>(),
                    Conversations = new ConversationUsage { Threads = 0, AverageLength = 0, TotalMessages = 0 },
                    Activity = new ActivityMetrics 
                    { 
                        DailyActivity = new Dictionary<string, int>(),
                        PeakHours = new Dictionary<int, int>()
                    }
                };
            }
        }

        private async Task<InteractionStats> GetInteractionStatsAsync(string tenantId, DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var query = new QueryDefinition(@"
                SELECT 
                    COUNT(1) as totalInteractions,
                    c.appContext
                FROM c 
                WHERE c.tenantId = @tenantId 
                AND c.createdDateTime >= @startDate 
                AND c.createdDateTime <= @endDate
                GROUP BY c.appContext")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@startDate", startDate)
                .WithParameter("@endDate", endDate);

            using var iterator = _interactionsContainer.GetItemQueryIterator<dynamic>(query);
            
            var stats = new InteractionStats
            {
                AppBreakdown = new Dictionary<string, int>()
            };

            var uniqueUserIds = new HashSet<string>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    stats.TotalInteractions += (int)(item.totalInteractions ?? 0);
                    
                    string appContext = item.appContext ?? "Unknown";
                    int appCount = (int)(item.totalInteractions ?? 0);
                    
                    if (stats.AppBreakdown.ContainsKey(appContext))
                        stats.AppBreakdown[appContext] += appCount;
                    else
                        stats.AppBreakdown[appContext] = appCount;
                }
            }

            // Get unique user count separately for accuracy
            var userQuery = new QueryDefinition(@"
                SELECT DISTINCT c.userId 
                FROM c 
                WHERE c.tenantId = @tenantId 
                AND c.createdDateTime >= @startDate 
                AND c.createdDateTime <= @endDate")
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@startDate", startDate)
                .WithParameter("@endDate", endDate);

            using var userIterator = _interactionsContainer.GetItemQueryIterator<dynamic>(userQuery);
            
            while (userIterator.HasMoreResults)
            {
                var response = await userIterator.ReadNextAsync();
                foreach (var item in response)
                {
                    if (item.userId != null)
                        uniqueUserIds.Add((string)item.userId);
                }
            }

            stats.UniqueUsers = uniqueUserIds.Count;
            return stats;
        }

        private async Task<ConversationStats> GetConversationStatsAsync(string tenantId, DateTimeOffset startDate, DateTimeOffset endDate)
        {
            try
            {
                var query = new QueryDefinition(@"
                    SELECT 
                        COUNT(1) as threadCount,
                        AVG(c.messageCount) as avgMessageLength,
                        SUM(c.messageCount) as totalMessages
                    FROM c 
                    WHERE c.tenantId = @tenantId 
                    AND c.lastActivity >= @startDate 
                    AND c.lastActivity <= @endDate")
                    .WithParameter("@tenantId", tenantId)
                    .WithParameter("@startDate", startDate)
                    .WithParameter("@endDate", endDate);

                using var iterator = _conversationsContainer.GetItemQueryIterator<dynamic>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var result = response.FirstOrDefault();
                    
                    return new ConversationStats
                    {
                        ThreadCount = (int)(result?.threadCount ?? 0),
                        AverageMessageLength = (double)(result?.avgMessageLength ?? 0),
                        TotalMessages = (int)(result?.totalMessages ?? 0)
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting conversation stats for tenant {TenantId}, using defaults", tenantId);
            }

            return new ConversationStats { ThreadCount = 0, AverageMessageLength = 0, TotalMessages = 0 };
        }

        private async Task<int> GetLicensedSeatCountAsync(string tenantId)
        {
            try
            {
                // Query tenant configuration for licensed seat count
                var query = new QueryDefinition(@"
                    SELECT c.licenseInfo.seatCount, c.licenseInfo.planType, c.licenseInfo.lastUpdated
                    FROM c 
                    WHERE c.id = @tenantId AND c.type = 'tenant'")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _tenantsContainer.GetItemQueryIterator<dynamic>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var result = response.FirstOrDefault();
                    
                    if (result?.licenseInfo?.seatCount != null)
                    {
                        var seatCount = (int)result.licenseInfo.seatCount;
                        var planType = (string)result.licenseInfo.planType;
                        
                        _logger.LogDebug("Retrieved licensed seat count for tenant {TenantId}: {SeatCount} ({PlanType})", 
                            tenantId, seatCount, planType);
                        
                        return Math.Max(seatCount, 1);
                    }
                }

                // Fallback: If no license info is configured, estimate based on usage patterns
                var uniqueUsers = await GetUniqueUserCountAsync(tenantId);
                var estimatedSeats = Math.Max(uniqueUsers * 2, 10); // Assume 50% utilization rate with minimum of 10
                
                _logger.LogInformation("No license configuration found for tenant {TenantId}, using estimated seat count: {EstimatedSeats} (based on {UniqueUsers} active users)", 
                    tenantId, estimatedSeats, uniqueUsers);
                
                return estimatedSeats;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting licensed seat count for tenant {TenantId}, using unique user count as fallback", tenantId);
                return Math.Max(await GetUniqueUserCountAsync(tenantId), 1);
            }
        }

        private async Task<int> GetUniqueUserCountAsync(string tenantId)
        {
            try
            {
                var query = new QueryDefinition(@"
                    SELECT COUNT(DISTINCT c.userId) as uniqueUsers
                    FROM c 
                    WHERE c.tenantId = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _interactionsContainer.GetItemQueryIterator<dynamic>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var result = response.FirstOrDefault();
                    return (int)(result?.uniqueUsers ?? 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting unique user count for tenant {TenantId}", tenantId);
            }

            return 0;
        }

        private async Task<Dictionary<string, int>> GetDailyActivityAsync(string tenantId, DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var dailyActivity = new Dictionary<string, int>();
            
            try
            {
                var query = new QueryDefinition(@"
                    SELECT 
                        SUBSTRING(c.createdDateTime, 0, 10) as date,
                        COUNT(1) as count
                    FROM c 
                    WHERE c.tenantId = @tenantId 
                    AND c.createdDateTime >= @startDate 
                    AND c.createdDateTime <= @endDate
                    GROUP BY SUBSTRING(c.createdDateTime, 0, 10)")
                    .WithParameter("@tenantId", tenantId)
                    .WithParameter("@startDate", startDate)
                    .WithParameter("@endDate", endDate);

                using var iterator = _interactionsContainer.GetItemQueryIterator<dynamic>(query);
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        string date = item.date ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
                        int count = (int)(item.count ?? 0);
                        dailyActivity[date] = count;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting daily activity for tenant {TenantId}", tenantId);
            }

            return dailyActivity;
        }

        private async Task<Dictionary<int, int>> GetPeakUsageHoursAsync(string tenantId, DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var hourlyUsage = new Dictionary<int, int>();
            
            try
            {
                var query = new QueryDefinition(@"
                    SELECT 
                        DateTimePart('hour', c.createdDateTime) as hour,
                        COUNT(1) as count
                    FROM c 
                    WHERE c.tenantId = @tenantId 
                    AND c.createdDateTime >= @startDate 
                    AND c.createdDateTime <= @endDate
                    GROUP BY DateTimePart('hour', c.createdDateTime)")
                    .WithParameter("@tenantId", tenantId)
                    .WithParameter("@startDate", startDate)
                    .WithParameter("@endDate", endDate);

                using var iterator = _interactionsContainer.GetItemQueryIterator<dynamic>(query);
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        int hour = (int)(item.hour ?? 0);
                        int count = (int)(item.count ?? 0);
                        hourlyUsage[hour] = count;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error getting peak usage hours for tenant {TenantId}", tenantId);
            }

            return hourlyUsage;
        }

        private double CalculateDailyAverage(int totalInteractions, DateTimeOffset startDate, DateTimeOffset endDate)
        {
            var days = Math.Max(1, (endDate - startDate).Days);
            return Math.Round((double)totalInteractions / days, 1);
        }

        private double CalculateGrowthRate(int current, int previous)
        {
            if (previous == 0) return current > 0 ? 100.0 : 0.0;
            return Math.Round(((double)(current - previous) / previous) * 100, 1);
        }
    }

    // Supporting classes
    public class InteractionStats
    {
        public int TotalInteractions { get; set; }
        public int UniqueUsers { get; set; }
        public Dictionary<string, int> AppBreakdown { get; set; } = new Dictionary<string, int>();
    }

    public class ConversationStats
    {
        public int ThreadCount { get; set; }
        public double AverageMessageLength { get; set; }
        public int TotalMessages { get; set; }
    }
}