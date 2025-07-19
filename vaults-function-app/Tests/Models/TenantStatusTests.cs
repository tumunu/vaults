using System;
using Newtonsoft.Json;
using VaultsFunctions.Core.Models;

namespace VaultsFunctions.Tests.Models;

public class TenantStatusTests
{
    [Fact]
    public void TenantStatus_Should_Initialize_With_Default_Values()
    {
        // Arrange & Act
        var tenantStatus = new TenantStatus();

        // Assert
        Assert.Null(tenantStatus.Id);
        Assert.Null(tenantStatus.LastSyncTime);
        Assert.Equal(0, tenantStatus.TotalInteractionsProcessed);
        Assert.Null(tenantStatus.LastFailureMessage);
        Assert.Null(tenantStatus.LastMonitoringRun);
        Assert.False(tenantStatus.OnboardingComplete);
        Assert.Equal(0, tenantStatus.InvitationRetryCount);
    }

    [Fact]
    public void TenantStatus_Should_Set_Properties_Correctly()
    {
        // Arrange
        var tenantId = "test-tenant-123";
        var lastSyncTime = DateTimeOffset.UtcNow.AddHours(-1);
        var updatedAt = DateTimeOffset.UtcNow;
        var invitationDate = DateTimeOffset.UtcNow.AddDays(-5);

        // Act
        var tenantStatus = new TenantStatus
        {
            Id = tenantId,
            LastSyncTime = lastSyncTime,
            TotalInteractionsProcessed = 5000,
            LastFailureMessage = "Test failure message",
            UpdatedAt = updatedAt,
            StripeCustomerId = "cus_test123",
            StripeSubscriptionId = "sub_test456",
            StripeSubscriptionStatus = "active",
            AdminEmail = "admin@test.com",
            InvitationState = "Completed",
            InvitationDateUtc = invitationDate,
            OnboardingComplete = true,
            AzureAdAppId = "12345678-1234-1234-1234-123456789abc",
            AzureStorageAccountName = "teststorageaccount",
            AzureStorageContainerName = "testcontainer",
            RetentionPolicy = "90days",
            ExportSchedule = "daily"
        };

        // Assert
        Assert.Equal(tenantId, tenantStatus.Id);
        Assert.Equal(lastSyncTime, tenantStatus.LastSyncTime);
        Assert.Equal(5000, tenantStatus.TotalInteractionsProcessed);
        Assert.Equal("Test failure message", tenantStatus.LastFailureMessage);
        Assert.Equal(updatedAt, tenantStatus.UpdatedAt);
        Assert.Equal("cus_test123", tenantStatus.StripeCustomerId);
        Assert.Equal("sub_test456", tenantStatus.StripeSubscriptionId);
        Assert.Equal("active", tenantStatus.StripeSubscriptionStatus);
        Assert.Equal("admin@test.com", tenantStatus.AdminEmail);
        Assert.Equal("Completed", tenantStatus.InvitationState);
        Assert.Equal(invitationDate, tenantStatus.InvitationDateUtc);
        Assert.True(tenantStatus.OnboardingComplete);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", tenantStatus.AzureAdAppId);
        Assert.Equal("teststorageaccount", tenantStatus.AzureStorageAccountName);
        Assert.Equal("testcontainer", tenantStatus.AzureStorageContainerName);
        Assert.Equal("90days", tenantStatus.RetentionPolicy);
        Assert.Equal("daily", tenantStatus.ExportSchedule);
    }

    [Fact]
    public void TenantStatus_Should_Serialize_To_Json_Correctly()
    {
        // Arrange
        var tenantStatus = new TenantStatus
        {
            Id = "test-tenant",
            LastSyncTime = new DateTimeOffset(2025, 6, 18, 10, 30, 0, TimeSpan.Zero),
            TotalInteractionsProcessed = 1000,
            OnboardingComplete = true,
            StripeCustomerId = "cus_test123",
            InvitationRetryCount = 2
        };

        // Act
        var json = JsonConvert.SerializeObject(tenantStatus);
        var deserializedStatus = JsonConvert.DeserializeObject<TenantStatus>(json);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"id\":\"test-tenant\"", json);
        Assert.Contains("\"totalInteractionsProcessed\":1000", json);
        Assert.Contains("\"onboardingComplete\":true", json);
        Assert.Contains("\"stripeCustomerId\":\"cus_test123\"", json);
        Assert.Contains("\"invitationRetryCount\":2", json);
        
        Assert.Equal(tenantStatus.Id, deserializedStatus.Id);
        Assert.Equal(tenantStatus.TotalInteractionsProcessed, deserializedStatus.TotalInteractionsProcessed);
        Assert.Equal(tenantStatus.OnboardingComplete, deserializedStatus.OnboardingComplete);
        Assert.Equal(tenantStatus.StripeCustomerId, deserializedStatus.StripeCustomerId);
        Assert.Equal(tenantStatus.InvitationRetryCount, deserializedStatus.InvitationRetryCount);
    }

    [Theory]
    [InlineData("Pending", false)]
    [InlineData("Sent", false)]
    [InlineData("Completed", true)]
    [InlineData("Failed", false)]
    [InlineData("Skipped", false)]
    [InlineData(null, false)]
    public void TenantStatus_Should_Indicate_Invitation_Success_Correctly(string invitationState, bool expectedSuccess)
    {
        // Arrange
        var tenantStatus = new TenantStatus
        {
            InvitationState = invitationState
        };

        // Act
        var isInvitationSuccessful = tenantStatus.InvitationState == "Completed";

        // Assert
        Assert.Equal(expectedSuccess, isInvitationSuccessful);
    }

    [Fact]
    public void TenantStatus_Should_Handle_Stripe_Invoice_Amount_In_Cents()
    {
        // Arrange
        var tenantStatus = new TenantStatus
        {
            LastInvoiceAmount = 2500 // $25.00 in cents
        };

        // Act
        var dollarAmount = tenantStatus.LastInvoiceAmount.HasValue ? tenantStatus.LastInvoiceAmount.Value / 100.0 : 0;

        // Assert
        Assert.Equal(25.0, dollarAmount);
    }

    [Theory]
    [InlineData("30days", 30)]
    [InlineData("90days", 90)]
    [InlineData("365days", 365)]
    [InlineData("custom", null)]
    public void TenantStatus_Should_Handle_Retention_Policy_Correctly(string retentionPolicy, int? expectedCustomDays)
    {
        // Arrange
        var tenantStatus = new TenantStatus
        {
            RetentionPolicy = retentionPolicy,
            CustomRetentionDays = expectedCustomDays
        };

        // Act
        var isCustomRetention = tenantStatus.RetentionPolicy == "custom";
        var effectiveDays = isCustomRetention ? tenantStatus.CustomRetentionDays : 
            int.TryParse(retentionPolicy?.Replace("days", ""), out var days) ? days : (int?)null;

        // Assert
        Assert.Equal(retentionPolicy, tenantStatus.RetentionPolicy);
        if (retentionPolicy != "custom")
        {
            Assert.Equal(expectedCustomDays, effectiveDays);
        }
        else
        {
            Assert.Equal(expectedCustomDays, tenantStatus.CustomRetentionDays);
        }
    }
}