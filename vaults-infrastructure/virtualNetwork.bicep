@description('Azure region for all resources')
param location string = 'newzealandnorth' // Explicitly set to newzealandnorth for optimal performance

var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id)
var resourcePrefix = 'copilotvault' // Assuming a default prefix for independent deployment
var envName = 'dev' // Assuming a default environment for independent deployment

var vnetName = 'vnet-${resourcePrefix}-${envName}-${take(uniqueSuffix, 6)}'
var subnetName = 'snet-${resourcePrefix}-${envName}-${take(uniqueSuffix, 6)}'

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2023-05-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: subnetName
        properties: {
          addressPrefix: '10.0.0.0/24'
          serviceEndpoints: [
            {
              service: 'Microsoft.AzureCosmosDB'
              locations: [location]
            }
            {
              service: 'Microsoft.Storage'
              locations: [location]
            }
          ]
          delegations: [
            {
              name: 'Microsoft.Web.serverFarms'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

output vnetName string = vnetName
output subnetName string = subnetName
