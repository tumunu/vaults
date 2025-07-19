// Enhanced APIM Configuration with RBAC for CopilotVault
@description('Environment name (dev, staging, prod)')
param environment string = 'prod'

@description('Location for resources')
param location string = resourceGroup().location

@description('Function App name to backend')
param functionAppName string

@description('Azure AD tenant ID')
param azureTenantId string

@description('Azure AD application client ID')
param azureClientId string

@description('Publisher email for APIM')
param publisherEmail string = 'admin@copilotvault.com'

@description('Publisher name for APIM')
param publisherName string = 'CopilotVault'

// APIM Service
resource apimService 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: 'apim-copilotvault-${environment}'
  location: location
  sku: {
    name: environment == 'prod' ? 'Standard' : 'Developer'
    capacity: environment == 'prod' ? 2 : 1
  }
  properties: {
    publisherEmail: publisherEmail
    publisherName: publisherName
    customProperties: {
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls10': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Tls11': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Protocols.Ssl30': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Tls10': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Tls11': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Security.Backend.Protocols.Ssl30': 'false'
      'Microsoft.WindowsAzure.ApiManagement.Gateway.Protocols.Server.Http2': 'true'
    }
    virtualNetworkType: 'None'
    disableGateway: false
    apiVersionConstraint: {
      minApiVersion: '2019-12-01'
    }
    publicNetworkAccess: 'Enabled'
  }
  identity: {
    type: 'SystemAssigned'
  }
  
  tags: {
    Environment: environment
    Purpose: 'API-Gateway'
    'Cost-Center': 'Engineering'
  }
}

// Named Values for configuration
resource namedValueTenantId 'Microsoft.ApiManagement/service/namedValues@2023-05-01-preview' = {
  parent: apimService
  name: 'azure-tenant-id'
  properties: {
    displayName: 'Azure Tenant ID'
    value: azureTenantId
    secret: false
  }
}

resource namedValueClientId 'Microsoft.ApiManagement/service/namedValues@2023-05-01-preview' = {
  parent: apimService
  name: 'azure-client-id'
  properties: {
    displayName: 'Azure Client ID'
    value: azureClientId
    secret: false
  }
}

resource namedValueEnvironment 'Microsoft.ApiManagement/service/namedValues@2023-05-01-preview' = {
  parent: apimService
  name: 'environment'
  properties: {
    displayName: 'Environment'
    value: environment
    secret: false
  }
}

// Backend for Function App
resource functionAppBackend 'Microsoft.ApiManagement/service/backends@2023-05-01-preview' = {
  parent: apimService
  name: 'copilotvault-function-backend'
  properties: {
    description: 'CopilotVault Function App Backend'
    url: 'https://${functionAppName}.azurewebsites.net'
    protocol: 'http'
    properties: {
      serviceFabricCluster: {}
    }
    tls: {
      validateCertificateChain: true
      validateCertificateName: true
    }
    credentials: {
      certificate: []
      query: {}
      header: {}
      authorization: {
        scheme: 'Bearer'
        parameter: '{{function-app-key}}'
      }
    }
  }
}

// Products for API versioning
resource legacyProduct 'Microsoft.ApiManagement/service/products@2023-05-01-preview' = {
  parent: apimService
  name: 'copilotvault-v0-legacy'
  properties: {
    displayName: 'CopilotVault v0 (Legacy)'
    description: 'Legacy API endpoints with function key authentication (deprecated)'
    subscriptionRequired: false
    approvalRequired: false
    state: 'published'
    terms: 'This API version is deprecated and will be removed on December 31, 2025. Please migrate to v1.'
  }
}

resource currentProduct 'Microsoft.ApiManagement/service/products@2023-05-01-preview' = {
  parent: apimService
  name: 'copilotvault-v1-ga'
  properties: {
    displayName: 'CopilotVault v1 (General Availability)'
    description: 'Modern API endpoints with Azure AD authentication and RBAC'
    subscriptionRequired: true
    approvalRequired: false
    state: 'published'
    terms: 'Production-ready API with enterprise-grade security and monitoring.'
  }
}

resource futureProduct 'Microsoft.ApiManagement/service/products@2023-05-01-preview' = {
  parent: apimService
  name: 'copilotvault-v2-preview'
  properties: {
    displayName: 'CopilotVault v2 (Preview)'
    description: 'Preview API endpoints for future features'
    subscriptionRequired: true
    approvalRequired: true
    state: 'published'
    terms: 'Preview API - subject to breaking changes. Use for testing only.'
  }
}

