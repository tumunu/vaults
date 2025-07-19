using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using VaultsFunctions.Core.Services;
using Azure.Identity;
using Azure.Core;

namespace VaultsFunctions.Tests.Services
{
    public class GraphCopilotServiceTests
    {
        private readonly Mock<ILogger<GraphCopilotService>> _mockLogger;
        private readonly Mock<IConfiguration> _mockConfiguration;

        public GraphCopilotServiceTests()
        {
            _mockLogger = new Mock<ILogger<GraphCopilotService>>();
            _mockConfiguration = new Mock<IConfiguration>();
        }

        [Fact]
        public void Constructor_ManagedIdentityEnabled_UsesDefaultAzureCredential()
        {
            // Arrange
            _mockConfiguration.Setup(c => c.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true))
                            .Returns(true);
            _mockConfiguration.Setup(c => c["AZURE_CLIENT_ID"])
                            .Returns("test-client-id");

            // Act & Assert
            var exception = Record.Exception(() => new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object));
            
            // Should not throw exception during construction
            Assert.Null(exception);
            
            // Verify managed identity is logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Using DefaultAzureCredential for managed identity authentication")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void Constructor_ManagedIdentityDisabled_UsesClientSecretCredential()
        {
            // Arrange
            _mockConfiguration.Setup(c => c.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true))
                            .Returns(false);
            _mockConfiguration.Setup(c => c["AZURE_TENANT_ID"])
                            .Returns("test-tenant-id");
            _mockConfiguration.Setup(c => c["AZURE_CLIENT_ID"])
                            .Returns("test-client-id");
            _mockConfiguration.Setup(c => c["AZURE_CLIENT_SECRET"])
                            .Returns("test-client-secret");

            // Act & Assert
            var exception = Record.Exception(() => new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object));
            
            // Should not throw exception during construction
            Assert.Null(exception);
            
            // Verify client secret credential warning is logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Using ClientSecretCredential (deprecated) - managed identity is disabled")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        [Fact]
        public void Constructor_ManagedIdentityDisabled_MissingConfiguration_ThrowsException()
        {
            // Arrange
            _mockConfiguration.Setup(c => c.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true))
                            .Returns(false);
            // Missing required configuration values

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => 
                new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object));
            
            Assert.Contains("Missing required Azure AD configuration", exception.Message);
        }

        [Fact]
        public async Task GetRecentAlertsAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetRecentAlertsAsync("test-tenant");

            // Assert
            Assert.NotNull(result);
            // Note: This will return fallback data in test environment without actual Graph API access
        }

        [Fact]
        public async Task GetHighRiskUsersAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetHighRiskUsersAsync("test-tenant");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetPolicyViolationsAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetPolicyViolationsAsync("test-tenant");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetInteractionHistoryAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetInteractionHistoryAsync("test-tenant", 10, "test-filter");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetCopilotUsersAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetCopilotUsersAsync("test-tenant");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetCopilotUsageSummaryAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetCopilotUsageSummaryAsync("D7");

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task GetCopilotUserCountAsync_ReturnsData()
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetCopilotUserCountAsync("D30");

            // Assert
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData("D7")]
        [InlineData("D30")]
        [InlineData("D90")]
        public async Task GetCopilotUsageSummaryAsync_ValidPeriods_ReturnsData(string period)
        {
            // Arrange
            SetupManagedIdentityConfiguration();
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Act
            var result = await service.GetCopilotUsageSummaryAsync(period);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void Constructor_InitializationSuccess_LogsCorrectCredentialType()
        {
            // Arrange
            SetupManagedIdentityConfiguration();

            // Act
            var service = new GraphCopilotService(_mockConfiguration.Object, _mockLogger.Object);

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("GraphServiceClient initialized successfully with DefaultAzureCredential")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                Times.Once);
        }

        private void SetupManagedIdentityConfiguration()
        {
            _mockConfiguration.Setup(c => c.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true))
                            .Returns(true);
            _mockConfiguration.Setup(c => c["AZURE_CLIENT_ID"])
                            .Returns("test-client-id");
        }
    }
}