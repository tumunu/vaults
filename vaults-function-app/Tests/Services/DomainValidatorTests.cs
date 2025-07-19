using Microsoft.Extensions.Options;
using Moq;
using VaultsFunctions.Core.Services;

namespace VaultsFunctions.Tests.Services;

public class DomainValidatorTests
{
    private readonly Mock<IOptionsMonitor<TrustedDomainsOptions>> _mockOptions;
    private readonly DomainValidator _domainValidator;

    public DomainValidatorTests()
    {
        _mockOptions = new Mock<IOptionsMonitor<TrustedDomainsOptions>>();
        _domainValidator = new DomainValidator(_mockOptions.Object);
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("admin@company.org", true)]
    [InlineData("test@subdomain.example.com", false)] // Not in trusted list
    [InlineData("user@EXAMPLE.COM", true)] // Case insensitive
    [InlineData("invalid-email", false)]
    [InlineData("user@", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrusted_Should_Return_Expected_Result(string email, bool expected)
    {
        // Arrange
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = new[] { "example.com", "company.org" }
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act
        var result = _domainValidator.IsTrusted(email);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsTrusted_Should_Return_False_When_No_Trusted_Domains_Configured()
    {
        // Arrange
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = new string[0]
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act
        var result = _domainValidator.IsTrusted("user@example.com");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsTrusted_Should_Handle_Null_Domains_Array()
    {
        // Arrange
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = null
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _domainValidator.IsTrusted("user@example.com"));
    }

    [Fact]
    public void GetTrustedDomains_Should_Return_Configured_Domains()
    {
        // Arrange
        var expectedDomains = new[] { "example.com", "company.org", "test.net" };
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = expectedDomains
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act
        var result = _domainValidator.GetTrustedDomains();

        // Assert
        Assert.Equal(expectedDomains, result);
    }

    [Fact]
    public void GetTrustedDomains_Should_Return_Empty_When_Null_Domains()
    {
        // Arrange
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = null
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act
        var result = _domainValidator.GetTrustedDomains();

        // Assert
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("user@example.com", "example.com")]
    [InlineData("admin@subdomain.company.org", "subdomain.company.org")]
    [InlineData("test@DOMAIN.COM", "domain.com")] // Should be case insensitive
    public void IsTrusted_Should_Extract_Domain_Correctly(string email, string expectedDomain)
    {
        // Arrange
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = new[] { expectedDomain.ToLowerInvariant() }
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act
        var result = _domainValidator.IsTrusted(email);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsTrusted_Should_Be_Case_Insensitive_For_Domain_Comparison()
    {
        // Arrange
        var trustedDomains = new TrustedDomainsOptions
        {
            Domains = new[] { "EXAMPLE.COM", "company.ORG" }
        };
        _mockOptions.Setup(x => x.CurrentValue).Returns(trustedDomains);

        // Act & Assert
        Assert.True(_domainValidator.IsTrusted("user@example.com"));
        Assert.True(_domainValidator.IsTrusted("admin@COMPANY.ORG"));
        Assert.True(_domainValidator.IsTrusted("test@Example.Com"));
    }
}