using System;
using System.IO;
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
using VaultsFunctions.Core.Helpers;
using Newtonsoft.Json;

namespace VaultsFunctions.Functions.Admin
{
    public class AuditPolicyFunction
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _auditPoliciesContainer;

        public AuditPolicyFunction(IConfiguration configuration, CosmosClient cosmosClient)
        {
            _configuration = configuration;
            _cosmosClient = cosmosClient;
            var database = _cosmosClient.GetDatabase(Constants.Databases.MainDatabase);
            _auditPoliciesContainer = database.GetContainer(Constants.Databases.AuditPoliciesContainer);
        }

        [Function("GetAuditPolicies")]
        public async Task<HttpResponseData> GetPolicies(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = Constants.ApiRoutes.AdminPolicies)] HttpRequestData req,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger<AuditPolicyFunction>();
            logger.LogInformation("Get audit policies function started.");

            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = queryParams["tenantId"];

                if (string.IsNullOrEmpty(tenantId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync("tenantId parameter is required");
                    return response;
                }

                var policies = await GetPoliciesForTenant(tenantId);

                // If no policies exist, create default ones
                if (!policies.Any())
                {
                    policies = await CreateDefaultPolicies(tenantId);
                }

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { policies = policies });
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting audit policies");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Error: {ex.Message}");
                return response;
            }
        }

        [Function("UpdateAuditPolicies")]
        public async Task<HttpResponseData> UpdatePolicies(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", "put", Route = Constants.ApiRoutes.AdminPoliciesConfig)] HttpRequestData req,
            FunctionContext executionContext)
        {
            var logger = executionContext.GetLogger<AuditPolicyFunction>();
            logger.LogInformation("Update audit policies function started.");

            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                var configRequest = JsonConvert.DeserializeObject<PolicyConfigurationRequest>(requestBody);

                if (configRequest == null || string.IsNullOrEmpty(configRequest.TenantId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync("Invalid request body or missing tenantId");
                    return response;
                }

                var updatedPolicies = new List<AuditPolicy>();

                foreach (var policy in configRequest.Policies)
                {
                    policy.TenantId = configRequest.TenantId;
                    policy.UpdatedAt = DateTimeOffset.UtcNow;
                    
                    // Validate policy data
                    if (string.IsNullOrEmpty(policy.Name))
                    {
                        response.StatusCode = HttpStatusCode.BadRequest;
                        await response.WriteStringAsync("Policy name is required");
                        return response;
                    }

                    if (policy.Sensitivity < 1 || policy.Sensitivity > 10)
                    {
                        response.StatusCode = HttpStatusCode.BadRequest;
                        await response.WriteStringAsync("Sensitivity must be between 1 and 10");
                        return response;
                    }

                    // Upsert the policy
                    var upsertResponse = await _auditPoliciesContainer.UpsertItemAsync(
                        policy, 
                        new PartitionKey(policy.TenantId));
                    
                    updatedPolicies.Add(upsertResponse.Resource);
                }

                logger.LogInformation($"Updated {updatedPolicies.Count} policies for tenant {configRequest.TenantId}");

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { 
                    message = "Policies updated successfully",
                    updatedCount = updatedPolicies.Count,
                    policies = updatedPolicies
                });
                return response;
            }
            catch (System.Text.Json.JsonException ex)
            {
                logger.LogError(ex, "Invalid JSON in request body");
                response.StatusCode = HttpStatusCode.BadRequest;
                await response.WriteStringAsync("Invalid JSON format");
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating audit policies");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Error: {ex.Message}");
                return response;
            }
        }

        [Function("DeleteAuditPolicy")]
        public async Task<HttpResponseData> DeletePolicy(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "policies/{policyId}")] HttpRequestData req,
            FunctionContext executionContext,
            string policyId)
        {
            var logger = executionContext.GetLogger<AuditPolicyFunction>();
            logger.LogInformation($"Delete audit policy function started for policy {policyId}.");

            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = queryParams["tenantId"];

                if (string.IsNullOrEmpty(tenantId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync("tenantId parameter is required");
                    return response;
                }

                await _auditPoliciesContainer.DeleteItemAsync<AuditPolicy>(
                    policyId, 
                    new PartitionKey(tenantId));

                logger.LogInformation($"Deleted policy {policyId} for tenant {tenantId}");

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(new { message = "Policy deleted successfully" });
                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                await response.WriteStringAsync("Policy not found");
                return response;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting audit policy");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteStringAsync($"Error: {ex.Message}");
                return response;
            }
        }

        private async Task<List<AuditPolicy>> GetPoliciesForTenant(string tenantId)
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.tenantId = @tenantId")
                .WithParameter("@tenantId", tenantId);

            using var iterator = _auditPoliciesContainer.GetItemQueryIterator<AuditPolicy>(query);
            var policies = new List<AuditPolicy>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                policies.AddRange(response);
            }

            return policies.OrderBy(p => p.Name).ToList();
        }

        private async Task<List<AuditPolicy>> CreateDefaultPolicies(string tenantId)
        {
            var defaultPolicies = DefaultPolicies.GetDefaultPolicies(tenantId);
            var createdPolicies = new List<AuditPolicy>();

            foreach (var policy in defaultPolicies)
            {
                var createResponse = await _auditPoliciesContainer.CreateItemAsync(
                    policy, 
                    new PartitionKey(policy.TenantId));
                
                createdPolicies.Add(createResponse.Resource);
            }

            return createdPolicies;
        }
    }
}