using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace VaultsFunctions.Core.Models
{
    public class AuditPolicy
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("riskLevel")]
        public RiskLevel RiskLevel { get; set; }

        [JsonProperty("action")]
        public PolicyAction Action { get; set; }

        [JsonProperty("sensitivity")]
        public int Sensitivity { get; set; } // 1-10 scale

        [JsonProperty("detectionRules")]
        public List<string> DetectionRules { get; set; } = new List<string>();

        [JsonProperty("categories")]
        public List<PolicyCategory> Categories { get; set; } = new List<PolicyCategory>();

        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; }

        [JsonProperty("createdAt")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("updatedAt")]
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        [JsonProperty("createdBy")]
        public string CreatedBy { get; set; }

        [JsonProperty("lastTriggered")]
        public DateTimeOffset? LastTriggered { get; set; }

        [JsonProperty("triggerCount")]
        public int TriggerCount { get; set; }
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High
    }

    public enum PolicyAction
    {
        LogOnly,
        AlertAdmin,
        BlockRequest
    }

    public enum PolicyCategory
    {
        Security,
        Compliance,
        Quality,
        Usage
    }

    public class PolicyConfigurationRequest
    {
        public string TenantId { get; set; }
        public List<AuditPolicy> Policies { get; set; }
    }

    public class DefaultPolicies
    {
        public static List<AuditPolicy> GetDefaultPolicies(string tenantId)
        {
            return new List<AuditPolicy>
            {
                new AuditPolicy
                {
                    TenantId = tenantId,
                    Name = "Sensitive Data Detection",
                    Description = "Detects credit cards, SSNs, personal information, and other sensitive data",
                    RiskLevel = RiskLevel.High,
                    Action = PolicyAction.AlertAdmin,
                    Sensitivity = 8,
                    DetectionRules = new List<string>
                    {
                        "Credit card numbers (Visa, MasterCard, Amex)",
                        "Social Security Numbers (SSN)",
                        "Email addresses and phone numbers",
                        "API keys and authentication tokens"
                    },
                    Categories = new List<PolicyCategory> { PolicyCategory.Security, PolicyCategory.Compliance },
                    IsEnabled = true
                },
                new AuditPolicy
                {
                    TenantId = tenantId,
                    Name = "Code Quality Analysis",
                    Description = "Monitors code suggestions for best practices and security vulnerabilities",
                    RiskLevel = RiskLevel.Medium,
                    Action = PolicyAction.LogOnly,
                    Sensitivity = 6,
                    DetectionRules = new List<string>
                    {
                        "Hardcoded passwords or secrets",
                        "SQL injection patterns",
                        "Cross-site scripting (XSS) vulnerabilities",
                        "Insecure cryptographic practices"
                    },
                    Categories = new List<PolicyCategory> { PolicyCategory.Security, PolicyCategory.Quality },
                    IsEnabled = true
                },
                new AuditPolicy
                {
                    TenantId = tenantId,
                    Name = "Prompt Injection Detection",
                    Description = "Identifies attempts to manipulate AI behavior through malicious prompts",
                    RiskLevel = RiskLevel.High,
                    Action = PolicyAction.BlockRequest,
                    Sensitivity = 9,
                    DetectionRules = new List<string>
                    {
                        "Role-playing attempts (pretend you are...)",
                        "System prompt override attempts",
                        "Jailbreaking patterns",
                        "Context manipulation techniques"
                    },
                    Categories = new List<PolicyCategory> { PolicyCategory.Security },
                    IsEnabled = true
                },
                new AuditPolicy
                {
                    TenantId = tenantId,
                    Name = "Regulatory Compliance",
                    Description = "Ensures interactions comply with GDPR, HIPAA, and other regulations",
                    RiskLevel = RiskLevel.High,
                    Action = PolicyAction.AlertAdmin,
                    Sensitivity = 7,
                    DetectionRules = new List<string>
                    {
                        "HIPAA protected health information",
                        "GDPR personal data categories",
                        "Financial regulatory data (PCI DSS)",
                        "Legal privilege information"
                    },
                    Categories = new List<PolicyCategory> { PolicyCategory.Compliance },
                    IsEnabled = true
                },
                new AuditPolicy
                {
                    TenantId = tenantId,
                    Name = "Usage Pattern Analysis",
                    Description = "Monitors usage patterns for anomalies and optimization opportunities",
                    RiskLevel = RiskLevel.Low,
                    Action = PolicyAction.LogOnly,
                    Sensitivity = 4,
                    DetectionRules = new List<string>
                    {
                        "Unusual request frequency patterns",
                        "Off-hours usage anomalies",
                        "Repetitive query patterns",
                        "Resource usage thresholds"
                    },
                    Categories = new List<PolicyCategory> { PolicyCategory.Usage },
                    IsEnabled = true
                }
            };
        }
    }
}