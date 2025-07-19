"""Microsoft Graph Copilot integration tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def get_copilot_root(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get the root Copilot API information."""
    try:
        result = await client.get("/api/copilot", requires_auth=True)
        
        response = f"**Microsoft 365 Copilot API Root**\n\n"
        response += f"- **ID:** {result.get('id', 'N/A')}\n"
        response += f"- **Display Name:** {result.get('displayName', 'N/A')}\n"
        response += f"- **Description:** {result.get('description', 'N/A')}\n\n"
        
        response += "**Available Endpoints:**\n"
        
        users_href = result.get('users', {}).get('href', 'N/A')
        response += f"- Users: {users_href}\n"
        
        interaction_href = result.get('interactionHistory', {}).get('href', 'N/A')
        response += f"- Interaction History: {interaction_href}\n"
        
        search_href = result.get('searchFunction', {}).get('href', 'N/A')
        response += f"- Search Function: {search_href}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get Copilot root failed: {e}")
        return [TextContent(type="text", text=f"Get Copilot root failed: {str(e)}")]


async def get_copilot_users(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get Copilot users for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        result = await client.get("/api/vaults/users", params=params, requires_auth=True)
        
        users = result.get('users', [])
        
        if not users:
            return [TextContent(type="text", text=f"No Copilot users found for tenant: {tenant_id}")]
        
        response = f"**Copilot Users for Tenant: {tenant_id}**\n\n"
        response += f"**Total Users: {len(users)}**\n\n"
        
        for i, user in enumerate(users, 1):
            response += f"**User {i}: {user.get('displayName', 'Unknown')}**\n"
            response += f"- ID: {user.get('id', 'N/A')}\n"
            response += f"- Email: {user.get('email', 'N/A')}\n"
            response += f"- Copilot Enabled: {'Yes' if user.get('copilotEnabled', False) else 'No'}\n"
            response += f"- Last Activity: {user.get('lastActivity', 'Never')}\n\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get Copilot users failed: {e}")
        return [TextContent(type="text", text=f"Get Copilot users failed: {str(e)}")]


async def get_interaction_history(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get Copilot interaction history for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        # Add optional OData parameters
        if "top" in arguments:
            params["$top"] = arguments["top"]
        if "filter" in arguments:
            params["$filter"] = arguments["filter"]
        
        result = await client.get("/api/vaults/interactionHistory", params=params)
        
        interactions = result.get('value', [])
        next_link = result.get('@odata.nextLink')
        
        if not interactions:
            return [TextContent(type="text", text=f"No interaction history found for tenant: {tenant_id}")]
        
        response = f"**Copilot Interaction History for Tenant: {tenant_id}**\n\n"
        response += f"**Showing {len(interactions)} interactions**\n\n"
        
        for i, interaction in enumerate(interactions, 1):
            response += f"**Interaction {i}**\n"
            response += f"- ID: {interaction.get('id', 'N/A')}\n"
            response += f"- User ID: {interaction.get('userId', 'N/A')}\n"
            response += f"- Session ID: {interaction.get('sessionId', 'N/A')}\n"
            response += f"- Created: {interaction.get('createdDateTime', 'Unknown')}\n"
            
            user_prompt = interaction.get('userPrompt', '')
            if user_prompt:
                # Truncate long prompts
                if len(user_prompt) > 150:
                    user_prompt = user_prompt[:150] + "..."
                response += f"- User Prompt: {user_prompt}\n"
            
            ai_response = interaction.get('aiResponse', '')
            if ai_response:
                # Truncate long responses
                if len(ai_response) > 150:
                    ai_response = ai_response[:150] + "..."
                response += f"- AI Response: {ai_response}\n"
            
            response += "\n"
        
        if next_link:
            response += f"**More Results Available:** {next_link}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get interaction history failed: {e}")
        return [TextContent(type="text", text=f"Get interaction history failed: {str(e)}")]


