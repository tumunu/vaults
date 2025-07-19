param keyVaultName string
param functionAppPrincipalId string
param tenantId string

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource keyVaultSecretUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionAppPrincipalId, 'Key Vault Secret User') // Generate a unique GUID for the role assignment
  scope: keyVault
  properties: {
    roleDefinitionId: '${subscription().id}/providers/Microsoft.Authorization/roleDefinitions/b86a8fe4-44ce-4948-aee5-eccb2c155cd7' // Key Vault Secrets Officer role ID
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}
