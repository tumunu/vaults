// Comprehensive Monitoring and Alerting for CopilotVault
@description('Environment name (dev, staging, prod)')
param environment string = 'prod'

@description('Location for resources')
param location string = resourceGroup().location

@description('Application Insights resource ID')
param applicationInsightsId string

@description('Log Analytics Workspace resource ID')
param logAnalyticsWorkspaceId string

@description('Action Group resource ID')
param actionGroupId string

@description('Function App name')
param functionAppName string

@description('APIM service name')
param apimServiceName string

// Critical Alert: Token Validation Failure Rate
resource tokenValidationAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-token-validation-failure-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when token validation failure rate exceeds 5%'
    severity: 0  // Critical
    enabled: true
    scopes: [
      applicationInsightsId
    ]
    evaluationFrequency: 'PT1M'  // Every 1 minute
    windowSize: 'PT5M'           // 5 minute window
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'TokenValidationFailureRate'
          metricNamespace: 'Microsoft.Insights/components'
          metricName: 'requests/failed'
          dimensions: [
            {
              name: 'request/resultCode'
              operator: 'Include'
              values: [
                '401'
                '403'
              ]
            }
          ]
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Percentage'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P1-Critical'
          runbook: 'Token-Validation-Failure'
        }
      }
    ]
  }
}

// High Priority Alert: APIM Rate Limit Violations
resource rateLimitAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-rate-limit-violations-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when rate limit violations exceed 50 per minute'
    severity: 1  // High
    enabled: true
    scopes: [
      resourceId('Microsoft.ApiManagement/service', apimServiceName)
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'RateLimitViolations'
          metricNamespace: 'Microsoft.ApiManagement/service'
          metricName: 'Requests'
          dimensions: [
            {
              name: 'BackendResponseCode'
              operator: 'Include'
              values: [
                '429'
              ]
            }
          ]
          operator: 'GreaterThan'
          threshold: 50
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P2-High'
          runbook: 'Rate-Limit-Breach'
        }
      }
    ]
  }
}

// High Priority Alert: Graph API Failure Rate
resource graphApiAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-graph-api-failures-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when Microsoft Graph API failure rate exceeds 10%'
    severity: 1  // High
    enabled: true
    scopes: [
      applicationInsightsId
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT10M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'GraphApiFailureRate'
          metricNamespace: 'Microsoft.Insights/components'
          metricName: 'dependencies/failed'
          dimensions: [
            {
              name: 'dependency/target'
              operator: 'Include'
              values: [
                'graph.microsoft.com'
              ]
            }
          ]
          operator: 'GreaterThan'
          threshold: 10
          timeAggregation: 'Percentage'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P2-High'
          runbook: 'Graph-API-Failures'
        }
      }
    ]
  }
}

// Medium Priority Alert: Function App High CPU
resource functionAppCpuAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-function-app-cpu-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when Function App CPU usage exceeds 80%'
    severity: 2  // Medium
    enabled: true
    scopes: [
      resourceId('Microsoft.Web/sites', functionAppName)
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'FunctionAppCpuPercentage'
          metricNamespace: 'Microsoft.Web/sites'
          metricName: 'CpuPercentage'
          operator: 'GreaterThan'
          threshold: 80
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P3-Medium'
          runbook: 'Function-App-Performance'
        }
      }
    ]
  }
}

// Medium Priority Alert: High Memory Usage
resource functionAppMemoryAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-function-app-memory-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when Function App memory usage exceeds 85%'
    severity: 2  // Medium
    enabled: true
    scopes: [
      resourceId('Microsoft.Web/sites', functionAppName)
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'FunctionAppMemoryPercentage'
          metricNamespace: 'Microsoft.Web/sites'
          metricName: 'MemoryPercentage'
          operator: 'GreaterThan'
          threshold: 85
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P3-Medium'
          runbook: 'Function-App-Performance'
        }
      }
    ]
  }
}

