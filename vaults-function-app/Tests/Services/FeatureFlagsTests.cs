using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using VaultsFunctions.Core.Configuration;

namespace VaultsFunctions.Tests.Services;

public class FeatureFlagsTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<FeatureFlags>> _mockLogger;

    public FeatureFlagsTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<FeatureFlags>>();
    }

    [Fact]
    public void FeatureFlags_Should_Return_Default_Values_When_Configuration_Missing()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[It.IsAny<string>()]).Returns((string)null);
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert
        Assert.False(featureFlags.IsServiceBusMonitoringEnabled);
        Assert.False(featureFlags.IsDeadLetterQueueProcessingEnabled);
        Assert.False(featureFlags.IsAdvancedTelemetryEnabled);
        Assert.False(featureFlags.IsComplianceCheckingEnabled);
        Assert.False(featureFlags.IsDataRetentionEnabled);
        Assert.False(featureFlags.IsAdvancedAnalyticsEnabled);
        Assert.False(featureFlags.IsAzureAdPremiumEnabled);
        Assert.False(featureFlags.IsEnterpriseReportingEnabled);
    }

    [Fact]
    public void FeatureFlags_Should_Return_Configured_Values()
    {
        // Arrange
        _mockConfiguration.Setup(x => x["Features:ServiceBusMonitoring:Enabled"]).Returns("true");
        _mockConfiguration.Setup(x => x["Features:DeadLetterProcessing:Enabled"]).Returns("true");
        _mockConfiguration.Setup(x => x["Features:AdvancedTelemetry:Enabled"]).Returns("false");
        _mockConfiguration.Setup(x => x["Features:AzureAdPremium:Enabled"]).Returns("true");
        _mockConfiguration.Setup(x => x["Features:EnterpriseReporting:Enabled"]).Returns("true");
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert
        Assert.True(featureFlags.IsServiceBusMonitoringEnabled);
        Assert.True(featureFlags.IsDeadLetterQueueProcessingEnabled);
        Assert.False(featureFlags.IsAdvancedTelemetryEnabled);
        Assert.True(featureFlags.IsAzureAdPremiumEnabled);
        Assert.True(featureFlags.IsEnterpriseReportingEnabled);
    }

    [Fact]
    public void FeatureFlags_Should_Return_Correct_Thresholds()
    {
        // Arrange
        _mockConfiguration.Setup(x => x["Features:ServiceBusMonitoring:UserThreshold"]).Returns("100");
        _mockConfiguration.Setup(x => x["Features:AdvancedTelemetry:UserThreshold"]).Returns("250");
        _mockConfiguration.Setup(x => x["Features:EnterpriseReporting:UserThreshold"]).Returns("500");
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert
        Assert.Equal(100, featureFlags.ServiceBusMonitoringThreshold);
        Assert.Equal(250, featureFlags.AdvancedTelemetryThreshold);
        Assert.Equal(500, featureFlags.EnterpriseReportingThreshold);
    }

    [Fact]
    public void FeatureFlags_Should_Use_Default_Thresholds_When_Not_Configured()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[It.IsAny<string>()]).Returns((string)null);
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert
        Assert.Equal(50, featureFlags.ServiceBusMonitoringThreshold);
        Assert.Equal(100, featureFlags.AdvancedTelemetryThreshold);
        Assert.Equal(250, featureFlags.EnterpriseReportingThreshold);
    }

    [Fact]
    public void FeatureFlags_Should_Handle_Invalid_Boolean_Values()
    {
        // Arrange
        _mockConfiguration.Setup(x => x["Features:ServiceBusMonitoring:Enabled"]).Returns("invalid");
        _mockConfiguration.Setup(x => x["Features:DeadLetterProcessing:Enabled"]).Returns("1");
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert
        Assert.False(featureFlags.IsServiceBusMonitoringEnabled); // Should use default false
        Assert.False(featureFlags.IsDeadLetterQueueProcessingEnabled); // Should use default false
    }

    [Fact]
    public void FeatureFlags_Should_Handle_Invalid_Integer_Values()
    {
        // Arrange
        _mockConfiguration.Setup(x => x["Features:ServiceBusMonitoring:UserThreshold"]).Returns("invalid");
        _mockConfiguration.Setup(x => x["Features:AdvancedTelemetry:UserThreshold"]).Returns("not-a-number");
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert
        Assert.Equal(50, featureFlags.ServiceBusMonitoringThreshold); // Should use default
        Assert.Equal(100, featureFlags.AdvancedTelemetryThreshold); // Should use default
    }

    [Fact]
    public void FeatureFlags_Should_Return_True_For_Default_Enabled_Features()
    {
        // Arrange
        _mockConfiguration.Setup(x => x[It.IsAny<string>()]).Returns((string)null);
        
        var featureFlags = new FeatureFlags(_mockConfiguration.Object, _mockLogger.Object);

        // Act & Assert - These should be enabled by default
        Assert.True(featureFlags.IsB2BInvitationEnabled);
        Assert.True(featureFlags.IsUsageMetricsEnabled);
        Assert.True(featureFlags.IsChatSearchEnabled);
        Assert.True(featureFlags.IsSeatManagementEnabled);
        Assert.True(featureFlags.IsAuditLoggingEnabled);
    }
}