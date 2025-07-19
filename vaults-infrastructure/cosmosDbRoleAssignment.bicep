@description('Name of the Cosmos DB Account')
param cosmosDbAccountName string

@description('Principal ID of the Function App for role assignment')
param functionAppPrincipalId string

resource cosmosDbAccount 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosDbAccountName
}

resource cosmosDbRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-04-15' = {
  parent: cosmosDbAccount
  name: guid(cosmosDbAccount.id, functionAppPrincipalId, 'CosmosDBDataContributor')
  properties: {
    roleDefinitionId: '${cosmosDbAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: functionAppPrincipalId
    scope: cosmosDbAccount.id
  }
}
