"""Data export tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def list_exports(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """List available exports for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        # Add optional date filter
        if "date" in arguments:
            params["date"] = arguments["date"]
        
        result = await client.get("/api/listexportsfunction", params=params)
        
        # Result should be an array of export files
        if not isinstance(result, list):
            return [TextContent(type="text", text=f"Unexpected response format for exports list")]
        
        if not result:
            date_filter = f" for date {arguments['date']}" if "date" in arguments else ""
            return [TextContent(type="text", text=f"No exports found for tenant: {tenant_id}{date_filter}")]
        
        response = f"**Available Exports for Tenant: {tenant_id}**\n\n"
        
        if "date" in arguments:
            response += f"**Date Filter:** {arguments['date']}\n\n"
        
        response += f"**Total Export Files: {len(result)}**\n\n"
        
        # Group exports by date for better organization
        exports_by_date = {}
        for export in result:
            # Extract date from path or use "Unknown"
            path = export.get('path', '')
            date_part = "Unknown"
            if '/' in path:
                path_parts = path.split('/')
                for part in path_parts:
                    if len(part) == 10 and part.count('-') == 2:  # YYYY-MM-DD format
                        date_part = part
                        break
            
            if date_part not in exports_by_date:
                exports_by_date[date_part] = []
            exports_by_date[date_part].append(export)
        
        # Display exports grouped by date
        for date, exports in sorted(exports_by_date.items()):
            response += f"**Date: {date}** ({len(exports)} files)\n"
            
            for export in exports:
                name = export.get('name', 'Unknown')
                size = export.get('size', 0)
                last_modified = export.get('lastModified', 'Unknown')
                url = export.get('url', 'No URL')
                
                # Format file size
                if size > 1024 * 1024:
                    size_str = f"{size / (1024 * 1024):.1f} MB"
                elif size > 1024:
                    size_str = f"{size / 1024:.1f} KB"
                else:
                    size_str = f"{size} bytes"
                
                response += f"- **{name}**\n"
                response += f"  - Size: {size_str}\n"
                response += f"  - Modified: {last_modified}\n"
                response += f"  - URL: {url}\n"
            
            response += "\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"List exports failed: {e}")
        return [TextContent(type="text", text=f"List exports failed: {str(e)}")]