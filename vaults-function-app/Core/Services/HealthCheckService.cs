#nullable enable
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Messaging.ServiceBus;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using VaultsFunctions.Core.Exceptions;
using Microsoft.Graph;
using System.Threading;

namespace VaultsFunctions.Core.Services
{
    public interface IHealthCheckService
    {
        Task<HealthCheckResult> CheckHealthAsync();
        Task<HealthCheckResult> CheckCosmosDbAsync();
        Task<HealthCheckResult> CheckServiceBusAsync();
        Task<HealthCheckResult> CheckGraphApiAsync();
        Task<HealthCheckResult> CheckExternalDependenciesAsync();
    }

    public class HealthCheckService : IHealthCheckService
    {
        private readonly CosmosClient _cosmosClient;
        private readonly ServiceBusClient? _serviceBusClient;
        private readonly GraphServiceClient? _graphClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<HealthCheckService> _logger;
        private readonly HttpClient _httpClient;

        public HealthCheckService(
            CosmosClient cosmosClient,
            ServiceBusClient? serviceBusClient,
            GraphServiceClient? graphClient,
            IConfiguration configuration,
            ILogger<HealthCheckService> logger,
            HttpClient httpClient)
        {
            _cosmosClient = cosmosClient;
            _serviceBusClient = serviceBusClient;
            _graphClient = graphClient;
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;
        }

