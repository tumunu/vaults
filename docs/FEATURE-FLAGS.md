# Feature Flags for Gradual Enablement

This document outlines the feature flag system designed to enable gradual rollout of functionality as the userbase grows.

## Overview

The CoPilot Vault Function App uses a feature flag system to control which features are enabled. This allows for:

- **Risk Management**: Enable complex features only when needed
- **Performance Optimization**: Avoid overhead for unused features
- **Gradual Rollout**: Test features with small user groups first
- **User-Based Thresholds**: Automatically enable features as user count grows

## Feature Categories

### ✅ Core Features (Always Enabled)
These are essential for basic functionality:

- **B2B Invitation System**: Enterprise admin onboarding
- **Usage Metrics**: Basic tenant usage statistics
- **Chat Search**: Search copilot conversations (core enterprise feature)
- **Seat Management**: Add/remove licensed seats (core enterprise feature)
- **Audit Logging**: Basic compliance logging

### 🔧 Monitoring Features (Threshold-Based)
Enable when you have operational complexity:

- **Service Bus Monitoring** (50+ users): Queue health, metrics, alerting
- **Dead Letter Processing** (50+ users): Failed message handling
- **Advanced Telemetry** (100+ users): Detailed Application Insights

### 🚀 Premium Features (Enterprise-Grade)
Enable for large customers with specific needs:

- **Enterprise Reporting** (250+ users): Advanced analytics dashboards
- **Compliance Checking** (Enterprise): Automated conversation scanning
- **Data Retention** (Enterprise): Automated retention policies
- **Advanced Analytics** (Enterprise): AI-powered insights

## Configuration

### Application Settings Format
```json
{
  "Features:B2BInvitation:Enabled": "true",
  "Features:UsageMetrics:Enabled": "true",
  "Features:ChatSearch:Enabled": "true",
  "Features:SeatManagement:Enabled": "true",
  
  "Features:ServiceBusMonitoring:Enabled": "false",
  "Features:ServiceBusMonitoring:UserThreshold": "50",
  "Features:DeadLetterProcessing:Enabled": "false",
  
  "Features:EnterpriseReporting:Enabled": "false",
  "Features:EnterpriseReporting:UserThreshold": "250"
}
```

### Feature Flag Usage in Code
```csharp
public class MyFunction
{
    private readonly IFeatureFlags _featureFlags;
    
    public async Task ProcessData()
    {
        if (!_featureFlags.IsServiceBusMonitoringEnabled)
        {
            _logger.LogDebug("Service Bus monitoring disabled");
            return;
        }
        
        // Monitoring logic here
    }
}
```

## Rollout Strategy

### Phase 1: Greenfield Launch (0-10 users)
```
✅ B2B Invitation System
✅ Usage Metrics  
✅ Chat Search
✅ Seat Management
✅ Basic Audit Logging
❌ All monitoring features disabled
❌ All premium features disabled
```

### Phase 2: Growing Business (10-50 users)
```
✅ All Phase 1 features
✅ Enhanced audit logging
❌ Service Bus monitoring (not needed yet)
❌ Premium features (not justified)
```

### Phase 3: Established Business (50-100 users)
```
✅ All Phase 2 features
✅ Service Bus Monitoring (operational complexity)
✅ Dead Letter Processing (reliability)
❌ Advanced telemetry (not needed yet)
❌ Premium features (not justified)
```

### Phase 4: Enterprise Scale (100+ users)
```
✅ All Phase 3 features
✅ Advanced Telemetry (performance insights)
✅ Enhanced monitoring and alerting
✅ Premium features (customer demand)
```

## Automatic Threshold-Based Enablement

The system can automatically enable features based on user count:

```csharp
// Check if feature should be auto-enabled
var currentUserCount = await GetCurrentUserCountAsync();
if (currentUserCount >= _featureFlags.ServiceBusMonitoringThreshold)
{
    // Feature automatically becomes available
    // (still requires manual config update to persist)
}
```

## Feature-Specific Endpoints

Many endpoints respect feature flags and return appropriate responses:

### Service Bus Health Endpoint
```
GET /api/admin/servicebus/health
```

**When Disabled:**
```json
{
  "status": "disabled",
  "message": "Service Bus monitoring is not enabled",
  "feature": "ServiceBusMonitoring",
  "enabled": false
}
```

**When Enabled:**
```json
{
  "timestamp": "2025-06-17T10:30:00Z",
  "overall": {
    "status": "healthy",
    "available": true,
    "monitoringEnabled": true
  },
  "queues": { ... }
}
```

## Benefits of This Approach

### 🚀 **Performance**
- No overhead from unused monitoring
- Functions don't start unnecessary timers
- Reduced resource consumption

### 🛡️ **Reliability**
- Complex features enabled only when needed
- Gradual testing of new functionality
- Ability to quickly disable problematic features

### 💰 **Cost Management**
- Avoid Service Bus costs until needed
- Monitoring costs scale with usage
- Premium features justify their overhead

### 🔧 **Operational Excellence**
- Clear feature lifecycle management
- Easy to understand what's enabled
- Supports A/B testing and canary deployments

## Configuration Management

### Development Environment
```bash
# Enable all features for testing
Features:ServiceBusMonitoring:Enabled=true
Features:DeadLetterProcessing:Enabled=true
Features:AdvancedTelemetry:Enabled=true
```

### Production Environment
```bash
# Conservative approach - enable only what's needed
Features:ServiceBusMonitoring:Enabled=false
Features:DeadLetterProcessing:Enabled=false
Features:AdvancedTelemetry:Enabled=false
```

### Staging Environment
```bash
# Test premium features
Features:EnterpriseReporting:Enabled=true
Features:ComplianceChecking:Enabled=true
```

## Migration Path

As your userbase grows, follow this migration path:

1. **Monitor user growth** via usage metrics
2. **Review performance** and operational needs
3. **Enable features gradually** starting with monitoring
4. **Validate stability** before enabling premium features
5. **Document configuration changes** for team awareness

This approach ensures your Function App scales complexity with actual business needs, maintaining simplicity in early stages while providing enterprise-grade capabilities as you grow.