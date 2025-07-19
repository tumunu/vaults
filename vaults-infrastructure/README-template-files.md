# Infrastructure Template Files

The following parameter files have been moved to `.private-config/` for security and contain live production data:

## Moved Parameter Files

- `parameters.json` - Contains Azure tenant ID and subscription ID
- `functionApp.parameters.json` - Contains live Azure Storage account keys, Cosmos DB connection strings, Application Insights keys, and Stripe webhook secrets
- `keyVault.parameters.json` - Contains Azure tenant ID and vault naming
- `keyVaultRoleAssignment.parameters.json` - Contains function app principal ID and tenant ID
- `cosmosDbSchema.parameters.json` - Contains Cosmos DB account ID and naming
- `storageAccountRoleAssignment.json` - Contains subscription ID and function app principal ID

## Moved Deployment Scripts

- `deploy-managed-identity.sh` - Contains hardcoded Azure resource names and deployment patterns
- `deploy-operational-foundation.sh` - Contains hardcoded Azure resource names and deployment patterns

## Creating Template Files for Public Deployment

To use this infrastructure in a new environment, create template versions of the moved files with placeholder values:

### Example: parameters.template.json
```json
{
  "$schema": "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
  "contentVersion": "1.0.0.0",
  "parameters": {
    "location": {
      "value": "YOUR_AZURE_REGION"
    },
    "tenantId": {
      "value": "YOUR_TENANT_ID"
    },
    "subscriptionId": {
      "value": "YOUR_SUBSCRIPTION_ID"
    }
  }
}
```

### Security Notes

- All moved files contain live production credentials and identifiers
- Never commit the actual parameter files to public repositories
- Always use template files with placeholder values for public deployment
- Ensure all subscription IDs, tenant IDs, and connection strings are properly templated