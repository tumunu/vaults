@description('Azure region for all resources')
param location string = 'newzealandnorth'

@description('Prefix for all resource names')
param resourcePrefix string = 'copilotvault'

@description('Environment name (dev, staging, prod)')
param envName string = 'dev'

var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id)
var appServicePlanName = 'plan-${resourcePrefix}-${envName}-${take(uniqueSuffix, 6)}'

resource functionAppServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  sku: {
    name: envName == 'prod' ? 'S1' : 'B1'
    tier: envName == 'prod' ? 'Standard' : 'Basic'
  }
  kind: 'app'
  properties: {
    reserved: false
  }
}

output appServicePlanId string = functionAppServicePlan.id
