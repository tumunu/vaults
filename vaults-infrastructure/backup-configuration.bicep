// Backup and Disaster Recovery Configuration for CopilotVault
@description('Environment name (dev, staging, prod)')
param environment string = 'prod'

@description('Location for resources')
param location string = resourceGroup().location

@description('Function App name')
param functionAppName string

@description('Storage account name for backups')
param backupStorageAccountName string

@description('Key Vault name')
param keyVaultName string

// Storage Account for Function App Backups
resource backupStorageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: backupStorageAccountName
  location: location
  sku: {
    name: 'Standard_GRS'  // Geo-redundant storage for disaster recovery
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Cool'  // Cost-effective for backup storage
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    encryption: {
      services: {
        blob: {
          enabled: true
        }
        file: {
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
    networkAcls: {
      defaultAction: 'Deny'
      virtualNetworkRules: []
      ipRules: []
      bypass: 'AzureServices'
    }
  }
  
  tags: {
    Environment: environment
    Purpose: 'FunctionAppBackup'
    'Backup-Retention': '30-days'
    'Cost-Center': 'Engineering'
  }
}

// Backup container for Function App
resource backupContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${backupStorageAccount.name}/default/function-app-backups'
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'function-app-automated-backups'
      retention: '30-days'
    }
  }
}

// Key Vault for secure storage of secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'premium'  // Premium for HSM-backed keys
    }
    tenantId: subscription().tenantId
    enabledForDeployment: false
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: false
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enableRbacAuthorization: true
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      ipRules: []
      virtualNetworkRules: []
    }
    publicNetworkAccess: 'Disabled'  // Private endpoint only
  }
  
  tags: {
    Environment: environment
    Purpose: 'SecretManagement'
    'Backup-Enabled': 'true'
    'Cost-Center': 'Engineering'
  }
}

// Log Analytics Workspace for centralized logging
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'law-copilotvault-${environment}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: environment == 'prod' ? 730 : 90  // 2 years for prod, 90 days for non-prod
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: environment == 'prod' ? 10 : 2
    }
  }
  
  tags: {
    Environment: environment
    Purpose: 'CentralizedLogging'
    'Retention-Days': environment == 'prod' ? '730' : '90'
    'Cost-Center': 'Engineering'
  }
}

// Application Insights for monitoring
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'ai-copilotvault-${environment}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    RetentionInDays: environment == 'prod' ? 730 : 90
    SamplingPercentage: environment == 'prod' ? 10 : 100
    DisableIpMasking: false
    DisableLocalAuth: true  // Use Azure AD authentication only
  }
  
  tags: {
    Environment: environment
    Purpose: 'ApplicationMonitoring'
    'Cost-Center': 'Engineering'
  }
}

// Action Group for alerts
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-copilotvault-${environment}'
  location: 'Global'
  properties: {
    groupShortName: 'CopilotVlt'
    enabled: true
    emailReceivers: [
      {
        name: 'Engineering Team'
        emailAddress: 'engineering-alerts@company.com'
        useCommonAlertSchema: true
      }
    ]
    smsReceivers: []
    webhookReceivers: []
    azureAppPushReceivers: []
    itsmReceivers: []
    azureAppPushReceivers: []
    automationRunbookReceivers: []
    voiceReceivers: []
    logicAppReceivers: []
    azureFunctionReceivers: []
    armRoleReceivers: []
  }
}

// Budget for cost monitoring
resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  scope: resourceGroup()
  name: 'budget-copilotvault-${environment}'
  properties: {
    timePeriod: {
      startDate: '2025-01-01'
      endDate: '2026-12-31'
    }
    timeGrain: 'Monthly'
    amount: environment == 'prod' ? 2000 : 500
    category: 'Cost'
    notifications: {
      'Actual_50_Percent': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        contactEmails: [
          'engineering-leads@company.com'
        ]
        contactRoles: []
        contactGroups: []
        thresholdType: 'Actual'
      }
      'Actual_80_Percent': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        contactEmails: [
          'engineering-leads@company.com'
          'finance-team@company.com'
        ]
        contactRoles: []
        contactGroups: []
        thresholdType: 'Actual'
      }
      'Forecast_100_Percent': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        contactEmails: [
          'on-call@company.com'
        ]
        contactRoles: []
        contactGroups: []
        thresholdType: 'Forecasted'
      }
      'Actual_150_Percent_Spike': {
        enabled: true
        operator: 'GreaterThan'
        threshold: 150
        contactEmails: [
          'on-call@company.com'
          'executives@company.com'
        ]
        contactRoles: []
        contactGroups: []
        thresholdType: 'Actual'
      }
    }
  }
}

// Outputs
output backupStorageAccountId string = backupStorageAccount.id
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output applicationInsightsId string = applicationInsights.id
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
output actionGroupId string = actionGroup.id
output budgetId string = budget.id