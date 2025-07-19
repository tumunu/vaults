@description('Azure region for all resources')
param location string = 'newzealandnorth'

@description('Prefix for all resource names')
param resourcePrefix string = 'copilotvault'

@description('Environment name (dev, staging, prod)')
param envName string = 'dev'

var logAnalyticsName = 'managed-appi-copilotvault-nz-ws'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 90
  }
}

output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
