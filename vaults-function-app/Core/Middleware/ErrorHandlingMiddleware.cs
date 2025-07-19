using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;
using System.IO;

namespace VaultsFunctions.Core.Middleware
{
    public class ErrorHandlingMiddleware : IFunctionsWorkerMiddleware
    {
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger)
        {
            _logger = logger;
        }

        public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
        {
            _logger.LogInformation("ErrorHandlingMiddleware: Processing function {FunctionName}", 
                context.FunctionDefinition.Name);
            
            try
            {
                await next(context);
                _logger.LogInformation("ErrorHandlingMiddleware: Function {FunctionName} completed successfully", 
                    context.FunctionDefinition.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ErrorHandlingMiddleware: Unhandled exception in function {FunctionName}: {Message}", 
                    context.FunctionDefinition.Name, ex.Message);
                _logger.LogError("ErrorHandlingMiddleware: Exception type: {ExceptionType}", ex.GetType().FullName);
                _logger.LogError("ErrorHandlingMiddleware: Stack trace: {StackTrace}", ex.StackTrace);
                
                if (ex.InnerException != null)
                {
                    _logger.LogError("ErrorHandlingMiddleware: Inner exception: {InnerMessage}", ex.InnerException.Message);
                }

                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(FunctionContext context, Exception exception)
        {
            var errorResponse = new
            {
                Error = new
                {
                    Message = GetUserFriendlyMessage(exception),
                    Type = exception.GetType().Name,
                    Timestamp = DateTimeOffset.UtcNow,
                    TraceId = System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString(),
                    FunctionName = context.FunctionDefinition.Name
                }
            };

            var statusCode = GetStatusCode(exception);
            
            // Try to get the HTTP request data from the context
            var requestData = await GetHttpRequestDataAsync(context);
            if (requestData != null)
            {
                var response = requestData.CreateResponse(statusCode);
                response.Headers.Add("Content-Type", "application/json");
                
                var jsonResponse = System.Text.Json.JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                
                await response.WriteStringAsync(jsonResponse);
                
                // Set the response in the context
                context.GetInvocationResult().Value = response;
            }
            else
            {
                // Fallback: Log the error details since we can't return an HTTP response
                _logger.LogError(exception, 
                    "HTTP request data not available for error response. Function: {FunctionName}, Error: {ErrorMessage}", 
                    context.FunctionDefinition.Name, exception.Message);
                
                // Try to set the exception in the invocation result
                var invocationResult = context.GetInvocationResult();
                invocationResult.Value = new
                {
                    StatusCode = (int)statusCode,
                    Error = errorResponse
                };
            }
        }

        private static string GetUserFriendlyMessage(Exception exception)
        {
            return exception switch
            {
                ArgumentNullException => "Required parameter is missing.",
                ArgumentException => "Invalid parameter provided.",
                UnauthorizedAccessException => "Access denied. Please check your permissions.",
                InvalidOperationException => "The requested operation is not valid at this time.",
                TimeoutException => "The operation timed out. Please try again.",
                _ => "An error occurred while processing your request."
            };
        }

        private static HttpStatusCode GetStatusCode(Exception exception)
        {
            return exception switch
            {
                ArgumentNullException => HttpStatusCode.BadRequest,
                ArgumentException => HttpStatusCode.BadRequest,
                UnauthorizedAccessException => HttpStatusCode.Unauthorized,
                InvalidOperationException => HttpStatusCode.Conflict,
                TimeoutException => HttpStatusCode.RequestTimeout,
                _ => HttpStatusCode.InternalServerError
            };
        }

        private static Task<HttpRequestData> GetHttpRequestDataAsync(FunctionContext context)
        {
            try
            {
                // Try to get HTTP request data from the binding context
                var bindingContext = context.BindingContext;
                var bindingData = bindingContext.BindingData;
                
                // Look for the HTTP request in binding data
                if (bindingData.TryGetValue("req", out var requestObj) && requestObj is HttpRequestData httpRequest)
                {
                    return Task.FromResult(httpRequest);
                }
                
                // Alternative approach: Check function input bindings
                var inputBindings = context.FunctionDefinition.InputBindings;
                foreach (var binding in inputBindings.Values)
                {
                    if (binding.Type == "httpTrigger")
                    {
                        // For HTTP triggers, the request should be available via parameter name
                        // Try to get it from binding context with standard parameter names
                        foreach (var paramName in new[] { "req", "request", "httpRequest" })
                        {
                            if (bindingData.TryGetValue(paramName, out var paramValue) && paramValue is HttpRequestData request)
                            {
                                return Task.FromResult(request);
                            }
                        }
                        break;
                    }
                }
                
                return Task.FromResult<HttpRequestData>(null);
            }
            catch (Exception ex)
            {
                // Log but don't throw - this is a fallback mechanism
                System.Diagnostics.Debug.WriteLine($"Could not extract HttpRequestData: {ex.Message}");
                return Task.FromResult<HttpRequestData>(null);
            }
        }
    }

    public static class ErrorHandlingMiddlewareExtensions
    {
        public static IServiceCollection AddErrorHandling(this IServiceCollection services)
        {
            services.AddSingleton<ErrorHandlingMiddleware>();
            return services;
        }
    }
}

// Custom exceptions for better error handling
namespace VaultsFunctions.Core.Exceptions
{
    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
        public ValidationException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class TenantNotFoundException : Exception
    {
        public string TenantId { get; }
        
        public TenantNotFoundException(string tenantId) 
            : base($"Tenant '{tenantId}' was not found.")
        {
            TenantId = tenantId;
        }
    }

    public class ExternalServiceException : Exception
    {
        public string ServiceName { get; }
        
        public ExternalServiceException(string serviceName, string message) 
            : base($"External service '{serviceName}' error: {message}")
        {
            ServiceName = serviceName;
        }
        
        public ExternalServiceException(string serviceName, string message, Exception innerException) 
            : base($"External service '{serviceName}' error: {message}", innerException)
        {
            ServiceName = serviceName;
        }
    }

    public class ConfigurationException : Exception
    {
        public string ConfigurationKey { get; }
        
        public ConfigurationException(string configurationKey, string message) 
            : base($"Configuration error for '{configurationKey}': {message}")
        {
            ConfigurationKey = configurationKey;
        }
    }
}