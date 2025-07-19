using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace VaultsFunctions.Core.Services
{
    public interface IDomainValidator
    {
        bool IsTrusted(string email);
        IEnumerable<string> GetTrustedDomains();
    }

    public class TrustedDomainsOptions
    {
        public const string SectionName = "TrustedDomains";
        public string[] Domains { get; set; } = Array.Empty<string>();
    }

    public class DomainValidator : IDomainValidator
    {
        private readonly IOptionsMonitor<TrustedDomainsOptions> _options;

        public DomainValidator(IOptionsMonitor<TrustedDomainsOptions> options)
        {
            _options = options;
        }

        public bool IsTrusted(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
                return false;

            var domain = email.Split('@')[1].ToLowerInvariant();
            var trustedDomains = _options.CurrentValue.Domains;
            
            return trustedDomains.Any(d => d.ToLowerInvariant() == domain);
        }

        public IEnumerable<string> GetTrustedDomains()
        {
            return _options.CurrentValue.Domains ?? Array.Empty<string>();
        }
    }
}