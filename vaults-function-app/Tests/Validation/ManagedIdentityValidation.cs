using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Azure.Core;
using System.Net.Http;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace VaultsFunctions.Tests.Validation
{
    /// <summary>
    /// Validation tests for managed identity deployment
    /// These tests verify that managed identity is properly configured and functional
    /// </summary>
    public class ManagedIdentityValidation
    {
        private readonly ITestOutputHelper _output;
        private readonly IConfiguration _configuration;
        private readonly ILogger _logger;

        public ManagedIdentityValidation(ITestOutputHelper output)
        {
            _output = output;
            
            var configBuilder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.dev.json", optional: true)
                .AddJsonFile("appsettings.staging.json", optional: true)
                .AddJsonFile("appsettings.prod.json", optional: true)
                .AddEnvironmentVariables();
            
            _configuration = configBuilder.Build();
            
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<ManagedIdentityValidation>();
        }

        [Fact]
        [Trait("Category", "Validation")]
        public void Validate_ManagedIdentityConfiguration()
        {
            // Arrange
            var managedIdentityEnabled = _configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            var clientId = _configuration["AZURE_CLIENT_ID"];
            var tenantId = _configuration["AZURE_TENANT_ID"];

            _output.WriteLine($"Managed Identity Enabled: {managedIdentityEnabled}");
            _output.WriteLine($"Azure Client ID: {clientId?.Substring(0, Math.Min(8, clientId.Length ?? 0))}...");
            _output.WriteLine($"Azure Tenant ID: {tenantId?.Substring(0, Math.Min(8, tenantId.Length ?? 0))}...");

            // Act & Assert
            if (managedIdentityEnabled)
            {
                Assert.False(string.IsNullOrEmpty(clientId), "AZURE_CLIENT_ID must be configured when managed identity is enabled");
                Assert.False(string.IsNullOrEmpty(tenantId), "AZURE_TENANT_ID must be configured");
                
                // Validate client ID format (should be a GUID)
                Assert.True(Guid.TryParse(clientId, out _), "AZURE_CLIENT_ID should be a valid GUID");
                Assert.True(Guid.TryParse(tenantId, out _), "AZURE_TENANT_ID should be a valid GUID");
            }
            else
            {
                // If managed identity is disabled, client secret should be available
                var clientSecret = _configuration["AZURE_CLIENT_SECRET"];
                Assert.False(string.IsNullOrEmpty(clientSecret), "AZURE_CLIENT_SECRET must be configured when managed identity is disabled");
            }
        }

        [Fact]
        [Trait("Category", "Validation")]
        public async Task Validate_DefaultAzureCredential_TokenAcquisition()
        {
            // Skip if managed identity is not enabled
            var managedIdentityEnabled = _configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            if (!managedIdentityEnabled)
            {
                _output.WriteLine("Skipping managed identity validation - MANAGED_IDENTITY_ENABLED is false");
                return;
            }

            // Arrange
            var clientId = _configuration["AZURE_CLIENT_ID"];
            var options = new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = clientId,
                ExcludeManagedIdentityCredential = false,
                ExcludeEnvironmentCredential = false,
                ExcludeAzureCliCredential = false,
                ExcludeInteractiveBrowserCredential = true,
                ExcludeVisualStudioCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeSharedTokenCacheCredential = true
            };

            var credential = new DefaultAzureCredential(options);
            
            // Act
            var exception = await Record.ExceptionAsync(async () =>
            {
                var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
                var token = await credential.GetTokenAsync(tokenRequestContext);
                
                _output.WriteLine($"Token acquired successfully. Expires: {token.ExpiresOn}");
                Assert.NotNull(token.Token);
                Assert.False(string.IsNullOrEmpty(token.Token));
            });

            // Assert
            if (exception != null)
            {
                _output.WriteLine($"Token acquisition failed: {exception.Message}");
                
                // In local development, this might fail due to lack of Azure context
                // In Azure deployment, this should succeed
                if (IsRunningInAzure())
                {
                    Assert.Null(exception); // Should succeed in Azure
                }
                else
                {
                    _output.WriteLine("Skipping token validation - not running in Azure environment");
                }
            }
        }

        [Fact]
        [Trait("Category", "Validation")]
        public async Task Validate_MicrosoftGraphPermissions()
        {
            // Skip if not in Azure environment
            if (!IsRunningInAzure())
            {
                _output.WriteLine("Skipping Graph permissions validation - not running in Azure environment");
                return;
            }

            // Arrange
            var managedIdentityEnabled = _configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            if (!managedIdentityEnabled)
            {
                _output.WriteLine("Skipping Graph permissions validation - managed identity disabled");
                return;
            }

            var clientId = _configuration["AZURE_CLIENT_ID"];
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = clientId
            });

            // Act
            var httpClient = new HttpClient();
            var tokenRequestContext = new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" });
            
            try
            {
                var token = await credential.GetTokenAsync(tokenRequestContext);
                httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

                // Test basic Graph API access
                var response = await httpClient.GetAsync("https://graph.microsoft.com/v1.0/me");
                
                if (response.IsSuccessStatusCode)
                {
                    _output.WriteLine("✅ Basic Graph API access successful");
                }
                else
                {
                    _output.WriteLine($"⚠️ Basic Graph API access failed: {response.StatusCode}");
                }

                // Test Copilot-specific endpoints
                var endpoints = new[]
                {
                    ("Security Alerts", "https://graph.microsoft.com/v1.0/security/alerts_v2?$top=1"),
                    ("Risky Users", "https://graph.microsoft.com/v1.0/identityProtection/riskyUsers?$top=1"),
                    ("Usage Reports", "https://graph.microsoft.com/v1.0/reports/getCopilotUsageUserSummary(period='D7')"),
                    ("User Count", "https://graph.microsoft.com/v1.0/reports/getCopilotUserCountSummary(period='D7')")
                };

                foreach (var (name, url) in endpoints)
                {
                    try
                    {
                        var endpointResponse = await httpClient.GetAsync(url);
                        if (endpointResponse.IsSuccessStatusCode)
                        {
                            _output.WriteLine($"✅ {name} endpoint accessible");
                        }
                        else
                        {
                            _output.WriteLine($"❌ {name} endpoint failed: {endpointResponse.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"❌ {name} endpoint error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Token acquisition failed: {ex.Message}");
                throw;
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Validation")]
        public void Validate_EnvironmentSpecificConfiguration()
        {
            // Arrange
            var environments = new[] { "dev", "staging", "prod" };
            
            foreach (var env in environments)
            {
                _output.WriteLine($"\n--- Validating {env.ToUpper()} Configuration ---");
                
                var envConfigPath = $"appsettings.{env}.json";
                var envConfig = new ConfigurationBuilder()
                    .AddJsonFile(envConfigPath, optional: true)
                    .Build();

                if (!envConfig.GetChildren().Any())
                {
                    _output.WriteLine($"No configuration found for {env} environment");
                    continue;
                }

                var managedIdentityEnabled = envConfig.GetValue<bool>("Values:MANAGED_IDENTITY_ENABLED", false);
                var clientId = envConfig["Values:AZURE_CLIENT_ID"];
                var clientSecret = envConfig["Values:AZURE_CLIENT_SECRET"];

                _output.WriteLine($"Managed Identity: {managedIdentityEnabled}");
                
                if (managedIdentityEnabled)
                {
                    Assert.False(string.IsNullOrEmpty(clientId), $"{env}: AZURE_CLIENT_ID required for managed identity");
                    _output.WriteLine($"✅ {env}: Managed identity properly configured");
                    
                    // Production should not have client secrets
                    if (env == "prod")
                    {
                        Assert.True(string.IsNullOrEmpty(clientSecret) || clientSecret.StartsWith("@Microsoft.KeyVault"), 
                            "Production should not have client secrets in plain text");
                        _output.WriteLine($"✅ {env}: No plain text secrets found");
                    }
                }
                else
                {
                    Assert.False(string.IsNullOrEmpty(clientSecret), $"{env}: AZURE_CLIENT_SECRET required when managed identity disabled");
                    _output.WriteLine($"✅ {env}: Client secret authentication configured");
                }
            }
        }

        private bool IsRunningInAzure()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
        }
    }
}