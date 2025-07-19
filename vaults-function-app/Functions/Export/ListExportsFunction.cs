using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;
using System.Net;
using System.Web;

namespace VaultsFunctions.Functions.Export
{
    public class ListExportsFunction
    {
        private readonly IConfiguration _configuration;

        public ListExportsFunction(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [Function("ListExportsFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "listexportsfunction")] HttpRequestData req,
            FunctionContext context)
        {
            var log = context.GetLogger<ListExportsFunction>();
            log.LogInformation("ListExportsFunction received a request.");

            var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
            string tenantId = queryParams["tenantId"];
            string dateFilter = queryParams["date"]; // Optional: YYYY-MM-DD

            if (string.IsNullOrEmpty(tenantId))
            {
                var badRequestResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badRequestResponse.WriteAsJsonAsync(new { error = "Tenant ID is required." });
                return badRequestResponse;
            }

            try
            {
                string storageAccountName = _configuration["AZURE_STORAGE_ACCOUNT_NAME"];
                string storageContainerName = _configuration["AZURE_STORAGE_CONTAINER_NAME"];
                string storageConnectionString = _configuration["AzureWebJobsStorage"]; // Or a specific connection string for blob storage

                if (string.IsNullOrEmpty(storageAccountName) || string.IsNullOrEmpty(storageContainerName) || string.IsNullOrEmpty(storageConnectionString))
                {
                    throw new InvalidOperationException("Azure Storage configuration is missing.");
                }

                BlobServiceClient blobServiceClient = new BlobServiceClient(storageConnectionString);
                BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);

                // Ensure the container exists
                await containerClient.CreateIfNotExistsAsync();

                List<object> exports = new List<object>();

                // Construct the prefix for the tenant's exports
                // Assuming blob paths are like: {tenantId}/exports/{year}-{month}-{day}/conversation-{uuid}.json
                string prefix = $"{tenantId}/exports/";
                if (!string.IsNullOrEmpty(dateFilter))
                {
                    // Example: tenantId/exports/2024-01-01/
                    prefix += $"{dateFilter}/";
                }

                await foreach (BlobItem blobItem in containerClient.GetBlobsAsync(prefix: prefix))
                {
                    // Extract relevant info from blobItem.Name
                    // Example: "tenant123/exports/2024-06-10/conversation-abc-123.json"
                    string fileName = Path.GetFileName(blobItem.Name);
                    string fullPath = blobItem.Name;
                    Uri blobUri = containerClient.GetBlobClient(blobItem.Name).Uri;

                    exports.Add(new
                    {
                        name = fileName,
                        path = fullPath,
                        url = blobUri.ToString(),
                        lastModified = blobItem.Properties.LastModified,
                        size = blobItem.Properties.ContentLength,
                        // Add more properties as needed
                    });
                }

                var okResponse = req.CreateResponse(HttpStatusCode.OK);
                await okResponse.WriteAsJsonAsync(exports);
                return okResponse;
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Error listing exports for tenant {tenantId}: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteAsJsonAsync(new { error = $"Failed to list exports: {ex.Message}" });
                return errorResponse;
            }
        }
    }
}
