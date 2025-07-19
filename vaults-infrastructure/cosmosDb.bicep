@description('Azure region for all resources')
param location string = 'newzealandnorth'

@description('Prefix for all resource names')
param resourcePrefix string = 'copilotvault'

@description('Environment name (dev, staging, prod)')
param envName string = 'dev'

@description('Name of the Virtual Network')
param vnetName string

@description('Name of the Subnet')
param subnetName string

var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id)
var cosmosDbName = 'cosmos-${resourcePrefix}-${envName}-${take(uniqueSuffix, 6)}'

resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: cosmosDbName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    backupPolicy: {
      type: 'Periodic'
      periodicModeProperties: {
        backupIntervalInMinutes: 240
        backupRetentionIntervalInHours: 8
        backupStorageRedundancy: 'Local'
      }
    }
    isVirtualNetworkFilterEnabled: true
    enableAutomaticFailover: true
    enableMultipleWriteLocations: false
    disableKeyBasedMetadataWriteAccess: false
    publicNetworkAccess: 'Disabled'
    virtualNetworkRules: [
      {
        id: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, subnetName)
        ignoreMissingVNetServiceEndpoint: false
      }
    ]
  }
  dependsOn: [
    // Assuming virtual network is deployed separately and its outputs are passed as parameters
  ]
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: cosmosDbAccount
  name: 'CopilotVault'
  properties: {
    resource: {
      id: 'CopilotVault'
    }
  }
}

resource cosmosContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: cosmosDatabase
  name: 'Interactions'
  properties: {
    resource: {
      id: 'Interactions'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          {
            path: '/*'
          }
        ]
        excludedPaths: [
          {
            path: '/"_etag"/?'
          }
        ]
      }
      defaultTtl: -1
    }
  }
}

resource tenantsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: cosmosDatabase
  name: 'Tenants'
  properties: {
    resource: {
      id: 'Tenants'
      partitionKey: {
        paths: ['/id']
        kind: 'Hash'
      }
    }
  }
}

output cosmosDbAccountName string = cosmosDbAccount.name
output cosmosDbEndpoint string = cosmosDbAccount.properties.documentEndpoint
output cosmosDbConnectionString string = cosmosDbAccount.listConnectionStrings().connectionStrings[0].connectionString
output cosmosDbAccountId string = cosmosDbAccount.id
