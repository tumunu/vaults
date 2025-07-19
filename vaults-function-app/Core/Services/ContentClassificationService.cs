using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Newtonsoft.Json;
using VaultsFunctions.Core.Models;
using System.Text;
using System.IO;

namespace VaultsFunctions.Core.Services
{
    public class ContentClassificationService
    {
        private readonly GraphServiceClient _graphServiceClient;
        private readonly ILogger<ContentClassificationService> _logger;
        private readonly HttpClient _httpClient;
        private readonly TokenCredential _credential;
        private readonly IConfiguration _configuration;

        public ContentClassificationService(IConfiguration configuration, ILogger<ContentClassificationService> logger)
        {
            _logger = logger;
            _configuration = configuration;
            
            // Configure Graph client with required scopes for content classification
            var scopes = new[] { 
                "https://graph.microsoft.com/.default" // Uses all granted application permissions
            };

            // Use system-assigned managed identity (production standard)
            var managedIdentityEnabled = configuration.GetValue<bool>("MANAGED_IDENTITY_ENABLED", true);
            
            if (managedIdentityEnabled)
            {
                _logger.LogInformation("ContentClassificationService: Using system-assigned ManagedIdentityCredential");
                _credential = new ManagedIdentityCredential();
            }
            else
            {
                _logger.LogWarning("ContentClassificationService: Using ClientSecretCredential (development only)");
                var tenantId = configuration["AZURE_TENANT_ID"];
                var clientId = configuration["AZURE_CLIENT_ID"];
                var clientSecret = configuration["AZURE_CLIENT_SECRET"];
                _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }

            _graphServiceClient = new GraphServiceClient(_credential, scopes);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Classify content using AI-powered analysis for governance
        /// Addresses Microsoft's acknowledged gap: "Non-Office file types lack automatic sensitivity labeling"
        /// </summary>
        public async Task<ContentClassificationResult> ClassifyContentAsync(
            string resourceId, 
            string contentType, 
            byte[] content = null,
            string tenantId = null)
        {
            try
            {
                _logger.LogInformation($"Classifying content: {resourceId}, Type: {contentType}");

                var result = new ContentClassificationResult
                {
                    ResourceId = resourceId,
                    ContentType = contentType,
                    ClassifiedAt = DateTime.UtcNow,
                    TenantId = tenantId ?? "default-tenant"
                };

                // Step 1: Determine classification approach based on content type
                var classificationMethod = DetermineClassificationMethod(contentType);
                
                switch (classificationMethod)
                {
                    case "GraphInformationProtection":
                        result = await ClassifyUsingGraphInformationProtectionAsync(result, content);
                        break;
                    case "AzureCognitiveServices":
                        result = await ClassifyUsingCognitiveServicesAsync(result, content);
                        break;
                    case "CustomAiModel":
                        result = await ClassifyUsingCustomAiModelAsync(result, content);
                        break;
                    case "PatternMatching":
                        result = await ClassifyUsingPatternMatchingAsync(result, content);
                        break;
                    default:
                        result = await ClassifyUsingHeuristicAnalysisAsync(result, content);
                        break;
                }

                // Step 2: Apply governance-specific enhancements
                result = await EnhanceWithGovernanceClassificationAsync(result);

                // Step 3: Generate automated sensitivity labeling recommendation
                result.RecommendedSensitivityLabel = GenerateSensitivityLabelRecommendation(result);

                // Step 4: Create governance actions based on classification
                result.GovernanceActions = GenerateGovernanceActions(result);

                _logger.LogInformation($"Content classification complete: {resourceId}, Sensitivity: {result.SensitivityLevel}, Risk: {result.RiskScore}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error classifying content: {resourceId}");
                
                return new ContentClassificationResult
                {
                    ResourceId = resourceId,
                    ContentType = contentType,
                    ClassifiedAt = DateTime.UtcNow,
                    TenantId = tenantId ?? "default-tenant",
                    ClassificationStatus = "ERROR",
                    ErrorMessage = ex.Message,
                    SensitivityLevel = "UNKNOWN",
                    RiskScore = 50 // Medium risk for unclassified content
                };
            }
        }

        /// <summary>
        /// Classify using Microsoft Graph Information Protection API
        /// </summary>
        private async Task<ContentClassificationResult> ClassifyUsingGraphInformationProtectionAsync(
            ContentClassificationResult result, 
            byte[] content)
        {
            try
            {
                _logger.LogInformation("Using Graph Information Protection for classification");

                // Use Microsoft Graph Information Protection API
                var evaluateRequest = new InformationProtectionContentFormat
                {
                    // Content format for classification
                };

                // For demonstration - actual implementation would use Graph API
                result.ClassificationMethod = "GraphInformationProtection";
                result.ClassificationStatus = "SUCCESS";
                result.SensitivityLevel = "INTERNAL"; // Would be determined by API
                result.RiskScore = 25;
                
                result.DetectedSensitiveInfoTypes = new List<string>
                {
                    "Email Address",
                    "Phone Number"
                };

                result.Confidence = 0.85;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Graph Information Protection classification");
                result.ClassificationStatus = "FALLBACK";
                return await ClassifyUsingHeuristicAnalysisAsync(result, content);
            }
        }

        /// <summary>
        /// Classify using Azure Cognitive Services
        /// </summary>
        private async Task<ContentClassificationResult> ClassifyUsingCognitiveServicesAsync(
            ContentClassificationResult result, 
            byte[] content)
        {
            try
            {
                _logger.LogInformation("Using Azure Cognitive Services for classification");

                // Use Azure Text Analytics for content classification
                var endpoint = _configuration["AZURE_TEXT_ANALYTICS_ENDPOINT"];
                var apiKey = _configuration["AZURE_TEXT_ANALYTICS_KEY"];

                if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("Azure Text Analytics not configured, using fallback");
                    return await ClassifyUsingHeuristicAnalysisAsync(result, content);
                }

                // Convert content to text for analysis
                var textContent = ExtractTextFromContent(content, result.ContentType);
                
                if (string.IsNullOrEmpty(textContent))
                {
                    result.ClassificationStatus = "NO_TEXT_CONTENT";
                    return result;
                }

                // Perform PII detection using Text Analytics
                var piiResults = await DetectPiiUsingTextAnalyticsAsync(textContent, endpoint, apiKey);
                
                result.ClassificationMethod = "AzureCognitiveServices";
                result.ClassificationStatus = "SUCCESS";
                result.DetectedSensitiveInfoTypes = piiResults.DetectedEntities;
                result.SensitivityLevel = DetermineSensitivityFromPii(piiResults);
                result.RiskScore = CalculateRiskScoreFromPii(piiResults);
                result.Confidence = piiResults.Confidence;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Cognitive Services classification");
                result.ClassificationStatus = "FALLBACK";
                return await ClassifyUsingHeuristicAnalysisAsync(result, content);
            }
        }

