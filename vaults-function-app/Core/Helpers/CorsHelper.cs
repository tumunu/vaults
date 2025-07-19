using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using VaultsFunctions.Core;
using System;
using System.Linq;

namespace VaultsFunctions.Core.Helpers
{
    public static class CorsHelper
    {
        public static void AddCorsHeaders(HttpResponseData response, IConfiguration configuration, bool isOptionsRequest = false, string requestOrigin = null)
        {
            // Get allowed origins from configuration
            string allowedOrigin = GetAllowedOrigin(configuration, requestOrigin);

            // Remove any existing headers to prevent duplication
            RemoveExistingHeaders(response);

            // Add security headers
            AddSecurityHeaders(response);

            // Add CORS headers
            response.Headers.Add("Access-Control-Allow-Origin", allowedOrigin);
            
            // For OPTIONS requests, add additional required headers
            if (isOptionsRequest)
            {
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, x-functions-key, x-correlation-id");
                response.Headers.Add("Access-Control-Allow-Credentials", "true");
                response.Headers.Add("Access-Control-Max-Age", "86400"); // 24 hours
            }
        }

        private static string GetAllowedOrigin(IConfiguration configuration, string requestOrigin)
        {
            // Get multiple allowed origins from configuration
            var corsOrigins = configuration["CORS_ALLOWED_ORIGINS"];
            var defaultOrigin = configuration[Constants.ConfigurationKeys.CorsAllowedOrigin] 
                ?? "https://func-vaults-dev-soldsu.azurestaticapps.net";

            // If no request origin provided, use default
            if (string.IsNullOrEmpty(requestOrigin))
            {
                return defaultOrigin;
            }

            // If multiple origins configured, check if request origin is allowed
            if (!string.IsNullOrEmpty(corsOrigins))
            {
                var allowedOrigins = corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.Trim())
                    .ToArray();

                // Check if request origin is in allowed list
                if (allowedOrigins.Contains(requestOrigin, StringComparer.OrdinalIgnoreCase))
                {
                    return requestOrigin;
                }
            }

            // Check single origin configuration
            if (string.Equals(requestOrigin, defaultOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return requestOrigin;
            }

            // For localhost development
            if (requestOrigin != null && (
                requestOrigin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase) ||
                requestOrigin.StartsWith("https://localhost:", StringComparison.OrdinalIgnoreCase)))
            {
                return requestOrigin;
            }

            // Fallback to default
            return defaultOrigin;
        }

        private static void RemoveExistingHeaders(HttpResponseData response)
        {
            response.Headers.Remove("Access-Control-Allow-Origin");
            response.Headers.Remove("Access-Control-Allow-Methods");
            response.Headers.Remove("Access-Control-Allow-Headers");
            response.Headers.Remove("Access-Control-Allow-Credentials");
            response.Headers.Remove("Access-Control-Max-Age");
            
            // Remove security headers to prevent duplication
            response.Headers.Remove("X-Frame-Options");
            response.Headers.Remove("X-Content-Type-Options");
            response.Headers.Remove("Strict-Transport-Security");
            response.Headers.Remove("X-XSS-Protection");
            response.Headers.Remove("Referrer-Policy");
        }

        private static void AddSecurityHeaders(HttpResponseData response)
        {
            // Security headers for all responses
            response.Headers.Add("X-Frame-Options", "DENY");
            response.Headers.Add("X-Content-Type-Options", "nosniff");
            response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
            response.Headers.Add("X-XSS-Protection", "1; mode=block");
            response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
        }

        public static string GetOriginFromRequest(HttpRequestData request)
        {
            if (request.Headers.TryGetValues("Origin", out var origins))
            {
                return origins.FirstOrDefault();
            }
            return null;
        }
    }
}
