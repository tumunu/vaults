using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using System.Linq;
using System.Collections.Generic;
using VaultsFunctions.Core.Models;
using Microsoft.Azure.Functions.Worker;

namespace VaultsFunctions.Functions.Admin
{
    public class MonitoringFunction
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;

        public MonitoringFunction(IConfiguration configuration)
        {
            _configuration = configuration;

            // Initialize Cosmos DB Client
            var cosmosDbConnectionString = _configuration["COSMOS_DB_CONNECTION_STRING"];
            _cosmosClient = new CosmosClient(cosmosDbConnectionString);
            
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");
        }

        [Function("MonitoringFunction")]
        public async Task Run([TimerTrigger("0 0 2 * * *")] FunctionContext context)
        {
            var log = context.GetLogger<MonitoringFunction>();
            log.LogInformation($"MonitoringFunction started at: {DateTime.UtcNow}");

            try
            {
                // Fetch all tenants to update their status
                var query = new QueryDefinition("SELECT * FROM c"); // Select full document
                using var iterator = _tenantsContainer.GetItemQueryIterator<TenantStatus>(query); // Use TenantStatus
                
                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var tenantStatus in response) // Iterate over TenantStatus objects
                    {
                        string tenantId = tenantStatus.Id;
                        log.LogInformation($"Updating status for tenant: {tenantId}");

                        try
                        {
                            // Update properties directly on the object and then upsert
                            tenantStatus.LastMonitoringRun = DateTimeOffset.UtcNow;
                            // For now, we'll just update the timestamp and clear failures.
                            // In a full implementation, this function would aggregate data
                            // from the IngestionFunction's logs or a separate metrics store
                            // to get actual processed counts and failures.
                            tenantStatus.TotalInteractionsProcessed = tenantStatus.TotalInteractionsProcessed; // Keep current count
                            tenantStatus.LastFailureMessage = null; // Clear previous failures
                            tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                            await _tenantsContainer.UpsertItemAsync(
                                tenantStatus,
                                new PartitionKey(tenantId)
                            );
                            log.LogInformation($"Successfully updated status for tenant: {tenantId}");
                        }
                        catch (Exception tenantEx)
                        {
                            log.LogError(tenantEx, $"Failed to update status for tenant {tenantId}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error in MonitoringFunction");
            }

            log.LogInformation($"MonitoringFunction finished at: {DateTime.UtcNow}");
        }
    }
}
