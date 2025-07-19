"""Usage metrics and analytics tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def get_usage_metrics(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get detailed usage metrics for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        # Add optional date range parameters
        if "start_date" in arguments:
            params["startDate"] = arguments["start_date"]
        if "end_date" in arguments:
            params["endDate"] = arguments["end_date"]
        
        result = await client.get("/api/metrics/usage", params=params, requires_auth=True)
        
        response = f"**Usage Metrics for Tenant: {result.get('tenantId', tenant_id)}**\n\n"
        
        # Period information
        period = result.get('period', {})
        if period:
            response += f"**Period:** {period.get('start', 'Unknown')} to {period.get('end', 'Unknown')}\n\n"
        
        # Seats information
        seats = result.get('seats', {})
        if seats:
            response += "**Seat Utilization:**\n"
            response += f"- Active Seats: {seats.get('active', 0)}\n"
            response += f"- Total Seats: {seats.get('total', 0)}\n"
            response += f"- Licensed Seats: {seats.get('licensed', 0)}\n"
            response += f"- Utilization Rate: {seats.get('utilizationRate', 0)}%\n\n"
        
        # Interactions information
        interactions = result.get('interactions', {})
        if interactions:
            response += "**Interaction Statistics:**\n"
            response += f"- Total Interactions: {interactions.get('total', 0):,}\n"
            response += f"- Daily Average: {interactions.get('dailyAverage', 0):.1f}\n"
            response += f"- Growth Rate: {interactions.get('growthRate', 0)}%\n\n"
        
        # Application usage
        apps = result.get('apps', {})
        if apps:
            response += "**Application Usage:**\n"
            total_app_usage = sum(apps.values())
            for app, count in sorted(apps.items(), key=lambda x: x[1], reverse=True):
                percentage = (count / total_app_usage * 100) if total_app_usage > 0 else 0
                response += f"- {app}: {count:,} interactions ({percentage:.1f}%)\n"
            response += "\n"
        
        # Conversations information
        conversations = result.get('conversations', {})
        if conversations:
            response += "**Conversation Statistics:**\n"
            response += f"- Total Threads: {conversations.get('threads', 0):,}\n"
            response += f"- Average Length: {conversations.get('averageLength', 0):.1f} messages\n"
            response += f"- Total Messages: {conversations.get('totalMessages', 0):,}\n\n"
        
        # Activity patterns
        activity = result.get('activity', {})
        if activity:
            response += "**Activity Patterns:**\n"
            
            daily_activity = activity.get('dailyActivity', {})
            if daily_activity:
                response += "- Recent Daily Activity:\n"
                for date, count in sorted(daily_activity.items())[-7:]:  # Last 7 days
                    response += f"  - {date}: {count} users\n"
            
            peak_hours = activity.get('peakHours', {})
            if peak_hours:
                response += "- Peak Hours (users active):\n"
                for hour, count in sorted(peak_hours.items(), key=lambda x: int(x[0])):
                    response += f"  - {hour}:00: {count} users\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get usage metrics failed: {e}")
        return [TextContent(type="text", text=f"Get usage metrics failed: {str(e)}")]


async def get_tenant_overview(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get high-level tenant overview and metrics."""
    try:
        tenant_id = arguments["tenant_id"]
        params = {"tenantId": tenant_id}
        
        result = await client.get("/api/metrics/overview", params=params, requires_auth=True)
        
        response = f"**Tenant Overview: {result.get('tenantId', tenant_id)}**\n\n"
        
        # Last updated info
        last_updated = result.get('lastUpdated', 'Unknown')
        response += f"**Last Updated:** {last_updated}\n\n"
        
        # Health Score
        health_score = result.get('healthScore', 0)
        if health_score >= 80:
            health_emoji = "🟢"
        elif health_score >= 60:
            health_emoji = "🟡"
        else:
            health_emoji = "🔴"
        
        response += f"**Health Score:** {health_emoji} {health_score}/100\n\n"
        
        # Current period metrics
        current = result.get('current', {})
        if current:
            response += "**Current Period Summary:**\n"
            
            period = current.get('period', {})
            if period:
                response += f"- Period: {period.get('start', 'Unknown')} to {period.get('end', 'Unknown')}\n"
            
            response += f"- Active Users: {current.get('activeUsers', 0)}\n"
            response += f"- Total Interactions: {current.get('totalInteractions', 0):,}\n"
            response += f"- Daily Average: {current.get('averageDaily', 0):.1f}\n\n"
            
            # Top applications
            top_apps = current.get('topApps', {})
            if top_apps:
                response += "**Top Applications:**\n"
                for app, count in sorted(top_apps.items(), key=lambda x: x[1], reverse=True)[:5]:
                    response += f"- {app}: {count:,} interactions\n"
                response += "\n"
        
        # Trends
        trends = result.get('trends', {})
        if trends:
            response += "**Growth Trends:**\n"
            
            user_growth = trends.get('userGrowth', 0)
            response += f"- User Growth: {'+' if user_growth >= 0 else ''}{user_growth}%\n"
            
            interaction_growth = trends.get('interactionGrowth', 0)
            response += f"- Interaction Growth: {'+' if interaction_growth >= 0 else ''}{interaction_growth}%\n"
            
            conversation_growth = trends.get('conversationGrowth', 0)
            response += f"- Conversation Growth: {'+' if conversation_growth >= 0 else ''}{conversation_growth}%\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get tenant overview failed: {e}")
        return [TextContent(type="text", text=f"Get tenant overview failed: {str(e)}")]