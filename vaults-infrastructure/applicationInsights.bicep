@description('Azure region for all resources')
param location string = 'newzealandnorth'

@description('Prefix for all resource names')
param resourcePrefix string = 'copilotvault'

@description('Environment name (dev, staging, prod)')
param envName string = 'dev'

var appInsightsName = 'appi-copilotvault-nz'

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Redfield'
    Request_Source: 'IbizaAIExtension'
    RetentionInDays: 90
    DisableIpMasking: false
  }
}

output applicationInsightsName string = applicationInsights.name
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
