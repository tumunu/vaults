using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Logging;
using VaultsFunctions.Core.Models;
using VaultsFunctions.Core.Services;

namespace VaultsFunctions.Core.Services
{
    public interface IGraphInvitationService
    {
        Task<InvitationResult> InviteAsync(string adminEmail, string redirectUrl, CancellationToken cancellationToken = default);
        Task<Microsoft.Graph.Models.User> CheckUserExistsAsync(string email, CancellationToken cancellationToken = default);
    }

    public class GraphInvitationService : IGraphInvitationService
    {
        private readonly GraphServiceClient _graphClient;
        private readonly IDomainValidator _domainValidator;
        private readonly ILogger<GraphInvitationService> _logger;

        public GraphInvitationService(
            GraphServiceClient graphClient,
            IDomainValidator domainValidator,
            ILogger<GraphInvitationService> logger)
        {
            _graphClient = graphClient;
            _domainValidator = domainValidator;
            _logger = logger;
        }

        public async Task<InvitationResult> InviteAsync(string adminEmail, string redirectUrl, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(adminEmail) || !adminEmail.Contains('@'))
            {
                _logger.LogWarning("Invalid email address provided: {Email}", adminEmail);
                return InvitationResult.Failed("INVALID_EMAIL");
            }

            // Security: Domain validation
            if (!_domainValidator.IsTrusted(adminEmail))
            {
                _logger.LogWarning("Untrusted domain for email: {Email}", adminEmail);
                return InvitationResult.Failed("UNTRUSTED_DOMAIN");
            }

            try
            {
                // Idempotency: Check if user already exists
                var existingUser = await CheckUserExistsAsync(adminEmail, cancellationToken);
                if (existingUser != null)
                {
                    _logger.LogInformation("User already exists in tenant: {Email}, UserId: {UserId}", adminEmail, existingUser.Id);
                    return InvitationResult.Skipped(existingUser.Id);
                }

                // Send B2B invitation
                var invitation = new Invitation
                {
                    InvitedUserEmailAddress = adminEmail,
                    InviteRedirectUrl = !string.IsNullOrEmpty(redirectUrl) ? redirectUrl : "https://myapplications.microsoft.com",
                    SendInvitationMessage = true,
                    InvitedUserMessageInfo = new InvitedUserMessageInfo
                    {
                        MessageLanguage = "en-US",
                        CustomizedMessageBody = "You have been invited to access Copilot Vault monitoring for your organization. Click the link to get started."
                    }
                };

                var response = await _graphClient.Invitations.PostAsync(invitation, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Invitation sent successfully: {Email}, InviteId: {InviteId}", adminEmail, response.Id);
                return InvitationResult.Sent(response.Id);
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.Conflict)
            {
                _logger.LogInformation("User already invited or exists: {Email}", adminEmail);
                return InvitationResult.Skipped("409_CONFLICT");
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.UnprocessableEntity)
            {
                _logger.LogWarning("Invalid invitation request: {Email}, Error: {Error}", adminEmail, ex.Message);
                return InvitationResult.Failed($"INVALID_REQUEST: {ex.Message}");
            }
            catch (ServiceException ex) when (ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("Graph API throttling: {Email}, RetryAfter: {RetryAfter}", adminEmail, ex.ResponseHeaders?.RetryAfter);
                return InvitationResult.Failed($"THROTTLED: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending invitation: {Email}", adminEmail);
                return InvitationResult.Failed($"UNEXPECTED_ERROR: {ex.Message}");
            }
        }

        public async Task<Microsoft.Graph.Models.User> CheckUserExistsAsync(string email, CancellationToken cancellationToken = default)
        {
            try
            {
                var users = await _graphClient.Users.GetAsync(requestConfiguration => 
                {
                    requestConfiguration.QueryParameters.Filter = $"mail eq '{email}' or userPrincipalName eq '{email}'";
                    requestConfiguration.QueryParameters.Select = new[] { "id", "mail", "userPrincipalName", "externalUserState" };
                    requestConfiguration.QueryParameters.Top = 1;
                }, cancellationToken: cancellationToken);

                return users?.Value?.FirstOrDefault();
            }
            catch (ServiceException ex)
            {
                _logger.LogWarning(ex, "Error checking user existence: {Email}", email);
                return null;
            }
        }
    }
}