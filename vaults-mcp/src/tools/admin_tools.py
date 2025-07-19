"""Administrative tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def get_admin_stats(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get administrative statistics for a tenant."""
    try:
        tenant_id = arguments.get("tenant_id", "default-tenant")
        params = {"tenantId": tenant_id}
        
        result = await client.get("/api/stats/adminstats", params=params)
        
        # Format the admin stats response
        response = f"**Administrative Statistics for Tenant: {result.get('TenantId', tenant_id)}**\n\n"
        
        response += "**User Statistics:**\n"
        response += f"- Total Users: {result.get('TotalUsers', 0)}\n"
        response += f"- Active Users: {result.get('ActiveUsers', 0)}\n\n"
        
        response += "**Policy Statistics:**\n"
        response += f"- Total Policies: {result.get('TotalPolicies', 0)}\n"
        response += f"- Active Policies: {result.get('ActivePolicies', 0)}\n"
        response += f"- High Risk Policies: {result.get('HighRiskPolicies', 0)}\n\n"
        
        response += "**Interaction Statistics:**\n"
        response += f"- Total Interactions: {result.get('TotalInteractions', 0)}\n"
        response += f"- Interactions with PII: {result.get('InteractionsWithPii', 0)}\n"
        response += f"- Policy Violations: {result.get('PolicyViolations', 0)}\n\n"
        
        response += "**System Information:**\n"
        response += f"- Last Sync Time: {result.get('LastSyncTime', 'Unknown')}\n"
        response += f"- Processed At: {result.get('ProcessedAt', 'Unknown')}\n"
        
        failure_msg = result.get('LastFailureMessage')
        if failure_msg:
            response += f"- Last Failure: {failure_msg}\n"
        else:
            response += "- Last Failure: None\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get admin stats failed: {e}")
        return [TextContent(type="text", text=f"Get admin stats failed: {str(e)}")]


async def get_audit_policies(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get audit policies for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        result = await client.get("/api/policies", params=params)
        
        policies = result.get("policies", [])
        
        if not policies:
            return [TextContent(type="text", text=f"No audit policies found for tenant: {tenant_id}")]
        
        response = f"**Audit Policies for Tenant: {tenant_id}**\n\n"
        response += f"**Total Policies: {len(policies)}**\n\n"
        
        for i, policy in enumerate(policies, 1):
            response += f"**Policy {i}: {policy.get('name', 'Unnamed')}**\n"
            response += f"- ID: {policy.get('id', 'N/A')}\n"
            response += f"- Description: {policy.get('description', 'No description')}\n"
            response += f"- Risk Level: {policy.get('riskLevel', 'Unknown')}\n"
            response += f"- Action: {policy.get('action', 'Unknown')}\n"
            response += f"- Sensitivity: {policy.get('sensitivity', 0)}/10\n"
            response += f"- Enabled: {'Yes' if policy.get('isEnabled', False) else 'No'}\n"
            response += f"- Trigger Count: {policy.get('triggerCount', 0)}\n"
            
            detection_rules = policy.get('detectionRules', [])
            if detection_rules:
                response += f"- Detection Rules: {', '.join(detection_rules)}\n"
            
            categories = policy.get('categories', [])
            if categories:
                response += f"- Categories: {', '.join(categories)}\n"
            
            response += f"- Created: {policy.get('createdAt', 'Unknown')}\n"
            response += f"- Updated: {policy.get('updatedAt', 'Unknown')}\n\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get audit policies failed: {e}")
        return [TextContent(type="text", text=f"Get audit policies failed: {str(e)}")]


async def update_audit_policies(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Update audit policies for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        policies = arguments["policies"]
        
        request_data = {
            "tenantId": tenant_id,
            "policies": policies
        }
        
        result = await client.post("/api/policies/config", json_data=request_data)
        
        response = f"**Policy Update Results**\n\n"
        response += f"- Message: {result.get('message', 'Update completed')}\n"
        response += f"- Updated Count: {result.get('updatedCount', 0)}\n\n"
        
        updated_policies = result.get('policies', [])
        if updated_policies:
            response += "**Updated Policies:**\n"
            for policy in updated_policies:
                response += f"- {policy.get('name', 'Unnamed')} (ID: {policy.get('id', 'N/A')})\n"
                response += f"  Updated at: {policy.get('updatedAt', 'Unknown')}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Update audit policies failed: {e}")
        return [TextContent(type="text", text=f"Update audit policies failed: {str(e)}")]


