using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using VaultsFunctions.Core;
using VaultsFunctions.Core.Models;

namespace VaultsFunctions.Functions.Admin
{
    public class AdminStatsFunction
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;
        private readonly Container _interactionsContainer;
        private readonly Container _auditPoliciesContainer;

        public AdminStatsFunction(IConfiguration configuration, CosmosClient cosmosClient)
        {
            _configuration = configuration;
            _cosmosClient = cosmosClient;
            var database = _cosmosClient.GetDatabase(Constants.Databases.MainDatabase);
            _tenantsContainer = database.GetContainer(Constants.Databases.TenantsContainer);
            _interactionsContainer = database.GetContainer(Constants.Databases.InteractionsContainer);
            _auditPoliciesContainer = database.GetContainer(Constants.Databases.AuditPoliciesContainer);
        }

        [Function("AdminStatsFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "stats/adminstats")] HttpRequestData req,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger<AdminStatsFunction>();
            logger.LogInformation("Admin stats function started.");

            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = queryParams["tenantId"] ?? "default-tenant";
                
                // Get tenant status
                var tenantStatus = await GetTenantStatus(tenantId);

                // Get interaction statistics
                var interactionStats = await GetInteractionStats(tenantId);

                // Get user statistics
                var userStats = await GetUserStats(tenantId);

                // Get policy statistics
                var policyStats = await GetPolicyStats(tenantId);

                var adminStats = new
                {
                    TenantId = tenantId,
                    TotalUsers = userStats.TotalUsers,
                    ActiveUsers = userStats.ActiveUsers,
                    TotalPolicies = policyStats.TotalPolicies,
                    ActivePolicies = policyStats.ActivePolicies,
                    HighRiskPolicies = policyStats.HighRiskPolicies,
                    TotalInteractions = interactionStats.TotalInteractions,
                    InteractionsWithPii = interactionStats.InteractionsWithPii,
                    PolicyViolations = policyStats.TotalViolations,
                    LastSyncTime = tenantStatus?.LastSyncTime,
                    LastFailureMessage = tenantStatus?.LastFailureMessage,
                    ProcessedAt = DateTimeOffset.UtcNow
                };

                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteAsJsonAsync(adminStats);
                return response;
            }
            catch (Exception ex)
            {
                var response = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await response.WriteStringAsync($"Error: {ex.Message}");
                logger.LogError(ex, "Error in Admin stats function");
                return response;
            }
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
            }

            return null;
        }

        private async Task<(int TotalInteractions, int InteractionsWithPii)> GetInteractionStats(string tenantId)
        {
            try
            {
                // Query for total interactions count
                var totalQuery = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var totalIterator = _interactionsContainer.GetItemQueryIterator<int>(totalQuery);
                int totalInteractions = 0;
                
                if (totalIterator.HasMoreResults)
                {
                    var response = await totalIterator.ReadNextAsync();
                    totalInteractions = response.FirstOrDefault();
                }

                // Query for interactions with PII
                var piiQuery = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId AND c.hasPii = true")
                    .WithParameter("@tenantId", tenantId);

                using var piiIterator = _interactionsContainer.GetItemQueryIterator<int>(piiQuery);
                int interactionsWithPii = 0;
                
                if (piiIterator.HasMoreResults)
                {
                    var response = await piiIterator.ReadNextAsync();
                    interactionsWithPii = response.FirstOrDefault();
                }

                return (totalInteractions, interactionsWithPii);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not retrieve interaction stats: {ex.Message}");
                return (0, 0);
            }
        }

        private async Task<(int TotalUsers, int ActiveUsers)> GetUserStats(string tenantId)
        {
            try
            {
                // Since user data comes from Graph API, we'll estimate based on interactions
                var query = new QueryDefinition("SELECT DISTINCT c.userId FROM c WHERE c.tenantId = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _interactionsContainer.GetItemQueryIterator<dynamic>(query);
                var uniqueUsers = new HashSet<string>();
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (item.userId != null)
                        {
                            uniqueUsers.Add(item.userId.ToString());
                        }
                    }
                }

                int totalUsers = uniqueUsers.Count();
                int activeUsers = Math.Max(1, (int)(totalUsers * 0.8)); // Estimate 80% active

                return (totalUsers, activeUsers);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not retrieve user stats: {ex.Message}");
                return (0, 0);
            }
        }

        private async Task<(int TotalPolicies, int ActivePolicies, int HighRiskPolicies, int TotalViolations)> GetPolicyStats(string tenantId)
        {
            try
            {
                // Get all policies for the tenant
                var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _auditPoliciesContainer.GetItemQueryIterator<AuditPolicy>(query);
                var policies = new List<AuditPolicy>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    policies.AddRange(response);
                }

                var totalPolicies = policies.Count;
                var activePolicies = policies.Count(p => p.IsEnabled);
                var highRiskPolicies = policies.Count(p => p.RiskLevel == RiskLevel.High && p.IsEnabled);
                var totalViolations = policies.Sum(p => p.TriggerCount);

                return (totalPolicies, activePolicies, highRiskPolicies, totalViolations);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not retrieve policy stats: {ex.Message}");
                return (0, 0, 0, 0);
            }
        }
    }
}