async def copilot_retrieve_content(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Retrieve content using Copilot search."""
    try:
        query = arguments["query"]
        filters = arguments.get("filters", {})
        
        request_data = {
            "query": query,
            "filters": filters
        }
        
        result = await client.post("/api/vaults/retrieve", json_data=request_data)
        
        results = result.get('results', [])
        
        if not results:
            return [TextContent(type="text", text=f"No content found for query: '{query}'")]
        
        response = f"**Copilot Content Retrieval Results**\n\n"
        response += f"**Query:** {query}\n"
        
        if filters:
            filter_desc = []
            for key, value in filters.items():
                filter_desc.append(f"{key}: {value}")
            response += f"**Filters:** {', '.join(filter_desc)}\n"
        
        response += f"**Results Found:** {len(results)}\n\n"
        
        for i, content in enumerate(results, 1):
            response += f"**Result {i}: {content.get('title', 'Untitled')}**\n"
            response += f"- ID: {content.get('id', 'N/A')}\n"
            response += f"- URL: {content.get('url', 'N/A')}\n"
            response += f"- Last Modified: {content.get('lastModified', 'Unknown')}\n"
            
            content_snippet = content.get('content', '')
            if content_snippet:
                # Truncate long content
                if len(content_snippet) > 200:
                    content_snippet = content_snippet[:200] + "..."
                response += f"- Content: {content_snippet}\n"
            
            response += "\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Copilot retrieve content failed: {e}")
        return [TextContent(type="text", text=f"Copilot retrieve content failed: {str(e)}")]


async def get_copilot_usage_summary(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get tenant-level Copilot usage summary from Microsoft Graph Reports API."""
    try:
        period = arguments.get("period", "D7")
        params = {"period": period}
        
        result = await client.get("/api/vaults/usage/summary", params=params, requires_auth=True)
        
        data = result.get('value', [])
        
        if not data:
            return [TextContent(type="text", text=f"No usage summary data available for period: {period}")]
        
        # Assuming single record for the tenant
        summary = data[0] if data else {}
        
        response = f"**Copilot Usage Summary (Period: {period})**\n\n"
        response += f"- **Report Date:** {summary.get('reportDate', 'Unknown')}\n"
        response += f"- **Report Refresh Date:** {summary.get('reportRefreshDate', 'Unknown')}\n"
        response += f"- **Report Period:** {summary.get('reportPeriod', period)}\n\n"
        
        response += "**Usage Metrics:**\n"
        response += f"- **Copilot Enabled Users:** {summary.get('copilotEnabledUsers', 0)}\n"
        response += f"- **Copilot Active Users:** {summary.get('copilotActiveUsers', 0)}\n"
        response += f"- **Utilization Rate:** {summary.get('utilizationRate', 0)}%\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get Copilot usage summary failed: {e}")
        return [TextContent(type="text", text=f"Get Copilot usage summary failed: {str(e)}")]


