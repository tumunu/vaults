using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using VaultsFunctions.Core.Services;

namespace VaultsFunctions.Functions.Monitoring
{
    /// <summary>
    /// Timer-triggered function that performs idempotent warm-up operations
    /// to keep critical endpoints responsive and validate production patterns.
    /// This function is designed to be deployment-safe and never fail slot swaps.
    /// </summary>
    public class WarmupFunction
    {
        private readonly IHealthCheckService _healthCheckService;
        private readonly ILogger<WarmupFunction> _logger;

        public WarmupFunction(
            IHealthCheckService healthCheckService,
            ILogger<WarmupFunction> logger)
        {
            _healthCheckService = healthCheckService;
            _logger = logger;
        }

        /// <summary>
        /// Timer-triggered warm-up function that runs every 5 minutes.
        /// Performs idempotent health checks to:
        /// 1. Keep containers warm and reduce cold start latency
        /// 2. Validate global usings and extension methods in production
        /// 3. Ensure critical dependencies remain accessible
        /// 4. Provide early warning of potential issues
        /// </summary>
        /// <param name="timer">Timer information from Azure Functions runtime</param>
        /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
        [Function("WarmupTimer")]
        public async Task Run(
            [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
            CancellationToken cancellationToken)
        {
            var startTime = DateTimeOffset.UtcNow;
            
            try
            {
                _logger.LogInformation("Starting warm-up ping at {StartTime}", startTime);
                
                // Validate timer information
                if (timer?.IsPastDue == true)
                {
                    _logger.LogWarning("Warm-up timer is past due, but continuing with warm-up");
                }
                
                // Check for cancellation before starting work
                cancellationToken.ThrowIfCancellationRequested();
                
                // Perform idempotent health check ping
                // This validates:
                // - Global usings are working correctly
                // - Extension methods are available
                // - Core services (Cosmos DB, Service Bus) are accessible
                // - Cancellation token pattern is functioning
                var healthResult = await _healthCheckService.CheckHealthAsync();
                
                // Log results for monitoring
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogInformation(
                    "Warm-up ping completed successfully in {Duration}ms. Health status: {Status}",
                    duration.TotalMilliseconds,
                    healthResult.Status);
                
                // Additional warm-up activities can be added here
                // Example: Pre-load commonly used containers, warm up Graph API connections, etc.
                
                // Validate that extension methods are working (production test)
                if (healthResult.Status == HealthStatus.Unhealthy)
                {
                    _logger.LogWarning("Health check indicates unhealthy status during warm-up");
                }
            }
            catch (OperationCanceledException)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                _logger.LogInformation("Warm-up ping was cancelled after {Duration}ms (non-critical)", 
                                     duration.TotalMilliseconds);
                // Don't rethrow - this should not fail the function
            }
            catch (Exception ex)
            {
                var duration = DateTimeOffset.UtcNow - startTime;
                // Swallow all exceptions to avoid failing deployment slot swaps
                // This is critical for production deployments
                _logger.LogWarning(ex, 
                    "Warm-up ping failed after {Duration}ms (non-critical): {Message}",
                    duration.TotalMilliseconds,
                    ex.Message);
                
                // Log additional context for troubleshooting
                _logger.LogDebug("Warm-up failure details: {ExceptionType} - {StackTrace}",
                               ex.GetType().Name,
                               ex.StackTrace);
            }
        }
    }
}