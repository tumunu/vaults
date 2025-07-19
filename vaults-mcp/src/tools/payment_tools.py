"""Stripe payment integration tools for Vaults MCP server."""

import logging
from typing import Any, Dict, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def create_stripe_checkout(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Create a Stripe checkout session for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        success_url = arguments["success_url"]
        cancel_url = arguments["cancel_url"]
        
        request_data = {
            "tenantId": tenant_id,
            "successUrl": success_url,
            "cancelUrl": cancel_url
        }
        
        result = await client.post("/api/v1/stripe/checkout", json_data=request_data)
        
        session_id = result.get('sessionId')
        checkout_url = result.get('url')
        
        response = f"**Stripe Checkout Session Created**\n\n"
        response += f"- **Tenant ID:** {tenant_id}\n"
        response += f"- **Session ID:** {session_id}\n"
        response += f"- **Checkout URL:** {checkout_url}\n\n"
        response += f"- **Success URL:** {success_url}\n"
        response += f"- **Cancel URL:** {cancel_url}\n\n"
        
        if checkout_url:
            response += "✅ **Ready for payment** - Customer can now complete checkout using the provided URL."
        else:
            response += "❌ **Error** - Failed to create checkout session."
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Create Stripe checkout failed: {e}")
        return [TextContent(type="text", text=f"Create Stripe checkout failed: {str(e)}")]


async def get_billing_status(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get billing status and invoice history for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        path = f"/api/v1/stripe/billing/{tenant_id}"
        
        result = await client.get(path, requires_auth=True)
        
        customer_email = result.get('customerEmail', 'N/A')
        subscription_status = result.get('subscriptionStatus', 'Unknown')
        current_period_end = result.get('currentPeriodEnd', 'Unknown')
        invoices = result.get('invoices', [])
        
        response = f"**Billing Status for Tenant: {tenant_id}**\n\n"
        
        # Subscription status with emoji
        status_emoji = {
            'active': '✅',
            'canceled': '❌',
            'past_due': '⚠️',
            'unpaid': '🔴',
            'trialing': '🆓'
        }.get(subscription_status.lower(), '❓')
        
        response += f"- **Customer Email:** {customer_email}\n"
        response += f"- **Subscription Status:** {status_emoji} {subscription_status.title()}\n"
        response += f"- **Current Period Ends:** {current_period_end}\n\n"
        
        if invoices:
            response += f"**Invoice History ({len(invoices)} invoices):**\n\n"
            
            for i, invoice in enumerate(invoices, 1):
                invoice_id = invoice.get('Id', 'N/A')
                amount_due = invoice.get('AmountDue', 0)
                status = invoice.get('Status', 'Unknown')
                created = invoice.get('Created', 'Unknown')
                invoice_pdf = invoice.get('InvoicePdf', '')
                
                # Format amount (assuming cents)
                amount_formatted = f"${amount_due / 100:.2f}" if amount_due else "$0.00"
                
                # Status emoji
                status_emoji = {
                    'paid': '✅',
                    'open': '📝',
                    'void': '❌',
                    'draft': '📄'
                }.get(status.lower(), '❓')
                
                response += f"**Invoice {i}:**\n"
                response += f"- ID: {invoice_id}\n"
                response += f"- Amount: {amount_formatted}\n"
                response += f"- Status: {status_emoji} {status.title()}\n"
                response += f"- Created: {created}\n"
                
                if invoice_pdf:
                    response += f"- PDF: {invoice_pdf}\n"
                
                response += "\n"
        else:
            response += "**No invoice history available.**\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get billing status failed: {e}")
        return [TextContent(type="text", text=f"Get billing status failed: {str(e)}")]


async def create_stripe_payment_link(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Create a Stripe payment link for seat-based billing."""
    try:
        tenant_id = arguments["tenant_id"]
        seats = arguments["seats"]
        success_url = arguments["success_url"]
        cancel_url = arguments["cancel_url"]
        
        request_data = {
            "tenantId": tenant_id,
            "seats": seats,
            "successUrl": success_url,
            "cancelUrl": cancel_url
        }
        
        result = await client.post("/api/v1/stripe/payment-links", json_data=request_data)
        
        payment_link_id = result.get('paymentLinkId')
        payment_url = result.get('url')
        seats_requested = result.get('seats')
        
        response = f"**Stripe Payment Link Created**\n\n"
        response += f"- **Tenant ID:** {tenant_id}\n"
        response += f"- **Payment Link ID:** {payment_link_id}\n"
        response += f"- **Seats Requested:** {seats_requested}\n"
        response += f"- **Payment URL:** {payment_url}\n\n"
        response += f"- **Success URL:** {success_url}\n"
        response += f"- **Cancel URL:** {cancel_url}\n\n"
        
        if payment_url:
            response += "✅ **Ready for payment** - Customer can now purchase seats using the payment link."
        else:
            response += "❌ **Error** - Failed to create payment link."
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Create Stripe payment link failed: {e}")
        return [TextContent(type="text", text=f"Create Stripe payment link failed: {str(e)}")]


async def get_seat_status(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Get current seat allocation and usage for a tenant."""
    try:
        tenant_id = arguments["tenant_id"]
        path = f"/api/v1/stripe/seats/{tenant_id}"
        
        result = await client.get(path, requires_auth=True)
        
        tenant_id_result = result.get('tenantId', tenant_id)
        purchased_seats = result.get('purchasedSeats', 0)
        active_seats = result.get('activeSeats', 0)
        max_seats = result.get('maxSeats', 0)
        is_enterprise = result.get('isEnterprise', False)
        last_seat_update = result.get('lastSeatUpdate', 'N/A')
        can_add_users = result.get('canAddUsers', False)
        
        response = f"**Seat Status for Tenant: {tenant_id_result}**\n\n"
        
        # Enterprise status
        if is_enterprise:
            response += "🏢 **Enterprise Customer** - Unlimited seats available\n\n"
        else:
            response += "💺 **Standard Seat-Based Billing**\n\n"
        
        # Seat allocation details
        response += f"- **Purchased Seats:** {purchased_seats}\n"
        response += f"- **Active Seats:** {active_seats}\n"
        response += f"- **Maximum Seats:** {max_seats}\n"
        response += f"- **Last Updated:** {last_seat_update}\n\n"
        
        # Usage status
        if is_enterprise:
            response += "✅ **Can Add Users:** Yes (Enterprise - No limits)\n"
        elif can_add_users:
            available_seats = max_seats - active_seats
            response += f"✅ **Can Add Users:** Yes ({available_seats} seats available)\n"
        else:
            response += "❌ **Can Add Users:** No (Seat limit reached)\n"
        
        # Usage visualization
        if not is_enterprise and max_seats > 0:
            usage_percent = (active_seats / max_seats) * 100
            usage_bar = "█" * int(usage_percent // 10) + "░" * (10 - int(usage_percent // 10))
            response += f"- **Usage:** {usage_percent:.1f}% [{usage_bar}]\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Get seat status failed: {e}")
        return [TextContent(type="text", text=f"Get seat status failed: {str(e)}")]


async def stripe_webhook(client: VaultsClient, arguments: Dict[str, Any]) -> Sequence[TextContent]:
    """Handle Stripe webhook events (informational access only)."""
    try:
        # This is a read-only tool for MCP server - webhooks are handled by the Azure Function
        # We can only provide information about webhook processing, not actually process webhooks
        
        response = "**Stripe Webhook Information**\n\n"
        response += "ℹ️ **Note:** Webhooks are processed automatically by the Vaults Azure Function App.\n\n"
        response += "**Supported Webhook Events:**\n"
        response += "- `checkout.session.completed` - Updates seat counts when payment completes\n"
        response += "- `invoice.payment_succeeded` - Processes successful subscription payments\n"
        response += "- `customer.created` - Links Stripe customer to tenant\n"
        response += "- `customer.subscription.created` - Sets up subscription tracking\n"
        response += "- `customer.subscription.updated` - Updates subscription status\n\n"
        response += "**Webhook Endpoint:** `POST /api/stripe/webhook`\n"
        response += "**Authentication:** Stripe signature validation\n\n"
        response += "🔧 **For debugging webhook issues:**\n"
        response += "1. Check Azure Function App logs\n"
        response += "2. Verify webhook signature configuration\n"
        response += "3. Monitor seat status updates after payments\n"
        
        return [TextContent(type="text", text=response)]
        
    except Exception as e:
        logger.error(f"Stripe webhook info failed: {e}")
        return [TextContent(type="text", text=f"Stripe webhook info failed: {str(e)}")]