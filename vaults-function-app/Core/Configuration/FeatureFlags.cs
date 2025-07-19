using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;

namespace VaultsFunctions.Core.Configuration
{
    public interface IFeatureFlags
    {
        bool IsServiceBusMonitoringEnabled { get; }
        bool IsDeadLetterQueueProcessingEnabled { get; }
        bool IsAdvancedTelemetryEnabled { get; }
        bool IsUsageMetricsEnabled { get; }
        bool IsB2BInvitationEnabled { get; }
        bool IsChatSearchEnabled { get; }
        bool IsSeatManagementEnabled { get; }
        bool IsEnterpriseReportingEnabled { get; }
        bool IsAuditLoggingEnabled { get; }
        bool IsComplianceCheckingEnabled { get; }
        bool IsDataRetentionEnabled { get; }
        bool IsAdvancedAnalyticsEnabled { get; }
        bool IsAzureAdPremiumEnabled { get; }
        
        // User tier thresholds
        int ServiceBusMonitoringThreshold { get; }
        int AdvancedTelemetryThreshold { get; }
        int EnterpriseReportingThreshold { get; }
    }

    public class FeatureFlags : IFeatureFlags
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<FeatureFlags> _logger;

        public FeatureFlags(IConfiguration configuration, ILogger<FeatureFlags> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        // Core features (always enabled for greenfield project)
        public bool IsB2BInvitationEnabled => GetFeatureFlag("Features:B2BInvitation:Enabled", true);
        public bool IsUsageMetricsEnabled => GetFeatureFlag("Features:UsageMetrics:Enabled", true);

        // Enterprise features (enabled by default, can be disabled)
        public bool IsChatSearchEnabled => GetFeatureFlag("Features:ChatSearch:Enabled", true);
        public bool IsSeatManagementEnabled => GetFeatureFlag("Features:SeatManagement:Enabled", true);
        public bool IsAuditLoggingEnabled => GetFeatureFlag("Features:AuditLogging:Enabled", true);

        // Advanced monitoring (disabled by default - enable as scale grows)
        public bool IsServiceBusMonitoringEnabled => GetFeatureFlag("Features:ServiceBusMonitoring:Enabled", false);
        public bool IsDeadLetterQueueProcessingEnabled => GetFeatureFlag("Features:DeadLetterProcessing:Enabled", false);
        public bool IsAdvancedTelemetryEnabled => GetFeatureFlag("Features:AdvancedTelemetry:Enabled", false);

        // Premium features (disabled by default - enable for enterprise customers)
        public bool IsEnterpriseReportingEnabled => GetFeatureFlag("Features:EnterpriseReporting:Enabled", false);
        public bool IsComplianceCheckingEnabled => GetFeatureFlag("Features:ComplianceChecking:Enabled", false);
        public bool IsDataRetentionEnabled => GetFeatureFlag("Features:DataRetention:Enabled", false);
        public bool IsAdvancedAnalyticsEnabled => GetFeatureFlag("Features:AdvancedAnalytics:Enabled", false);
        public bool IsAzureAdPremiumEnabled => GetFeatureFlag("Features:AzureAdPremium:Enabled", false);

        // User count thresholds for automatic feature enablement
        public int ServiceBusMonitoringThreshold => GetIntValue("Features:ServiceBusMonitoring:UserThreshold", 50);
        public int AdvancedTelemetryThreshold => GetIntValue("Features:AdvancedTelemetry:UserThreshold", 100);
        public int EnterpriseReportingThreshold => GetIntValue("Features:EnterpriseReporting:UserThreshold", 250);

        private bool GetFeatureFlag(string key, bool defaultValue)
        {
            try
            {
                var value = _configuration[key];
                if (string.IsNullOrEmpty(value))
                {
                    _logger.LogDebug("Feature flag {Key} not configured, using default: {DefaultValue}", key, defaultValue);
                    return defaultValue;
                }

                if (bool.TryParse(value, out var result))
                {
                    _logger.LogDebug("Feature flag {Key} = {Value}", key, result);
                    return result;
                }

                _logger.LogWarning("Invalid value for feature flag {Key}: {Value}, using default: {DefaultValue}", 
                    key, value, defaultValue);
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading feature flag {Key}, using default: {DefaultValue}", key, defaultValue);
                return defaultValue;
            }
        }

        private int GetIntValue(string key, int defaultValue)
        {
            try
            {
                var value = _configuration[key];
                if (string.IsNullOrEmpty(value))
                    return defaultValue;

                if (int.TryParse(value, out var result))
                    return result;

                _logger.LogWarning("Invalid value for config {Key}: {Value}, using default: {DefaultValue}", 
                    key, value, defaultValue);
                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading config {Key}, using default: {DefaultValue}", key, defaultValue);
                return defaultValue;
            }
        }
    }

    public static class FeatureFlagExtensions
    {
        public static bool ShouldEnableBasedOnUserCount(this IFeatureFlags featureFlags, string featureName, int currentUserCount)
        {
            return featureName switch
            {
                "ServiceBusMonitoring" => currentUserCount >= featureFlags.ServiceBusMonitoringThreshold,
                "AdvancedTelemetry" => currentUserCount >= featureFlags.AdvancedTelemetryThreshold,
                "EnterpriseReporting" => currentUserCount >= featureFlags.EnterpriseReportingThreshold,
                _ => false
            };
        }
    }
}