using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker.Http;

namespace VaultsFunctions.Core.Models
{
    public static class RequestBodyHelper
    {
        /// <summary>
        /// Reads the request body as a string and enables re-use by replacing the body stream.
        /// </summary>
        /// <param name="req">The HTTP request</param>
        /// <returns>Body string</returns>
        public static async Task<string> ReadBodyAsStringAndEnableReuse(HttpRequestData req)
        {
            using var reader = new StreamReader(req.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            string body = await reader.ReadToEndAsync();
            req.Body.Position = 0;
            return body;
        }
    }
}