        public async Task<HealthCheckResult> CheckHealthAsync()
        {
            _logger.LogInformation("Starting comprehensive health check");
            var startTime = DateTimeOffset.UtcNow;
            
            var healthChecks = new List<Task<HealthCheckResult>>
            {
                CheckCosmosDbAsync(),
                CheckServiceBusAsync(),
                CheckGraphApiAsync(),
                CheckExternalDependenciesAsync()
            };

            var results = await Task.WhenAll(healthChecks);
            
            var overallHealth = HealthStatus.Healthy;
            var details = new Dictionary<string, object>();
            var totalDuration = TimeSpan.Zero;
            var unhealthyComponents = new List<string>();
            var degradedComponents = new List<string>();

            foreach (var result in results)
            {
                _logger.LogInformation("Health check result for {ComponentName}: {Status} ({Duration}ms) - {Message}", 
                    result.ComponentName, result.Status, result.Duration.TotalMilliseconds, result.Message);

                if (result.Status == HealthStatus.Unhealthy)
                {
                    overallHealth = HealthStatus.Unhealthy;
                    unhealthyComponents.Add(result.ComponentName);
                }
                else if (result.Status == HealthStatus.Degraded && overallHealth == HealthStatus.Healthy)
                {
                    overallHealth = HealthStatus.Degraded;
                    degradedComponents.Add(result.ComponentName);
                }

                details[result.ComponentName] = new
                {
                    status = result.Status.ToString(),
                    duration = result.Duration.TotalMilliseconds,
                    message = result.Message,
                    details = result.Details
                };

                totalDuration = totalDuration.Add(result.Duration);
            }

            var totalHealthCheckDuration = DateTimeOffset.UtcNow - startTime;
            
            var summaryMessage = overallHealth switch
            {
                HealthStatus.Healthy => "All systems operational",
                HealthStatus.Degraded => $"Some systems degraded: {string.Join(", ", degradedComponents)}",
                HealthStatus.Unhealthy => $"Critical systems unhealthy: {string.Join(", ", unhealthyComponents)}",
                _ => "Unknown health status"
            };

            _logger.LogInformation("Overall health check completed: {Status} in {Duration}ms - {Message}", 
                overallHealth, totalHealthCheckDuration.TotalMilliseconds, summaryMessage);

            return new HealthCheckResult
            {
                Status = overallHealth,
                ComponentName = "Overall",
                Duration = totalHealthCheckDuration,
                Message = summaryMessage,
                Details = details,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        public async Task<HealthCheckResult> CheckCosmosDbAsync()
        {
            var startTime = DateTimeOffset.UtcNow;
            try
            {
                // Try to access the main database
                var database = _cosmosClient.GetDatabase(Constants.Databases.MainDatabase);
                var response = await database.ReadAsync();

                var duration = DateTimeOffset.UtcNow - startTime;
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Healthy,
                    ComponentName = "CosmosDB",
                    Duration = duration,
                    Message = "CosmosDB is accessible",
                    Details = new Dictionary<string, object>
                    {
                        ["database"] = response.Database.Id,
                        ["requestCharge"] = response.RequestCharge
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
            catch (CosmosException ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogError(ex, "CosmosDB health check failed");
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Unhealthy,
                    ComponentName = "CosmosDB",
                    Duration = duration,
                    Message = $"CosmosDB error: {ex.Message}",
                    Details = new Dictionary<string, object>
                    {
                        ["statusCode"] = ex.StatusCode,
                        ["subStatusCode"] = ex.SubStatusCode,
                        ["requestCharge"] = ex.RequestCharge
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
            catch (Exception ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogError(ex, "CosmosDB health check failed");
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Unhealthy,
                    ComponentName = "CosmosDB",
                    Duration = duration,
                    Message = $"CosmosDB connection failed: {ex.Message}",
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
        }

        public async Task<HealthCheckResult> CheckServiceBusAsync()
        {
            var startTime = DateTimeOffset.UtcNow;
            
            if (_serviceBusClient == null)
            {
                _logger.LogWarning("ServiceBus client is null - check ServiceBusConnection configuration");
                return new HealthCheckResult
                {
                    Status = HealthStatus.Degraded,
                    ComponentName = "ServiceBus",
                    Duration = TimeSpan.Zero,
                    Message = "ServiceBus client not configured - check ServiceBusConnection setting",
                    Details = new Dictionary<string, object>
                    {
                        ["configured"] = false,
                        ["reason"] = "ServiceBusConnection configuration missing or invalid"
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            try
            {
                // Create a receiver to test queue connectivity
                var receiver = _serviceBusClient.CreateReceiver("invite-queue");
                
                // Try to peek a message (doesn't consume it)
                var messages = await receiver.PeekMessagesAsync(maxMessages: 1);
                
                await receiver.DisposeAsync();
                
                var duration = DateTimeOffset.UtcNow - startTime;
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Healthy,
                    ComponentName = "ServiceBus",
                    Duration = duration,
                    Message = "ServiceBus queue is accessible",
                    Details = new Dictionary<string, object>
                    {
                        ["queueName"] = "invite-queue",
                        ["peekedMessages"] = messages.Count,
                        ["configured"] = true
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
            catch (ServiceBusException ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogError(ex, "ServiceBus health check failed - ServiceBusException: {Reason}", ex.Reason);
                
                // Determine if this is a configuration issue or transient issue
                var status = ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound || 
                           ex.Reason == ServiceBusFailureReason.GeneralError
                    ? HealthStatus.Unhealthy 
                    : HealthStatus.Degraded;
                
                return new HealthCheckResult
                {
                    Status = status,
                    ComponentName = "ServiceBus",
                    Duration = duration,
                    Message = $"ServiceBus error: {ex.Message}",
                    Details = new Dictionary<string, object>
                    {
                        ["reason"] = ex.Reason.ToString(),
                        ["isTransient"] = ex.IsTransient,
                        ["configured"] = true
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
            catch (Exception ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogError(ex, "ServiceBus health check failed with unexpected exception");
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Unhealthy,
                    ComponentName = "ServiceBus",
                    Duration = duration,
                    Message = $"ServiceBus connection failed: {ex.Message}",
                    Details = new Dictionary<string, object>
                    {
                        ["configured"] = true,
                        ["exceptionType"] = ex.GetType().Name
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
        }

        public async Task<HealthCheckResult> CheckGraphApiAsync()
        {
            var startTime = DateTimeOffset.UtcNow;
            
            if (_graphClient == null)
            {
                _logger.LogWarning("Graph API client is null - check Azure AD configuration");
                return new HealthCheckResult
                {
                    Status = HealthStatus.Degraded,
                    ComponentName = "GraphAPI",
                    Duration = TimeSpan.Zero,
                    Message = "Graph API client not configured - check Azure AD credentials",
                    Details = new Dictionary<string, object>
                    {
                        ["configured"] = false,
                        ["reason"] = "Azure AD configuration missing (AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET)"
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }

            try
            {
                // Test Graph API connectivity by getting organization info
                var organization = await _graphClient.Organization.GetAsync();
                
                var duration = DateTimeOffset.UtcNow - startTime;
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Healthy,
                    ComponentName = "GraphAPI",
                    Duration = duration,
                    Message = "Microsoft Graph API is accessible",
                    Details = new Dictionary<string, object>
                    {
                        ["organizationCount"] = organization?.Value?.Count ?? 0,
                        ["configured"] = true
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
            catch (ServiceException ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogError(ex, "Graph API health check failed - ServiceException: {StatusCode} {Message}", 
                    ex.ResponseStatusCode, ex.Message);
                
                var status = ex.ResponseStatusCode == 401 || ex.ResponseStatusCode == 403 
                    ? HealthStatus.Unhealthy 
                    : HealthStatus.Degraded;
                
                return new HealthCheckResult
                {
                    Status = status,
                    ComponentName = "GraphAPI",
                    Duration = duration,
                    Message = $"Graph API error: {ex.Message}",
                    Details = new Dictionary<string, object>
                    {
                        ["statusCode"] = ex.ResponseStatusCode,
                        ["errorCode"] = "ServiceException",
                        ["configured"] = true
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
            catch (Exception ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogError(ex, "Graph API health check failed with unexpected exception");
                
                return new HealthCheckResult
                {
                    Status = HealthStatus.Unhealthy,
                    ComponentName = "GraphAPI",
                    Duration = duration,
                    Message = $"Graph API connection failed: {ex.Message}",
                    Details = new Dictionary<string, object>
                    {
                        ["configured"] = true,
                        ["exceptionType"] = ex.GetType().Name
                    },
                    Timestamp = DateTimeOffset.UtcNow
                };
            }
        }

        public async Task<HealthCheckResult> CheckExternalDependenciesAsync()
        {
            var startTime = DateTimeOffset.UtcNow;
            var checks = new List<Task<(string name, bool success, TimeSpan duration, string message)>>();

            // Check Stripe API
            var stripeKey = _configuration["STRIPE_SECRET_KEY"];
            if (!string.IsNullOrEmpty(stripeKey))
            {
                checks.Add(CheckHttpEndpointAsync("Stripe", "https://api.stripe.com/v1/customers?limit=1", 
                    new Dictionary<string, string> { ["Authorization"] = $"Bearer {stripeKey}" }));
            }

            // Check Microsoft Login endpoint
            checks.Add(CheckHttpEndpointAsync("Microsoft Login", "https://login.microsoftonline.com/common/v2.0/.well-known/openid_configuration"));

            var results = await Task.WhenAll(checks);
            
            var allHealthy = true;
            var details = new Dictionary<string, object>();
            var totalDuration = DateTimeOffset.UtcNow - startTime;

            foreach (var (name, success, duration, message) in results)
            {
                if (!success) allHealthy = false;
                
                details[name] = new
                {
                    success,
                    duration = duration.TotalMilliseconds,
                    message
                };
            }

            return new HealthCheckResult
            {
                Status = allHealthy ? HealthStatus.Healthy : HealthStatus.Degraded,
                ComponentName = "ExternalDependencies",
                Duration = totalDuration,
                Message = allHealthy ? "All external dependencies accessible" : "Some external dependencies have issues",
                Details = details,
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        private async Task<(string name, bool success, TimeSpan duration, string message)> CheckHttpEndpointAsync(
            string name, string url, Dictionary<string, string>? headers = null)
        {
            var startTime = DateTimeOffset.UtcNow;
            
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var response = await _httpClient.SendAsync(request, cts.Token);
                
                var duration = DateTimeOffset.UtcNow - startTime;
                var success = response.IsSuccessStatusCode;
                var message = success ? "Endpoint accessible" : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
                
                return (name, success, duration, message);
            }
            catch (TaskCanceledException)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                return (name, false, duration, "Request timeout");
            }
            catch (Exception ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                return (name, false, duration, ex.Message);
            }
        }
    }

    public class HealthCheckResult
    {
        public HealthStatus Status { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
        public DateTimeOffset Timestamp { get; set; }
    }

    public enum HealthStatus
    {
        Healthy,
        Degraded,
        Unhealthy
    }
}