async def delete_audit_policy(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Delete a specific audit policy."""
    try:
        policy_id = arguments["policy_id"]
        tenant_id = arguments["tenant_id"]
        
        params = {"tenantId": tenant_id}
        path = f"/api/policies/{policy_id}"
        
        result = await client.delete(path, params=params)
        
        message = result.get('message', 'Policy deleted successfully')
        return [TextContent(type="text", text=f"**Policy Deletion Result**\n\n{message}")]
        
    except Exception as e:
        logger.error(f"Delete audit policy failed: {e}")
        return [TextContent(type="text", text=f"Delete audit policy failed: {str(e)}")]


async def list_tenant_users(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """List users in a tenant."""
    try:
        params = {}
        
        if "top" in arguments:
            params["top"] = min(arguments["top"], 200)  # Max 200 as per spec
        else:
            params["top"] = 50  # Default
        
        if "filter" in arguments:
            params["filter"] = arguments["filter"]
        
        if "user_type" in arguments:
            params["userType"] = arguments["user_type"]
        
        result = await client.get("/api/tenant/users", params=params, requires_auth=True)
        
        users = result.get("users", [])
        total_count = result.get("totalCount", len(users))
        has_more = result.get("hasMore", False)
        
        if not users:
            return [TextContent(type="text", text="No users found for the specified criteria.")]
        
        response = f"**Tenant Users ({len(users)} of {total_count} total)**\n\n"
        
        if has_more:
            response += "⚠️ *More users available - adjust your query parameters to see more*\n\n"
        
        for i, user in enumerate(users, 1):
            response += f"**User {i}: {user.get('displayName', 'Unknown')}**\n"
            response += f"- ID: {user.get('id', 'N/A')}\n"
            response += f"- Email: {user.get('email', 'N/A')}\n"
            response += f"- User Type: {user.get('userType', 'Unknown')}\n"
            response += f"- Account Enabled: {'Yes' if user.get('accountEnabled', False) else 'No'}\n"
            response += f"- Created: {user.get('createdDateTime', 'Unknown')}\n"
            response += f"- Last Sign In: {user.get('lastSignIn', 'Never')}\n"
            
            external_state = user.get('externalUserState')
            if external_state:
                response += f"- External User State: {external_state}\n"
            
            response += "\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"List tenant users failed: {e}")
        return [TextContent(type="text", text=f"List tenant users failed: {str(e)}")]


async def get_user_invitation_status(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get invitation status for a specific user."""
    try:
        user_id = arguments["user_id"]
        path = f"/api/tenant/users/{user_id}/invitation"
        
        result = await client.get(path, requires_auth=True)
        
        response = f"**User Invitation Status**\n\n"
        response += f"**User Information:**\n"
        response += f"- ID: {result.get('userId', user_id)}\n"
        response += f"- Display Name: {result.get('displayName', 'Unknown')}\n"
        response += f"- Email: {result.get('email', 'N/A')}\n"
        response += f"- User Type: {result.get('userType', 'Unknown')}\n"
        response += f"- Account Enabled: {'Yes' if result.get('accountEnabled', False) else 'No'}\n\n"
        
        response += f"**Invitation Status:**\n"
        response += f"- Is Invited: {'Yes' if result.get('isInvited', False) else 'No'}\n"
        response += f"- Invitation Accepted: {'Yes' if result.get('invitationAccepted', False) else 'No'}\n"
        
        external_state = result.get('externalUserState')
        if external_state:
            response += f"- External User State: {external_state}\n"
            
        state_change_date = result.get('externalUserStateChangeDateTime')
        if state_change_date:
            response += f"- State Changed: {state_change_date}\n"
        
        response += f"- Created: {result.get('createdDateTime', 'Unknown')}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get user invitation status failed: {e}")
        return [TextContent(type="text", text=f"Get user invitation status failed: {str(e)}")]