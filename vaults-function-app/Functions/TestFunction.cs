using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Threading.Tasks;

namespace VaultsFunctions.Functions
{
    public class TestFunction
    {
        private readonly ILogger<TestFunction> _logger;

        public TestFunction(ILogger<TestFunction> logger)
        {
            _logger = logger;
        }

        [Function("TestMinimal")]
        public async Task<HttpResponseData> TestMinimal(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "test/minimal")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("TestMinimal function started - no dependencies");
            
            try
            {
                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                
                await response.WriteStringAsync("{\"status\":\"ok\",\"message\":\"minimal test works\"}");
                
                _logger.LogInformation("TestMinimal function completed successfully");
                return response;
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "TestMinimal function failed: {Message}", ex.Message);
                throw;
            }
        }
    }
}