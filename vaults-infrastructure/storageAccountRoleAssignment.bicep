@description('Resource ID of the Storage Account in format: /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.Storage/storageAccounts/{name}')
@minLength(1)
param storageAccountId string

@description('Principal ID of the Function App for role assignment')
param functionAppPrincipalId string

var storageAccountName = last(split(storageAccountId, '/'))

resource storageAccount 'Microsoft.Storage/storageAccounts@2022-09-01' existing = {
  name: storageAccountName
}

resource storageBlobDataContributorRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, functionAppPrincipalId, 'StorageBlobDataContributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}
