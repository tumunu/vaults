@description('Azure region for all resources')
param location string = 'newzealandnorth' // Explicitly set to newzealandnorth for optimal performance

param vnetName string
param subnetName string

var storageAccountName  = 'stcopilotvaultnz'      // Max 24 chars, only lowercase + numbers

// ─────────────────────────────────────────────────────────────────────────────
// 2) Storage Account (Function App backend) – TLS 1.2, encryption at rest
// ─────────────────────────────────────────────────────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
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
      defaultAction: 'Deny' // Restrict public access for production
      bypass: 'AzureServices' // Allow Azure services like Function Apps
      virtualNetworkRules: [
        {
          id: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, subnetName)
          action: 'Allow'
          
        }
      ]
    }
  }
}

resource storageContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${storageAccount.name}/default/exports'
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'copilot-vault-exports'
    }
  }
  dependsOn: [
    storageAccount
  ]
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output storageAccountConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
