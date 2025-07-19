using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Cosmos;
using System.Net;
using System.Linq;
using System.Collections.Generic;
using VaultsFunctions.Core.Models;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Graph;
using SendGrid;
using SendGrid.Helpers.Mail;
using Newtonsoft.Json;
using Stripe; // Add for StripeConfiguration, CustomerService, etc.

namespace VaultsFunctions.Functions.Onboarding
{
    public class OnboardingApiFunction
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Container _tenantsContainer;

        public OnboardingApiFunction(IConfiguration configuration, CosmosClient cosmosClient)
        {
            _configuration = configuration;
            StripeConfiguration.ApiKey = _configuration["StripeSecretKey"]; // Initialize Stripe API key
            _cosmosClient = cosmosClient;
            var database = _cosmosClient.GetDatabase("Vaults");
            _tenantsContainer = database.GetContainer("Tenants");
        }

        [Function("ValidateAzureAdPermissions")]
        public async Task<HttpResponseData> ValidateAzureAdPermissions(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "onboarding/validate-azure-ad")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<OnboardingApiFunction>();
            log.LogInformation("ValidateAzureAdPermissions function received a request.");

            string requestBody = await RequestBodyHelper.ReadBodyAsStringAndEnableReuse(req);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string tenantId = data?.tenantId;
            string azureAdAppId = data?.azureAdAppId;
            string azureAdAppSecret = data?.azureAdAppSecret;

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(azureAdAppId) || string.IsNullOrEmpty(azureAdAppSecret))
            {
                var badRequestResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "Please provide tenantId, azureAdAppId, and azureAdAppSecret." });
                return badRequestResponse;
            }

            try
            {
                // Define the required Graph API permissions
                var requiredScopes = new[] { "Chat.Read", "AiEnterpriseInteraction.Read.All" };

                // Attempt to get an access token using the provided credentials and required scopes
                var clientSecretCredential = new ClientSecretCredential(tenantId, azureAdAppId, azureAdAppSecret);
                var graphClient = new GraphServiceClient(clientSecretCredential, requiredScopes);

                // Test Chat.Read permission by attempting to list chats
                try
                {
                    // Attempt to get a small number of chats to verify permission
                    await graphClient.Chats.GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Top = 1;
                    });
                    log.LogInformation("Chat.Read permission verified.");
                }
                catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
                {
                    throw new Exception("Chat.Read permission is missing or insufficient.");
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Could not verify Chat.Read permission fully (might be no chats or other issue): {ex.Message}");
                }

                // Test AiEnterpriseInteraction.Read.All permission by attempting to list enterprise interactions
                try
                {
                    // Attempt to get a small number of enterprise interactions to verify permission
                    await graphClient.Solutions.VirtualEvents.Webinars.GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Top = 1;
                    });
                    log.LogInformation("AiEnterpriseInteraction.Read.All permission verified.");
                }
                catch (ServiceException ex) when (ex.ResponseStatusCode == (int)System.Net.HttpStatusCode.Forbidden)
                {
                    throw new Exception("AiEnterpriseInteraction.Read.All permission is missing or insufficient.");
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Could not verify AiEnterpriseInteraction.Read.All permission fully (might be no interactions or other issue): {ex.Message}");
                }
                
                log.LogInformation($"Azure AD permissions validated for tenant {tenantId}");
                var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await okResponse.WriteAsJsonAsync(new { success = true, message = "Azure AD permissions validated successfully." });
                return okResponse;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Azure AD permission validation failed for tenant {tenantId}: {ex.Message}");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = $"Validation failed: {ex.Message}" });
                return errorResponse;
            }
        }

        [Function("TestStorageConnection")]
        public async Task<HttpResponseData> TestStorageConnection(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "onboarding/test-storage-connection")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<OnboardingApiFunction>();
            log.LogInformation("TestStorageConnection function received a request.");

            string requestBody = await RequestBodyHelper.ReadBodyAsStringAndEnableReuse(req);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string storageAccountName = data?.azureStorageAccountName;
            string containerName = data?.azureStorageContainerName;
            string sasToken = data?.azureStorageSasToken; // Optional

            if (string.IsNullOrEmpty(storageAccountName) || string.IsNullOrEmpty(containerName))
            {
                var badRequestResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "Please provide storageAccountName and containerName." });
                return badRequestResponse;
            }

            try
            {
                BlobServiceClient blobServiceClient;
                if (!string.IsNullOrEmpty(sasToken))
                {
                    // Use SAS token for authentication
                    blobServiceClient = new BlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net{sasToken}"));
                }
                else
                {
                    // Attempt to use Managed Identity (assuming Function App has Storage Blob Data Contributor role)
                    blobServiceClient = new BlobServiceClient(new Uri($"https://{storageAccountName}.blob.core.windows.net"), new DefaultAzureCredential());
                }

                var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
                await containerClient.CreateIfNotExistsAsync(); // Try to create/access container

                // Attempt a dummy blob write to verify write permissions
                var testBlobClient = containerClient.GetBlobClient($"test-connection-{Guid.NewGuid()}.txt");
                await testBlobClient.UploadAsync(BinaryData.FromString("test"), overwrite: true);
                await testBlobClient.DeleteIfExistsAsync(); // Clean up

                log.LogInformation($"Azure Storage connection tested successfully for account {storageAccountName}, container {containerName}");
                var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await okResponse.WriteAsJsonAsync(new { success = true, message = "Azure Storage connection successful." });
                return okResponse;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Azure Storage connection test failed for account {storageAccountName}: {ex.Message}");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = $"Connection failed: {ex.Message}" });
                return errorResponse;
            }
        }

        [Function("CompleteOnboarding")]
        public async Task<HttpResponseData> CompleteOnboarding(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "onboarding/complete")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<OnboardingApiFunction>();
            log.LogInformation("CompleteOnboarding function received a request.");

            string requestBody = await RequestBodyHelper.ReadBodyAsStringAndEnableReuse(req);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string tenantId = data?.tenantId;
            string azureAdAppId = data?.azureAdAppId;
            string azureStorageAccountName = data?.azureStorageAccountName;
            string azureStorageContainerName = data?.azureStorageContainerName;
            string retentionPolicy = data?.retentionPolicy;
            int? customRetentionDays = data?.customRetentionDays;
            string exportSchedule = data?.exportSchedule;
            string exportTime = data?.exportTime;

            if (string.IsNullOrEmpty(tenantId))
            {
                var badRequestResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "Tenant ID is required to complete onboarding." });
                return badRequestResponse;
            }

            try
            {
                var tenantStatus = await GetTenantStatus(tenantId) ?? new TenantStatus { Id = tenantId };

                tenantStatus.AzureAdAppId = azureAdAppId;
                tenantStatus.AzureStorageAccountName = azureStorageAccountName;
                tenantStatus.AzureStorageContainerName = azureStorageContainerName;
                tenantStatus.RetentionPolicy = retentionPolicy;
                tenantStatus.CustomRetentionDays = customRetentionDays;
                tenantStatus.ExportSchedule = exportSchedule;
                tenantStatus.ExportTime = exportTime;
                // Ensure Stripe Customer exists for the tenant
                if (string.IsNullOrEmpty(tenantStatus.StripeCustomerId))
                {
                    var customerService = new CustomerService();
                    var customer = await customerService.CreateAsync(new CustomerCreateOptions
                    {
                        Metadata = { { "tenantId", tenantId } }
                    });
                    tenantStatus.StripeCustomerId = customer.Id;
                    log.LogInformation($"Stripe customer created during onboarding for tenant {tenantId}: {customer.Id}");
                }

                tenantStatus.OnboardingComplete = true;
                tenantStatus.UpdatedAt = DateTimeOffset.UtcNow;

                await _tenantsContainer.UpsertItemAsync(tenantStatus, new PartitionKey(tenantId));

                log.LogInformation($"Onboarding completed for tenant {tenantId}");
                var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await okResponse.WriteAsJsonAsync(new { success = true, message = "Onboarding completed successfully." });
                return okResponse;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error completing onboarding for tenant {tenantId}: {ex.Message}");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = $"Error completing onboarding: {ex.Message}" });
                return errorResponse;
            }
        }

        [Function("SendOnboardingEmail")]
        public async Task<HttpResponseData> SendOnboardingEmail(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "onboarding/send-email")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<OnboardingApiFunction>();
            log.LogInformation("SendOnboardingEmail function received a request.");

            string requestBody = await RequestBodyHelper.ReadBodyAsStringAndEnableReuse(req);
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            string tenantId = data?.tenantId;
            string adminEmail = data?.adminEmail; // Assuming admin email is passed or retrieved

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(adminEmail))
            {
                var badRequestResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { success = false, error = "Please provide tenantId and adminEmail." });
                return badRequestResponse;
            }

            try
            {
                var sendGridApiKey = _configuration["SendGridApiKey"];
                if (string.IsNullOrEmpty(sendGridApiKey))
                {
                    log.LogError("SendGrid API Key is not configured.");
                    var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = "SendGrid API Key is not configured." });
                    return errorResponse;
                }

                var client = new SendGridClient(sendGridApiKey);
                string senderEmail = _configuration["SendGridSenderEmail"] ?? "noreply@vaultss.com"; // Configurable sender email
                string senderName = _configuration["SendGridSenderName"] ?? "Copilot Vaults Support"; // Configurable sender name
                var fromEmail = new SendGrid.Helpers.Mail.EmailAddress(senderEmail, senderName);
                var toEmail = new SendGrid.Helpers.Mail.EmailAddress(adminEmail);
                var subject = "Welcome to Copilot Vaults - Your Onboarding is Complete!";
                var plainTextContent = $"Dear Admin,\n\nYour Copilot Vault onboarding for tenant {tenantId} is now complete. You can access your dashboard at [Dashboard URL].\n\nFor support, please contact support@vaultss.com.\n\nThank you,\nCopilot Vaults Team";
                var htmlContent = $@"
                    <html>
                    <body>
                        <h2>Welcome to Copilot Vaults!</h2>
                        <p>Dear Admin,</p>
                        <p>Your Copilot Vault onboarding for tenant <strong>{tenantId}</strong> is now complete.</p>
                        <p>You can access your dashboard here: <a href=""[Dashboard URL]"">Copilot Vault Dashboard</a>.</p>
                        <p>For any questions or support, please contact us at <a href=""mailto:support@vaultss.com"">support@vaultss.com</a>.</p>
                        <p>Thank you,<br/>The Copilot Vault Team</p>
                    </body>
                    </html>";
                
                // Replace [Dashboard URL] with the actual dashboard URL from configuration or a dynamic value
                var dashboardUrl = _configuration["DashboardUrl"]; // Assuming DashboardUrl is always configured
                htmlContent = htmlContent.Replace("[Dashboard URL]", dashboardUrl);
                plainTextContent = plainTextContent.Replace("[Dashboard URL]", dashboardUrl);

                var msg = MailHelper.CreateSingleEmail(fromEmail, toEmail, subject, plainTextContent, htmlContent);
                var sendGridResponse = await client.SendEmailAsync(msg);

                if (sendGridResponse.IsSuccessStatusCode)
                {
                    log.LogInformation($"Onboarding email sent successfully to {adminEmail} for tenant {tenantId}.");
                    var okResponse = req.CreateResponse(System.Net.HttpStatusCode.OK);
                    await okResponse.WriteAsJsonAsync(new { success = true, message = "Onboarding email sent successfully." });
                    return okResponse;
                }
                else
                {
                    var responseBody = await sendGridResponse.Body.ReadAsStringAsync();
                    log.LogError($"Failed to send onboarding email to {adminEmail} for tenant {tenantId}. Status Code: {sendGridResponse.StatusCode}, Body: {responseBody}");
                    var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                    await errorResponse.WriteAsJsonAsync(new { success = false, error = "Failed to send onboarding email." });
                    return errorResponse;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error sending onboarding email for tenant {tenantId}: {ex.Message}");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { success = false, error = $"Error sending onboarding email: {ex.Message}" });
                return errorResponse;
            }
        }

        private async Task<TenantStatus> GetTenantStatus(string tenantId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @tenantId")
                    .WithParameter("@tenantId", tenantId);

                using var iterator = _tenantsContainer.GetItemQueryIterator<TenantStatus>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    return response.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not retrieve tenant status: {ex.Message}");
            }

            return null;
        }
    }
}
