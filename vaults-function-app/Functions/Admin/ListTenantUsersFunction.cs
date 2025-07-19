using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Configuration;
using Microsoft.ApplicationInsights;
using System.Collections.Generic;

namespace VaultsFunctions.Functions.Admin
{
    public class ListTenantUsersFunction
    {
        private readonly GraphServiceClient _graphClient;
        private readonly ILogger<ListTenantUsersFunction> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly IFeatureFlags _featureFlags;

        public ListTenantUsersFunction(
            GraphServiceClient graphClient,
            ILogger<ListTenantUsersFunction> logger,
            TelemetryClient telemetryClient,
            IFeatureFlags featureFlags)
        {
            _graphClient = graphClient;
            _logger = logger;
            _telemetryClient = telemetryClient;
            _featureFlags = featureFlags;
        }

        [Function("ListTenantUsers")]
        public async Task<HttpResponseData> ListTenantUsers(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenant/users")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("ListTenantUsers function triggered");
            _telemetryClient.TrackEvent("ListTenantUsersTriggered");

            // Check if Graph client is available
            if (_graphClient == null)
            {
                _logger.LogError("GraphServiceClient is null - Azure AD configuration missing");
                var configErrorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await configErrorResponse.WriteAsJsonAsync(new { error = "Graph API client not configured" });
                return configErrorResponse;
            }

            try
            {
                // TODO: Add proper admin authentication here
                // For now, using Function-level auth, but should validate admin roles
                
                // Parse query parameters
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var top = int.TryParse(queryParams["top"], out var topValue) ? topValue : 50;
                var filter = queryParams["filter"];
                var userType = queryParams["userType"]; // "guest", "member", or null for all

                top = Math.Min(top, 200); // Cap at 200 for performance

                _logger.LogInformation("Fetching users with top={Top}, filter={Filter}, userType={UserType}", 
                    top, filter, userType);

                // Build Graph API query with conditional premium fields
                var basicFields = new List<string>
                { 
                    "id", "displayName", "mail", "userPrincipalName", 
                    "userType", "externalUserState", "createdDateTime",
                    "accountEnabled"
                };

                // Add premium fields only if Azure AD Premium is enabled
                if (_featureFlags.IsAzureAdPremiumEnabled)
                {
                    basicFields.Add("signInActivity");
                    _logger.LogDebug("Including premium Azure AD fields in user query");
                }
                else
                {
                    _logger.LogDebug("Excluding premium Azure AD fields - feature flag disabled");
                }

                var users = await _graphClient.Users.GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Top = top;
                    requestConfiguration.QueryParameters.Select = basicFields.ToArray();

                    // Apply filters
                    var filters = new List<string>();
                    
                    if (!string.IsNullOrEmpty(userType))
                    {
                        if (userType.ToLower() == "guest")
                            filters.Add("userType eq 'Guest'");
                        else if (userType.ToLower() == "member")
                            filters.Add("userType eq 'Member'");
                    }

                    if (!string.IsNullOrEmpty(filter))
                    {
                        filters.Add($"startswith(displayName,'{filter}') or startswith(mail,'{filter}')");
                    }

                    if (filters.Any())
                    {
                        requestConfiguration.QueryParameters.Filter = string.Join(" and ", filters);
                    }

                    requestConfiguration.QueryParameters.Orderby = new[] { "displayName" };
                });

