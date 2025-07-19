"""Health and monitoring tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def health_check(client: VaultsClient) -> Sequence[TextContent]:
    """Check the health status of the Vaults system."""
    try:
        result = await client.get("/api/health")
        
        # Format the health check response
        status = result.get("status", "unknown")
        message = result.get("message", "No message")
        timestamp = result.get("timestamp", "Unknown")
        
        response = f"**System Health Status: {status.upper()}**\n\n"
        response += f"**Timestamp:** {timestamp}\n"
        response += f"**Message:** {message}\n\n"
        
        if "components" in result:
            response += "**Component Health:**\n"
            for component, details in result["components"].items():
                comp_status = details.get("status", "unknown")
                comp_message = details.get("message", "No message")
                comp_duration = details.get("duration", 0)
                
                response += f"- **{component}:** {comp_status} ({comp_duration}ms) - {comp_message}\n"
        
        if "version" in result:
            response += f"\n**Version:** {result['version']}"
        
        if "environment" in result:
            response += f"\n**Environment:** {result['environment']}"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Health check failed: {e}")
        return [TextContent(type="text", text=f"Health check failed: {str(e)}")]


async def get_service_bus_health(client: VaultsClient) -> Sequence[TextContent]:
    """Get Service Bus health status and queue metrics."""
    try:
        result = await client.get("/api/admin/servicebus/health", requires_auth=True)
        
        # Format the Service Bus health response
        overall = result.get("overall", {})
        status = overall.get("status", "unknown")
        available = overall.get("available", False)
        monitoring = overall.get("monitoringEnabled", False)
        
        response = f"**Service Bus Health Status: {status.upper()}**\n\n"
        response += f"**Available:** {'Yes' if available else 'No'}\n"
        response += f"**Monitoring Enabled:** {'Yes' if monitoring else 'No'}\n\n"
        
        if "queues" in result:
            response += "**Queue Status:**\n"
            for queue_key, queue_data in result["queues"].items():
                queue_name = queue_data.get("name", queue_key)
                queue_status = queue_data.get("status", "unknown")
                queue_available = queue_data.get("available", False)
                
                response += f"- **{queue_name}:** {queue_status} (Available: {'Yes' if queue_available else 'No'})\n"
                
                if "metrics" in queue_data:
                    metrics = queue_data["metrics"]
                    response += f"  - Active Messages: {metrics.get('activeMessages', 0)}\n"
                    response += f"  - Dead Letter Messages: {metrics.get('deadLetterMessages', 0)}\n"
                    response += f"  - Total Messages: {metrics.get('totalMessages', 0)}\n"
                    response += f"  - Size: {metrics.get('sizeInBytes', 0)} bytes\n"
        
        if "features" in result:
            features = result["features"]
            response += "\n**Features:**\n"
            for feature, enabled in features.items():
                response += f"- {feature}: {'Enabled' if enabled else 'Disabled'}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Service Bus health check failed: {e}")
        return [TextContent(type="text", text=f"Service Bus health check failed: {str(e)}")]


async def get_queue_metrics(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get detailed metrics for a specific queue."""
    try:
        queue_name = arguments.get("queue", "invite-queue")
        params = {"queue": queue_name}
        
        result = await client.get("/api/admin/servicebus/metrics", params=params, requires_auth=True)
        
        # Format the queue metrics response
        response = f"**Queue Metrics: {result.get('queueName', queue_name)}**\n\n"
        response += f"**Timestamp:** {result.get('timestamp', 'Unknown')}\n\n"
        
        if "messages" in result:
            messages = result["messages"]
            response += "**Message Counts:**\n"
            response += f"- Active: {messages.get('active', 0)}\n"
            response += f"- Dead Letter: {messages.get('deadLetter', 0)}\n"
            response += f"- Scheduled: {messages.get('scheduled', 0)}\n"
            response += f"- Total: {messages.get('total', 0)}\n\n"
        
        if "size" in result:
            size = result["size"]
            response += "**Size Information:**\n"
            response += f"- Current Size: {size.get('currentBytes', 0)} bytes\n"
            response += f"- Max Size: {size.get('maxMegabytes', 0)} MB\n"
            response += f"- Utilization: {size.get('utilizationPercent', 0)}%\n\n"
        
        if "configuration" in result:
            config = result["configuration"]
            response += "**Configuration:**\n"
            response += f"- Max Delivery Count: {config.get('maxDeliveryCount', 0)}\n\n"
        
        if "timestamps" in result:
            timestamps = result["timestamps"]
            response += "**Timestamps:**\n"
            response += f"- Created: {timestamps.get('created', 'Unknown')}\n"
            response += f"- Updated: {timestamps.get('updated', 'Unknown')}\n"
            response += f"- Accessed: {timestamps.get('accessed', 'Unknown')}\n\n"
        
        if "featureStatus" in result:
            features = result["featureStatus"]
            response += "**Features:**\n"
            for feature, enabled in features.items():
                response += f"- {feature}: {'Enabled' if enabled else 'Disabled'}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Queue metrics failed: {e}")
        return [TextContent(type="text", text=f"Queue metrics failed: {str(e)}")]


