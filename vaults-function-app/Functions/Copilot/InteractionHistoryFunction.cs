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
    public class InteractionHistoryFunction
    {
        private readonly IConfiguration _configuration;
        private readonly GraphCopilotService _graphCopilotService;

        public InteractionHistoryFunction(IConfiguration configuration, GraphCopilotService graphCopilotService)
        {
            _configuration = configuration;
            _graphCopilotService = graphCopilotService;
        }

        [Function("InteractionHistoryFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "copilot/interactionHistory")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<InteractionHistoryFunction>();
            log.LogInformation("InteractionHistoryFunction received a request.");

            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            try
            {
                var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
                string tenantId = queryParams["tenantId"];
                string topParam = queryParams["$top"];
                string filter = queryParams["$filter"];

                if (string.IsNullOrEmpty(tenantId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "tenantId parameter is required" });
                    return response;
                }

                int? top = null;
                if (!string.IsNullOrEmpty(topParam) && int.TryParse(topParam, out int topValue))
                {
                    top = topValue;
                }

                var interactionHistory = await _graphCopilotService.GetInteractionHistoryAsync(tenantId, top, filter);

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteAsJsonAsync(interactionHistory);
                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error getting interaction history");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = $"Failed to get interaction history: {ex.Message}" });
                return response;
            }
        }
    }
}