// Log Alert: Key Vault Access Failures
resource keyVaultAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-keyvault-failures-${environment}'
  location: location
  properties: {
    displayName: 'Key Vault Access Failures'
    description: 'Alert when Key Vault access failures occur'
    severity: 1  // High
    enabled: true
    scopes: [
      logAnalyticsWorkspaceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT10M'
    criteria: {
      allOf: [
        {
          query: 'KeyVaultData | where TimeGenerated > ago(10m) | where ResultType != "Success" | summarize count() by bin(TimeGenerated, 5m)'
          timeAggregation: 'Total'
          dimensions: []
          operator: 'GreaterThan'
          threshold: 5
          failingPeriods: {
            numberOfEvaluationPeriods: 2
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupId
      ]
      customProperties: {
        severity: 'P2-High'
        runbook: 'Key-Vault-Access-Failures'
      }
    }
    autoMitigate: true
  }
}

// Log Alert: Authentication Anomalies
resource authAnomalyAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'alert-auth-anomalies-${environment}'
  location: location
  properties: {
    displayName: 'Authentication Anomalies'
    description: 'Alert when unusual authentication patterns are detected'
    severity: 1  // High
    enabled: true
    scopes: [
      logAnalyticsWorkspaceId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
            requests
            | where timestamp > ago(15m)
            | where resultCode in (401, 403)
            | summarize FailureCount = count() by client_IP, bin(timestamp, 1m)
            | where FailureCount > 10
            | summarize UniqueIPs = dcount(client_IP), TotalFailures = sum(FailureCount)
          '''
          timeAggregation: 'Total'
          dimensions: []
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupId
      ]
      customProperties: {
        severity: 'P2-High'
        runbook: 'Authentication-Anomalies'
      }
    }
    autoMitigate: true
  }
}

// Custom Metric Alert: Business KPI - Active Users Drop
resource activeUsersAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-active-users-drop-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when active user count drops by more than 20%'
    severity: 2  // Medium
    enabled: true
    scopes: [
      applicationInsightsId
    ]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ActiveUsersCount'
          metricNamespace: 'Microsoft.Insights/components'
          metricName: 'users/count'
          operator: 'LessThan'
          threshold: 80  // 80% of baseline (20% drop)
          timeAggregation: 'Average'
          criterionType: 'DynamicThresholdCriterion'
          alertSensitivity: 'Medium'
          ignoreDataBefore: '2025-01-01T00:00:00Z'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P3-Medium'
          runbook: 'Business-KPI-Degradation'
        }
      }
    ]
  }
}

// Availability Test for Critical Endpoints
resource availabilityTest 'Microsoft.Insights/webtests@2022-06-15' = {
  name: 'webtest-copilotvault-health-${environment}'
  location: location
  kind: 'standard'
  properties: {
    SyntheticMonitorId: 'webtest-copilotvault-health-${environment}'
    Name: 'CopilotVault Health Check'
    Description: 'Synthetic test for CopilotVault health endpoint'
    Enabled: true
    Frequency: 300  // 5 minutes
    Timeout: 30
    Kind: 'standard'
    RetryEnabled: true
    Locations: [
      {
        Id: 'us-ca-sjc-azr'  // West US
      }
      {
        Id: 'us-va-ash-azr'  // East US
      }
      {
        Id: 'emea-nl-ams-azr'  // West Europe
      }
    ]
    Configuration: {
      WebTest: '''
        <WebTest Name="CopilotVault Health Test" Id="health-test" Enabled="True" CssProjectStructure="" CssIteration="" Timeout="30" WorkItemIds="" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" Description="" CredentialUserName="" CredentialPassword="" PreAuthenticate="True" Proxy="default" StopOnError="False" RecordedResultFile="" ResultsLocale="">
          <Items>
            <Request Method="GET" Guid="health-check" Version="1.1" Url="https://apim-copilotvault-${environment}.azure-api.net/api/v1/health" ThinkTime="0" Timeout="30" ParseDependentRequests="False" FollowRedirects="True" RecordResult="True" Cache="False" ResponseTimeGoal="0" Encoding="utf-8" ExpectedHttpStatusCode="200" ExpectedResponseUrl="" ReportingName="" IgnoreHttpStatusCode="False">
              <Headers>
                <Header Name="Accept" Value="application/json" />
                <Header Name="User-Agent" Value="CopilotVault-HealthCheck/1.0" />
              </Headers>
              <ValidationRules>
                <ValidationRule Classname="Microsoft.VisualStudio.TestTools.WebTesting.Rules.ValidateResponseUrl" DisplayName="Response URL" Description="Validates that the response URL after redirects are followed is the same as the recorded response URL.  QueryString parameters are ignored." Level="Low" ExecOrder="BeforeDependents" />
                <ValidationRule Classname="Microsoft.VisualStudio.TestTools.WebTesting.Rules.ValidationRuleFindText" DisplayName="Find Text" Description="Verifies the existence of the specified text in the response." Level="High" ExecOrder="BeforeDependents">
                  <RuleParameters>
                    <RuleParameter Name="FindText" Value="healthy" CaseSensitive="False" UseRegularExpression="False" PassIfTextFound="True" />
                  </RuleParameters>
                </ValidationRule>
              </ValidationRules>
            </Request>
          </Items>
        </WebTest>
      '''
    }
  }
}

// Availability Alert
resource availabilityAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-availability-${environment}'
  location: 'Global'
  properties: {
    description: 'Alert when availability drops below 99%'
    severity: 0  // Critical
    enabled: true
    scopes: [
      availabilityTest.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'AvailabilityPercentage'
          metricNamespace: 'Microsoft.Insights/webtests'
          metricName: 'availabilityResults/availabilityPercentage'
          operator: 'LessThan'
          threshold: 99
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
        webHookProperties: {
          severity: 'P1-Critical'
          runbook: 'Service-Availability-Degradation'
        }
      }
    ]
  }
}

// Outputs
output tokenValidationAlertId string = tokenValidationAlert.id
output rateLimitAlertId string = rateLimitAlert.id
output graphApiAlertId string = graphApiAlert.id
output functionAppCpuAlertId string = functionAppCpuAlert.id
output functionAppMemoryAlertId string = functionAppMemoryAlert.id
output keyVaultAlertId string = keyVaultAlert.id
output authAnomalyAlertId string = authAnomalyAlert.id
output activeUsersAlertId string = activeUsersAlert.id
output availabilityTestId string = availabilityTest.id
output availabilityAlertId string = availabilityAlert.id