// Updated Constants.cs - Remove "api/" prefix from routes

namespace VaultsFunctions.Core
{
    public static class Constants
    {
        public static class ConfigurationKeys
        {
            public const string CosmosDbConnectionString = "COSMOS_DB_CONNECTION_STRING";
            public const string CorsAllowedOrigin = "CORS_ALLOWED_ORIGIN";
            public const string StripeSecretKey = "STRIPE_SECRET_KEY";
            public const string StripeWebhookSecret = "STRIPE_WEBHOOK_SECRET";
            public const string StorageAccountName = "STORAGE_ACCOUNT_NAME";
            public const string StorageContainerName = "STORAGE_CONTAINER_NAME";
            public const string StorageConnectionString = "CUSTOMER_BLOB_STORAGE_CONNECTION_STRING";
            public const string SendGridApiKey = "SENDGRID_API_KEY";
            public const string SendGridSenderEmail = "SENDGRID_SENDER_EMAIL";
            public const string SendGridSenderName = "SENDGRID_SENDER_NAME";
            public const string DashboardUrl = "DASHBOARD_URL";
            
            // Microsoft Graph authentication
            public const string AzureTenantId = "AZURE_TENANT_ID";
            public const string AzureClientId = "AZURE_CLIENT_ID";
            public const string AzureClientSecret = "AZURE_CLIENT_SECRET";
            
            // Frontend integration and security
            public const string CorsAllowedOrigins = "CORS_ALLOWED_ORIGINS";
            public const string FrontendUrl = "FRONTEND_URL";
            public const string StaticWebAppUrl = "STATIC_WEB_APP_URL";
            
            // Environment configuration
            public const string Environment = "ENVIRONMENT";
            public const string KeyVaultName = "KEY_VAULT_NAME";
        }

        public static class Databases
        {
            public const string MainDatabase = "Vaults";
            public const string TenantsContainer = "Tenants";
            public const string InteractionsContainer = "Interactions";
            public const string ProcessedConversationsContainer = "ProcessedConversations";
            public const string AuditPoliciesContainer = "AuditPolicies";
        }

        public static class ApiRoutes
        {
            // FIXED: Removed "api/" prefix - Azure Functions adds this automatically
            public const string AdminStats = "admin/stats";
            public const string AdminPolicies = "policies";
            public const string AdminPoliciesConfig = "policies/config";
            public const string Conversations = "conversations";
            public const string Ingestion = "ingestion";
            public const string Exports = "exports/list";
            public const string Search = "search";
            public const string StripeCheckout = "stripe/checkout";
            public const string StripeBilling = "stripe/billing/{tenantId}";
            public const string StripeWebhook = "stripe/webhook";
            public const string OnboardingValidateAd = "onboarding/validate-azure-ad";
            public const string OnboardingTestStorage = "onboarding/test-storage-connection";
            public const string OnboardingComplete = "onboarding/complete";
            public const string OnboardingSendEmail = "onboarding/send-email";
            
            // Microsoft Graph Copilot API routes
            public const string CopilotRoot = "copilot";
            public const string CopilotUsers = "copilot/users";
            public const string CopilotInteractionHistory = "copilot/interactionHistory";
            public const string CopilotRetrieve = "copilot/retrieve";
        }
    }
}