async def health_check_live(client: VaultsClient) -> Sequence[TextContent]:
    """Check liveness status of the Vaults system."""
    try:
        result = await client.get("/api/health/live")
        
        status = result.get("status", "unknown")
        message = result.get("message", "No message")
        timestamp = result.get("timestamp", "Unknown")
        
        response = f"**Liveness Status: {status.upper()}**\n\n"
        response += f"**Timestamp:** {timestamp}\n"
        response += f"**Message:** {message}"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Liveness check failed: {e}")
        return [TextContent(type="text", text=f"Liveness check failed: {str(e)}")]


async def health_check_ready(client: VaultsClient) -> Sequence[TextContent]:
    """Check readiness status of the Vaults system."""
    try:
        result = await client.get("/api/health/ready")
        
        status = result.get("status", "unknown")
        timestamp = result.get("timestamp", "Unknown")
        
        response = f"**Readiness Status: {status.upper()}**\n\n"
        response += f"**Timestamp:** {timestamp}\n\n"
        
        if "checks" in result:
            response += "**Dependency Checks:**\n"
            checks = result["checks"]
            for component, check_status in checks.items():
                response += f"- **{component}:** {check_status}\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Readiness check failed: {e}")
        return [TextContent(type="text", text=f"Readiness check failed: {str(e)}")]


async def health_check_simple(client: VaultsClient) -> Sequence[TextContent]:
    """Simple health check with minimal dependencies."""
    try:
        result = await client.get("/api/health/simple")
        
        status = result.get("status", "unknown")
        message = result.get("message", "No message")
        dependencies = result.get("dependencies", [])
        
        response = f"**Simple Health Check: {status.upper()}**\n\n"
        response += f"**Message:** {message}\n"
        response += f"**Dependencies:** {', '.join(dependencies)}"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Simple health check failed: {e}")
        return [TextContent(type="text", text=f"Simple health check failed: {str(e)}")]


async def health_check_config(client: VaultsClient) -> Sequence[TextContent]:
    """Configuration health check."""
    try:
        result = await client.get("/api/health/config")
        
        status = result.get("status", "unknown")
        message = result.get("message", "No message")
        dependencies = result.get("dependencies", [])
        cosmos_config = result.get("cosmosConfigExists", False)
        
        response = f"**Configuration Health Check: {status.upper()}**\n\n"
        response += f"**Message:** {message}\n"
        response += f"**Dependencies:** {', '.join(dependencies)}\n"
        response += f"**Cosmos Config Available:** {'Yes' if cosmos_config else 'No'}"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Configuration health check failed: {e}")
        return [TextContent(type="text", text=f"Configuration health check failed: {str(e)}")]


async def health_check_service(client: VaultsClient) -> Sequence[TextContent]:
    """Service dependency health check."""
    try:
        result = await client.get("/api/health/service")
        
        status = result.get("status", "unknown")
        message = result.get("message", "No message")
        dependencies = result.get("dependencies", [])
        health_service = result.get("healthServiceExists", False)
        
        response = f"**Service Health Check: {status.upper()}**\n\n"
        response += f"**Message:** {message}\n"
        response += f"**Dependencies:** {', '.join(dependencies)}\n"
        response += f"**Health Service Available:** {'Yes' if health_service else 'No'}"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Service health check failed: {e}")
        return [TextContent(type="text", text=f"Service health check failed: {str(e)}")]