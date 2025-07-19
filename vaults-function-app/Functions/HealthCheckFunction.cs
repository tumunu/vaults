using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Helpers;
using System;

namespace VaultsFunctions.Functions
{
    public class HealthCheckFunction
    {
        private readonly IHealthCheckService _healthCheckService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HealthCheckFunction> _logger;

        public HealthCheckFunction(
            IHealthCheckService healthCheckService,
            IConfiguration configuration,
            ILogger<HealthCheckFunction> logger)
        {
            _healthCheckService = healthCheckService;
            _configuration = configuration;
            _logger = logger;
        }

        [Function("HealthCheck")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req,
            FunctionContext context,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("HealthCheck function started");
            
            // Log dependency injection status
            _logger.LogInformation("HealthCheckService is null: {IsNull}", _healthCheckService == null);
            _logger.LogInformation("Configuration is null: {IsNull}", _configuration == null);
            _logger.LogInformation("Logger is null: {IsNull}", _logger == null);
            
            string requestOrigin = null;
            try
            {
                requestOrigin = CorsHelper.GetOriginFromRequest(req);
                _logger.LogInformation("Request origin obtained: {Origin}", requestOrigin ?? "null");
            }
            catch (Exception corsEx)
            {
                _logger.LogError(corsEx, "Error getting request origin in main health check");
                requestOrigin = null;
            }
            
            try
            {
                // Check for cancellation before starting
                cancellationToken.ThrowIfCancellationRequested();
                
                _logger.LogInformation("Starting health check service call");
                var healthResult = await _healthCheckService.CheckHealthAsync();
                _logger.LogInformation("Health check service completed with status: {Status}", healthResult.Status);
                
                // Check for cancellation after health check
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Health check cancelled by Azure Functions runtime");
                    return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                }
                
                var statusCode = healthResult.Status switch
                {
                    HealthStatus.Healthy => HttpStatusCode.OK,
                    HealthStatus.Degraded => HttpStatusCode.OK, // Still OK but with warnings
                    HealthStatus.Unhealthy => HttpStatusCode.ServiceUnavailable,
                    _ => HttpStatusCode.InternalServerError
                };

                var response = req.CreateResponse(statusCode);
                CorsHelper.AddCorsHeaders(response, _configuration, requestOrigin: requestOrigin);
                
                await response.WriteAsJsonAsync(new
                {
                    status = healthResult.Status.ToString().ToLowerInvariant(),
                    timestamp = healthResult.Timestamp,
                    duration = healthResult.Duration.TotalMilliseconds,
                    message = healthResult.Message,
                    components = healthResult.Details,
                    version = "1.0.0", // Consider making this dynamic
                    environment = _configuration["ENVIRONMENT"] ?? "unknown"
                });

                return response;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Health check operation was cancelled");
                var response = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                CorsHelper.AddCorsHeaders(response, _configuration, requestOrigin: requestOrigin);
                
                await response.WriteAsJsonAsync(new
                {
                    status = "cancelled",
                    timestamp = DateTimeOffset.UtcNow,
                    message = "Health check was cancelled",
                    version = "1.0.0",
                    environment = _configuration["ENVIRONMENT"] ?? "unknown"
                });
                
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed with exception");
                
                var response = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                CorsHelper.AddCorsHeaders(response, _configuration, requestOrigin: requestOrigin);
                
                await response.WriteAsJsonAsync(new
                {
                    status = "unhealthy",
                    timestamp = DateTimeOffset.UtcNow,
                    message = "Health check failed",
                    error = ex.Message
                });

                return response;
            }
        }

