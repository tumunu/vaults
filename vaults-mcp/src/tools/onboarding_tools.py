"""Tenant onboarding tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def validate_azure_ad_permissions(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Validate Azure AD permissions for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        azure_ad_app_id = arguments["azure_ad_app_id"]
        azure_ad_app_secret = arguments["azure_ad_app_secret"]
        
        request_data = {
            "tenantId": tenant_id,
            "azureAdAppId": azure_ad_app_id,
            "azureAdAppSecret": azure_ad_app_secret
        }
        
        result = await client.post("/api/onboarding/validate-azure-ad", json_data=request_data)
        
        success = result.get('success', False)
        message = result.get('message', 'No message provided')
        
        response = f"**Azure AD Permissions Validation**\n\n"
        response += f"- **Status:** {'✅ Success' if success else '❌ Failed'}\n"
        response += f"- **Tenant ID:** {tenant_id}\n"
        response += f"- **App ID:** {azure_ad_app_id}\n"
        response += f"- **Message:** {message}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Validate Azure AD permissions failed: {e}")
        return [TextContent(type="text", text=f"Validate Azure AD permissions failed: {str(e)}")]


async def test_storage_connection(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Test Azure Storage connection for exports."""
    try:
        storage_account = arguments["azure_storage_account_name"]
        container_name = arguments["azure_storage_container_name"]
        sas_token = arguments["azure_storage_sas_token"]
        
        request_data = {
            "azureStorageAccountName": storage_account,
            "azureStorageContainerName": container_name,
            "azureStorageSasToken": sas_token
        }
        
        result = await client.post("/api/onboarding/test-storage-connection", json_data=request_data)
        
        success = result.get('success', False)
        message = result.get('message', 'No message provided')
        
        response = f"**Azure Storage Connection Test**\n\n"
        response += f"- **Status:** {'✅ Success' if success else '❌ Failed'}\n"
        response += f"- **Storage Account:** {storage_account}\n"
        response += f"- **Container:** {container_name}\n"
        response += f"- **Message:** {message}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Test storage connection failed: {e}")
        return [TextContent(type="text", text=f"Test storage connection failed: {str(e)}")]


async def complete_onboarding(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Complete the onboarding process for a tenant."""
    try:
        request_data = {
            "tenantId": arguments["tenant_id"],
            "azureAdAppId": arguments["azure_ad_app_id"],
            "azureStorageAccountName": arguments["azure_storage_account_name"],
            "azureStorageContainerName": arguments["azure_storage_container_name"],
            "retentionPolicy": arguments.get("retention_policy", "90days"),
            "customRetentionDays": arguments.get("custom_retention_days", 90),
            "exportSchedule": arguments.get("export_schedule", "daily"),
            "exportTime": arguments.get("export_time", "02:00")
        }
        
        result = await client.post("/api/onboarding/complete", json_data=request_data)
        
        success = result.get('success', False)
        message = result.get('message', 'No message provided')
        
        response = f"**Onboarding Completion**\n\n"
        response += f"- **Status:** {'✅ Success' if success else '❌ Failed'}\n"
        response += f"- **Tenant ID:** {arguments['tenant_id']}\n"
        response += f"- **Message:** {message}\n\n"
        
        response += "**Configuration Applied:**\n"
        response += f"- Azure AD App ID: {arguments['azure_ad_app_id']}\n"
        response += f"- Storage Account: {arguments['azure_storage_account_name']}\n"
        response += f"- Container: {arguments['azure_storage_container_name']}\n"
        response += f"- Retention Policy: {request_data['retentionPolicy']}\n"
        response += f"- Export Schedule: {request_data['exportSchedule']} at {request_data['exportTime']}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Complete onboarding failed: {e}")
        return [TextContent(type="text", text=f"Complete onboarding failed: {str(e)}")]


async def send_onboarding_email(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Send onboarding email to tenant admin."""
    try:
        tenant_id = arguments["tenant_id"]
        admin_email = arguments["admin_email"]
        
        request_data = {
            "tenantId": tenant_id,
            "adminEmail": admin_email
        }
        
        result = await client.post("/api/onboarding/send-email", json_data=request_data)
        
        success = result.get('success', False)
        message = result.get('message', 'No message provided')
        
        response = f"**Onboarding Email**\n\n"
        response += f"- **Status:** {'✅ Sent' if success else '❌ Failed'}\n"
        response += f"- **Tenant ID:** {tenant_id}\n"
        response += f"- **Admin Email:** {admin_email}\n"
        response += f"- **Message:** {message}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Send onboarding email failed: {e}")
        return [TextContent(type="text", text=f"Send onboarding email failed: {str(e)}")]


async def invite_user(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Invite a user via HTTP endpoint."""
    try:
        tenant_id = arguments["tenant_id"]
        admin_email = arguments["admin_email"]
        redirect_url = arguments.get("redirect_url", "https://myapplications.microsoft.com")
        invited_by = arguments.get("invited_by", "System")
        
        request_data = {
            "tenantId": tenant_id,
            "adminEmail": admin_email,
            "redirectUrl": redirect_url,
            "invitedBy": invited_by
        }
        
        result = await client.post("/api/invite/user", json_data=request_data, requires_auth=True)
        
        success = result.get('success', False)
        state = result.get('state', 'Unknown')
        invite_id = result.get('inviteId')
        user_id = result.get('userId')
        error = result.get('error')
        
        response = f"**User Invitation**\n\n"
        response += f"- **Status:** {'✅ Success' if success else '❌ Failed'}\n"
        response += f"- **State:** {state}\n"
        response += f"- **Tenant ID:** {tenant_id}\n"
        response += f"- **Admin Email:** {admin_email}\n"
        response += f"- **Redirect URL:** {redirect_url}\n"
        response += f"- **Invited By:** {invited_by}\n"
        
        if invite_id:
            response += f"- **Invitation ID:** {invite_id}\n"
        
        if user_id:
            response += f"- **User ID:** {user_id}\n"
        
        if error:
            response += f"- **Error:** {error}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Invite user failed: {e}")
        return [TextContent(type="text", text=f"Invite user failed: {str(e)}")]


async def resend_invitation(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Resend an invitation to a user."""
    try:
        tenant_id = arguments["tenant_id"]
        redirect_url = arguments.get("redirect_url", "https://myapplications.microsoft.com")
        requested_by = arguments.get("requested_by", "Admin")
        
        request_data = {
            "tenantId": tenant_id,
            "redirectUrl": redirect_url,
            "requestedBy": requested_by
        }
        
        result = await client.post("/api/invite/resend", json_data=request_data, requires_auth=True)
        
        success = result.get('success', False)
        state = result.get('state', 'Unknown')
        message = result.get('message', 'No message provided')
        retry_count = result.get('retryCount', 0)
        max_retries = result.get('maxRetries', 5)
        
        response = f"**Resend Invitation**\n\n"
        response += f"- **Status:** {'✅ Success' if success else '❌ Failed'}\n"
        response += f"- **State:** {state}\n"
        response += f"- **Tenant ID:** {tenant_id}\n"
        response += f"- **Redirect URL:** {redirect_url}\n"
        response += f"- **Requested By:** {requested_by}\n"
        response += f"- **Retry Count:** {retry_count}/{max_retries}\n"
        response += f"- **Message:** {message}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Resend invitation failed: {e}")
        return [TextContent(type="text", text=f"Resend invitation failed: {str(e)}")]