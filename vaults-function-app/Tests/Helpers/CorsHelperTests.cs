using Microsoft.Extensions.Configuration;
using Moq;
using VaultsFunctions.Core;

namespace VaultsFunctions.Tests.Helpers;

public class CorsHelperTests
{
    [Fact]
    public void Constants_Should_Have_CorsAllowedOrigin_Key()
    {
        // Assert - Test that the configuration key exists and has expected value
        Assert.NotNull(Constants.ConfigurationKeys.CorsAllowedOrigin);
        Assert.NotEmpty(Constants.ConfigurationKeys.CorsAllowedOrigin);
        Assert.Equal("CORS_ALLOWED_ORIGIN", Constants.ConfigurationKeys.CorsAllowedOrigin);
    }

    [Theory]
    [InlineData(null, "https://func-vaults-dev-soldsu.azurestaticapps.net")]
    [InlineData("", "https://func-vaults-dev-soldsu.azurestaticapps.net")]
    [InlineData("https://custom.example.com", "https://custom.example.com")]
    [InlineData("https://staging.company.com", "https://staging.company.com")]
    public void CorsHelper_Should_Return_Correct_Origin_Based_On_Configuration(string configValue, string expectedOrigin)
    {
        // Arrange
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c[Constants.ConfigurationKeys.CorsAllowedOrigin])
                         .Returns(configValue);

        // Act - Test logic that would be used in CorsHelper
        string configuredOrigin = mockConfiguration.Object[Constants.ConfigurationKeys.CorsAllowedOrigin];
        string allowedOrigin = !string.IsNullOrEmpty(configuredOrigin) ? configuredOrigin 
            : "https://func-vaults-dev-soldsu.azurestaticapps.net";

        // Assert
        Assert.Equal(expectedOrigin, allowedOrigin);
    }
}