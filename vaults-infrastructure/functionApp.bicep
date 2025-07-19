@description('Azure region for all resources')
param location string = 'newzealandnorth'

@description('Prefix for all resource names')
param resourcePrefix string = 'copilotvault'

@description('Environment name (dev, staging, prod)')
param envName string = 'dev'

@description('ID of the App Service Plan')
param appServicePlanId string

@description('Connection string for the Storage Account')
@secure()
param storageAccountConnectionString string

@description('Connection string for Cosmos DB')
@secure()
param cosmosDbConnectionString string

@description('Connection string for Application Insights')
@secure()
param applicationInsightsConnectionString string

@description('Stripe Webhook Secret for signature validation')
@secure()
param stripeWebhookSecret string

@description('Custom domain for the backend API')
param customDomain string = 'muri.copilotvaults.com'

var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id)
var functionAppName = 'func-${resourcePrefix}-${envName}-${take(uniqueSuffix, 6)}'

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
        {
          name: 'AzureWebJobsStorage'
          value: storageAccountConnectionString
        }
        {
          name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
          value: storageAccountConnectionString
        }
        {
          name: 'STORAGE_ACCOUNT_NAME'
          value: substring(split(storageAccountConnectionString, ';')[1], 12)
        }
        {
          name: 'CUSTOMER_BLOB_STORAGE_CONNECTION_STRING'
          value: storageAccountConnectionString
        }
        {
          name: 'AzureWebJobsStorage__accountName'
          value: substring(split(storageAccountConnectionString, ';')[1], 12)
        }
        {
          name: 'WEBSITE_CONTENTSHARE'
          value: toLower('content-${functionAppName}')
        }
        {
          name: 'STORAGE_CONTAINER_NAME'
          value: 'exports'
        }
        {
          name: 'COSMOS_DB_CONNECTION_STRING'
          value: cosmosDbConnectionString
        }
        {
          name: 'ENVIRONMENT'
          value: envName
        }
        {
          name: 'StripeWebhookSecret'
          value: stripeWebhookSecret
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        {
          name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE'
          value: 'true'
        }
      ]
    }
    // virtualNetworkSubnetId: resourceId('Microsoft.Network/virtualNetworks/subnets', vnetName, subnetName)
  }
}

resource customDomainBinding 'Microsoft.Web/sites/hostNameBindings@2023-01-01' = {
  parent: functionApp
  name: customDomain
  properties: {
    customHostNameDnsRecordType: 'CName'
    hostNameType: 'Verified'
  }
}

output functionAppName string = functionApp.name
output functionAppUrl string = 'https://${customDomain}'
output functionAppDefaultUrl string = 'https://${functionApp.properties.defaultHostName}'
output functionAppPrincipalId string = functionApp.identity.principalId
output functionAppResourceId string = functionApp.id
