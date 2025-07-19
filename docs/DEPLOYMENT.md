# B2B Invitation System - Deployment Guide

## Overview
This deployment guide covers the setup of the automated B2B invitation system that solves the "account needs to be added as external user" error for corporate clients.

## Prerequisites

### Azure Resources Required
1. **Azure Function App** (Consumption or Premium plan)
2. **Service Bus Namespace** (Standard tier for sessions/deduplication)
3. **Cosmos DB Account** (existing)
4. **Application Insights** (for telemetry)
5. **Azure AD App Registration** (with proper Graph permissions)

### Azure AD App Registration Setup
The Function App requires an app registration with the following permissions:

#### Application Permissions (Admin Consent Required)
- `User.Invite.All` - Send B2B invitations
- `Directory.ReadWrite.All` - Read user information
- `User.Read.All` - List tenant users

#### Setup Steps
1. Create new App Registration in Azure AD
2. Generate client secret (store in Key Vault)
3. Grant admin consent for required permissions
4. Assign Function App system-assigned managed identity to required roles

## Configuration

### Application Settings
Add the following settings to your Function App:

```json
{
  "AZURE_TENANT_ID": "your-tenant-id",
  "AZURE_CLIENT_ID": "your-app-registration-client-id",
  "AZURE_CLIENT_SECRET": "@Microsoft.KeyVault(SecretUri=https://vault.vault.azure.net/secrets/graph-client-secret/)",
  "ServiceBusConnection": "@Microsoft.KeyVault(SecretUri=https://vault.vault.azure.net/secrets/servicebus-connection/)",
  "DashboardUrl": "https://portal.copilotvaults.com"
}
```

### Trusted Domains Configuration
Configure in `appsettings.json` or App Settings:

```json
{
  "TrustedDomains": {
    "Domains": [
      "company1.com",
      "company2.com",
      "enterprise.com"
    ]
  }
}
```

### Service Bus Queue Setup
Create queue with these settings:
- **Name**: `invite-queue`
- **Requires Session**: `true` (for deduplication)
- **Duplicate Detection History**: `3 hours`
- **Max Delivery Count**: `5`
- **Lock Duration**: `5 minutes`
- **Dead Letter on Expiration**: `true`

## Security Configuration

### Managed Identity (Recommended)
1. Enable system-assigned managed identity on Function App
2. Grant identity access to:
   - Key Vault (Secrets Get)
   - Service Bus (Send/Receive)
   - Cosmos DB (Data Contributor)

### Key Vault Secrets
Store sensitive configuration:
- `graph-client-secret`: Azure AD app secret
- `servicebus-connection`: Service Bus connection string
- `stripe-webhook-secret`: Stripe webhook secret

## Application Insights Setup

### Custom Events to Monitor
- `InviteStarted` - Invitation process initiated
- `InviteSent` - Graph API invitation successful
- `InviteSkipped` - User already exists/invited
- `InviteFailed` - Invitation failed
- `InvitationQueued` - Invitation added to queue
- `RetryFailedInvitationsTriggered` - Retry timer function

### Alert Rules
1. **InviteFailed > 3 in 10 minutes** (Severity 2)
2. **Webhook latency > 30 seconds** (Severity 3)
3. **Service Bus dead letter count > 0** (Severity 2)

### KQL Queries for Monitoring
```kql
// Failed invitations by error type
customEvents
| where name == "InviteFailed"
| summarize count() by tostring(customDimensions.Error)
| order by count_ desc

// Invitation success rate
customEvents
| where name in ("InviteSent", "InviteSkipped", "InviteFailed")
| summarize total=count(), 
    successful=countif(name in ("InviteSent", "InviteSkipped")),
    failed=countif(name == "InviteFailed")
| extend successRate = (successful * 100.0) / total
```

## Cost Estimates (NZD)

### Azure Resources
- **Function App (Consumption)**: ~$10-30/month
- **Service Bus (Standard)**: ~$40/month
- **Application Insights**: ~$5-15/month
- **Key Vault**: ~$3/month

**Total Monthly**: ~$60-90 NZD

### Scaling Considerations
- Service Bus Standard supports up to 1,000 concurrent connections
- Function App Consumption scales automatically
- Cosmos DB RU consumption minimal for invitation tracking

## Testing

### Unit Tests
```bash
dotnet test --filter Category=Unit
```

### Integration Tests
Requires MSAL Lab tenant or test environment:
```bash
dotnet test --filter Category=Integration
```

### Manual Testing Flow
1. **Stripe Test Payment**: Use test webhook to trigger invitation
2. **Direct HTTP Call**: POST to `/api/invite/user`
3. **Admin Functions**: GET `/api/admin/users` to verify
4. **Retry Logic**: Force failure and verify retry behavior

## Deployment Steps

1. **Deploy Function App Code**
   ```bash
   func azure functionapp publish YourFunctionApp
   ```

2. **Create Service Bus Queue**
   ```bash
   az servicebus queue create --name invite-queue --namespace-name your-namespace --requires-session true
   ```

3. **Configure App Settings**
   ```bash
   az functionapp config appsettings set --name YourFunctionApp --settings @appsettings.json
   ```

4. **Verify Graph Permissions**
   - Test `/api/admin/users` endpoint
   - Verify invitation flow with test domain

## Monitoring & Maintenance

### Daily Checks
- Review Application Insights dashboard
- Monitor Service Bus dead letter queue
- Check invitation success rates

### Weekly Maintenance
- Review failed invitations in logs
- Update trusted domains list if needed
- Monitor cost usage against budget

### Troubleshooting
- **403 Errors**: Check Graph API permissions
- **Queue Backlog**: Verify Service Bus connection
- **Failed Invitations**: Check domain validation and user existence

## Support

For issues with the B2B invitation system:
1. Check Application Insights logs for specific error messages
2. Verify Azure AD app registration permissions
3. Test Graph API connectivity using Graph Explorer
4. Review Service Bus queue health and dead letter messages