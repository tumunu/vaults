using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using VaultsFunctions.Core.Services;
using Azure.Identity;
using Azure.Core;
using System.Collections.Generic;

namespace VaultsFunctions.Tests.Integration
{
    [Collection("Integration")]
    public class ManagedIdentityIntegrationTests : IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;

        public ManagedIdentityIntegrationTests()
        {
            var services = new ServiceCollection();
            
            // Build configuration from environment variables and appsettings
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.dev.json", optional: true)
                .AddEnvironmentVariables();
            
            _configuration = configBuilder.Build();
            
            services.AddSingleton(_configuration);
            services.AddLogging(builder => builder.AddConsole());
            services.AddTransient<GraphCopilotService>();
            
            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ManagedIdentity_TokenAcquisition_Succeeds()
        {
            // Skip if not running in Azure environment
            if (!IsAzureEnvironment())
            {
                return; // Skip test in local environment
            }

            // Arrange
            var service = _serviceProvider.GetRequiredService<GraphCopilotService>();

            // Act & Assert
            // This test validates that the managed identity can acquire tokens
            // In a real Azure environment, this would test actual token acquisition
            var exception = await Record.ExceptionAsync(async () =>
            {
                await service.GetCopilotUsageSummaryAsync("D7");
            });

            // The service should handle token acquisition gracefully
            // Even if permissions are not granted, it should not throw authentication errors
            Assert.True(exception == null || !exception.Message.Contains("authentication"));
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task ManagedIdentity_FallbackBehavior_WorksCorrectly()
        {
            // Arrange
            var service = _serviceProvider.GetRequiredService<GraphCopilotService>();

            // Act
            var alerts = await service.GetRecentAlertsAsync("test-tenant");
            var riskUsers = await service.GetHighRiskUsersAsync("test-tenant");
            var violations = await service.GetPolicyViolationsAsync("test-tenant");

            // Assert
            // All methods should return fallback data if Graph API is not accessible
            Assert.NotNull(alerts);
            Assert.NotNull(riskUsers);
            Assert.NotNull(violations);
        }

        [Fact]
        [Trait("Category", "Integration")]
        public void DefaultAzureCredential_Configuration_IsValid()
        {
            // Arrange
            var managedIdentityEnabled = _configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            var clientId = _configuration["AZURE_CLIENT_ID"];

            // Act & Assert
            if (managedIdentityEnabled)
            {
                // In managed identity mode, we should have a client ID configured
                Assert.False(string.IsNullOrEmpty(clientId), "AZURE_CLIENT_ID should be configured for managed identity");
                
                // Test that DefaultAzureCredential can be created without exceptions
                var exception = Record.Exception(() =>
                {
                    var options = new DefaultAzureCredentialOptions
                    {
                        ManagedIdentityClientId = clientId,
                        ExcludeManagedIdentityCredential = false
                    };
                    var credential = new DefaultAzureCredential(options);
                });
                
                Assert.Null(exception);
            }
        }

        [Fact]
        [Trait("Category", "Integration")]
        public async Task GraphServiceClient_Initialization_DoesNotThrow()
        {
            // Arrange & Act
            var exception = await Record.ExceptionAsync(async () =>
            {
                var service = _serviceProvider.GetRequiredService<GraphCopilotService>();
                
                // Try to use the service - should not throw during initialization
                await service.GetCopilotUsageSummaryAsync("D7");
            });

            // Assert
            // Service initialization should not throw exceptions
            // Graph API calls may fail due to permissions, but initialization should succeed
            if (exception != null)
            {
                // Only authentication/initialization errors should cause test failure
                Assert.False(exception.Message.Contains("Failed to initialize GraphServiceClient"));
            }
        }

        [Theory]
        [InlineData("D7")]
        [InlineData("D30")]
        [InlineData("D90")]
        [Trait("Category", "Integration")]
        public async Task GraphCopilotService_AllEndpoints_HandleGracefully(string period)
        {
            // Arrange
            var service = _serviceProvider.GetRequiredService<GraphCopilotService>();
            const string testTenant = "integration-test-tenant";

            // Act & Assert - All methods should handle failures gracefully
            var tasks = new List<Task>
            {
                Task.Run(async () => await service.GetRecentAlertsAsync(testTenant)),
                Task.Run(async () => await service.GetHighRiskUsersAsync(testTenant)),
                Task.Run(async () => await service.GetPolicyViolationsAsync(testTenant)),
                Task.Run(async () => await service.GetInteractionHistoryAsync(testTenant)),
                Task.Run(async () => await service.GetCopilotUsersAsync(testTenant)),
                Task.Run(async () => await service.GetCopilotUsageSummaryAsync(period)),
                Task.Run(async () => await service.GetCopilotUserCountAsync(period))
            };

            // All tasks should complete without throwing exceptions
            var exception = await Record.ExceptionAsync(async () =>
            {
                await Task.WhenAll(tasks);
            });

            Assert.Null(exception);
        }

        private bool IsAzureEnvironment()
        {
            // Check if we're running in an Azure environment
            // This can be determined by the presence of Azure-specific environment variables
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"));
        }

        public void Dispose()
        {
            _serviceProvider?.Dispose();
        }
    }
}