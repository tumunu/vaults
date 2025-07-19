using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using VaultsFunctions.Core.Helpers;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Middleware;

namespace VaultsFunctions.Functions.Copilot
{
    public class CopilotRootFunction
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly GraphCopilotService _graphCopilotService;
        private readonly ILogger<CopilotRootFunction> _logger;

        public CopilotRootFunction(
            IConfiguration configuration, 
            HttpClient httpClient,
            GraphCopilotService graphCopilotService,
            ILogger<CopilotRootFunction> logger)
        {
            _configuration = configuration;
            _httpClient = httpClient;
            _graphCopilotService = graphCopilotService;
            _logger = logger;
        }

        [Function("CopilotRootFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "copilot")] HttpRequestData req)
        {
            _logger.LogInformation("CopilotRootFunction received a request");

            try
            {
                var tenantId = "default-tenant";
                _logger.LogInformation("Providing Copilot root metadata for tenant {TenantId}", tenantId);

                // Return comprehensive API metadata for Microsoft 365 Copilot endpoints
                var copilotRoot = new
                {
                    id = "copilot",
                    displayName = "Microsoft 365 Copilot Analytics",
                    description = "Enterprise analytics and governance for Microsoft 365 Copilot usage",
                    version = "v1",
                    tenantId = tenantId,
                    endpoints = new
                    {
                        usageSummary = new { 
                            href = "/api/vaults/usage/summary",
                            description = "Copilot usage metrics and summary data",
                            parameters = new[] { "period" }
                        },
                        userCount = new { 
                            href = "/api/vaults/users/count",
                            description = "Count of users interacting with Copilot",
                            parameters = new[] { "period" }
                        },
                        users = new { 
                            href = "/api/vaults/users",
                            description = "Detailed user information and usage patterns",
                            parameters = new[] { "includeUsage" }
                        },
                        interactionHistory = new { 
                            href = "/api/vaults/interactions/history",
                            description = "Enterprise interaction history and audit trail",
                            parameters = new[] { "top", "filter", "userId" }
                        },
                        securityAlerts = new { 
                            href = "/api/vaults/security/alerts",
                            description = "Security alerts related to Copilot usage",
                            parameters = new[] { "severity", "limit" }
                        },
                        riskyUsers = new { 
                            href = "/api/vaults/security/risky-users",
                            description = "Users identified as high-risk",
                            parameters = new[] { "riskLevel", "limit" }
                        },
                        complianceViolations = new { 
                            href = "/api/vaults/compliance/violations",
                            description = "Policy violations and compliance issues",
                            parameters = new[] { "severity", "limit" }
                        }
                    },
                    permissions = new
                    {
                        required = new[] { "Vaults.ReadUsage", "Vaults.ReadSecurity" },
                        microsoftGraph = new[] { 
                            "Reports.Read.All", 
                            "SecurityEvents.Read.All", 
                            "IdentityRiskEvent.Read.All",
                            "AiEnterpriseInteraction.Read.All",
                            "InformationProtectionPolicy.Read.All"
                        }
                    },
                    metadata = new
                    {
                        source = "Microsoft Graph API",
                        lastUpdated = DateTimeOffset.UtcNow,
                        managedIdentity = _configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true)
                    }
                };

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                await response.WriteStringAsync(JsonConvert.SerializeObject(copilotRoot, Formatting.Indented));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Copilot root function");
                return await req.CreateInternalServerErrorResponseAsync("Failed to get Copilot root metadata", ex);
            }
        }

    }
}