        [Function("HealthCheckLive")]
        public async Task<HttpResponseData> LivenessCheck(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/live")] HttpRequestData req,
            FunctionContext context)
        {
            try
            {
                _logger.LogInformation("HealthCheckLive function started - entry point reached");
                _logger.LogInformation("FunctionContext ID: {ContextId}", context.InvocationId);
                _logger.LogInformation("Request URL: {Url}", req.Url);
                _logger.LogInformation("Request Method: {Method}", req.Method);
                
                try
                {
                _logger.LogInformation("Creating response object");
                var response = req.CreateResponse(HttpStatusCode.OK);
                
                _logger.LogInformation("Getting request origin");
                string requestOrigin = null;
                try
                {
                    requestOrigin = CorsHelper.GetOriginFromRequest(req);
                    _logger.LogInformation("Request origin: {Origin}", requestOrigin ?? "null");
                }
                catch (Exception corsEx)
                {
                    _logger.LogError(corsEx, "Error getting request origin");
                    requestOrigin = null;
                }
                
                _logger.LogInformation("Adding CORS headers");
                try
                {
                    CorsHelper.AddCorsHeaders(response, _configuration, requestOrigin: requestOrigin);
                    _logger.LogInformation("CORS headers added successfully");
                }
                catch (Exception corsHeaderEx)
                {
                    _logger.LogError(corsHeaderEx, "Error adding CORS headers");
                }
                
                _logger.LogInformation("Writing JSON response");
                await response.WriteAsJsonAsync(new
                {
                    status = "alive",
                    timestamp = DateTimeOffset.UtcNow,
                    message = "Function App is running"
                });
                
                _logger.LogInformation("HealthCheckLive function completed successfully");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HealthCheckLive function failed with exception: {Message}", ex.Message);
                _logger.LogError("Stack trace: {StackTrace}", ex.StackTrace);
                
                try
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteAsJsonAsync(new
                    {
                        status = "error",
                        timestamp = DateTimeOffset.UtcNow,
                        message = "Health check failed",
                        error = ex.Message
                    });
                    return errorResponse;
                }
                catch (Exception innerEx)
                {
                    _logger.LogError(innerEx, "Failed to create error response: {Message}", innerEx.Message);
                    throw;
                }
            }
            }
            catch (Exception topLevelEx)
            {
                _logger.LogError(topLevelEx, "CRITICAL: Top-level exception in HealthCheckLive function");
                _logger.LogError("Exception Type: {ExceptionType}", topLevelEx.GetType().FullName);
                _logger.LogError("Exception Message: {Message}", topLevelEx.Message);
                _logger.LogError("Stack Trace: {StackTrace}", topLevelEx.StackTrace);
                
                if (topLevelEx.InnerException != null)
                {
                    _logger.LogError("Inner Exception Type: {InnerType}", topLevelEx.InnerException.GetType().FullName);
                    _logger.LogError("Inner Exception Message: {InnerMessage}", topLevelEx.InnerException.Message);
                    _logger.LogError("Inner Stack Trace: {InnerStackTrace}", topLevelEx.InnerException.StackTrace);
                }
                
                // Try to create a minimal error response
                try
                {
                    var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                    await errorResponse.WriteStringAsync($"{{\"error\":\"Top-level exception\",\"type\":\"{topLevelEx.GetType().Name}\",\"message\":\"{topLevelEx.Message}\"}}");
                    return errorResponse;
                }
                catch
                {
                    // If we can't even create a response, re-throw the original exception
                    throw;
                }
            }
        }

        [Function("HealthCheckReady")]
        public async Task<HttpResponseData> ReadinessCheck(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/ready")] HttpRequestData req,
            FunctionContext context)
        {
            var requestOrigin = CorsHelper.GetOriginFromRequest(req);
            
            try
            {
                // Check critical dependencies for readiness
                var cosmosCheck = await _healthCheckService.CheckCosmosDbAsync();
                var graphCheck = await _healthCheckService.CheckGraphApiAsync();
                
                var isReady = cosmosCheck.Status != HealthStatus.Unhealthy && 
                             graphCheck.Status != HealthStatus.Unhealthy;
                
                var statusCode = isReady ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
                var response = req.CreateResponse(statusCode);
                
                CorsHelper.AddCorsHeaders(response, _configuration, requestOrigin: requestOrigin);
                
                await response.WriteAsJsonAsync(new
                {
                    status = isReady ? "ready" : "not ready",
                    timestamp = DateTimeOffset.UtcNow,
                    checks = new
                    {
                        cosmosdb = cosmosCheck.Status.ToString().ToLowerInvariant(),
                        graphapi = graphCheck.Status.ToString().ToLowerInvariant()
                    }
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Readiness check failed");
                
                var response = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                CorsHelper.AddCorsHeaders(response, _configuration, requestOrigin: requestOrigin);
                
                await response.WriteAsJsonAsync(new
                {
                    status = "not ready",
                    timestamp = DateTimeOffset.UtcNow,
                    error = ex.Message
                });

                return response;
            }
        }
    }
}