using Microsoft.Azure.Functions.Worker.Http;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using VaultsFunctions.Core.Helpers;

namespace VaultsFunctions.Core.Extensions
{
    /// <summary>
    /// Extension methods for HttpRequestData to provide consistent error response handling
    /// across all Azure Functions with proper CORS support and structured JSON responses.
    /// </summary>
    public static class HttpRequestDataExtensions
    {
        /// <summary>
        /// Creates a standardized error response with a simple string message.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="status">HTTP status code to return</param>
        /// <param name="message">Error message to include</param>
        /// <param name="includeExceptionDetails">Whether to include detailed exception information</param>
        /// <returns>HTTP response with error details</returns>
        public static async Task<HttpResponseData> CreateErrorResponseAsync(
            this HttpRequestData req,
            HttpStatusCode status,
            string message,
            bool includeExceptionDetails = false)
        {
            var response = req.CreateResponse(status);
            
            // Apply CORS headers consistently
            var requestOrigin = CorsHelper.GetOriginFromRequest(req);
            CorsHelper.AddCorsHeaders(response, null, requestOrigin: requestOrigin);
            
            // Set JSON content type
            response.Headers.Add("Content-Type", "application/json");
            
            var errorPayload = new
            {
                error = message,
                timestamp = DateTimeOffset.UtcNow,
                status = (int)status,
                path = req.Url.PathAndQuery
            };
            
            await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            
            return response;
        }

        /// <summary>
        /// Creates a standardized error response from an exception with optional stack trace details.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="status">HTTP status code to return</param>
        /// <param name="exception">Exception that occurred</param>
        /// <param name="includeExceptionDetails">Whether to include stack trace and detailed exception info</param>
        /// <returns>HTTP response with error details</returns>
        public static async Task<HttpResponseData> CreateErrorResponseAsync(
            this HttpRequestData req,
            HttpStatusCode status,
            Exception exception,
            bool includeExceptionDetails = false)
        {
            var response = req.CreateResponse(status);
            
            // Apply CORS headers consistently
            var requestOrigin = CorsHelper.GetOriginFromRequest(req);
            CorsHelper.AddCorsHeaders(response, null, requestOrigin: requestOrigin);
            
            // Set JSON content type
            response.Headers.Add("Content-Type", "application/json");
            
            var errorPayload = new
            {
                error = exception.Message,
                timestamp = DateTimeOffset.UtcNow,
                status = (int)status,
                path = req.Url.PathAndQuery,
                details = includeExceptionDetails ? new
                {
                    type = exception.GetType().Name,
                    stackTrace = exception.StackTrace,
                    innerException = exception.InnerException?.Message
                } : null
            };
            
            await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));
            
            return response;
        }

        /// <summary>
        /// Creates a structured JSON error response with custom error details object.
        /// This allows functions to return consistent error formats with additional context.
        /// </summary>
        /// <typeparam name="T">Type of the error details object</typeparam>
        /// <param name="req">The HTTP request data</param>
        /// <param name="status">HTTP status code to return</param>
        /// <param name="errorDetails">Custom error details object to serialize</param>
        /// <param name="message">Optional error message (defaults to "Error")</param>
        /// <returns>HTTP response with structured error details</returns>
        public static async Task<HttpResponseData> CreateErrorResponseAsync<T>(
            this HttpRequestData req,
            HttpStatusCode status,
            T errorDetails,
            string message = null)
        {
            var response = req.CreateResponse(status);
            
            // Apply CORS headers consistently
            var requestOrigin = CorsHelper.GetOriginFromRequest(req);
            CorsHelper.AddCorsHeaders(response, null, requestOrigin: requestOrigin);
            
            // Set JSON content type
            response.Headers.Add("Content-Type", "application/json");
            
            var errorPayload = new
            {
                error = message ?? "Error",
                timestamp = DateTimeOffset.UtcNow,
                status = (int)status,
                path = req.Url.PathAndQuery,
                details = errorDetails
            };
            
            await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(errorPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            }));
            
            return response;
        }

        /// <summary>
        /// Creates a standardized unauthorized (401) response with consistent formatting.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="message">Custom unauthorized message (defaults to standard message)</param>
        /// <returns>HTTP 401 response with error details</returns>
        public static async Task<HttpResponseData> CreateUnauthorizedResponseAsync(
            this HttpRequestData req,
            string message = "Unauthorized access. Please check your authentication credentials.")
        {
            return await req.CreateErrorResponseAsync(HttpStatusCode.Unauthorized, message);
        }

        /// <summary>
        /// Creates a standardized internal server error (500) response with exception details.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="message">Error message to include</param>
        /// <param name="exception">Optional exception for detailed error information</param>
        /// <param name="includeExceptionDetails">Whether to include stack trace details</param>
        /// <returns>HTTP 500 response with error details</returns>
        public static async Task<HttpResponseData> CreateInternalServerErrorResponseAsync(
            this HttpRequestData req,
            string message,
            Exception exception = null,
            bool includeExceptionDetails = false)
        {
            if (exception != null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, exception, includeExceptionDetails);
            }
            
            return await req.CreateErrorResponseAsync(HttpStatusCode.InternalServerError, message, includeExceptionDetails);
        }

        /// <summary>
        /// Creates a standardized forbidden (403) response with consistent formatting.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="message">Custom forbidden message (defaults to standard message)</param>
        /// <returns>HTTP 403 response with error details</returns>
        public static async Task<HttpResponseData> CreateForbiddenResponseAsync(
            this HttpRequestData req,
            string message = "Forbidden. You do not have permission to access this resource.")
        {
            return await req.CreateErrorResponseAsync(HttpStatusCode.Forbidden, message);
        }

        /// <summary>
        /// Creates a standardized bad request (400) response with validation details.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="message">Validation error message</param>
        /// <param name="validationErrors">Optional validation error details</param>
        /// <returns>HTTP 400 response with error details</returns>
        public static async Task<HttpResponseData> CreateBadRequestResponseAsync<T>(
            this HttpRequestData req,
            string message,
            T validationErrors = default)
        {
            if (validationErrors != null)
            {
                return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, validationErrors, message);
            }
            
            return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, message);
        }

        /// <summary>
        /// Creates a standardized bad request (400) response with simple message.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="message">Validation error message</param>
        /// <returns>HTTP 400 response with error details</returns>
        public static async Task<HttpResponseData> CreateBadRequestResponseAsync(
            this HttpRequestData req,
            string message)
        {
            return await req.CreateErrorResponseAsync(HttpStatusCode.BadRequest, message);
        }

        /// <summary>
        /// Creates a standardized not found (404) response with consistent formatting.
        /// </summary>
        /// <param name="req">The HTTP request data</param>
        /// <param name="message">Custom not found message (defaults to standard message)</param>
        /// <returns>HTTP 404 response with error details</returns>
        public static async Task<HttpResponseData> CreateNotFoundResponseAsync(
            this HttpRequestData req,
            string message = "The requested resource was not found.")
        {
            return await req.CreateErrorResponseAsync(HttpStatusCode.NotFound, message);
        }
    }
}