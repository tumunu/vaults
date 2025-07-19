using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Threading.Tasks;

namespace VaultsFunctions.Core.Middleware
{
    /// <summary>
    /// Authentication middleware for validating requests and extracting claims
    /// in Azure Functions. Provides static methods for authentication validation.
    /// </summary>
    public static class AuthenticationMiddleware
    {
        /// <summary>
        /// Validates an HTTP request for proper authentication and authorization.
        /// </summary>
        /// <param name="req">The HTTP request to validate</param>
        /// <param name="configuration">Application configuration</param>
        /// <param name="logger">Logger for authentication events</param>
        /// <returns>ValidationResult with authentication status and claims</returns>
        public static Task<ValidationResult> ValidateRequestAsync(
            HttpRequestData req, 
            IConfiguration configuration, 
            ILogger logger)
        {
            try
            {
                // Check for function key authentication first (highest priority)
                if (HasValidFunctionKey(req, configuration))
                {
                    logger.LogDebug("Request authenticated with function key");
                    return Task.FromResult(ValidationResult.Success(new ClaimsPrincipal()));
                }

                // Check for Bearer token authentication
                var authHeader = req.Headers.GetValues("Authorization")?.FirstOrDefault();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    logger.LogWarning("Missing or invalid Authorization header");
                    return Task.FromResult(ValidationResult.Failed("Missing or invalid Authorization header"));
                }

                var token = authHeader.Substring("Bearer ".Length).Trim();
                
                // Parse and validate JWT token
                var tokenHandler = new JwtSecurityTokenHandler();
                if (!tokenHandler.CanReadToken(token))
                {
                    logger.LogWarning("Invalid JWT token format");
                    return Task.FromResult(ValidationResult.Failed("Invalid token format"));
                }

                var jwtToken = tokenHandler.ReadJwtToken(token);
                var claims = new ClaimsPrincipal(new ClaimsIdentity(jwtToken.Claims, "jwt"));

                // Validate token expiration
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    logger.LogWarning("JWT token has expired");
                    return Task.FromResult(ValidationResult.Failed("Token has expired"));
                }

                // Validate issuer if configured
                var expectedIssuer = configuration["AZURE_AD_ISSUER"];
                if (!string.IsNullOrEmpty(expectedIssuer) && 
                    !string.Equals(jwtToken.Issuer, expectedIssuer, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Invalid token issuer: {Issuer}", jwtToken.Issuer);
                    return Task.FromResult(ValidationResult.Failed("Invalid token issuer"));
                }

                logger.LogDebug("Request authenticated with JWT token");
                return Task.FromResult(ValidationResult.Success(claims));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating authentication");
                return Task.FromResult(ValidationResult.Failed("Authentication validation failed"));
            }
        }

        /// <summary>
        /// Checks if the authenticated user has the required scopes for the operation.
        /// </summary>
        /// <param name="claims">ClaimsPrincipal from authentication</param>
        /// <param name="requiredScopes">Array of required scope values</param>
        /// <returns>True if user has at least one of the required scopes</returns>
        public static bool HasRequiredScope(ClaimsPrincipal claims, string[] requiredScopes)
        {
            if (claims?.Identity == null || !claims.Identity.IsAuthenticated)
            {
                return false;
            }

            if (requiredScopes == null || requiredScopes.Length == 0)
            {
                return true; // No scopes required
            }

            // Check for scope claims (both 'scp' and 'scope' claim types)
            var scopeClaims = claims.FindAll("scp").Concat(claims.FindAll("scope"));
            var userScopes = scopeClaims
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Check if user has any of the required scopes
            return requiredScopes.Any(scope => userScopes.Contains(scope));
        }

        /// <summary>
        /// Checks if the request contains a valid function key for function-level authentication.
        /// </summary>
        /// <param name="req">HTTP request to check</param>
        /// <param name="configuration">Application configuration</param>
        /// <returns>True if valid function key is present</returns>
        private static bool HasValidFunctionKey(HttpRequestData req, IConfiguration configuration)
        {
            // Check x-functions-key header
            var functionKeyHeader = req.Headers.GetValues("x-functions-key")?.FirstOrDefault();
            if (!string.IsNullOrEmpty(functionKeyHeader))
            {
                return IsValidFunctionKey(functionKeyHeader, configuration);
            }

            // Check code query parameter
            var codeParam = req.Url.Query.Contains("code=") 
                ? System.Web.HttpUtility.ParseQueryString(req.Url.Query)["code"]
                : null;

            if (!string.IsNullOrEmpty(codeParam))
            {
                return IsValidFunctionKey(codeParam, configuration);
            }

            return false;
        }

        /// <summary>
        /// Validates a function key against configured values.
        /// </summary>
        /// <param name="providedKey">Function key from request</param>
        /// <param name="configuration">Application configuration</param>
        /// <returns>True if key is valid</returns>
        private static bool IsValidFunctionKey(string providedKey, IConfiguration configuration)
        {
            // Check against configured function keys
            var validKeys = new[]
            {
                configuration["FUNCTION_KEY"],
                configuration["AzureWebJobsStorage"], // For local development
                configuration["MASTER_KEY"]
            }.Where(k => !string.IsNullOrEmpty(k));

            return validKeys.Any(key => string.Equals(key, providedKey, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Result of authentication validation containing status and claims information.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Gets whether the authentication validation was successful.
        /// </summary>
        public bool IsValid { get; private set; }

        /// <summary>
        /// Gets the error message if validation failed.
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Gets the authenticated user's claims if validation succeeded.
        /// </summary>
        public ClaimsPrincipal Claims { get; private set; }

        /// <summary>
        /// Gets the tenant ID from the authenticated user's claims.
        /// Returns null if not authenticated or tenant claim not found.
        /// </summary>
        public string TenantId => GetTenantIdFromClaims();

        private ValidationResult(bool isValid, string errorMessage, ClaimsPrincipal claims)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
            Claims = claims;
        }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        /// <param name="claims">Authenticated user's claims</param>
        /// <returns>Successful ValidationResult</returns>
        public static ValidationResult Success(ClaimsPrincipal claims)
        {
            return new ValidationResult(true, null, claims);
        }

        /// <summary>
        /// Creates a failed validation result.
        /// </summary>
        /// <param name="errorMessage">Error message describing the failure</param>
        /// <returns>Failed ValidationResult</returns>
        public static ValidationResult Failed(string errorMessage)
        {
            return new ValidationResult(false, errorMessage, null);
        }

        /// <summary>
        /// Extracts tenant ID from JWT claims using common Azure AD claim types.
        /// </summary>
        /// <returns>Tenant ID if found, otherwise null</returns>
        private string GetTenantIdFromClaims()
        {
            if (Claims?.Identity == null || !Claims.Identity.IsAuthenticated)
            {
                return null;
            }

            // Try common Azure AD tenant claim types
            var tenantClaim = Claims.FindFirst("tid") ?? // Azure AD tenant ID
                             Claims.FindFirst("tenantid") ?? // Custom tenant claim
                             Claims.FindFirst("tenant_id") ?? // Alternative format
                             Claims.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid"); // Full URI

            return tenantClaim?.Value;
        }
    }
}