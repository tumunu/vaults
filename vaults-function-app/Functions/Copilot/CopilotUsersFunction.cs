using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Web;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Helpers;

namespace VaultsFunctions.Functions.Copilot
{
    public class CopilotUsersFunction
    {
        private readonly IConfiguration _configuration;
        private readonly GraphCopilotService _graphCopilotService;

        public CopilotUsersFunction(IConfiguration configuration, GraphCopilotService graphCopilotService)
        {
            _configuration = configuration;
            _graphCopilotService = graphCopilotService;
        }

        [Function("CopilotUsersFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "copilot/users")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<CopilotUsersFunction>();
            log.LogInformation("CopilotUsersFunction received a request.");

            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            try
            {
                var tenantId = "default-tenant";

                var copilotUsers = await _graphCopilotService.GetCopilotUsersAsync(tenantId);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(copilotUsers);
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting Copilot users");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = $"Failed to get Copilot users: {ex.Message}" });
                return response;
            }
        }
    }
}