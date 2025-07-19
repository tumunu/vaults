"""Conversation and search tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def get_conversation(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get a specific conversation by ID."""
    try:
        tenant_id = arguments["tenant_id"]
        conversation_id = arguments["conversation_id"]
        
        params = {
            "tenantId": tenant_id,
            "conversationId": conversation_id
        }
        
        result = await client.get("/api/conversations", params=params)
        
        response = f"**Conversation Details**\n\n"
        response += f"- **ID:** {result.get('id', 'N/A')}\n"
        response += f"- **Tenant ID:** {result.get('tenantId', 'N/A')}\n"
        response += f"- **User ID:** {result.get('userId', 'N/A')}\n"
        response += f"- **Title:** {result.get('title', 'Untitled')}\n"
        response += f"- **Last Activity:** {result.get('lastActivity', 'Unknown')}\n\n"
        
        content = result.get('content', '')
        if content:
            response += f"**Content:**\n```\n{content}\n```"
        else:
            response += "**Content:** No content available"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get conversation failed: {e}")
        return [TextContent(type="text", text=f"Get conversation failed: {str(e)}")]


async def process_ingestion(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Process data ingestion for a tenant."""
    try:
        tenant_id = arguments.get("tenant_id", "default-tenant")
        params = {"tenantId": tenant_id}
        
        result = await client.get("/api/ingestion", params=params)
        
        success = result.get('Success', False)
        response = f"**Ingestion Process Results**\n\n"
        response += f"- **Status:** {'Success' if success else 'Failed'}\n"
        response += f"- **Tenant ID:** {result.get('TenantId', tenant_id)}\n"
        response += f"- **Users Processed:** {result.get('UsersProcessed', 0)}\n"
        response += f"- **Interactions Processed:** {result.get('InteractionsProcessed', 0)}\n"
        response += f"- **Last Sync Time:** {result.get('LastSyncTime', 'Unknown')}\n"
        response += f"- **Processed At:** {result.get('ProcessedAt', 'Unknown')}\n"
        
        failure_msg = result.get('LastFailureMessage')
        if failure_msg:
            response += f"- **Last Failure:** {failure_msg}\n"
        else:
            response += "- **Last Failure:** None\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Process ingestion failed: {e}")
        return [TextContent(type="text", text=f"Process ingestion failed: {str(e)}")]


async def search_conversations(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Search conversations with various filters."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        # Add optional parameters
        if "type" in arguments:
            params["type"] = arguments["type"]
        if "user" in arguments:
            params["user"] = arguments["user"]
        if "start_date" in arguments:
            params["startDate"] = arguments["start_date"]
        if "end_date" in arguments:
            params["endDate"] = arguments["end_date"]
        if "keyword" in arguments:
            params["keyword"] = arguments["keyword"]
        if "page" in arguments:
            params["page"] = arguments["page"]
        else:
            params["page"] = 1
        if "page_size" in arguments:
            params["pageSize"] = arguments["page_size"]
        else:
            params["pageSize"] = 10
        
        result = await client.get("/api/searchfunction", params=params)
        
        results = result.get('results', [])
        page = result.get('page', 1)
        page_size = result.get('pageSize', 10)
        total_count = result.get('totalCount', 0)
        total_pages = result.get('totalPages', 0)
        
        if not results:
            return [TextContent(type="text", text=f"No conversations found for tenant: {tenant_id} with the specified criteria.")]
        
        response = f"**Search Results for Tenant: {tenant_id}**\n\n"
        response += f"**Page {page} of {total_pages} (Total: {total_count} conversations)**\n\n"
        
        # Add search criteria summary
        criteria = []
        if "type" in arguments:
            criteria.append(f"Type: {arguments['type']}")
        if "user" in arguments:
            criteria.append(f"User: {arguments['user']}")
        if "keyword" in arguments:
            criteria.append(f"Keyword: {arguments['keyword']}")
        if "start_date" in arguments:
            criteria.append(f"From: {arguments['start_date']}")
        if "end_date" in arguments:
            criteria.append(f"To: {arguments['end_date']}")
        
        if criteria:
            response += f"**Search Criteria:** {', '.join(criteria)}\n\n"
        
        for i, conversation in enumerate(results, 1):
            response += f"**Result {i}: {conversation.get('title', 'Untitled')}**\n"
            response += f"- ID: {conversation.get('id', 'N/A')}\n"
            response += f"- Tenant ID: {conversation.get('tenantId', 'N/A')}\n"
            response += f"- Last Activity: {conversation.get('lastActivity', 'Unknown')}\n"
            
            preview = conversation.get('preview', '')
            if preview:
                # Truncate preview if too long
                if len(preview) > 200:
                    preview = preview[:200] + "..."
                response += f"- Preview: {preview}\n"
            
            response += "\n"
        
        # Add pagination info
        if total_pages > 1:
            response += f"**Pagination:** Showing page {page} of {total_pages}. "
            if page < total_pages:
                response += f"Use page={page + 1} to see more results."
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Search conversations failed: {e}")
        return [TextContent(type="text", text=f"Search conversations failed: {str(e)}")]