                // Transform to simplified response
                var userList = users?.Value?.Select(user => new
                {
                    id = user.Id,
                    displayName = user.DisplayName,
                    email = user.Mail ?? user.UserPrincipalName,
                    userType = user.UserType,
                    externalUserState = user.ExternalUserState,
                    createdDateTime = user.CreatedDateTime,
                    accountEnabled = user.AccountEnabled,
                    lastSignIn = user.SignInActivity?.LastSignInDateTime
                }).ToList();

                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    users = userList,
                    totalCount = userList?.Count ?? 0,
                    hasMore = (userList?.Count ?? 0) >= top
                });

                _telemetryClient.TrackEvent("ListTenantUsersCompleted", new Dictionary<string, string>
                {
                    { "UserCount", (userList?.Count ?? 0).ToString() },
                    { "UserType", userType ?? "all" },
                    { "HasFilter", (!string.IsNullOrEmpty(filter)).ToString() }
                });

                return response;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Forbidden)
            {
                _logger.LogError(ex, "Insufficient permissions to list users. Status: {StatusCode}, Message: {Message}", 
                    ex.ResponseStatusCode, ex.Message);
                _telemetryClient.TrackException(ex);
                
                var forbiddenResponse = req.CreateResponse(HttpStatusCode.Forbidden);
                await forbiddenResponse.WriteAsJsonAsync(new { 
                    error = "Insufficient permissions to list users",
                    details = ex.Message,
                    statusCode = ex.ResponseStatusCode
                });
                return forbiddenResponse;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning(ex, "Graph API throttling when listing users");
                _telemetryClient.TrackException(ex);
                
                var throttleResponse = req.CreateResponse(HttpStatusCode.TooManyRequests);
                throttleResponse.Headers.Add("Retry-After", ex.ResponseHeaders?.RetryAfter?.ToString() ?? "60");
                await throttleResponse.WriteAsJsonAsync(new { error = "Rate limited, please retry later" });
                return throttleResponse;
            }
            catch (ServiceException ex) when (ex.Message?.Contains("premium license") == true || ex.Message?.Contains("B2C tenant") == true)
            {
                _logger.LogWarning(ex, "Azure AD Premium license required for requested features. Status: {StatusCode}, Message: {Message}", 
                    ex.ResponseStatusCode, ex.Message);
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "GraphStatusCode", ex.ResponseStatusCode.ToString() },
                    { "GraphErrorMessage", ex.Message ?? "unknown" },
                    { "ErrorType", "PremiumLicenseRequired" }
                });
                
                var premiumErrorResponse = req.CreateResponse(HttpStatusCode.PaymentRequired);
                await premiumErrorResponse.WriteAsJsonAsync(new { 
                    error = "Azure AD Premium license required",
                    details = "The requested features require Azure AD Premium P1 or P2 license",
                    suggestion = "Contact your administrator to upgrade Azure AD license or disable premium features",
                    featureFlag = "Features:AzureAdPremium:Enabled"
                });
                return premiumErrorResponse;
            }
            catch (ServiceException ex)
            {
                _logger.LogError(ex, "Graph API error listing users. Status: {StatusCode}, Message: {Message}", 
                    ex.ResponseStatusCode, ex.Message);
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "GraphStatusCode", ex.ResponseStatusCode.ToString() },
                    { "GraphErrorMessage", ex.Message ?? "unknown" }
                });
                
                var graphErrorResponse = req.CreateResponse((HttpStatusCode)ex.ResponseStatusCode);
                await graphErrorResponse.WriteAsJsonAsync(new { 
                    error = "Graph API error",
                    details = ex.Message,
                    statusCode = ex.ResponseStatusCode
                });
                return graphErrorResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error listing tenant users. Exception type: {ExceptionType}, Message: {Message}", 
                    ex.GetType().Name, ex.Message);
                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "ExceptionType", ex.GetType().Name },
                    { "InnerException", ex.InnerException?.Message ?? "none" }
                });
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { 
                    error = "Internal server error",
                    type = ex.GetType().Name,
                    message = ex.Message
                });
                return errorResponse;
            }
        }

        [Function("GetUserInvitationStatus")]
        public async Task<HttpResponseData> GetUserInvitationStatus(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "tenant/users/{userId}/invitation")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("GetUserInvitationStatus function triggered");
            
            try
            {
                var userId = context.BindingContext.BindingData["userId"]?.ToString();
                if (string.IsNullOrEmpty(userId))
                {
                    var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badRequest.WriteAsJsonAsync(new { error = "User ID is required" });
                    return badRequest;
                }

                // Get user details from Graph
                var user = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[] 
                    { 
                        "id", "displayName", "mail", "userPrincipalName", 
                        "userType", "externalUserState", "externalUserStateChangeDateTime",
                        "createdDateTime", "accountEnabled"
                    };
                });

                if (user == null)
                {
                    var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                    await notFound.WriteAsJsonAsync(new { error = "User not found" });
                    return notFound;
                }

                // Return user invitation status
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteAsJsonAsync(new
                {
                    userId = user.Id,
                    displayName = user.DisplayName,
                    email = user.Mail ?? user.UserPrincipalName,
                    userType = user.UserType,
                    externalUserState = user.ExternalUserState,
                    externalUserStateChangeDateTime = user.ExternalUserStateChangeDateTime,
                    createdDateTime = user.CreatedDateTime,
                    accountEnabled = user.AccountEnabled,
                    isInvited = user.UserType == "Guest",
                    invitationAccepted = user.ExternalUserState == "Accepted"
                });

                return response;
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.NotFound)
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteAsJsonAsync(new { error = "User not found" });
                return notFound;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user invitation status");
                _telemetryClient.TrackException(ex);
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = "Internal server error" });
                return errorResponse;
            }
        }
    }
}