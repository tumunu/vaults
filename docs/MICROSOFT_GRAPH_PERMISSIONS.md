# Microsoft Graph API Permissions for CopilotVault

## Required Application Permissions

After updating to official Microsoft Graph Copilot endpoints, your Azure AD application registration requires the following permissions:

### Core Copilot Permissions
- **`AiEnterpriseInteraction.Read.All`** - For accessing Copilot interaction history
  - Required for: `GetInteractionHistoryAsync()`
  - Endpoint: `/v1.0/copilot/users/getAllEnterpriseInteractions`

### Usage Reports Permissions  
- **`Reports.Read.All`** - For accessing Copilot usage metrics
  - Required for: `GetCopilotUsersAsync()`, `GetCopilotUsageSummaryAsync()`, `GetCopilotUserCountAsync()`
  - Endpoints: `/v1.0/reports/getCopilotUsage*`

### Security & Compliance Permissions
- **`SecurityEvents.Read.All`** - For accessing security alerts
  - Required for: `GetRecentAlertsAsync()`
  - Endpoint: `/v1.0/security/alerts_v2`

- **`IdentityRiskEvent.Read.All`** - For accessing risky users
  - Required for: `GetHighRiskUsersAsync()`
  - Endpoint: `/v1.0/identityProtection/riskyUsers`

- **`InformationProtectionPolicy.Read.All`** - For compliance data
  - Required for: `GetPolicyViolationsAsync()`
  - Endpoint: `/v1.0/compliance/complianceManagementPartner`

## Configuration Steps

### 1. Azure AD App Registration
1. Navigate to Azure Portal > Azure Active Directory > App registrations
2. Select your CopilotVault application
3. Go to **API permissions**
4. Click **Add a permission** > **Microsoft Graph** > **Application permissions**
5. Add all permissions listed above
6. Click **Grant admin consent** for your tenant

### 2. Environment Variables
Ensure these are configured in your Azure Function App:
```
AZURE_TENANT_ID=your-tenant-id
AZURE_CLIENT_ID=your-app-client-id  
AZURE_CLIENT_SECRET=your-app-client-secret
```

### 3. License Requirements
- **Microsoft 365 Copilot license** required for users whose data is being accessed
- **Azure AD Premium P2** recommended for Identity Protection features
- **Microsoft 365 E5** or **Security & Compliance E5** for full security features

## Updated Endpoints Summary

| Function | Old Endpoint (Non-existent) | New Official Endpoint |
|----------|----------------------------|----------------------|
| `GetRecentAlertsAsync()` | `/copilot/searchfunction?type=recent-alerts` | `/security/alerts_v2` |
| `GetHighRiskUsersAsync()` | `/copilot/searchfunction?type=high-risk-users` | `/identityProtection/riskyUsers` |
| `GetPolicyViolationsAsync()` | `/copilot/searchfunction?type=policy-violations` | `/compliance/complianceManagementPartner` |
| `GetInteractionHistoryAsync()` | `/copilot/interactionHistory` | `/copilot/users/getAllEnterpriseInteractions` |
| `GetCopilotUsersAsync()` | `/copilot/users` | `/reports/getCopilotUsageUserDetail(period='D7')` |

## New Capabilities Added

### Official Usage Metrics
- `GetCopilotUsageSummaryAsync()` - Tenant-level usage summary
- `GetCopilotUserCountAsync()` - Licensed vs active user counts

### Enhanced Error Handling
All methods now include specific permission requirement messages in error logs.

## Testing

Use **Microsoft Graph Explorer** (https://developer.microsoft.com/graph/graph-explorer) to test these endpoints with your credentials before deployment.

## Notes

- All endpoints now use official Microsoft Graph APIs (post-beta)
- Fallback mock data is maintained for development/testing scenarios
- Enhanced logging includes specific permission requirements for troubleshooting