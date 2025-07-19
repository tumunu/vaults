# CopilotVault MCP Server

A Model Context Protocol (MCP) server that provides Claude with comprehensive access to Microsoft 365 Copilot data through the CopilotVault Azure Function App.

## Overview

This MCP server connects to the live production CopilotVault system at `func-copilotvault-nz.azurewebsites.net` and provides Claude with 27 specialized tools for:

- **Health & Monitoring** - System health checks and Service Bus monitoring
- **Administration** - User management, audit policies, and tenant statistics  
- **Conversations** - Search and retrieve Copilot conversation data
- **Copilot Integration** - Direct Microsoft Graph Copilot API access
- **Data Export** - List and manage data exports
- **Metrics & Analytics** - Usage metrics and tenant overview analytics
- **Onboarding** - Tenant setup and Azure configuration validation
- **Billing** - Stripe payment integration and billing status

## Quick Start

### Prerequisites

- Python 3.11 or higher
- Access to CopilotVault Azure Function App
- Function key for authenticated endpoints

### Installation

1. **Clone and setup:**
```bash
git clone <repository-url>
cd CoPilot_MCP
pip install -r requirements.txt
```

2. **Configure environment:**
```bash
cp .env.example .env
# Edit .env with your configuration
```

3. **Required environment variables:**
```bash
COPILOT_VAULT_BASE_URL=https://func-copilotvault-nz.azurewebsites.net
COPILOT_VAULT_FUNCTION_KEY=your-function-key-here
```

4. **Run the server:**
```bash
python -m src.server
```

### Claude Code Integration

Add to your MCP configuration:

```json
{
  "servers": {
    "copilot-vault": {
      "command": "python",
      "args": ["-m", "src.server"],
      "cwd": "/path/to/CoPilot_MCP",
      "env": {
        "COPILOT_VAULT_BASE_URL": "https://func-copilotvault-nz.azurewebsites.net",
        "COPILOT_VAULT_FUNCTION_KEY": "your-function-key"
      }
    }
  }
}
```

## Available Tools

### Health & Monitoring (3 tools)
- `health_check` - Check overall system health
- `get_service_bus_health` - Service Bus status and metrics
- `get_queue_metrics` - Detailed queue metrics

### Administration (6 tools)
- `get_admin_stats` - Tenant administrative statistics
- `get_audit_policies` - List audit policies for a tenant
- `update_audit_policies` - Update audit policies
- `delete_audit_policy` - Delete a specific audit policy
- `list_tenant_users` - List users in a tenant
- `get_user_invitation_status` - Get user invitation status

### Conversations & Search (3 tools)
- `get_conversation` - Get a specific conversation by ID
- `process_ingestion` - Process data ingestion for a tenant
- `search_conversations` - Search conversations with filters

### Copilot Integration (4 tools)
- `get_copilot_root` - Get Copilot API root information
- `get_copilot_users` - Get Copilot-enabled users
- `get_interaction_history` - Get interaction history
- `copilot_retrieve_content` - Search and retrieve content

### Data Export (1 tool)
- `list_exports` - List available data exports

### Metrics & Analytics (2 tools)
- `get_usage_metrics` - Detailed usage metrics and analytics
- `get_tenant_overview` - High-level tenant overview

### Onboarding (6 tools)
- `validate_azure_ad_permissions` - Validate Azure AD setup
- `test_storage_connection` - Test Azure Storage connection
- `complete_onboarding` - Complete tenant onboarding
- `send_onboarding_email` - Send onboarding email
- `invite_user` - Invite users to tenant
- `resend_invitation` - Resend user invitations

### Billing (2 tools)
- `create_stripe_checkout` - Create Stripe checkout session
- `get_billing_status` - Get billing status and invoices

## Usage Examples

### Get System Health
```python
# Claude can call this tool
health_status = await call_tool("health_check", {})
```

### Search for Policy Violations
```python
violations = await call_tool("search_conversations", {
    "tenant_id": "tenant-123",
    "type": "policy-violations",
    "page": 1,
    "pageSize": 20
})
```

### Get Usage Analytics
```python
metrics = await call_tool("get_usage_metrics", {
    "tenant_id": "tenant-123",
    "start_date": "2025-05-01T00:00:00Z",
    "end_date": "2025-06-01T00:00:00Z"
})
```

## Configuration

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `COPILOT_VAULT_BASE_URL` | Yes | `https://func-copilotvault-nz.azurewebsites.net` | Function App URL |
| `COPILOT_VAULT_FUNCTION_KEY` | Yes | - | Function key for authenticated endpoints |
| `COPILOT_VAULT_TIMEOUT` | No | `30` | HTTP request timeout (seconds) |
| `COPILOT_VAULT_MAX_RETRIES` | No | `5` | Maximum retry attempts |
| `COPILOT_VAULT_RETRY_DELAY` | No | `1.0` | Retry delay (seconds) |
| `COPILOT_VAULT_LOG_LEVEL` | No | `INFO` | Logging level |

### Authentication

The server handles two types of endpoints:

- **Anonymous endpoints** - No authentication required
- **Function-level endpoints** - Require function key via `x-functions-key` header

Function keys are automatically handled by the HTTP client when `requires_auth=True`.

## Error Handling

The server provides comprehensive error handling with:

- **Retry Logic** - Automatic retries with exponential backoff
- **Custom Exceptions** - Specific error types for different failure modes
- **Detailed Logging** - Structured logging for debugging
- **Graceful Degradation** - Informative error messages to Claude

## Development

### Project Structure
```
CoPilot_MCP/
├── src/
│   ├── server.py              # Main MCP server
│   ├── client.py              # HTTP client
│   ├── config.py              # Configuration management
│   ├── exceptions.py          # Custom exceptions
│   └── tools/                 # MCP tool implementations
│       ├── health_tools.py
│       ├── admin_tools.py
│       ├── conversation_tools.py
│       ├── copilot_tools.py
│       ├── export_tools.py
│       ├── metrics_tools.py
│       ├── onboarding_tools.py
│       └── payment_tools.py
├── tests/                     # Test suite
├── docs/                      # Documentation
├── requirements.txt
├── pyproject.toml
└── .env.example
```

### Running Tests
```bash
pytest tests/
```

### Development Setup
```bash
pip install -r requirements.txt
pip install -e .[dev]
```

## Docker Deployment

Build and run with Docker:

```bash
docker build -t copilot-vault-mcp .
docker run -e COPILOT_VAULT_FUNCTION_KEY=your-key copilot-vault-mcp
```

## Troubleshooting

### Common Issues

1. **401 Authentication Errors**
   - Verify `COPILOT_VAULT_FUNCTION_KEY` is correct
   - Check if endpoint requires Function-level authentication

2. **Connection Errors**
   - Verify Function App URL is accessible
   - Check network connectivity and SSL certificates

3. **Rate Limiting**
   - Server implements automatic exponential backoff
   - Monitor Function App metrics for usage patterns

### Debug Mode
```bash
export COPILOT_VAULT_LOG_LEVEL=DEBUG
python -m src.server
```

## Support

For issues and feature requests, please check:
- Function App logs in Azure Portal
- Server logs with DEBUG level enabled
- Network connectivity to `func-copilotvault-nz.azurewebsites.net`

## License

MIT License - see LICENSE file for details.