using Azure.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Azure.Storage.Blobs;
using Azure.Messaging.ServiceBus;
using Microsoft.Graph;
using Stripe;
using VaultsFunctions.Core;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Middleware;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults() // User code exception visibility is enabled by default in .NET 8
    .ConfigureServices((context, services) =>
    {
        // Add Application Insights
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        
        // Remove default Application Insights filter to enable detailed logging and exception visibility
        services.Configure<LoggerFilterOptions>(options =>
        {
            var defaultRule = options.Rules.FirstOrDefault(rule => 
                rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule != null)
            {
                options.Rules.Remove(defaultRule);
            }
        });

        // Add configuration
        services.AddSingleton<IConfiguration>(context.Configuration);
        
        // Add feature flags
        services.AddSingleton<VaultsFunctions.Core.Configuration.IFeatureFlags, VaultsFunctions.Core.Configuration.FeatureFlags>();

        // Validate required configuration
        var config = context.Configuration;
        Console.WriteLine("Program.cs: Starting configuration validation");
        try
        {
            ValidateConfiguration(config);
            Console.WriteLine("Program.cs: Configuration validation passed");
        }
        catch (Exception ex)
        {
            // Log validation errors but don't crash startup
            Console.WriteLine($"Program.cs: Configuration validation warning: {ex.Message}");
        }

        // Add Cosmos DB client as singleton
        services.AddSingleton(sp =>
        {
            Console.WriteLine("Program.cs: Creating Cosmos DB client");
            try
            {
                var connectionString = config[VaultsFunctions.Core.Constants.ConfigurationKeys.CosmosDbConnectionString];
                Console.WriteLine($"Program.cs: Cosmos connection string present: {!string.IsNullOrEmpty(connectionString)}");
                
                var cosmosClient = new CosmosClient(connectionString, new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                    }
                });

                Console.WriteLine("Program.cs: Cosmos DB client created successfully");
                return cosmosClient;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Program.cs: Error creating Cosmos DB client: {ex.Message}");
                throw;
            }
        });

        // Add Azure Blob Storage client
        services.AddSingleton(sp =>
        {
            var storageConnectionString = config[VaultsFunctions.Core.Constants.ConfigurationKeys.StorageConnectionString] 
                ?? config["AzureWebJobsStorage"];
            
            if (string.IsNullOrEmpty(storageConnectionString))
            {
                var accountName = config["AzureWebJobsStorageAccountName"];
                if (!string.IsNullOrEmpty(accountName))
                {
                    return new BlobServiceClient(
                        new Uri($"https://{accountName}.blob.core.windows.net"),
                        new DefaultAzureCredential());
                }
                return new BlobServiceClient(config["AzureWebJobsStorage"]);
            }
            return new BlobServiceClient(storageConnectionString);
        });

        // Configure Stripe
        var stripeSecretKey = config[VaultsFunctions.Core.Constants.ConfigurationKeys.StripeSecretKey];
        if (!string.IsNullOrEmpty(stripeSecretKey))
        {
            StripeConfiguration.ApiKey = stripeSecretKey;
        }

        // Add other services here as needed
        services.AddHttpClient();
        
        // Register HttpClient for dependency injection (HealthCheckService needs HttpClient directly)
        services.AddTransient<HttpClient>(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient());
        
        // Configure Graph Service Client for B2B invitations
        services.AddSingleton<GraphServiceClient>(sp =>
        {
            var scopes = new[] { "https://graph.microsoft.com/.default" };
            
            var tenantId = config["AZURE_TENANT_ID"] ?? ExtractTenantIdFromIssuerUrl(config["AAD_TOKEN_ISSUER_URL"]);
            var clientId = config["AZURE_CLIENT_ID"] ?? config["AAD_CLIENT_ID"];
            var clientSecret = config["AZURE_CLIENT_SECRET"] ?? config["AAD_CLIENT_SECRET"] ?? config["MICROSOFT_PROVIDER_AUTHENTICATION_SECRET"];

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            {
                Console.WriteLine("Warning: Missing Azure AD configuration for Graph API");
                return null;
            }

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential, scopes);
        });

        // Configure domain validation with hot-reload
        services.Configure<TrustedDomainsOptions>(config.GetSection(TrustedDomainsOptions.SectionName));
        services.AddSingleton<IDomainValidator, DomainValidator>();

        // Add Service Bus client (nullable for resilience)
        services.AddSingleton<ServiceBusClient>(sp =>
        {
            Console.WriteLine("Program.cs: Creating Service Bus client");
            try
            {
                var connectionString = config["ServiceBusConnection"];
                Console.WriteLine($"Program.cs: ServiceBusConnection present: {!string.IsNullOrEmpty(connectionString)}");
                
                if (string.IsNullOrEmpty(connectionString))
                {
                    Console.WriteLine("Program.cs: ServiceBusConnection not configured - using direct processing fallback");
                    return null;
                }
                
                var serviceBusClient = new ServiceBusClient(connectionString);
                Console.WriteLine("Program.cs: Service Bus client created successfully");
                return serviceBusClient;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Program.cs: Failed to create ServiceBusClient: {ex.Message} - using direct processing fallback");
                return null;
            }
        });

        // Add invitation services
        Console.WriteLine("Program.cs: Registering IGraphInvitationService");
        services.AddScoped<IGraphInvitationService, GraphInvitationService>();
        
        // Add tenant stats service
        Console.WriteLine("Program.cs: Registering ITenantStatsService");
        services.AddScoped<ITenantStatsService, TenantStatsService>();
        
        // Add Service Bus monitoring service
        Console.WriteLine("Program.cs: Registering IServiceBusMonitoringService");
        services.AddScoped<IServiceBusMonitoringService, ServiceBusMonitoringService>();
        
        // Add Graph Copilot Service
        Console.WriteLine("Program.cs: Registering GraphCopilotService");
        services.AddScoped<GraphCopilotService>();
        
        // Add Purview Audit Service (Governance-First Architecture)
        Console.WriteLine("Program.cs: Registering PurviewAuditService");
        services.AddScoped<PurviewAuditService>();
        
        // Add Purview DLP Service (Real-time Governance)
        Console.WriteLine("Program.cs: Registering PurviewDlpService");
        services.AddScoped<PurviewDlpService>();
        
        // Add Permission Validation Service (Principle of Least Privilege)
        Console.WriteLine("Program.cs: Registering PermissionValidationService");
        services.AddScoped<PermissionValidationService>();
        
        // Add Content Classification Service (AI-Powered Governance)
        Console.WriteLine("Program.cs: Registering ContentClassificationService");
        services.AddScoped<ContentClassificationService>();
        
        // Add health check service
        Console.WriteLine("Program.cs: Registering IHealthCheckService");
        services.AddScoped<IHealthCheckService, HealthCheckService>();
        Console.WriteLine("Program.cs: HealthCheckService registration completed");
        
        // Add error handling middleware
        Console.WriteLine("Program.cs: Registering error handling middleware");
        services.AddErrorHandling();
        Console.WriteLine("Program.cs: All service registrations completed successfully");
    })
    .ConfigureFunctionsWorkerDefaults() // Temporarily using parameterless overload while ErrorHandlingMiddleware is disabled
    .Build();

Console.WriteLine("Program.cs: Host built successfully, starting host...");
host.Run();

static string ExtractTenantIdFromIssuerUrl(string issuerUrl)
{
    if (string.IsNullOrEmpty(issuerUrl))
        return null;

    try
    {
        var uri = new Uri(issuerUrl);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var segment in segments)
        {
            if (Guid.TryParse(segment, out _))
            {
                return segment;
            }
        }
        return null;
    }
    catch
    {
        return null;
    }
}

void ValidateConfiguration(IConfiguration config)
{
    var requiredConfigs = new[]
    {
        VaultsFunctions.Core.Constants.ConfigurationKeys.CosmosDbConnectionString,
        VaultsFunctions.Core.Constants.ConfigurationKeys.CorsAllowedOrigin,
        VaultsFunctions.Core.Constants.ConfigurationKeys.StripeSecretKey,
        VaultsFunctions.Core.Constants.ConfigurationKeys.StripeWebhookSecret
    };

    foreach (var configKey in requiredConfigs)
    {
        if (string.IsNullOrEmpty(config[configKey]))
        {
            throw new InvalidOperationException($"Required configuration '{configKey}' is missing.");
        }
    }
}

