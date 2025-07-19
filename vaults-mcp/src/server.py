"""Main MCP server for Vaults integration using standard MCP protocol."""

import asyncio
import logging
import sys
from typing import Any, Dict, List, Optional, Sequence

from mcp.server import Server
from mcp.types import (
    TextContent,
    Tool,
    CallToolRequest,
    CallToolResult,
    ListToolsRequest,
)
import mcp.server.stdio

from .client import VaultsClient
from .config import get_config
from .exceptions import VaultsException

# Import all tool modules
from .tools import (
    health_tools,
    admin_tools,
    conversation_tools,
    copilot_tools,
    export_tools,
    metrics_tools,
    onboarding_tools,
    payment_tools,
    governance_tools,
)

logger = logging.getLogger(__name__)

# Initialize configuration and client globally
config = get_config()
client = VaultsClient(config)

# Setup logging
logging.basicConfig(
    level=getattr(logging, config.log_level),
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    handlers=[logging.StreamHandler(sys.stderr)]
)

# Create standard MCP server instance
server = Server("vaults")

logger.info(f"Vaults MCP Server initialized (v{config.server_version})")

# Tool definitions - all 40 tools properly defined
TOOLS = [
    # Health & Monitoring Tools (8 tools)
    Tool(
        name="health_check",
        description="Check the health status of the Vaults system.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_service_bus_health",
        description="Get Service Bus health status and queue metrics.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_queue_metrics",
        description="Get detailed metrics for a specific queue.",
        inputSchema={
            "type": "object",
            "properties": {
                "queue": {
                    "type": "string",
                    "description": "Queue name",
                    "default": "invite-queue"
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="health_check_live",
        description="Check liveness status of the Vaults system.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="health_check_ready",
        description="Check readiness status of the Vaults system.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="health_check_simple",
        description="Simple health check with minimal dependencies.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="health_check_config",
        description="Configuration health check.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="health_check_service",
        description="Service dependency health check.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    
    # Administration Tools (6 tools)
    Tool(
        name="get_admin_stats",
        description="Get administrative statistics for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_audit_policies",
        description="Get audit policies for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="list_tenant_users",
        description="List users in a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "filter": {
                    "type": ["string", "null"],
                    "description": "Filter string",
                    "default": None
                },
                "top": {
                    "type": "integer",
                    "description": "Number of users to return",
                    "default": 50
                },
                "user_type": {
                    "type": ["string", "null"],
                    "description": "User type filter",
                    "default": None
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="update_audit_policies",
        description="Update audit policies for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "policies": {
                    "type": "array",
                    "description": "List of policies to update",
                    "items": {
                        "type": "object",
                        "additionalProperties": True
                    }
                }
            },
            "required": ["tenant_id", "policies"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="delete_audit_policy",
        description="Delete a specific audit policy.",
        inputSchema={
            "type": "object",
            "properties": {
                "policy_id": {
                    "type": "string",
                    "description": "Policy ID to delete"
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                }
            },
            "required": ["policy_id", "tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_user_invitation_status",
        description="Get invitation status for a specific user.",
        inputSchema={
            "type": "object",
            "properties": {
                "user_id": {
                    "type": "string",
                    "description": "User ID"
                }
            },
            "required": ["user_id"],
            "additionalProperties": False
        }
    ),
    
    # Copilot Integration Tools (6 tools)
    Tool(
        name="get_copilot_root",
        description="Get the root Copilot API information.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_copilot_users",
        description="Get Copilot users for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_interaction_history",
        description="Get Copilot interaction history for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "top": {
                    "type": ["integer", "null"],
                    "description": "Number of interactions to return",
                    "default": None
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="copilot_retrieve_content",
        description="Retrieve content using Copilot search.",
        inputSchema={
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search query"
                },
                "filters": {
                    "type": ["object", "null"],
                    "description": "Search filters",
                    "default": None,
                    "additionalProperties": True
                }
            },
            "required": ["query"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_copilot_usage_summary",
        description="Get tenant-level Copilot usage summary from Microsoft Graph Reports API.",
        inputSchema={
            "type": "object",
            "properties": {
                "period": {
                    "type": "string",
                    "description": "Report period",
                    "default": "D7"
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_copilot_user_count",
        description="Get Copilot user count summary from Microsoft Graph Reports API.",
        inputSchema={
            "type": "object",
            "properties": {
                "period": {
                    "type": "string",
                    "description": "Report period",
                    "default": "D7"
                }
            },
            "additionalProperties": False
        }
    ),
    
    # Security Tools (3 tools)
    Tool(
        name="get_security_alerts",
        description="Get security alerts from Microsoft Graph Security API.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_high_risk_users",
        description="Get high-risk users from Microsoft Graph Identity Protection API.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_policy_violations",
        description="Get compliance policy information from Microsoft Graph Compliance API.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    
    # Conversation Tools (2 tools)
    Tool(
        name="get_conversation",
        description="Get a specific conversation by ID.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "conversation_id": {
                    "type": "string",
                    "description": "Conversation ID"
                }
            },
            "required": ["tenant_id", "conversation_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="search_conversations",
        description="Search conversations with various filters.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "type": {
                    "type": ["string", "null"],
                    "description": "Conversation type filter",
                    "default": None
                },
                "user": {
                    "type": ["string", "null"],
                    "description": "User filter",
                    "default": None
                },
                "keyword": {
                    "type": ["string", "null"],
                    "description": "Keyword search",
                    "default": None
                },
                "page": {
                    "type": "integer",
                    "description": "Page number",
                    "default": 1
                },
                "page_size": {
                    "type": "integer",
                    "description": "Page size",
                    "default": 10
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="process_ingestion",
        description="Process data ingestion for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "additionalProperties": False
        }
    ),
    
    # Metrics Tools (2 tools)
    Tool(
        name="get_usage_metrics",
        description="Get detailed usage metrics for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "start_date": {
                    "type": ["string", "null"],
                    "description": "Start date (YYYY-MM-DD)",
                    "default": None
                },
                "end_date": {
                    "type": ["string", "null"],
                    "description": "End date (YYYY-MM-DD)",
                    "default": None
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_tenant_overview",
        description="Get high-level tenant overview and metrics.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    
    # Export Tools (1 tool)
    Tool(
        name="list_exports",
        description="List available exports for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "date": {
                    "type": ["string", "null"],
                    "description": "Export date filter",
                    "default": None
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    
    # Payment Tools (5 tools)
    Tool(
        name="get_billing_status",
        description="Get billing status and invoice history for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="create_stripe_checkout",
        description="Create a Stripe checkout session for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "success_url": {
                    "type": "string",
                    "description": "Success URL"
                },
                "cancel_url": {
                    "type": "string",
                    "description": "Cancel URL"
                }
            },
            "required": ["tenant_id", "success_url", "cancel_url"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="create_stripe_payment_link",
        description="Create a Stripe payment link for seat-based billing.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "seats": {
                    "type": "integer",
                    "description": "Number of seats"
                },
                "success_url": {
                    "type": "string",
                    "description": "Success URL"
                },
                "cancel_url": {
                    "type": "string",
                    "description": "Cancel URL"
                }
            },
            "required": ["tenant_id", "seats", "success_url", "cancel_url"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_seat_status",
        description="Get current seat allocation and usage for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="stripe_webhook",
        description="Get information about Stripe webhook processing.",
        inputSchema={
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    ),
    
    # Onboarding Tools (6 tools)
    Tool(
        name="validate_azure_ad_permissions",
        description="Validate Azure AD permissions for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "azure_ad_app_id": {
                    "type": "string",
                    "description": "Azure AD App ID"
                },
                "azure_ad_app_secret": {
                    "type": "string",
                    "description": "Azure AD App Secret"
                }
            },
            "required": ["tenant_id", "azure_ad_app_id", "azure_ad_app_secret"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="test_storage_connection",
        description="Test Azure Storage connection for exports.",
        inputSchema={
            "type": "object",
            "properties": {
                "azure_storage_account_name": {
                    "type": "string",
                    "description": "Storage account name"
                },
                "azure_storage_container_name": {
                    "type": "string",
                    "description": "Container name"
                },
                "azure_storage_sas_token": {
                    "type": "string",
                    "description": "SAS token"
                }
            },
            "required": ["azure_storage_account_name", "azure_storage_container_name", "azure_storage_sas_token"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="complete_onboarding",
        description="Complete the onboarding process for a tenant.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "azure_ad_app_id": {
                    "type": "string",
                    "description": "Azure AD App ID"
                },
                "azure_storage_account_name": {
                    "type": "string",
                    "description": "Storage account name"
                },
                "azure_storage_container_name": {
                    "type": "string",
                    "description": "Container name"
                },
                "retention_policy": {
                    "type": "string",
                    "description": "Retention policy",
                    "default": "90days"
                },
                "custom_retention_days": {
                    "type": "integer",
                    "description": "Custom retention days",
                    "default": 90
                },
                "export_schedule": {
                    "type": "string",
                    "description": "Export schedule",
                    "default": "daily"
                },
                "export_time": {
                    "type": "string",
                    "description": "Export time",
                    "default": "02:00"
                }
            },
            "required": ["tenant_id", "azure_ad_app_id", "azure_storage_account_name", "azure_storage_container_name"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="send_onboarding_email",
        description="Send onboarding email to tenant admin.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "admin_email": {
                    "type": "string",
                    "description": "Admin email address"
                }
            },
            "required": ["tenant_id", "admin_email"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="invite_user",
        description="Invite a user via HTTP endpoint.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "admin_email": {
                    "type": "string",
                    "description": "Admin email address"
                },
                "redirect_url": {
                    "type": "string",
                    "description": "Redirect URL",
                    "default": "https://myapplications.microsoft.com"
                },
                "invited_by": {
                    "type": "string",
                    "description": "Invited by",
                    "default": "System"
                }
            },
            "required": ["tenant_id", "admin_email"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="resend_invitation",
        description="Resend an invitation to a user.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID"
                },
                "redirect_url": {
                    "type": "string",
                    "description": "Redirect URL",
                    "default": "https://myapplications.microsoft.com"
                },
                "requested_by": {
                    "type": "string",
                    "description": "Requested by",
                    "default": "Admin"
                }
            },
            "required": ["tenant_id"],
            "additionalProperties": False
        }
    ),
    
    # Governance Tools (12 tools) - NEW: Governance-First Architecture
    Tool(
        name="get_purview_audit_logs",
        description="Retrieve Copilot audit logs from Microsoft Purview with governance insights.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                },
                "start_time": {
                    "type": "string",
                    "description": "Start time for audit log search (ISO format)"
                },
                "end_time": {
                    "type": "string",
                    "description": "End time for audit log search (ISO format)"
                },
                "max_results": {
                    "type": "integer",
                    "description": "Maximum number of results to return",
                    "default": 1000
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="subscribe_purview_audit_logs",
        description="Subscribe to real-time Purview audit log events for immediate governance.",
        inputSchema={
            "type": "object",
            "properties": {
                "webhook_url": {
                    "type": "string",
                    "description": "URL to receive webhook notifications"
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "required": ["webhook_url"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_dlp_policies",
        description="Get DLP policies with AI governance enhancements.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="assess_dlp_violation_risk",
        description="Assess DLP violation risk with AI-specific governance analysis.",
        inputSchema={
            "type": "object",
            "properties": {
                "violation_data": {
                    "type": "object",
                    "description": "DLP violation event data"
                },
                "apply_actions": {
                    "type": "boolean",
                    "description": "Whether to apply governance actions automatically",
                    "default": false
                }
            },
            "required": ["violation_data"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="validate_ai_permissions",
        description="Validate user permissions before AI interaction using principle of least privilege.",
        inputSchema={
            "type": "object",
            "properties": {
                "user_id": {
                    "type": "string",
                    "description": "User identifier"
                },
                "resource_id": {
                    "type": "string",
                    "description": "Resource identifier"
                },
                "operation": {
                    "type": "string",
                    "description": "Operation type",
                    "default": "read"
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "required": ["user_id", "resource_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="validate_ai_permissions_batch",
        description="Batch validate permissions for multiple AI interactions.",
        inputSchema={
            "type": "object",
            "properties": {
                "validation_requests": {
                    "type": "array",
                    "description": "List of validation request objects",
                    "items": {
                        "type": "object",
                        "properties": {
                            "user_id": {"type": "string"},
                            "resource_id": {"type": "string"},
                            "operation": {"type": "string", "default": "read"},
                            "tenant_id": {"type": "string"}
                        },
                        "required": ["user_id", "resource_id"]
                    }
                }
            },
            "required": ["validation_requests"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_user_permission_summary",
        description="Get user permission summary for governance dashboard.",
        inputSchema={
            "type": "object",
            "properties": {
                "user_id": {
                    "type": "string",
                    "description": "User identifier"
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "required": ["user_id"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="classify_content",
        description="Classify content using AI-powered analysis, addressing Microsoft's non-Office file gaps.",
        inputSchema={
            "type": "object",
            "properties": {
                "resource_id": {
                    "type": "string",
                    "description": "Resource identifier"
                },
                "content_type": {
                    "type": "string",
                    "description": "MIME type of content"
                },
                "content_base64": {
                    "type": "string",
                    "description": "Base64 encoded content (optional)"
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "required": ["resource_id", "content_type"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="classify_content_batch",
        description="Batch classify multiple content items for scalable governance.",
        inputSchema={
            "type": "object",
            "properties": {
                "classification_requests": {
                    "type": "array",
                    "description": "List of content classification requests",
                    "items": {
                        "type": "object",
                        "properties": {
                            "resource_id": {"type": "string"},
                            "content_type": {"type": "string"},
                            "content_base64": {"type": "string"}
                        },
                        "required": ["resource_id", "content_type"]
                    }
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                }
            },
            "required": ["classification_requests"],
            "additionalProperties": False
        }
    ),
    Tool(
        name="get_content_classification_summary",
        description="Get content classification summary for governance analytics.",
        inputSchema={
            "type": "object",
            "properties": {
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                },
                "days": {
                    "type": "integer",
                    "description": "Number of days for historical analysis",
                    "default": 30
                }
            },
            "additionalProperties": False
        }
    ),
    Tool(
        name="apply_sensitivity_labels",
        description="Apply automated sensitivity labeling for content Microsoft Purview cannot handle.",
        inputSchema={
            "type": "object",
            "properties": {
                "resource_ids": {
                    "type": "array",
                    "description": "List of resource identifiers to label",
                    "items": {"type": "string"}
                },
                "tenant_id": {
                    "type": "string",
                    "description": "Tenant ID",
                    "default": "default-tenant"
                },
                "force_reclassification": {
                    "type": "boolean",
                    "description": "Force re-classification of already labeled content",
                    "default": false
                }
            },
            "required": ["resource_ids"],
            "additionalProperties": False
        }
    )
]

@server.list_tools()
async def handle_list_tools() -> List[Tool]:
    """Return the list of available tools."""
    logger.info("Listing available tools")
    return TOOLS

@server.call_tool()
async def handle_call_tool(name: str, arguments: Dict[str, Any]) -> List[TextContent]:
    """Handle tool calls."""
    logger.info(f"Calling tool: {name} with arguments: {arguments}")
    
    try:
        # Health & Monitoring Tools
        if name == "health_check":
            result = await health_tools.health_check(client)
        elif name == "get_service_bus_health":
            result = await health_tools.get_service_bus_health(client)
        elif name == "get_queue_metrics":
            result = await health_tools.get_queue_metrics(client, arguments)
        elif name == "health_check_live":
            result = await health_tools.health_check_live(client)
        elif name == "health_check_ready":
            result = await health_tools.health_check_ready(client)
        elif name == "health_check_simple":
            result = await health_tools.health_check_simple(client)
        elif name == "health_check_config":
            result = await health_tools.health_check_config(client)
        elif name == "health_check_service":
            result = await health_tools.health_check_service(client)
        
        # Administration Tools
        elif name == "get_admin_stats":
            result = await admin_tools.get_admin_stats(client, arguments)
        elif name == "get_audit_policies":
            result = await admin_tools.get_audit_policies(client, arguments)
        elif name == "list_tenant_users":
            result = await admin_tools.list_tenant_users(client, arguments)
        elif name == "update_audit_policies":
            result = await admin_tools.update_audit_policies(client, arguments)
        elif name == "delete_audit_policy":
            result = await admin_tools.delete_audit_policy(client, arguments)
        elif name == "get_user_invitation_status":
            result = await admin_tools.get_user_invitation_status(client, arguments)
        
        # Copilot Integration Tools
        elif name == "get_copilot_root":
            result = await copilot_tools.get_copilot_root(client, arguments)
        elif name == "get_copilot_users":
            result = await copilot_tools.get_copilot_users(client, arguments)
        elif name == "get_interaction_history":
            result = await copilot_tools.get_interaction_history(client, arguments)
        elif name == "copilot_retrieve_content":
            result = await copilot_tools.copilot_retrieve_content(client, arguments)
        elif name == "get_copilot_usage_summary":
            result = await copilot_tools.get_copilot_usage_summary(client, arguments)
        elif name == "get_copilot_user_count":
            result = await copilot_tools.get_copilot_user_count(client, arguments)
        
        # Security Tools
        elif name == "get_security_alerts":
            result = await copilot_tools.get_security_alerts(client)
        elif name == "get_high_risk_users":
            result = await copilot_tools.get_high_risk_users(client)
        elif name == "get_policy_violations":
            result = await copilot_tools.get_policy_violations(client)
        
        # Conversation Tools
        elif name == "get_conversation":
            result = await conversation_tools.get_conversation(client, arguments)
        elif name == "search_conversations":
            result = await conversation_tools.search_conversations(client, arguments)
        elif name == "process_ingestion":
            result = await conversation_tools.process_ingestion(client, arguments)
        
        # Metrics Tools
        elif name == "get_usage_metrics":
            result = await metrics_tools.get_usage_metrics(client, arguments)
        elif name == "get_tenant_overview":
            result = await metrics_tools.get_tenant_overview(client, arguments)
        
        # Export Tools
        elif name == "list_exports":
            result = await export_tools.list_exports(client, arguments)
        
        # Payment Tools
        elif name == "get_billing_status":
            result = await payment_tools.get_billing_status(client, arguments)
        elif name == "create_stripe_checkout":
            result = await payment_tools.create_stripe_checkout(client, arguments)
        elif name == "create_stripe_payment_link":
            result = await payment_tools.create_stripe_payment_link(client, arguments)
        elif name == "get_seat_status":
            result = await payment_tools.get_seat_status(client, arguments)
        elif name == "stripe_webhook":
            result = await payment_tools.stripe_webhook(client, arguments)
        
        # Onboarding Tools
        elif name == "validate_azure_ad_permissions":
            result = await onboarding_tools.validate_azure_ad_permissions(client, arguments)
        elif name == "test_storage_connection":
            result = await onboarding_tools.test_storage_connection(client, arguments)
        elif name == "complete_onboarding":
            result = await onboarding_tools.complete_onboarding(client, arguments)
        elif name == "send_onboarding_email":
            result = await onboarding_tools.send_onboarding_email(client, arguments)
        elif name == "invite_user":
            result = await onboarding_tools.invite_user(client, arguments)
        elif name == "resend_invitation":
            result = await onboarding_tools.resend_invitation(client, arguments)
        
        # Governance Tools - NEW: Governance-First Architecture
        elif name == "get_purview_audit_logs":
            result = await governance_tools.get_purview_audit_logs(
                client, 
                arguments.get("tenant_id", "default-tenant"),
                arguments.get("start_time"),
                arguments.get("end_time"),
                arguments.get("max_results", 1000)
            )
        elif name == "subscribe_purview_audit_logs":
            result = await governance_tools.subscribe_purview_audit_logs(
                client,
                arguments["webhook_url"],
                arguments.get("tenant_id", "default-tenant")
            )
        elif name == "get_dlp_policies":
            result = await governance_tools.get_dlp_policies(
                client,
                arguments.get("tenant_id", "default-tenant")
            )
        elif name == "assess_dlp_violation_risk":
            result = await governance_tools.assess_dlp_violation_risk(
                client,
                arguments["violation_data"],
                arguments.get("apply_actions", False)
            )
        elif name == "validate_ai_permissions":
            result = await governance_tools.validate_ai_permissions(
                client,
                arguments["user_id"],
                arguments["resource_id"],
                arguments.get("operation", "read"),
                arguments.get("tenant_id", "default-tenant")
            )
        elif name == "validate_ai_permissions_batch":
            result = await governance_tools.validate_ai_permissions_batch(
                client,
                arguments["validation_requests"]
            )
        elif name == "get_user_permission_summary":
            result = await governance_tools.get_user_permission_summary(
                client,
                arguments["user_id"],
                arguments.get("tenant_id", "default-tenant")
            )
        elif name == "classify_content":
            result = await governance_tools.classify_content(
                client,
                arguments["resource_id"],
                arguments["content_type"],
                arguments.get("content_base64"),
                arguments.get("tenant_id", "default-tenant")
            )
        elif name == "classify_content_batch":
            result = await governance_tools.classify_content_batch(
                client,
                arguments["classification_requests"],
                arguments.get("tenant_id", "default-tenant")
            )
        elif name == "get_content_classification_summary":
            result = await governance_tools.get_content_classification_summary(
                client,
                arguments.get("tenant_id", "default-tenant"),
                arguments.get("days", 30)
            )
        elif name == "apply_sensitivity_labels":
            result = await governance_tools.apply_sensitivity_labels(
                client,
                arguments["resource_ids"],
                arguments.get("tenant_id", "default-tenant"),
                arguments.get("force_reclassification", False)
            )
        
        else:
            raise ValueError(f"Unknown tool: {name}")
        
        return result
        
    except Exception as e:
        logger.error(f"Error calling tool {name}: {e}")
        return [TextContent(type="text", text=f"Error: {str(e)}")]

async def main():
    """Main entry point for stdio server."""
    logger.info("Starting Vaults MCP Server")
    async with mcp.server.stdio.stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            server.create_initialization_options()
        )

if __name__ == "__main__":
    asyncio.run(main())