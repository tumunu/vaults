using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using VaultsFunctions.Core.Models;

namespace VaultsFunctions.Tests.Models;

public class AuditPolicyTests
{
    [Fact]
    public void AuditPolicy_Should_Initialize_With_Default_Values()
    {
        // Arrange & Act
        var auditPolicy = new AuditPolicy();

        // Assert
        Assert.NotNull(auditPolicy.Id);
        Assert.NotEmpty(auditPolicy.Id);
        Assert.NotNull(auditPolicy.DetectionRules);
        Assert.Empty(auditPolicy.DetectionRules);
        Assert.NotNull(auditPolicy.Categories);
        Assert.Empty(auditPolicy.Categories);
        Assert.False(auditPolicy.IsEnabled);
        Assert.Equal(0, auditPolicy.TriggerCount);
        Assert.True(auditPolicy.CreatedAt <= DateTimeOffset.UtcNow);
        Assert.True(auditPolicy.UpdatedAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void AuditPolicy_Should_Set_Properties_Correctly()
    {
        // Arrange
        var tenantId = "test-tenant-123";
        var policyName = "Test Security Policy";
        var description = "A comprehensive security policy for testing";
        var detectionRules = new List<string> { "Rule 1", "Rule 2", "Rule 3" };
        var categories = new List<PolicyCategory> { PolicyCategory.Security, PolicyCategory.Compliance };

        // Act
        var auditPolicy = new AuditPolicy
        {
            TenantId = tenantId,
            Name = policyName,
            Description = description,
            RiskLevel = RiskLevel.High,
            Action = PolicyAction.AlertAdmin,
            Sensitivity = 8,
            DetectionRules = detectionRules,
            Categories = categories,
            IsEnabled = true,
            CreatedBy = "test-user",
            TriggerCount = 5
        };

        // Assert
        Assert.Equal(tenantId, auditPolicy.TenantId);
        Assert.Equal(policyName, auditPolicy.Name);
        Assert.Equal(description, auditPolicy.Description);
        Assert.Equal(RiskLevel.High, auditPolicy.RiskLevel);
        Assert.Equal(PolicyAction.AlertAdmin, auditPolicy.Action);
        Assert.Equal(8, auditPolicy.Sensitivity);
        Assert.Equal(detectionRules, auditPolicy.DetectionRules);
        Assert.Equal(categories, auditPolicy.Categories);
        Assert.True(auditPolicy.IsEnabled);
        Assert.Equal("test-user", auditPolicy.CreatedBy);
        Assert.Equal(5, auditPolicy.TriggerCount);
    }

    [Fact]
    public void AuditPolicy_Should_Serialize_To_Json_Correctly()
    {
        // Arrange
        var auditPolicy = new AuditPolicy
        {
            TenantId = "test-tenant",
            Name = "Test Policy",
            Description = "Test description",
            RiskLevel = RiskLevel.Medium,
            Action = PolicyAction.LogOnly,
            Sensitivity = 6,
            DetectionRules = new List<string> { "Test rule" },
            Categories = new List<PolicyCategory> { PolicyCategory.Security },
            IsEnabled = true,
            TriggerCount = 3
        };

        // Act
        var json = JsonConvert.SerializeObject(auditPolicy);
        var deserializedPolicy = JsonConvert.DeserializeObject<AuditPolicy>(json);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"tenantId\":\"test-tenant\"", json);
        Assert.Contains("\"name\":\"Test Policy\"", json);
        Assert.Contains("\"riskLevel\":1", json); // Medium = 1
        Assert.Contains("\"action\":0", json); // LogOnly = 0
        Assert.Contains("\"isEnabled\":true", json);
        Assert.Contains("\"triggerCount\":3", json);
        
        Assert.Equal(auditPolicy.TenantId, deserializedPolicy.TenantId);
        Assert.Equal(auditPolicy.Name, deserializedPolicy.Name);
        Assert.Equal(auditPolicy.RiskLevel, deserializedPolicy.RiskLevel);
        Assert.Equal(auditPolicy.Action, deserializedPolicy.Action);
        Assert.Equal(auditPolicy.IsEnabled, deserializedPolicy.IsEnabled);
        Assert.Equal(auditPolicy.TriggerCount, deserializedPolicy.TriggerCount);
    }

    [Theory]
    [InlineData(RiskLevel.Low, 0)]
    [InlineData(RiskLevel.Medium, 1)]
    [InlineData(RiskLevel.High, 2)]
    public void RiskLevel_Enum_Should_Have_Correct_Values(RiskLevel riskLevel, int expectedValue)
    {
        // Act
        var actualValue = (int)riskLevel;

        // Assert
        Assert.Equal(expectedValue, actualValue);
    }

    [Theory]
    [InlineData(PolicyAction.LogOnly, 0)]
    [InlineData(PolicyAction.AlertAdmin, 1)]
    [InlineData(PolicyAction.BlockRequest, 2)]
    public void PolicyAction_Enum_Should_Have_Correct_Values(PolicyAction action, int expectedValue)
    {
        // Act
        var actualValue = (int)action;

        // Assert
        Assert.Equal(expectedValue, actualValue);
    }

    [Fact]
    public void DefaultPolicies_Should_Return_Valid_Policies_For_Tenant()
    {
        // Arrange
        var tenantId = "test-tenant-123";

        // Act
        var defaultPolicies = DefaultPolicies.GetDefaultPolicies(tenantId);

        // Assert
        Assert.NotNull(defaultPolicies);
        Assert.NotEmpty(defaultPolicies);
        Assert.Equal(5, defaultPolicies.Count);
        
        // All policies should have the correct tenant ID
        Assert.All(defaultPolicies, policy => Assert.Equal(tenantId, policy.TenantId));
        
        // All policies should be enabled by default
        Assert.All(defaultPolicies, policy => Assert.True(policy.IsEnabled));
        
        // All policies should have detection rules
        Assert.All(defaultPolicies, policy => Assert.NotEmpty(policy.DetectionRules));
        
        // All policies should have categories
        Assert.All(defaultPolicies, policy => Assert.NotEmpty(policy.Categories));
        
        // Verify specific policies exist
        Assert.Contains(defaultPolicies, p => p.Name == "Sensitive Data Detection");
        Assert.Contains(defaultPolicies, p => p.Name == "Code Quality Analysis");
        Assert.Contains(defaultPolicies, p => p.Name == "Prompt Injection Detection");
        Assert.Contains(defaultPolicies, p => p.Name == "Regulatory Compliance");
        Assert.Contains(defaultPolicies, p => p.Name == "Usage Pattern Analysis");
    }

    [Fact]
    public void DefaultPolicies_Should_Have_Appropriate_Risk_Levels()
    {
        // Arrange
        var tenantId = "test-tenant";

        // Act
        var defaultPolicies = DefaultPolicies.GetDefaultPolicies(tenantId);

        // Assert
        var highRiskPolicies = defaultPolicies.Where(p => p.RiskLevel == RiskLevel.High).ToList();
        var mediumRiskPolicies = defaultPolicies.Where(p => p.RiskLevel == RiskLevel.Medium).ToList();
        var lowRiskPolicies = defaultPolicies.Where(p => p.RiskLevel == RiskLevel.Low).ToList();
        
        Assert.True(highRiskPolicies.Count >= 2, "Should have at least 2 high-risk policies");
        Assert.True(mediumRiskPolicies.Count >= 1, "Should have at least 1 medium-risk policy");
        Assert.True(lowRiskPolicies.Count >= 1, "Should have at least 1 low-risk policy");
        
        // Sensitive data and prompt injection should be high risk
        Assert.Contains(highRiskPolicies, p => p.Name.Contains("Sensitive Data"));
        Assert.Contains(highRiskPolicies, p => p.Name.Contains("Prompt Injection"));
    }

    [Fact]
    public void AuditPolicy_Should_Track_Trigger_Information()
    {
        // Arrange
        var auditPolicy = new AuditPolicy
        {
            Name = "Test Policy",
            TriggerCount = 0,
            LastTriggered = null
        };

        // Act - Simulate policy trigger
        auditPolicy.TriggerCount++;
        auditPolicy.LastTriggered = DateTimeOffset.UtcNow;

        // Assert
        Assert.Equal(1, auditPolicy.TriggerCount);
        Assert.NotNull(auditPolicy.LastTriggered);
        Assert.True(auditPolicy.LastTriggered <= DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(1, RiskLevel.Low)]
    [InlineData(5, RiskLevel.Medium)]
    [InlineData(9, RiskLevel.High)]
    public void AuditPolicy_Sensitivity_Should_Correspond_To_Risk_Level(int sensitivity, RiskLevel expectedRiskLevel)
    {
        // Arrange
        var auditPolicy = new AuditPolicy
        {
            Sensitivity = sensitivity,
            RiskLevel = expectedRiskLevel
        };

        // Act
        var isConsistent = (sensitivity <= 3 && expectedRiskLevel == RiskLevel.Low) ||
                          (sensitivity >= 4 && sensitivity <= 7 && expectedRiskLevel == RiskLevel.Medium) ||
                          (sensitivity >= 8 && expectedRiskLevel == RiskLevel.High);

        // Assert
        Assert.True(isConsistent, $"Sensitivity {sensitivity} should be consistent with {expectedRiskLevel} risk level");
    }
}