// API Definition for V1
resource apiV1 'Microsoft.ApiManagement/service/apis@2023-05-01-preview' = {
  parent: apimService
  name: 'copilotvault-v1'
  properties: {
    displayName: 'CopilotVault API v1'
    description: 'Enterprise Microsoft 365 Copilot governance and auditing platform'
    serviceUrl: 'https://${functionAppName}.azurewebsites.net/api'
    path: 'api/v1'
    protocols: ['https']
    subscriptionRequired: true
    apiVersion: 'v1'
    apiVersionSetId: apiVersionSet.id
    authenticationSettings: {
      oAuth2: {
        authorizationServerId: 'azure-ad-oauth'
        scope: 'CopilotVault.ReadUsage CopilotVault.ReadSecurity CopilotVault.ManageUsers CopilotVault.ReadBilling'
      }
    }
    subscriptionKeyParameterNames: {
      header: 'Ocp-Apim-Subscription-Key'
      query: 'subscription-key'
    }
    format: 'openapi+json'
    value: loadTextContent('./apim-openapi-spec.json')
  }
}

// API Version Set
resource apiVersionSet 'Microsoft.ApiManagement/service/apiVersionSets@2023-05-01-preview' = {
  parent: apimService
  name: 'copilotvault-versions'
  properties: {
    displayName: 'CopilotVault API Versions'
    description: 'Version set for CopilotVault APIs'
    versioningScheme: 'Segment'
    versionQueryName: 'api-version'
    versionHeaderName: 'Api-Version'
  }
}

// OAuth2 Authorization Server
resource oauthServer 'Microsoft.ApiManagement/service/authorizationServers@2023-05-01-preview' = {
  parent: apimService
  name: 'azure-ad-oauth'
  properties: {
    displayName: 'Azure AD OAuth2'
    description: 'Azure Active Directory OAuth2 authorization server'
    clientRegistrationEndpoint: 'https://login.microsoftonline.com/${azureTenantId}/oauth2/v2.0/authorize'
    authorizationEndpoint: 'https://login.microsoftonline.com/${azureTenantId}/oauth2/v2.0/authorize'
    tokenEndpoint: 'https://login.microsoftonline.com/${azureTenantId}/oauth2/v2.0/token'
    clientId: azureClientId
    grantTypes: ['authorizationCode', 'implicit']
    authorizationMethods: ['GET', 'POST']
    bearerTokenSendingMethods: ['authorizationHeader']
    defaultScope: 'CopilotVault.ReadUsage'
    supportState: true
    tokenBodyParameters: []
    clientSecret: '{{azure-client-secret}}'
  }
}

// Global Policy for all APIs
resource globalPolicy 'Microsoft.ApiManagement/service/policies@2023-05-01-preview' = {
  parent: apimService
  name: 'policy'
  properties: {
    value: '''
<policies>
  <inbound>
    <!-- Remove Server header for security -->
    <set-header name="Server" exists-action="delete" />
    
    <!-- Add security headers -->
    <set-header name="X-Content-Type-Options" exists-action="override">
      <value>nosniff</value>
    </set-header>
    <set-header name="X-Frame-Options" exists-action="override">
      <value>DENY</value>
    </set-header>
    <set-header name="X-XSS-Protection" exists-action="override">
      <value>1; mode=block</value>
    </set-header>
    <set-header name="Strict-Transport-Security" exists-action="override">
      <value>max-age=31536000; includeSubDomains</value>
    </set-header>
    
    <!-- CORS policy -->
    <cors allow-credentials="true">
      <allowed-origins>
        <origin>https://app.copilotvault.com</origin>
        <origin>https://staging.copilotvault.com</origin>
        <origin>http://localhost:3000</origin>
        <origin>http://localhost:3001</origin>
      </allowed-origins>
      <allowed-methods>
        <method>GET</method>
        <method>POST</method>
        <method>PUT</method>
        <method>DELETE</method>
        <method>OPTIONS</method>
      </allowed-methods>
      <allowed-headers>
        <header>Authorization</header>
        <header>Content-Type</header>
        <header>Accept</header>
        <header>X-Requested-With</header>
        <header>Api-Version</header>
      </allowed-headers>
      <expose-headers>
        <header>X-RateLimit-Remaining</header>
        <header>X-RateLimit-Limit</header>
        <header>X-Response-Time</header>
      </expose-headers>
    </cors>
    
    <!-- Request ID for tracing -->
    <set-variable name="requestId" value="@(Guid.NewGuid().ToString())" />
    <set-header name="X-Request-ID" exists-action="override">
      <value>@((string)context.Variables["requestId"])</value>
    </set-header>
    
    <!-- Response time tracking -->
    <set-variable name="requestStartTime" value="@(DateTime.UtcNow)" />
  </inbound>
  
  <backend>
    <forward-request />
  </backend>
  
  <outbound>
    <!-- Add response time header -->
    <set-header name="X-Response-Time" exists-action="override">
      <value>@{
        var startTime = (DateTime)context.Variables["requestStartTime"];
        var duration = DateTime.UtcNow - startTime;
        return duration.TotalMilliseconds.ToString() + "ms";
      }</value>
    </set-header>
    
    <!-- Remove sensitive headers from response -->
    <set-header name="X-Powered-By" exists-action="delete" />
    <set-header name="X-AspNet-Version" exists-action="delete" />
  </outbound>
  
  <on-error>
    <!-- Log errors for monitoring -->
    <trace source="global-error-handler" severity="error">
      <message>@{
        return $"API Error: {context.LastError.Source} - {context.LastError.Reason} - {context.LastError.Message}";
      }</message>
      <metadata name="requestId" value="@((string)context.Variables["requestId"])" />
      <metadata name="statusCode" value="@(context.Response.StatusCode.ToString())" />
    </trace>
    
    <!-- Return standardized error response -->
    <return-response>
      <set-status code="@(context.Response.StatusCode)" reason="@(context.Response.StatusReason)" />
      <set-header name="Content-Type" exists-action="override">
        <value>application/json</value>
      </set-header>
      <set-body>@{
        return new JObject(
          new JProperty("error", new JObject(
            new JProperty("code", context.Response.StatusCode),
            new JProperty("message", context.LastError.Message),
            new JProperty("requestId", context.Variables["requestId"]),
            new JProperty("timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
          ))
        ).ToString();
      }</set-body>
    </return-response>
  </on-error>
</policies>
    '''
    format: 'xml'
  }
}

