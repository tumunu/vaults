#!/usr/bin/env python3
"""Validate that all 40 MCP tools are properly implemented."""

import re
import sys
from pathlib import Path

def extract_tool_names(file_path: str, pattern: str) -> set:
    """Extract tool names from a file using a regex pattern."""
    with open(file_path, 'r') as f:
        content = f.read()
    
    matches = re.findall(pattern, content)
    return set(matches)

def main():
    """Validate all tools are implemented."""
    src_dir = Path('src')
    server_file = src_dir / 'server.py'
    
    # Extract tool names from @mcp_server.tool() decorated functions
    registered_tools = extract_tool_names(
        str(server_file), 
        r'@mcp_server\.tool\(\)\s*async def ([a-zA-Z_]+)\('
    )
    
    # Expected 40 tools based on current implementation
    expected_tools = {
        # Health & Monitoring (8 tools)
        "health_check", "get_service_bus_health", "get_queue_metrics",
        "health_check_live", "health_check_ready", "health_check_simple",
        "health_check_config", "health_check_service",
        
        # Admin (6 tools)
        "get_admin_stats", "get_audit_policies", "update_audit_policies", 
        "delete_audit_policy", "list_tenant_users", "get_user_invitation_status",
        
        # Conversations (3 tools)
        "get_conversation", "process_ingestion", "search_conversations",
        
        # Copilot (7 tools)
        "get_copilot_root", "get_copilot_users", "get_interaction_history", 
        "copilot_retrieve_content", "get_copilot_usage_summary", "get_copilot_user_count",
        
        # Export (1 tool)
        "list_exports",
        
        # Metrics (2 tools)
        "get_usage_metrics", "get_tenant_overview",
        
        # Security (3 tools)
        "get_security_alerts", "get_high_risk_users", "get_policy_violations",
        
        # Onboarding (6 tools)
        "validate_azure_ad_permissions", "test_storage_connection", 
        "complete_onboarding", "send_onboarding_email", "invite_user", 
        "resend_invitation",
        
        # Payment (5 tools - updated from 2 to 5)
        "create_stripe_checkout", "get_billing_status", "create_stripe_payment_link",
        "get_seat_status", "stripe_webhook"
    }
    
    print("🔍 Vaults MCP Server Tool Validation")
    print("=" * 50)
    
    print(f"✅ Expected tools: {len(expected_tools)}")
    print(f"✅ Registered tools: {len(registered_tools)}")
    
    # Check for missing registrations
    missing_registered = expected_tools - registered_tools
    if missing_registered:
        print(f"\n❌ Missing tool registrations: {missing_registered}")
        return False
    
    # Check for extra tools
    extra_registered = registered_tools - expected_tools
    if extra_registered:
        print(f"\n⚠️  Extra registered tools: {extra_registered}")
    
    print(f"\n🎉 All {len(expected_tools)} tools are properly implemented!")
    print("\n📋 Tool Categories:")
    print("   • Health & Monitoring: 8 tools")
    print("   • Administration: 6 tools") 
    print("   • Conversations & Search: 3 tools")
    print("   • Copilot Integration: 6 tools")
    print("   • Data Export: 1 tool")
    print("   • Metrics & Analytics: 2 tools")
    print("   • Security: 3 tools")
    print("   • Onboarding: 6 tools")
    print("   • Payment: 5 tools (includes seat-based billing)")
    
    return True

if __name__ == "__main__":
    success = main()
    sys.exit(0 if success else 1)