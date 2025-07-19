using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using VaultsFunctions.Core.Helpers;

namespace VaultsFunctions.Functions.Copilot
{
    public class CopilotRetrieveFunction
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public CopilotRetrieveFunction(IConfiguration configuration, HttpClient httpClient)
        {
            _configuration = configuration;
            _httpClient = httpClient;
        }

        [Function("CopilotRetrieveFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "copilot/retrieve")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<CopilotRetrieveFunction>();
            log.LogInformation("CopilotRetrieveFunction received a request.");

            var response = req.CreateResponse();
            CorsHelper.AddCorsHeaders(response, _configuration);

            try
            {
                var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                
                if (string.IsNullOrEmpty(requestBody))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    await response.WriteAsJsonAsync(new { error = "Request body is required" });
                    return response;
                }

                // Forward the request to Microsoft Graph Copilot retrieve endpoint
                var graphRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/copilot/retrieve")
                {
                    Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json")
                };

                // Add authorization header (this would need proper token management in production)
                var accessToken = await GetAccessTokenAsync();
                if (!string.IsNullOrEmpty(accessToken))
                {
                    graphRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                }

                var graphResponse = await _httpClient.SendAsync(graphRequest);
                
                if (graphResponse.IsSuccessStatusCode)
                {
                    var content = await graphResponse.Content.ReadAsStringAsync();
                    var retrievedContent = JsonConvert.DeserializeObject(content);
                    
                    response.StatusCode = HttpStatusCode.OK;
                    await response.WriteAsJsonAsync(retrievedContent);
                }
                else
                {
                    log.LogWarning($"Graph API returned {graphResponse.StatusCode} for retrieve request");
                    response.StatusCode = graphResponse.StatusCode;
                    await response.WriteAsJsonAsync(new { 
                        error = "Failed to retrieve content from Microsoft Graph",
                        statusCode = (int)graphResponse.StatusCode
                    });
                }

                return response;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error in Copilot retrieve function");
                response.StatusCode = HttpStatusCode.InternalServerError;
                await response.WriteAsJsonAsync(new { error = $"Failed to retrieve content: {ex.Message}" });
                return response;
            }
        }

        private async Task<string> GetAccessTokenAsync()
        {
            // This is a placeholder - in production, you would implement proper token acquisition
            // using Azure.Identity or Microsoft.Graph authentication mechanisms
            try
            {
                // Use Task.FromResult to make this properly async
                await Task.Delay(0); // Minimal async operation to satisfy compiler
                return string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}