// V1 API Policy with JWT validation and RBAC
resource apiV1Policy 'Microsoft.ApiManagement/service/apis/policies@2023-05-01-preview' = {
  parent: apiV1
  name: 'policy'
  properties: {
    value: '''
<policies>
  <inbound>
    <base />
    
    <!-- JWT Validation for Azure AD -->
    <validate-jwt header-name="Authorization" failed-validation-httpcode="401" failed-validation-error-message="Unauthorized. Valid JWT token required.">
      <openid-config url="https://login.microsoftonline.com/{{azure-tenant-id}}/.well-known/openid_configuration" />
      <required-claims>
        <claim name="aud" match="any">
          <value>{{azure-client-id}}</value>
          <value>api://copilotvault-{{environment}}</value>
        </claim>
        <claim name="iss" match="any">
          <value>https://login.microsoftonline.com/{{azure-tenant-id}}/v2.0</value>
          <value>https://sts.windows.net/{{azure-tenant-id}}/</value>
        </claim>
      </required-claims>
    </validate-jwt>
    
    <!-- Rate limiting by user -->
    <rate-limit-by-key calls="100" renewal-period="60" 
                       counter-key="@(context.Request.Headers.GetValueOrDefault("Authorization","").AsJwt()?.Subject ?? "anonymous")"
                       increment-condition="@(context.Response.StatusCode < 400)" />
    
    <!-- Extract user context -->
    <set-variable name="userObjectId" value="@(context.Request.Headers.GetValueOrDefault("Authorization","").AsJwt()?.Claims.GetValueOrDefault("oid", ""))" />
    <set-variable name="userEmail" value="@(context.Request.Headers.GetValueOrDefault("Authorization","").AsJwt()?.Claims.GetValueOrDefault("upn", ""))" />
    <set-variable name="userRoles" value="@(context.Request.Headers.GetValueOrDefault("Authorization","").AsJwt()?.Claims.GetValueOrDefault("roles", ""))" />
    
    <!-- Add user context headers for backend -->
    <set-header name="X-User-Object-Id" exists-action="override">
      <value>@((string)context.Variables["userObjectId"])</value>
    </set-header>
    <set-header name="X-User-Email" exists-action="override">
      <value>@((string)context.Variables["userEmail"])</value>
    </set-header>
  </inbound>
  
  <backend>
    <base />
  </backend>
  
  <outbound>
    <base />
    
    <!-- Add rate limit headers -->
    <set-header name="X-RateLimit-Limit" exists-action="override">
      <value>100</value>
    </set-header>
    <set-header name="X-RateLimit-Remaining" exists-action="override">
      <value>@{
        var key = context.Request.Headers.GetValueOrDefault("Authorization","").AsJwt()?.Subject ?? "anonymous";
        return (100 - context.Variables.GetValueOrDefault<int>($"rate-limit-{key}", 0)).ToString();
      }</value>
    </set-header>
  </outbound>
  
  <on-error>
    <base />
  </on-error>
</policies>
    '''
    format: 'xml'
  }
}

// Diagnostic settings for APIM
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'apim-diagnostics'
  scope: apimService
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 90 : 30
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: environment == 'prod' ? 90 : 30
        }
      }
    ]
  }
}

// Parameters that will be passed from main deployment
param logAnalyticsWorkspaceId string

// Outputs
output apimServiceId string = apimService.id
output apimServiceName string = apimService.name
output apimGatewayUrl string = apimService.properties.gatewayUrl
output apimDeveloperPortalUrl string = apimService.properties.developerPortalUrl
output apimManagementApiUrl string = apimService.properties.managementApiUrl
output apimManagedIdentityPrincipalId string = apimService.identity.principalId