        /// <summary>
        /// Classify using custom AI model for specialized content types
        /// </summary>
        private async Task<ContentClassificationResult> ClassifyUsingCustomAiModelAsync(
            ContentClassificationResult result, 
            byte[] content)
        {
            try
            {
                _logger.LogInformation("Using Custom AI Model for classification");

                // This would integrate with a custom-trained model for specific industry needs
                // For example: Healthcare records, Financial documents, Legal contracts
                
                result.ClassificationMethod = "CustomAiModel";
                result.ClassificationStatus = "SUCCESS";
                
                // Example classification for demonstration
                if (result.ContentType.Contains("pdf"))
                {
                    result.SensitivityLevel = "CONFIDENTIAL";
                    result.RiskScore = 60;
                    result.DetectedSensitiveInfoTypes.Add("Financial Information");
                }
                
                result.Confidence = 0.92;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Custom AI Model classification");
                result.ClassificationStatus = "FALLBACK";
                return await ClassifyUsingHeuristicAnalysisAsync(result, content);
            }
        }

        /// <summary>
        /// Classify using pattern matching for structured content
        /// </summary>
        private async Task<ContentClassificationResult> ClassifyUsingPatternMatchingAsync(
            ContentClassificationResult result, 
            byte[] content)
        {
            try
            {
                _logger.LogInformation("Using Pattern Matching for classification");

                var textContent = ExtractTextFromContent(content, result.ContentType);
                var detectedPatterns = new List<string>();
                var riskScore = 0;

                // Credit Card Number Pattern
                if (System.Text.RegularExpressions.Regex.IsMatch(textContent, @"\b(?:\d{4}[-\s]?){3}\d{4}\b"))
                {
                    detectedPatterns.Add("Credit Card Number");
                    riskScore += 40;
                }

                // Social Security Number Pattern
                if (System.Text.RegularExpressions.Regex.IsMatch(textContent, @"\b\d{3}-\d{2}-\d{4}\b"))
                {
                    detectedPatterns.Add("Social Security Number");
                    riskScore += 50;
                }

                // Email Pattern
                if (System.Text.RegularExpressions.Regex.IsMatch(textContent, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b"))
                {
                    detectedPatterns.Add("Email Address");
                    riskScore += 10;
                }

                // Phone Number Pattern
                if (System.Text.RegularExpressions.Regex.IsMatch(textContent, @"\b(?:\+?1[-.\s]?)?\(?[0-9]{3}\)?[-.\s]?[0-9]{3}[-.\s]?[0-9]{4}\b"))
                {
                    detectedPatterns.Add("Phone Number");
                    riskScore += 5;
                }

                result.ClassificationMethod = "PatternMatching";
                result.ClassificationStatus = "SUCCESS";
                result.DetectedSensitiveInfoTypes = detectedPatterns;
                result.RiskScore = Math.Min(riskScore, 100);
                result.SensitivityLevel = DetermineSensitivityFromRiskScore(result.RiskScore);
                result.Confidence = 0.75;

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Pattern Matching classification");
                result.ClassificationStatus = "ERROR";
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Fallback heuristic analysis for unsupported content types
        /// </summary>
        private async Task<ContentClassificationResult> ClassifyUsingHeuristicAnalysisAsync(
            ContentClassificationResult result, 
            byte[] content)
        {
            try
            {
                _logger.LogInformation("Using Heuristic Analysis for classification");

                result.ClassificationMethod = "HeuristicAnalysis";
                result.ClassificationStatus = "SUCCESS";

                // Analyze content type and size for basic classification
                var contentSize = content?.Length ?? 0;
                
                // Basic heuristics based on file type and size
                switch (result.ContentType.ToLower())
                {
                    case var ct when ct.Contains("image"):
                        result.SensitivityLevel = "PUBLIC";
                        result.RiskScore = 15;
                        result.DetectedSensitiveInfoTypes.Add("Image Content");
                        break;
                    case var ct when ct.Contains("video"):
                        result.SensitivityLevel = "INTERNAL";
                        result.RiskScore = 25;
                        result.DetectedSensitiveInfoTypes.Add("Video Content");
                        break;
                    case var ct when ct.Contains("pdf"):
                        result.SensitivityLevel = "CONFIDENTIAL";
                        result.RiskScore = 45;
                        result.DetectedSensitiveInfoTypes.Add("Document Content");
                        break;
                    default:
                        result.SensitivityLevel = "INTERNAL";
                        result.RiskScore = 30;
                        break;
                }

                // Adjust risk based on content size
                if (contentSize > 10 * 1024 * 1024) // > 10MB
                {
                    result.RiskScore += 10;
                }

                result.Confidence = 0.60; // Lower confidence for heuristic analysis
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Heuristic Analysis classification");
                result.ClassificationStatus = "ERROR";
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Enhance classification with governance-specific analysis
        /// </summary>
        private async Task<ContentClassificationResult> EnhanceWithGovernanceClassificationAsync(ContentClassificationResult result)
        {
            try
            {
                // Add governance-specific metadata
                result.GovernanceMetadata = new Dictionary<string, object>
                {
                    ["copilotAccessRestrictions"] = DetermineCopilotRestrictions(result),
                    ["aiInteractionRules"] = GenerateAiInteractionRules(result),
                    ["complianceRequirements"] = DetermineComplianceRequirements(result),
                    ["retentionPolicy"] = DetermineRetentionPolicy(result),
                    ["accessControls"] = GenerateAccessControls(result)
                };

                // Calculate governance risk score
                result.GovernanceRiskScore = CalculateGovernanceRiskScore(result);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enhancing classification with governance metadata");
                return result;
            }
        }

        /// <summary>
        /// Batch classify multiple content items for efficiency
        /// </summary>
        public async Task<List<ContentClassificationResult>> BatchClassifyContentAsync(
            List<ContentClassificationRequest> requests,
            string tenantId = null)
        {
            try
            {
                _logger.LogInformation($"Starting batch content classification for {requests.Count} items");

                var results = new List<ContentClassificationResult>();
                var batchSize = 10; // Process in batches to avoid overwhelming services

                for (int i = 0; i < requests.Count; i += batchSize)
                {
                    var batch = requests.Skip(i).Take(batchSize);
                    var batchTasks = batch.Select(async request =>
                    {
                        try
                        {
                            return await ClassifyContentAsync(
                                request.ResourceId,
                                request.ContentType,
                                request.Content,
                                tenantId
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error classifying content in batch: {request.ResourceId}");
                            return new ContentClassificationResult
                            {
                                ResourceId = request.ResourceId,
                                ContentType = request.ContentType,
                                ClassificationStatus = "ERROR",
                                ErrorMessage = ex.Message
                            };
                        }
                    });

                    var batchResults = await Task.WhenAll(batchTasks);
                    results.AddRange(batchResults);

                    // Add small delay between batches to be respectful of API limits
                    if (i + batchSize < requests.Count)
                    {
                        await Task.Delay(100);
                    }
                }

                _logger.LogInformation($"Batch content classification completed: {results.Count} items processed");
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch content classification");
                throw;
            }
        }

        // Helper methods
        private string DetermineClassificationMethod(string contentType)
        {
            return contentType.ToLower() switch
            {
                var ct when ct.Contains("text") || ct.Contains("json") || ct.Contains("xml") => "AzureCognitiveServices",
                var ct when ct.Contains("pdf") || ct.Contains("doc") => "CustomAiModel",
                var ct when ct.Contains("image") || ct.Contains("video") => "HeuristicAnalysis",
                _ => "PatternMatching"
            };
        }

        private string ExtractTextFromContent(byte[] content, string contentType)
        {
            if (content == null) return string.Empty;

            try
            {
                // Basic text extraction - in production, would use specialized libraries
                if (contentType.Contains("text") || contentType.Contains("json"))
                {
                    return Encoding.UTF8.GetString(content);
                }
                
                // For other types, would use OCR or document processing libraries
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<PiiDetectionResult> DetectPiiUsingTextAnalyticsAsync(string text, string endpoint, string apiKey)
        {
            // Placeholder for Azure Text Analytics PII detection
            return new PiiDetectionResult
            {
                DetectedEntities = new List<string> { "Email Address" },
                Confidence = 0.85
            };
        }

        private string DetermineSensitivityFromPii(PiiDetectionResult piiResult)
        {
            if (piiResult.DetectedEntities.Any(e => e.Contains("Credit Card") || e.Contains("Social Security")))
                return "HIGHLY_CONFIDENTIAL";
            if (piiResult.DetectedEntities.Any(e => e.Contains("Email") || e.Contains("Phone")))
                return "CONFIDENTIAL";
            if (piiResult.DetectedEntities.Any())
                return "INTERNAL";
            
            return "PUBLIC";
        }

        private int CalculateRiskScoreFromPii(PiiDetectionResult piiResult)
        {
            int score = 0;
            foreach (var entity in piiResult.DetectedEntities)
            {
                score += entity switch
                {
                    var e when e.Contains("Credit Card") => 50,
                    var e when e.Contains("Social Security") => 60,
                    var e when e.Contains("Email") => 10,
                    var e when e.Contains("Phone") => 5,
                    _ => 5
                };
            }
            return Math.Min(score, 100);
        }

        private string DetermineSensitivityFromRiskScore(int riskScore)
        {
            return riskScore switch
            {
                >= 70 => "HIGHLY_CONFIDENTIAL",
                >= 40 => "CONFIDENTIAL",
                >= 20 => "INTERNAL",
                _ => "PUBLIC"
            };
        }

        private string GenerateSensitivityLabelRecommendation(ContentClassificationResult result)
        {
            return result.SensitivityLevel switch
            {
                "HIGHLY_CONFIDENTIAL" => "Highly Confidential",
                "CONFIDENTIAL" => "Confidential",
                "INTERNAL" => "Internal",
                _ => "Public"
            };
        }

        private List<string> GenerateGovernanceActions(ContentClassificationResult result)
        {
            var actions = new List<string>();

            if (result.RiskScore >= 70)
            {
                actions.Add("BLOCK_COPILOT_ACCESS");
                actions.Add("REQUIRE_APPROVAL");
                actions.Add("NOTIFY_SECURITY_TEAM");
            }
            else if (result.RiskScore >= 40)
            {
                actions.Add("RESTRICT_COPILOT_RESPONSE");
                actions.Add("ENHANCED_LOGGING");
            }
            else if (result.RiskScore >= 20)
            {
                actions.Add("MONITOR_ACCESS");
                actions.Add("LOG_INTERACTION");
            }

            return actions;
        }

        private object DetermineCopilotRestrictions(ContentClassificationResult result)
        {
            return new
            {
                allowDirectAccess = result.RiskScore < 40,
                requireApproval = result.RiskScore >= 40,
                blockAccess = result.RiskScore >= 70,
                filterResponse = result.RiskScore >= 30
            };
        }

        private List<string> GenerateAiInteractionRules(ContentClassificationResult result)
        {
            var rules = new List<string>();

            if (result.DetectedSensitiveInfoTypes.Any(t => t.Contains("Credit Card") || t.Contains("Social Security")))
            {
                rules.Add("REDACT_SENSITIVE_DATA");
                rules.Add("NO_EXTERNAL_SHARING");
            }

            if (result.SensitivityLevel == "CONFIDENTIAL" || result.SensitivityLevel == "HIGHLY_CONFIDENTIAL")
            {
                rules.Add("INTERNAL_USE_ONLY");
                rules.Add("REQUIRE_CLEARANCE");
            }

            return rules;
        }

        private List<string> DetermineComplianceRequirements(ContentClassificationResult result)
        {
            var requirements = new List<string>();

            if (result.DetectedSensitiveInfoTypes.Any(t => t.Contains("Credit Card")))
            {
                requirements.Add("PCI_DSS");
            }

            if (result.DetectedSensitiveInfoTypes.Any(t => t.Contains("Health") || t.Contains("Medical")))
            {
                requirements.Add("HIPAA");
            }

            if (result.DetectedSensitiveInfoTypes.Any(t => t.Contains("Personal")))
            {
                requirements.Add("GDPR");
            }

            return requirements;
        }

        private string DetermineRetentionPolicy(ContentClassificationResult result)
        {
            return result.SensitivityLevel switch
            {
                "HIGHLY_CONFIDENTIAL" => "7_YEARS",
                "CONFIDENTIAL" => "5_YEARS",
                "INTERNAL" => "3_YEARS",
                _ => "1_YEAR"
            };
        }

        private object GenerateAccessControls(ContentClassificationResult result)
        {
            return new
            {
                minimumClearanceLevel = result.SensitivityLevel,
                requiresJustification = result.RiskScore >= 40,
                allowedOperations = result.RiskScore switch
                {
                    >= 70 => new[] { "VIEW_METADATA" },
                    >= 40 => new[] { "READ", "VIEW_METADATA" },
                    >= 20 => new[] { "READ", "DOWNLOAD", "VIEW_METADATA" },
                    _ => new[] { "READ", "DOWNLOAD", "SHARE", "VIEW_METADATA" }
                }
            };
        }

        private int CalculateGovernanceRiskScore(ContentClassificationResult result)
        {
            var baseScore = result.RiskScore;

            // Adjust based on content type risks
            if (result.ContentType.Contains("executable") || result.ContentType.Contains("script"))
            {
                baseScore += 20;
            }

            // Adjust based on detected sensitive info types
            if (result.DetectedSensitiveInfoTypes.Count > 3)
            {
                baseScore += 15;
            }

            return Math.Min(baseScore, 100);
        }
    }

    // Supporting classes
    public class ContentClassificationResult
    {
        public string ResourceId { get; set; }
        public string ContentType { get; set; }
        public string TenantId { get; set; }
        public DateTime ClassifiedAt { get; set; }
        public string ClassificationMethod { get; set; }
        public string ClassificationStatus { get; set; }
        public string ErrorMessage { get; set; }
        public string SensitivityLevel { get; set; }
        public int RiskScore { get; set; }
        public int GovernanceRiskScore { get; set; }
        public double Confidence { get; set; }
        public List<string> DetectedSensitiveInfoTypes { get; set; } = new List<string>();
        public string RecommendedSensitivityLabel { get; set; }
        public List<string> GovernanceActions { get; set; } = new List<string>();
        public Dictionary<string, object> GovernanceMetadata { get; set; } = new Dictionary<string, object>();
    }

    public class ContentClassificationRequest
    {
        public string ResourceId { get; set; }
        public string ContentType { get; set; }
        public byte[] Content { get; set; }
    }

    public class PiiDetectionResult
    {
        public List<string> DetectedEntities { get; set; } = new List<string>();
        public double Confidence { get; set; }
    }

    public class InformationProtectionContentFormat
    {
        // Placeholder for Microsoft Graph Information Protection request format
    }
}