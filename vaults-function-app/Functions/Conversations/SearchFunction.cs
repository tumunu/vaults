using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Web;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using VaultsFunctions.Core.Models; // Assuming ConversationThread model is here or similar
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core;

namespace VaultsFunctions.Functions.Conversations
{
    public class SearchFunction
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _conversationsContainer;
        private readonly GraphCopilotService _graphCopilotService;

        public SearchFunction(IConfiguration configuration, CosmosClient cosmosClient, GraphCopilotService graphCopilotService)
        {
            _configuration = configuration;
            _cosmosClient = cosmosClient;
            _graphCopilotService = graphCopilotService;
            
            // Initialize database and container lazily
            try
            {
                var database = _cosmosClient.GetDatabase(Constants.Databases.MainDatabase);
                _conversationsContainer = database.GetContainer(Constants.Databases.ProcessedConversationsContainer);
            }
            catch (Exception ex)
            {
                // Log but don't crash - will be handled in function execution
                Console.WriteLine($"Cosmos DB initialization warning: {ex.Message}");
            }
        }

        [Function("SearchFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "searchfunction")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<SearchFunction>();
            log.LogInformation("SearchFunction received a request.");

            var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
            string tenantId = queryParams["tenantId"];
            string type = queryParams["type"];
            string user = queryParams["user"];
            string startDate = queryParams["startDate"];
            string endDate = queryParams["endDate"];
            string keyword = queryParams["keyword"];
            int page = int.TryParse(queryParams["page"], out int p) ? p : 1;
            int pageSize = int.TryParse(queryParams["pageSize"], out int ps) ? ps : 10;

            if (string.IsNullOrEmpty(tenantId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Tenant ID is required." });
                return badRequestResponse;
            }

            // Check if container is available
            if (_conversationsContainer == null)
            {
                var serviceUnavailableResponse = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                await serviceUnavailableResponse.WriteAsJsonAsync(new { error = "Database service is not available." });
                return serviceUnavailableResponse;
            }

            try
            {
                // Handle specialized search types
                if (!string.IsNullOrEmpty(type))
                {
                    return await HandleSpecializedSearch(req, tenantId, type, log);
                }

                // Build Cosmos DB query for conversation search
                var sqlQuery = "SELECT * FROM c WHERE c.tenantId = @tenantId";
                var parameters = new List<Tuple<string, object>>
                {
                    Tuple.Create("@tenantId", (object)tenantId)
                };

                if (!string.IsNullOrEmpty(user))
                {
                    sqlQuery += " AND c.userId = @userId";
                    parameters.Add(Tuple.Create("@userId", (object)user));
                }
                if (!string.IsNullOrEmpty(keyword))
                {
                    // Case-insensitive search for keyword in title or preview/content
                    sqlQuery += " AND (CONTAINS(c.title, @keyword, true) OR CONTAINS(c.preview, @keyword, true) OR CONTAINS(c.content, @keyword, true))";
                    parameters.Add(Tuple.Create("@keyword", (object)keyword));
                }
                if (!string.IsNullOrEmpty(startDate))
                {
                    // Assuming lastActivity is stored as ISO 8601 string or Unix timestamp
                    sqlQuery += " AND c.lastActivity >= @startDate";
                    parameters.Add(Tuple.Create("@startDate", (object)startDate));
                }
                if (!string.IsNullOrEmpty(endDate))
                {
                    sqlQuery += " AND c.lastActivity <= @endDate";
                    parameters.Add(Tuple.Create("@endDate", (object)endDate));
                }
                // Add status filter if needed, assuming 'status' field exists in Cosmos DB
                // if (!string.IsNullOrEmpty(status)) { sqlQuery += " AND c.status = @status"; parameters.Add(Tuple.Create("@status", (object)status)); }


                // For pagination, Cosmos DB typically uses continuation tokens for efficient paging.
                // For simplicity here, we'll use OFFSET LIMIT, but for large datasets, continuation tokens are better.
                // You'd need to manage continuation tokens on the client side or implement a more complex paging strategy.
                int offset = (page - 1) * pageSize;
                sqlQuery += $" ORDER BY c.lastActivity DESC OFFSET {offset} LIMIT {pageSize}";

                var queryDefinition = new QueryDefinition(sqlQuery);
                foreach (var param in parameters)
                {
                    queryDefinition.WithParameter(param.Item1, param.Item2);
                }

                List<ConversationThread> results = new List<ConversationThread>();
                using (var queryIterator = _conversationsContainer.GetItemQueryIterator<ConversationThread>(queryDefinition))
                {
                    while (queryIterator.HasMoreResults)
                    {
                        foreach (var item in await queryIterator.ReadNextAsync())
                        {
                            results.Add(item);
                        }
                    }
                }

                // For total pages, you'd typically run a separate COUNT query without OFFSET/LIMIT
                // Or, if you're using continuation tokens, you might not have a total page count easily.
                // For now, we'll just return the current page results.
                // A more robust solution would involve a separate count query or a different paging approach.
                int totalCount = 0; // Placeholder for actual total count
                try
                {
                    var countQuery = "SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId";
                    var countQueryDefinition = new QueryDefinition(countQuery);
                    foreach (var param in parameters)
                    {
                        // Only include parameters relevant to the count query (exclude OFFSET/LIMIT specific ones)
                        if (param.Item1 != "@page" && param.Item1 != "@pageSize")
                        {
                            countQueryDefinition.WithParameter(param.Item1, param.Item2);
                        }
                    }
                    using var countIterator = _conversationsContainer.GetItemQueryIterator<int>(countQueryDefinition);
                    if (countIterator.HasMoreResults)
                    {
                        var countResponse = await countIterator.ReadNextAsync();
                        totalCount = countResponse.FirstOrDefault();
                    }
                }
                catch (Exception countEx)
                {
                    log.LogWarning($"Could not get total count for search: {countEx.Message}");
                }

                var okResponse = req.CreateResponse(HttpStatusCode.OK);
                await okResponse.WriteAsJsonAsync(new 
                { 
                    results = results, 
                    page = page, 
                    pageSize = pageSize, 
                    totalCount = totalCount,
                    totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                });
                return okResponse;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error searching conversations for tenant {tenantId}: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Failed to search conversations: {ex.Message}" });
                return errorResponse;
            }
        }

        private async Task<HttpResponseData> HandleSpecializedSearch(HttpRequestData req, string tenantId, string type, ILogger log)
        {
            var response = req.CreateResponse(HttpStatusCode.OK);

            try
            {
                switch (type.ToLower())
                {
                    case "recent-alerts":
                        var recentAlerts = await _graphCopilotService.GetRecentAlertsAsync(tenantId);
                        await response.WriteAsJsonAsync(recentAlerts);
                        break;

                    case "high-risk-users":
                        var highRiskUsers = await _graphCopilotService.GetHighRiskUsersAsync(tenantId);
                        await response.WriteAsJsonAsync(highRiskUsers);
                        break;

                    case "policy-violations":
                        var policyViolations = await _graphCopilotService.GetPolicyViolationsAsync(tenantId);
                        await response.WriteAsJsonAsync(policyViolations);
                        break;

                    default:
                        response.StatusCode = HttpStatusCode.BadRequest;
                        await response.WriteAsJsonAsync(new { error = $"Unknown search type: {type}" });
                        break;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error in specialized search for type {type}");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = $"Failed to execute search: {ex.Message}" });
            }

            return response;
        }

    }
}
