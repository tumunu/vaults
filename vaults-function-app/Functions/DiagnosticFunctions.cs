using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;
using VaultsFunctions.Core.Services;

namespace VaultsFunctions.Functions
{
    public class DiagnosticFunctions
    {
        private readonly ILogger<DiagnosticFunctions> _logger;

        public DiagnosticFunctions(ILogger<DiagnosticFunctions> logger)
        {
            _logger = logger;
        }

        [Function("HealthCheckSimple")]
        public async Task<HttpResponseData> HealthCheckSimple(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/simple")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("HealthCheckSimple started - only ILogger dependency");
            
            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                await response.WriteStringAsync("{\"status\":\"ok\",\"message\":\"simple health check works\",\"dependencies\":[\"ILogger\"]}");
                
                _logger.LogInformation("HealthCheckSimple completed successfully");
                return response;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "HealthCheckSimple failed: {Message}", ex.Message);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"{{\"error\":\"{ex.Message}\"}}");
                return errorResponse;
            }
        }
    }

    public class DiagnosticWithConfigFunctions
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiagnosticWithConfigFunctions> _logger;

        public DiagnosticWithConfigFunctions(
            IConfiguration configuration,
            ILogger<DiagnosticWithConfigFunctions> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [Function("HealthCheckWithConfig")]
        public async Task<HttpResponseData> HealthCheckWithConfig(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/config")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("HealthCheckWithConfig started - ILogger + IConfiguration dependencies");
            
            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                // Test configuration access
                var cosmosConfig = _configuration["COSMOS_DB_CONNECTION_STRING"];
                var configExists = !string.IsNullOrEmpty(cosmosConfig);
                
                await response.WriteStringAsync($"{{\"status\":\"ok\",\"message\":\"config health check works\",\"dependencies\":[\"ILogger\",\"IConfiguration\"],\"cosmosConfigExists\":{configExists.ToString().ToLower()}}}");
                
                _logger.LogInformation("HealthCheckWithConfig completed successfully");
                return response;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "HealthCheckWithConfig failed: {Message}", ex.Message);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"{{\"error\":\"{ex.Message}\"}}");
                return errorResponse;
            }
        }
    }

    public class DiagnosticWithHealthServiceFunctions
    {
        private readonly IHealthCheckService _healthCheckService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DiagnosticWithHealthServiceFunctions> _logger;

        public DiagnosticWithHealthServiceFunctions(
            IHealthCheckService healthCheckService,
            IConfiguration configuration,
            ILogger<DiagnosticWithHealthServiceFunctions> logger)
        {
            _healthCheckService = healthCheckService;
            _configuration = configuration;
            _logger = logger;
        }

        [Function("HealthCheckWithService")]
        public async Task<HttpResponseData> HealthCheckWithService(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/service")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("HealthCheckWithService started - ILogger + IConfiguration + IHealthCheckService dependencies");
            
            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                // Test if health service is injected properly
                var serviceExists = _healthCheckService != null;
                
                await response.WriteStringAsync($"{{\"status\":\"ok\",\"message\":\"service health check works\",\"dependencies\":[\"ILogger\",\"IConfiguration\",\"IHealthCheckService\"],\"healthServiceExists\":{serviceExists.ToString().ToLower()}}}");
                
                _logger.LogInformation("HealthCheckWithService completed successfully");
                return response;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "HealthCheckWithService failed: {Message}", ex.Message);
                _logger.LogError("Exception type: {ExceptionType}", ex.GetType().FullName);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync($"{{\"error\":\"{ex.Message}\",\"type\":\"{ex.GetType().Name}\"}}");
                return errorResponse;
            }
        }
    }
}