async def get_copilot_user_count(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get Copilot user count summary from Microsoft Graph Reports API."""
    try:
        period = arguments.get("period", "D7")
        params = {"period": period}
        
        result = await client.get("/api/vaults/users/count", params=params, requires_auth=True)
        
        data = result.get('value', [])
        
        if not data:
            return [TextContent(type="text", text=f"No user count data available for period: {period}")]
        
        # Assuming single record for the tenant
        count_data = data[0] if data else {}
        
        response = f"**Copilot User Count Summary (Period: {period})**\n\n"
        response += f"- **Report Date:** {count_data.get('reportDate', 'Unknown')}\n"
        response += f"- **Report Refresh Date:** {count_data.get('reportRefreshDate', 'Unknown')}\n"
        response += f"- **Report Period:** {count_data.get('reportPeriod', period)}\n\n"
        
        response += "**User Counts:**\n"
        response += f"- **Total Users:** {count_data.get('totalUsers', 0)}\n"
        response += f"- **Enabled Users:** {count_data.get('enabledUsers', 0)}\n"
        response += f"- **Active Users:** {count_data.get('activeUsers', 0)}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get Copilot user count failed: {e}")
        return [TextContent(type="text", text=f"Get Copilot user count failed: {str(e)}")]


async def get_security_alerts(client: VaultsClient) -> Sequence[TextContent]:
    """Get security alerts from Microsoft Graph Security API."""
    try:
        result = await client.get("/api/v1/copilot/security/alerts", requires_auth=True)
        
        alerts = result.get('value', [])
        
        if not alerts:
            return [TextContent(type="text", text="No security alerts found.")]
        
        response = f"**Security Alerts ({len(alerts)} alerts)**\n\n"
        
        for i, alert in enumerate(alerts, 1):
            response += f"**Alert {i}: {alert.get('displayName', 'Unnamed Alert')}**\n"
            response += f"- **ID:** {alert.get('id', 'N/A')}\n"
            response += f"- **Category:** {alert.get('category', 'Unknown')}\n"
            response += f"- **Severity:** {alert.get('severity', 'Unknown').upper()}\n"
            response += f"- **Status:** {alert.get('status', 'Unknown')}\n"
            response += f"- **Created:** {alert.get('createdDateTime', 'Unknown')}\n"
            response += f"- **Assigned To:** {alert.get('assignedTo', 'Unassigned')}\n\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get security alerts failed: {e}")
        return [TextContent(type="text", text=f"Get security alerts failed: {str(e)}")]


async def get_high_risk_users(client: VaultsClient) -> Sequence[TextContent]:
    """Get high-risk users from Microsoft Graph Identity Protection API."""
    try:
        result = await client.get("/api/v1/copilot/security/risky-users", requires_auth=True)
        
        users = result.get('value', [])
        
        if not users:
            return [TextContent(type="text", text="No high-risk users found.")]
        
        response = f"**High-Risk Users ({len(users)} users)**\n\n"
        
        for i, user in enumerate(users, 1):
            response += f"**User {i}: {user.get('userDisplayName', 'Unknown')}**\n"
            response += f"- **ID:** {user.get('id', 'N/A')}\n"
            response += f"- **UPN:** {user.get('userPrincipalName', 'N/A')}\n"
            response += f"- **Risk Level:** {user.get('riskLevel', 'Unknown').upper()}\n"
            response += f"- **Risk State:** {user.get('riskState', 'Unknown')}\n"
            response += f"- **Risk Detail:** {user.get('riskDetail', 'Unknown')}\n"
            response += f"- **Last Updated:** {user.get('riskLastUpdatedDateTime', 'Unknown')}\n\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get high-risk users failed: {e}")
        return [TextContent(type="text", text=f"Get high-risk users failed: {str(e)}")]


async def get_policy_violations(client: VaultsClient) -> Sequence[TextContent]:
    """Get compliance policy information from Microsoft Graph Compliance API."""
    try:
        result = await client.get("/api/v1/copilot/compliance/violations", requires_auth=True)
        
        violations = result.get('value', [])
        
        if not violations:
            return [TextContent(type="text", text="No policy violations found.")]
        
        response = f"**Policy Violations ({len(violations)} violations)**\n\n"
        
        for i, violation in enumerate(violations, 1):
            response += f"**Violation {i}: {violation.get('displayName', 'Unknown Violation')}**\n"
            response += f"- **ID:** {violation.get('id', 'N/A')}\n"
            response += f"- **Partner Tenant ID:** {violation.get('partnerTenantId', 'N/A')}\n"
            response += f"- **Partner State:** {violation.get('partnerState', 'Unknown')}\n"
            response += f"- **Last Heartbeat:** {violation.get('lastHeartbeatDateTime', 'Unknown')}\n\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get policy violations failed: {e}")
        return [TextContent(type="text", text=f"Get policy violations failed: {str(e)}")]