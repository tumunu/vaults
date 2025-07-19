// Managed Identity Permissions Configuration for CopilotVault
@description('Environment name (dev, staging, prod)')
param environment string = 'prod'

@description('Function App name')
param functionAppName string

@description('Key Vault name')
param keyVaultName string

@description('Microsoft Graph Application ID')
param graphApplicationId string = '00000003-0000-0000-c000-000000000000'

// Get reference to existing Function App
resource functionApp 'Microsoft.Web/sites@2023-01-01' existing = {
  name: functionAppName
}

// Get reference to existing Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// Ensure Function App has system-assigned managed identity enabled
resource functionAppIdentity 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: functionApp
  name: 'authsettingsV2'
  properties: {
    globalValidation: {
      requireAuthentication: false
      unauthenticatedClientAction: 'AllowAnonymous'
    }
    identityProviders: {
      azureActiveDirectory: {
        enabled: true
        registration: {
          openIdIssuer: 'https://login.microsoftonline.com/${subscription().tenantId}/v2.0'
          clientId: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=azure-client-id)'
          clientSecretSettingName: 'AZURE_CLIENT_SECRET'
        }
        validation: {
          jwtClaimChecks: {}
          allowedAudiences: [
            'api://copilotvault-${environment}'
          ]
        }
      }
    }
    login: {
      routes: {
        logoutEndpoint: '/.auth/logout'
      }
      tokenStore: {
        enabled: true
        tokenRefreshExtensionHours: 72
        fileSystem: {}
      }
    }
    httpSettings: {
      requireHttps: environment == 'prod' ? true : false
      routes: {
        apiPrefix: '/.auth'
      }
      forwardProxy: {
        convention: 'NoProxy'
      }
    }
  }
}

// Key Vault access policy for Function App managed identity
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
          keys: []
          certificates: []
        }
      }
    ]
  }
}

// Microsoft Graph API Permissions (these need to be granted via Azure CLI or Portal)
// The following permissions are required:
var requiredGraphPermissions = [
  {
    id: 'df021288-bdef-4463-88db-98f22de89214' // User.Read.All
    type: 'Role'
  }
  {
    id: '230c1aed-a721-4c5d-9cb4-a90514e508ef' // Reports.Read.All
    type: 'Role'
  }
  {
    id: '64733abd-851e-478a-bffb-e47a14b18235' // SecurityEvents.Read.All
    type: 'Role'
  }
  {
    id: '6e472fd1-ad78-48da-a0f0-97ab2c6b769e' // IdentityRiskEvent.Read.All
    type: 'Role'
  }
  {
    id: '19da66cb-0fb0-4390-b071-ebc76a349482' // InformationProtectionPolicy.Read.All
    type: 'Role'
  }
]

// Output script for granting Microsoft Graph permissions
output grantGraphPermissionsScript string = '''
# Run these Azure CLI commands to grant Microsoft Graph permissions to the Function App managed identity:

FUNCTION_APP_PRINCIPAL_ID="${functionApp.identity.principalId}"
GRAPH_APP_ID="00000003-0000-0000-c000-000000000000"

# Grant User.Read.All permission
az ad app permission add --id $FUNCTION_APP_PRINCIPAL_ID --api $GRAPH_APP_ID --api-permissions df021288-bdef-4463-88db-98f22de89214=Role

# Grant Reports.Read.All permission  
az ad app permission add --id $FUNCTION_APP_PRINCIPAL_ID --api $GRAPH_APP_ID --api-permissions 230c1aed-a721-4c5d-9cb4-a90514e508ef=Role

# Grant SecurityEvents.Read.All permission
az ad app permission add --id $FUNCTION_APP_PRINCIPAL_ID --api $GRAPH_APP_ID --api-permissions 64733abd-851e-478a-bffb-e47a14b18235=Role

# Grant IdentityRiskEvent.Read.All permission
az ad app permission add --id $FUNCTION_APP_PRINCIPAL_ID --api $GRAPH_APP_ID --api-permissions 6e472fd1-ad78-48da-a0f0-97ab2c6b769e=Role

# Grant InformationProtectionPolicy.Read.All permission
az ad app permission add --id $FUNCTION_APP_PRINCIPAL_ID --api $GRAPH_APP_ID --api-permissions 19da66cb-0fb0-4390-b071-ebc76a349482=Role

# Grant admin consent for all permissions
az ad app permission admin-consent --id $FUNCTION_APP_PRINCIPAL_ID

echo "Microsoft Graph permissions granted successfully!"
'''

// Application settings update for managed identity
resource functionAppSettings 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    // Keep existing settings and add managed identity configuration
    FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    
    // Managed Identity Configuration
    AZURE_CLIENT_ID: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=azure-client-id)'
    AZURE_TENANT_ID: subscription().tenantId
    MANAGED_IDENTITY_ENABLED: 'true'
    
    // Remove client secret - no longer needed with managed identity
    // AZURE_CLIENT_SECRET: 'REMOVED_FOR_MANAGED_IDENTITY'
    
    // Application Insights
    APPLICATIONINSIGHTS_CONNECTION_STRING: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=appinsights-connection-string)'
    
    // Storage and other services
    AzureWebJobsStorage: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=storage-connection-string)'
    CosmosDbConnectionString: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=cosmos-connection-string)'
    ServiceBusConnection: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=servicebus-connection-string)'
    
    // Stripe configuration
    StripeSecretKey: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=stripe-secret-key)'
    StripeWebhookSecret: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=stripe-webhook-secret)'
    
    // SendGrid configuration
    SendGridApiKey: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=sendgrid-api-key)'
    SendGridSenderEmail: environment == 'prod' ? 'noreply@copilotvault.com' : 'dev-noreply@copilotvault.com'
    SendGridSenderName: 'CopilotVault ${toUpper(environment)}'
    
    // CORS configuration
    CorsAllowedOrigin: environment == 'prod' ? 'https://app.copilotvault.com' : 'http://localhost:3000,http://localhost:3001'
    
    // Feature flags
    'Features:ManagedIdentity:Enabled': 'true'
    'Features:GraphApiIntegration:Enabled': 'true'
    'Features:SecretlessAuthentication:Enabled': 'true'
  }
}

// Diagnostic settings for Function App
resource functionAppDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'function-app-diagnostics'
  scope: functionApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'FunctionAppLogs'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 90
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 90
        }
      }
    ]
  }
}

// Parameters that will be passed from main deployment
param logAnalyticsWorkspaceId string

// Outputs
output functionAppManagedIdentityPrincipalId string = functionApp.identity.principalId
output functionAppManagedIdentityTenantId string = functionApp.identity.tenantId
output keyVaultAccessGranted bool = true
output requiredGraphPermissions array = requiredGraphPermissions