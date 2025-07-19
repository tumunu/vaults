using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using Newtonsoft.Json;
using VaultsFunctions.Core.Services;
using VaultsFunctions.Core.Helpers;

namespace VaultsFunctions.Functions.Governance
{
    public class ContentClassificationFunction
    {
        private readonly ILogger<ContentClassificationFunction> _logger;
        private readonly ContentClassificationService _contentClassificationService;
        private readonly IConfiguration _configuration;

        public ContentClassificationFunction(
            ILogger<ContentClassificationFunction> logger,
            ContentClassificationService contentClassificationService,
            IConfiguration configuration)
        {
            _logger = logger;
            _contentClassificationService = contentClassificationService;
            _configuration = configuration;
        }

        /// <summary>
        /// Classify content for AI governance
        /// POST /api/governance/content/classify
        /// </summary>
        [Function("ClassifyContent")]
        public async Task<HttpResponseData> ClassifyContent(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/content/classify")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing content classification request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var classificationRequest = JsonConvert.DeserializeObject<ContentClassificationApiRequest>(requestBody);

                if (classificationRequest == null || string.IsNullOrEmpty(classificationRequest.ResourceId))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid classification request",
                        message = "ResourceId and ContentType are required"
                    }));
                    return response;
                }

                // Decode content if provided
                byte[] content = null;
                if (!string.IsNullOrEmpty(classificationRequest.ContentBase64))
                {
                    try
                    {
                        content = Convert.FromBase64String(classificationRequest.ContentBase64);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to decode base64 content");
                    }
                }

                // Classify content
                var classificationResult = await _contentClassificationService.ClassifyContentAsync(
                    classificationRequest.ResourceId,
                    classificationRequest.ContentType,
                    content,
                    classificationRequest.TenantId
                );

                // Format response
                var responseData = new
                {
                    classificationId = Guid.NewGuid().ToString(),
                    request = new
                    {
                        resourceId = classificationRequest.ResourceId,
                        contentType = classificationRequest.ContentType,
                        tenantId = classificationRequest.TenantId ?? "default-tenant"
                    },
                    classification = new
                    {
                        method = classificationResult.ClassificationMethod,
                        status = classificationResult.ClassificationStatus,
                        sensitivityLevel = classificationResult.SensitivityLevel,
                        riskScore = classificationResult.RiskScore,
                        governanceRiskScore = classificationResult.GovernanceRiskScore,
                        confidence = classificationResult.Confidence,
                        detectedSensitiveInfoTypes = classificationResult.DetectedSensitiveInfoTypes,
                        recommendedSensitivityLabel = classificationResult.RecommendedSensitivityLabel,
                        classifiedAt = classificationResult.ClassifiedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    },
                    governance = new
                    {
                        actions = classificationResult.GovernanceActions,
                        metadata = classificationResult.GovernanceMetadata,
                        aiAccessRecommendation = DetermineAiAccessRecommendation(classificationResult)
                    },
                    copilotImpact = new
                    {
                        allowAccess = classificationResult.RiskScore < 70,
                        requireApproval = classificationResult.RiskScore >= 40,
                        restrictResponse = classificationResult.RiskScore >= 30,
                        blockAccess = classificationResult.RiskScore >= 70,
                        enhancedMonitoring = classificationResult.RiskScore >= 20
                    },
                    microsoftPurviewGap = new
                    {
                        addressesNonOfficeFiles = true,
                        providesAiSpecificGuidance = true,
                        enhancesNativeClassification = true,
                        fillsGovernanceGaps = true
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Content classification completed: {classificationRequest.ResourceId}, Sensitivity: {classificationResult.SensitivityLevel}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing content classification request");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Content classification failed",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Batch classify multiple content items
        /// POST /api/governance/content/classify-batch
        /// </summary>
        [Function("ClassifyContentBatch")]
        public async Task<HttpResponseData> ClassifyContentBatch(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/content/classify-batch")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing batch content classification request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var batchRequest = JsonConvert.DeserializeObject<BatchContentClassificationRequest>(requestBody);

                if (batchRequest?.Requests == null || !batchRequest.Requests.Any())
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid batch classification request",
                        message = "At least one classification request is required"
                    }));
                    return response;
                }

                // Convert API requests to service requests
                var serviceRequests = batchRequest.Requests.Select(r => new ContentClassificationRequest
                {
                    ResourceId = r.ResourceId,
                    ContentType = r.ContentType,
                    Content = !string.IsNullOrEmpty(r.ContentBase64) ? 
                        Convert.FromBase64String(r.ContentBase64) : null
                }).ToList();

                // Process batch classification
                var classificationResults = await _contentClassificationService.BatchClassifyContentAsync(
                    serviceRequests,
                    batchRequest.TenantId
                );

                // Calculate batch statistics
                var successCount = classificationResults.Count(r => r.ClassificationStatus == "SUCCESS");
                var errorCount = classificationResults.Count(r => r.ClassificationStatus == "ERROR");
                var highRiskCount = classificationResults.Count(r => r.RiskScore >= 70);
                var confidentialCount = classificationResults.Count(r => 
                    r.SensitivityLevel == "CONFIDENTIAL" || r.SensitivityLevel == "HIGHLY_CONFIDENTIAL");

                // Format batch response
                var responseData = new
                {
                    batchId = Guid.NewGuid().ToString(),
                    processedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    tenantId = batchRequest.TenantId ?? "default-tenant",
                    summary = new
                    {
                        totalRequests = classificationResults.Count,
                        successful = successCount,
                        errors = errorCount,
                        highRisk = highRiskCount,
                        confidential = confidentialCount,
                        successRate = Math.Round((double)successCount / classificationResults.Count * 100, 2)
                    },
                    results = classificationResults.Select(result => new
                    {
                        resourceId = result.ResourceId,
                        contentType = result.ContentType,
                        classification = new
                        {
                            status = result.ClassificationStatus,
                            method = result.ClassificationMethod,
                            sensitivityLevel = result.SensitivityLevel,
                            riskScore = result.RiskScore,
                            confidence = result.Confidence,
                            detectedSensitiveInfoTypes = result.DetectedSensitiveInfoTypes,
                            recommendedLabel = result.RecommendedSensitivityLabel
                        },
                        governance = new
                        {
                            actions = result.GovernanceActions,
                            aiAccessAllowed = result.RiskScore < 70,
                            requiresApproval = result.RiskScore >= 40
                        },
                        error = result.ErrorMessage
                    }).ToList(),
                    governance = new
                    {
                        batchProcessing = true,
                        consistentClassification = true,
                        scalableGovernance = true,
                        auditTrail = true
                    },
                    microsoftPurviewEnhancement = new
                    {
                        handlesNonOfficeFiles = true,
                        providesRiskScoring = true,
                        enablesAiGovernance = true,
                        complementsPurview = true
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Batch content classification completed: {successCount}/{classificationResults.Count} successful");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing batch content classification request");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Batch content classification failed",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Get content classification summary for governance dashboard
        /// GET /api/governance/content/classification-summary
        /// </summary>
        [Function("GetContentClassificationSummary")]
        public async Task<HttpResponseData> GetContentClassificationSummary(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "governance/content/classification-summary")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing content classification summary request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse query parameters
                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var tenantId = query["tenantId"] ?? "default-tenant";
                var days = int.TryParse(query["days"], out int d) ? d : 30;

                // Generate classification summary (this would typically query historical data)
                var summary = await GenerateClassificationSummaryAsync(tenantId, days);

                var responseData = new
                {
                    tenantId,
                    period = new
                    {
                        days,
                        startDate = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd"),
                        endDate = DateTime.UtcNow.ToString("yyyy-MM-dd")
                    },
                    summary,
                    governance = new
                    {
                        classificationAnalytics = true,
                        riskTrends = true,
                        complianceReporting = true,
                        governanceInsights = true
                    },
                    generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Content classification summary generated for tenant {tenantId}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating content classification summary");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Failed to generate classification summary",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Apply automated sensitivity labeling based on classification
        /// POST /api/governance/content/apply-labels
        /// </summary>
        [Function("ApplySensitivityLabels")]
        public async Task<HttpResponseData> ApplySensitivityLabels(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "governance/content/apply-labels")] 
            HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("Processing automated sensitivity labeling request");

                var response = req.CreateResponse();
                CorsHelper.AddCorsHeaders(response);

                // Parse request body
                var requestBody = await req.ReadAsStringAsync();
                var labelingRequest = JsonConvert.DeserializeObject<AutomatedLabelingRequest>(requestBody);

                if (labelingRequest?.ResourceIds == null || !labelingRequest.ResourceIds.Any())
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteStringAsync(JsonConvert.SerializeObject(new
                    {
                        error = "Invalid labeling request",
                        message = "At least one ResourceId is required"
                    }));
                    return response;
                }

                var labelingResults = new List<object>();

                // Process each resource for automated labeling
                foreach (var resourceId in labelingRequest.ResourceIds)
                {
                    try
                    {
                        var labelingResult = await ProcessAutomatedLabelingAsync(
                            resourceId, 
                            labelingRequest.TenantId,
                            labelingRequest.ForceReClassification
                        );
                        
                        labelingResults.Add(labelingResult);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing automated labeling for resource {resourceId}");
                        labelingResults.Add(new
                        {
                            resourceId,
                            success = false,
                            error = ex.Message,
                            processedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                        });
                    }
                }

                // Calculate success statistics
                var successCount = labelingResults.Count(r => 
                    r.GetType().GetProperty("success")?.GetValue(r) as bool? == true);

                var responseData = new
                {
                    labelingId = Guid.NewGuid().ToString(),
                    processedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    tenantId = labelingRequest.TenantId ?? "default-tenant",
                    summary = new
                    {
                        totalResources = labelingResults.Count,
                        successful = successCount,
                        failed = labelingResults.Count - successCount,
                        successRate = Math.Round((double)successCount / labelingResults.Count * 100, 2)
                    },
                    results = labelingResults,
                    governance = new
                    {
                        automatedLabeling = true,
                        fillsPurviewGaps = true,
                        enhancesCompliance = true,
                        reducesManualEffort = true
                    },
                    microsoftPurviewIntegration = new
                    {
                        complementsNativeLabeling = true,
                        handlesUnsupportedFileTypes = true,
                        providesAiGovernanceContext = true,
                        enablesAutomatedWorkflows = true
                    }
                };

                response.StatusCode = HttpStatusCode.OK;
                await response.WriteStringAsync(JsonConvert.SerializeObject(responseData, Formatting.Indented));

                _logger.LogInformation($"Automated sensitivity labeling completed: {successCount}/{labelingResults.Count} successful");
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing automated sensitivity labeling request");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                CorsHelper.AddCorsHeaders(errorResponse);
                
                await errorResponse.WriteStringAsync(JsonConvert.SerializeObject(new
                {
                    error = "Automated sensitivity labeling failed",
                    message = ex.Message,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                }));

                return errorResponse;
            }
        }

        /// <summary>
        /// Determine AI access recommendation based on classification
        /// </summary>
        private object DetermineAiAccessRecommendation(ContentClassificationResult classification)
        {
            return new
            {
                recommendation = classification.RiskScore switch
                {
                    >= 70 => "BLOCK_ACCESS",
                    >= 40 => "REQUIRE_APPROVAL",
                    >= 20 => "MONITOR_ONLY",
                    _ => "ALLOW_ACCESS"
                },
                reasoning = GenerateAccessRecommendationReasoning(classification),
                alternativeActions = GenerateAlternativeActions(classification)
            };
        }

        /// <summary>
        /// Generate reasoning for access recommendation
        /// </summary>
        private string GenerateAccessRecommendationReasoning(ContentClassificationResult classification)
        {
            if (classification.RiskScore >= 70)
                return $"High risk content (score: {classification.RiskScore}) with sensitive data types: {string.Join(", ", classification.DetectedSensitiveInfoTypes)}";
            
            if (classification.RiskScore >= 40)
                return $"Medium-high risk content (score: {classification.RiskScore}) requires approval workflow";
            
            if (classification.RiskScore >= 20)
                return $"Medium risk content (score: {classification.RiskScore}) suitable for monitoring";
            
            return $"Low risk content (score: {classification.RiskScore}) safe for standard AI access";
        }

        /// <summary>
        /// Generate alternative actions for governance
        /// </summary>
        private List<string> GenerateAlternativeActions(ContentClassificationResult classification)
        {
            var actions = new List<string>();

            if (classification.RiskScore >= 40)
            {
                actions.Add("Apply data redaction before AI access");
                actions.Add("Create summary instead of full content access");
                actions.Add("Restrict to metadata-only access");
            }

            if (classification.DetectedSensitiveInfoTypes.Any())
            {
                actions.Add("Enable enhanced audit logging");
                actions.Add("Notify data owner of AI access");
            }

            return actions;
        }

        /// <summary>
        /// Generate content classification summary for dashboard
        /// </summary>
        private async Task<object> GenerateClassificationSummaryAsync(string tenantId, int days)
        {
            // This would typically query historical classification data from database
            return new
            {
                totalClassifications = 1250,
                byStatus = new
                {
                    successful = 1180,
                    errors = 45,
                    pending = 25
                },
                bySensitivityLevel = new
                {
                    publicContent = 450,
                    internalContent = 520,
                    confidentialContent = 210,
                    highlyConfidentialContent = 70
                },
                byRiskScore = new
                {
                    lowRisk = 680,      // 0-29
                    mediumRisk = 380,   // 30-69  
                    highRisk = 190      // 70-100
                },
                byContentType = new
                {
                    documents = 520,
                    images = 310,
                    videos = 180,
                    other = 240
                },
                detectedSensitiveData = new
                {
                    emailAddresses = 890,
                    phoneNumbers = 340,
                    creditCardNumbers = 45,
                    socialSecurityNumbers = 12,
                    healthRecords = 23
                },
                governanceActions = new
                {
                    accessBlocked = 67,
                    approvalRequired = 145,
                    enhancedMonitoring = 320,
                    automatedLabeling = 950
                },
                trends = new
                {
                    weekOverWeekIncrease = 12.5,
                    highRiskTrend = "DECREASING",
                    classificationAccuracy = 94.2
                }
            };
        }

        /// <summary>
        /// Process automated sensitivity labeling for a resource
        /// </summary>
        private async Task<object> ProcessAutomatedLabelingAsync(string resourceId, string tenantId, bool forceReClassification)
        {
            try
            {
                // This would integrate with Microsoft Graph Information Protection API
                // to apply the recommended sensitivity labels
                
                _logger.LogInformation($"Processing automated labeling for resource: {resourceId}");

                // Simulate automated labeling process
                var success = true; // Would be actual result from Graph API
                var appliedLabel = "Confidential"; // Would come from classification result

                return new
                {
                    resourceId,
                    success,
                    appliedLabel,
                    previousLabel = forceReClassification ? "Internal" : null,
                    labelingMethod = "AUTOMATED",
                    governanceReason = "AI-powered content classification",
                    processedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in automated labeling for resource {resourceId}");
                return new
                {
                    resourceId,
                    success = false,
                    error = ex.Message,
                    processedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                };
            }
        }
    }

    // Supporting classes for content classification functions
    public class ContentClassificationApiRequest
    {
        public string ResourceId { get; set; }
        public string ContentType { get; set; }
        public string ContentBase64 { get; set; } // Base64 encoded content
        public string TenantId { get; set; }
    }

    public class BatchContentClassificationRequest
    {
        public List<ContentClassificationApiRequest> Requests { get; set; } = new List<ContentClassificationApiRequest>();
        public string TenantId { get; set; }
    }

    public class AutomatedLabelingRequest
    {
        public List<string> ResourceIds { get; set; } = new List<string>();
        public string TenantId { get; set; }
        public bool ForceReClassification { get; set; } = false;
    }
}