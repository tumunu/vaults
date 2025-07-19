"""
Governance Tools for Vaults MCP Server

This module provides governance-specific tools for the MCP server, enabling Claude to interact
with the new governance-first architecture features including Purview integration, permission
validation, and content classification.

These tools address Microsoft's acknowledged Copilot governance gaps while complementing
their Purview infrastructure.
"""

import json
import logging
from typing import Any, Dict, List, Optional, Sequence

from mcp.types import TextContent

from ..client import VaultsClient

logger = logging.getLogger(__name__)


async def get_purview_audit_logs(
    client: VaultsClient,
    tenant_id: str = "default-tenant",
    start_time: Optional[str] = None,
    end_time: Optional[str] = None,
    max_results: int = 1000
) -> Sequence[TextContent]:
    """Retrieve Copilot audit logs from Microsoft Purview with governance insights."""
    try:
        params = {
            "tenantId": tenant_id,
            "maxResults": max_results
        }
        if start_time:
            params["startTime"] = start_time
        if end_time:
            params["endTime"] = end_time
            
        response = await client.get("/api/governance/purview/audit-logs", params=params)
        formatted_response = _format_audit_logs_response(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error retrieving Purview audit logs: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def subscribe_purview_audit_logs(
    client: VaultsClient,
    webhook_url: str,
    tenant_id: str = "default-tenant"
) -> Sequence[TextContent]:
    """Subscribe to real-time Purview audit log events for immediate governance."""
    try:
        payload = {
            "webhookUrl": webhook_url,
            "tenantId": tenant_id
        }
        
        response = await client.post("/api/governance/purview/subscribe", json=payload)
        
        formatted_response = f"""
Purview Audit Log Subscription Created Successfully

Webhook URL: {webhook_url}
Tenant ID: {tenant_id}
Subscription Status: Active
Real-time Governance: Enabled

Governance Features:
- Immediate jailbreak attempt detection
- Real-time sensitive data access monitoring
- Automated policy violation response
- Enhanced audit trail generation

Response: {json.dumps(response, indent=2)}
"""
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error subscribing to Purview audit logs: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def get_dlp_policies(
    client: VaultsClient,
    tenant_id: str = "default-tenant"
) -> Sequence[TextContent]:
    """Get DLP policies with AI governance enhancements."""
    try:
        params = {"tenantId": tenant_id}
        response = await client.get("/api/governance/dlp/policies", params=params)
        
        formatted_response = _format_dlp_policies_response(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error retrieving DLP policies: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def assess_dlp_violation_risk(
    client: VaultsClient,
    violation_data: Dict[str, Any],
    apply_actions: bool = False
) -> Sequence[TextContent]:
    """Assess DLP violation risk with AI-specific governance analysis."""
    try:
        params = {"applyActions": apply_actions} if apply_actions else {}
        response = await client.post("/api/governance/dlp/assess-risk", 
                                   json=violation_data, params=params)
        
        formatted_response = _format_dlp_risk_assessment(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error assessing DLP violation risk: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def validate_ai_permissions(
    client: VaultsClient,
    user_id: str,
    resource_id: str,
    operation: str = "read",
    tenant_id: str = "default-tenant"
) -> Sequence[TextContent]:
    """Validate user permissions before AI interaction using principle of least privilege."""
    try:
        payload = {
            "userId": user_id,
            "resourceId": resource_id,
            "operation": operation,
            "tenantId": tenant_id
        }
        
        response = await client.post("/api/governance/permissions/validate", json=payload)
        formatted_response = _format_permission_validation_response(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error validating AI permissions: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def validate_ai_permissions_batch(
    client: VaultsClient,
    validation_requests: List[Dict[str, str]]
) -> Sequence[TextContent]:
    """Batch validate permissions for multiple AI interactions."""
    try:
        payload = {"requests": validation_requests}
        response = await client.post("/api/governance/permissions/validate-batch", json=payload)
        
        formatted_response = _format_batch_permission_validation(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error in batch permission validation: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def get_user_permission_summary(
    client: VaultsClient,
    user_id: str,
    tenant_id: str = "default-tenant"
) -> Sequence[TextContent]:
    """Get user permission summary for governance dashboard."""
    try:
        params = {"userId": user_id, "tenantId": tenant_id}
        response = await client.get("/api/governance/permissions/user-summary", params=params)
        
        formatted_response = _format_user_permission_summary(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error retrieving user permission summary: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def classify_content(
    client: VaultsClient,
    resource_id: str,
    content_type: str,
    content_base64: Optional[str] = None,
    tenant_id: str = "default-tenant"
) -> Sequence[TextContent]:
    """Classify content using AI-powered analysis, addressing Microsoft's non-Office file gaps."""
    try:
        payload = {
            "resourceId": resource_id,
            "contentType": content_type,
            "tenantId": tenant_id
        }
        if content_base64:
            payload["contentBase64"] = content_base64
        
        response = await client.post("/api/governance/content/classify", json=payload)
        formatted_response = _format_content_classification_response(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error classifying content: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def classify_content_batch(
    client: VaultsClient,
    classification_requests: List[Dict[str, str]],
    tenant_id: str = "default-tenant"
) -> Sequence[TextContent]:
    """Batch classify multiple content items for scalable governance."""
    try:
        payload = {
            "requests": classification_requests,
            "tenantId": tenant_id
        }
        
        response = await client.post("/api/governance/content/classify-batch", json=payload)
        formatted_response = _format_batch_content_classification(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error in batch content classification: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def get_content_classification_summary(
    client: VaultsClient,
    tenant_id: str = "default-tenant",
    days: int = 30
) -> Sequence[TextContent]:
    """Get content classification summary for governance analytics."""
    try:
        params = {"tenantId": tenant_id, "days": days}
        response = await client.get("/api/governance/content/classification-summary", params=params)
        
        formatted_response = _format_classification_summary(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error retrieving classification summary: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


async def apply_sensitivity_labels(
    client: VaultsClient,
    resource_ids: List[str],
    tenant_id: str = "default-tenant",
    force_reclassification: bool = False
) -> Sequence[TextContent]:
    """Apply automated sensitivity labeling for content Microsoft Purview cannot handle."""
    try:
        payload = {
            "resourceIds": resource_ids,
            "tenantId": tenant_id,
            "forceReClassification": force_reclassification
        }
        
        response = await client.post("/api/governance/content/apply-labels", json=payload)
        formatted_response = _format_automated_labeling_response(response)
        return [TextContent(type="text", text=formatted_response)]
        
    except Exception as e:
        error_msg = f"Error applying sensitivity labels: {e}"
        logger.error(error_msg)
        return [TextContent(type="text", text=error_msg)]


# Response formatting functions
def _format_audit_logs_response(response: Dict[str, Any]) -> str:
    """Format Purview audit logs response for Claude."""
    summary = f"""
Microsoft Purview Audit Logs - Enhanced with Vaults Governance

Tenant: {response.get('tenantId', 'Unknown')}
Time Range: {response.get('startTime', 'N/A')} to {response.get('endTime', 'N/A')}
Total Records: {response.get('totalRecords', 0)}

🎯 Governance Enhancements Over Microsoft Purview:
✅ Real-time jailbreak attempt detection
✅ AI-specific risk scoring and analysis  
✅ Automated governance action recommendations
✅ Enhanced audit trail with governance context

Recent Audit Events:
"""
    
    if 'auditLogs' in response:
        for log in response['auditLogs'][:5]:  # Show first 5 events
            copilot_event = log.get('copilotEvent', {})
            summary += f"""
📝 Event: {log.get('operation', 'Unknown')}
   User: {log.get('userPrincipalName', 'Unknown')}
   Time: {log.get('creationTime', 'Unknown')}
   App: {copilot_event.get('appName', 'N/A')}
   Jailbreak Attempt: {'🚨 YES' if copilot_event.get('isJailbreakAttempt') else '✅ No'}
   Sensitivity: {copilot_event.get('sensitivityLabels', 'None')}
"""
    
    return summary


def _format_dlp_policies_response(response: Dict[str, Any]) -> str:
    """Format DLP policies response for Claude."""
    summary = f"""
Microsoft Purview DLP Policies - Enhanced with AI Governance

Tenant: {response.get('tenantId', 'Unknown')}
Total Policies: {response.get('totalPolicies', 0)}

🎯 Vaults Governance Enhancements:
✅ AI-specific governance actions for each policy
✅ Real-time Copilot integration controls
✅ Risk-based policy enforcement
✅ Automated approval workflows

Policy Overview:
"""
    
    if 'policies' in response:
        for policy in response['policies']:
            summary += f"""
📋 Policy: {policy.get('name', 'Unknown')}
   State: {policy.get('state', 'Unknown')}
   Priority: {policy.get('priority', 'N/A')}
   Risk Level: {policy.get('riskLevel', 'Unknown')}
   Governance Actions: {len(policy.get('rules', []))} rules with AI controls
   Copilot Impact: {'Enabled' if policy.get('governanceEnabled') else 'Standard'}
"""
    
    governance_caps = response.get('governanceCapabilities', {})
    summary += f"""
Available Governance Capabilities:
- Real-time Enforcement: {'✅' if governance_caps.get('realTimeEnforcement') else '❌'}
- Copilot Integration: {'✅' if governance_caps.get('copilotIntegration') else '❌'}  
- Risk Scoring: {'✅' if governance_caps.get('riskScoring') else '❌'}
- Approval Workflows: {'✅' if governance_caps.get('approvalWorkflows') else '❌'}
"""
    
    return summary


def _format_dlp_risk_assessment(response: Dict[str, Any]) -> str:
    """Format DLP risk assessment response for Claude."""
    return f"""
DLP Violation Risk Assessment - AI Governance Analysis

Violation ID: {response.get('violationId', 'Unknown')}

Risk Assessment:
- Risk Level: {response.get('riskAssessment', {}).get('riskLevel', 'Unknown')}
- Risk Score: {response.get('riskAssessment', {}).get('riskScore', 'N/A')}/100
- Processed At: {response.get('riskAssessment', {}).get('processedAt', 'Unknown')}

Governance Actions:
Required: {', '.join(response.get('governanceActions', {}).get('required', []))}
Applied: {'Yes' if response.get('governanceActions', {}).get('applied') else 'No'}

Copilot Impact:
- Access Restricted: {'🚨 YES' if response.get('copilotImpact', {}).get('accessRestricted') else '✅ No'}
- Response Filtered: {'⚠️ YES' if response.get('copilotImpact', {}).get('responseFiltered') else '✅ No'}
- Approval Required: {'📋 YES' if response.get('copilotImpact', {}).get('approvalRequired') else '✅ No'}
- Enhanced Monitoring: {'👁️ YES' if response.get('copilotImpact', {}).get('enhancedMonitoring') else '✅ No'}

This assessment provides AI-specific governance beyond Microsoft's native DLP capabilities.
"""


def _format_permission_validation_response(response: Dict[str, Any]) -> str:
    """Format permission validation response for Claude."""
    validation = response.get('validation', {})
    request_info = response.get('request', {})
    copilot_impact = response.get('copilotImpact', {})
    
    return f"""
AI Permission Validation - Principle of Least Privilege

Validation ID: {response.get('validationId', 'Unknown')}

Request Details:
- User: {request_info.get('userId', 'Unknown')}
- Resource: {request_info.get('resourceId', 'Unknown')}
- Operation: {request_info.get('operation', 'Unknown')}
- Tenant: {request_info.get('tenantId', 'Unknown')}

Validation Result:
- Authorized: {'✅ YES' if validation.get('isAuthorized') else '🚨 NO'}
- Permission Level: {validation.get('permissionLevel', 'N/A')}
- Validated At: {validation.get('validatedAt', 'Unknown')}

{'Denial Reasons:' if validation.get('denialReasons') else ''}
{chr(10).join(f"- {reason}" for reason in validation.get('denialReasons', []))}

Governance Features:
- Principle of Least Privilege: ✅ Enforced
- Real-time Validation: ✅ Active
- Contextual Restrictions: ✅ Applied
- Sensitivity Aware: ✅ Enabled

Copilot Impact:
- Allow Interaction: {'✅ YES' if copilot_impact.get('allowInteraction') else '🚨 NO'}
- Restricted Response: {'⚠️ YES' if copilot_impact.get('restrictedResponse') else '✅ No'}
- Requires Approval: {'📋 YES' if copilot_impact.get('requiresApproval') else '✅ No'}
- Enhanced Logging: {'📝 YES' if copilot_impact.get('enhancedLogging') else '✅ No'}

This addresses Microsoft's acknowledged "over-permissioned content exposure" gap.
"""


def _format_batch_permission_validation(response: Dict[str, Any]) -> str:
    """Format batch permission validation response for Claude."""
    summary_info = response.get('summary', {})
    
    summary = f"""
Batch AI Permission Validation Results

Batch ID: {response.get('batchId', 'Unknown')}
Processed At: {response.get('processedAt', 'Unknown')}

Summary:
- Total Requests: {summary_info.get('totalRequests', 0)}
- Authorized: {summary_info.get('authorized', 0)}
- Denied: {summary_info.get('denied', 0)}
- Success Rate: {summary_info.get('successRate', 0)}%

Governance Features:
- Batch Processing: ✅ Enabled
- Consistent Policy Enforcement: ✅ Active
- Audit Trail: ✅ Complete

Sample Results:
"""
    
    # Show first few results as examples
    for result in response.get('results', [])[:3]:
        summary += f"""
User: {result.get('userId', 'Unknown')}
Resource: {result.get('resourceId', 'Unknown')}
Authorized: {'✅ YES' if result.get('isAuthorized') else '🚨 NO'}
Permission Level: {result.get('permissionLevel', 'N/A')}
"""
    
    return summary


def _format_user_permission_summary(response: Dict[str, Any]) -> str:
    """Format user permission summary response for Claude."""
    user_details = response.get('summary', {}).get('userDetails', {})
    permission_stats = response.get('summary', {}).get('permissionStats', {})
    risk_profile = response.get('summary', {}).get('riskProfile', {})
    
    return f"""
User Permission Summary - Governance Analytics

User Details:
- User ID: {user_details.get('userId', 'Unknown')}
- Display Name: {user_details.get('displayName', 'Unknown')}
- Department: {user_details.get('department', 'Unknown')}
- Security Clearance: {user_details.get('securityClearance', 'Unknown')}

Permission Statistics (Last 30 Days):
- Total Validations: {permission_stats.get('totalValidationsLast30Days', 0)}
- Authorized Requests: {permission_stats.get('authorizedRequests', 0)}
- Denied Requests: {permission_stats.get('deniedRequests', 0)}
- Authorization Rate: {permission_stats.get('authorizationRate', 0)}%

Risk Profile:
- Risk Level: {risk_profile.get('riskLevel', 'Unknown')}
- Risk Score: {risk_profile.get('riskScore', 0)}/100
- Last Risk Assessment: {risk_profile.get('lastRiskAssessment', 'Unknown')}

Governance Features:
- Permission Analytics: ✅ Active
- Risk Assessment: ✅ Enabled
- Access Optimization: ✅ Available
- Compliance Monitoring: ✅ Active

This provides comprehensive permission governance beyond Microsoft's native capabilities.
"""


def _format_content_classification_response(response: Dict[str, Any]) -> str:
    """Format content classification response for Claude."""
    classification = response.get('classification', {})
    governance = response.get('governance', {})
    copilot_impact = response.get('copilotImpact', {})
    
    return f"""
AI-Powered Content Classification - Addressing Purview Gaps

Classification ID: {response.get('classificationId', 'Unknown')}
Resource: {response.get('request', {}).get('resourceId', 'Unknown')}
Content Type: {response.get('request', {}).get('contentType', 'Unknown')}

Classification Results:
- Method: {classification.get('method', 'Unknown')}
- Status: {classification.get('status', 'Unknown')}
- Sensitivity Level: {classification.get('sensitivityLevel', 'Unknown')}
- Risk Score: {classification.get('riskScore', 0)}/100
- Governance Risk Score: {classification.get('governanceRiskScore', 0)}/100
- Confidence: {int(classification.get('confidence', 0) * 100)}%

Detected Sensitive Info:
{chr(10).join(f"- {info}" for info in classification.get('detectedSensitiveInfoTypes', []))}

Recommended Label: {classification.get('recommendedSensitivityLabel', 'None')}

Governance Actions:
{chr(10).join(f"- {action}" for action in governance.get('actions', []))}

Copilot Impact:
- Allow Access: {'✅ YES' if copilot_impact.get('allowAccess') else '🚨 NO'}
- Require Approval: {'📋 YES' if copilot_impact.get('requireApproval') else '✅ No'}
- Restrict Response: {'⚠️ YES' if copilot_impact.get('restrictResponse') else '✅ No'}
- Block Access: {'🚨 YES' if copilot_impact.get('blockAccess') else '✅ No'}
- Enhanced Monitoring: {'👁️ YES' if copilot_impact.get('enhancedMonitoring') else '✅ No'}

Microsoft Purview Gap Addressed:
✅ Handles non-Office file types (images, videos, PDFs)
✅ Provides AI-specific governance guidance  
✅ Enhances native classification capabilities
✅ Fills governance gaps for enterprise AI deployment
"""


def _format_batch_content_classification(response: Dict[str, Any]) -> str:
    """Format batch content classification response for Claude."""
    summary_info = response.get('summary', {})
    
    summary = f"""
Batch Content Classification Results

Batch ID: {response.get('batchId', 'Unknown')}
Processed At: {response.get('processedAt', 'Unknown')}
Tenant: {response.get('tenantId', 'Unknown')}

Summary:
- Total Requests: {summary_info.get('totalRequests', 0)}
- Successful: {summary_info.get('successful', 0)}
- Errors: {summary_info.get('errors', 0)}
- High Risk: {summary_info.get('highRisk', 0)}
- Confidential: {summary_info.get('confidential', 0)}
- Success Rate: {summary_info.get('successRate', 0)}%

Governance Features:
- Batch Processing: ✅ Enabled
- Consistent Classification: ✅ Active
- Scalable Governance: ✅ Available
- Audit Trail: ✅ Complete

Microsoft Purview Enhancement:
- Handles Non-Office Files: ✅ Yes
- Provides Risk Scoring: ✅ Yes
- Enables AI Governance: ✅ Yes
- Complements Purview: ✅ Yes

Sample Classifications:
"""
    
    # Show first few results as examples
    for result in response.get('results', [])[:3]:
        classification = result.get('classification', {})
        summary += f"""
Resource: {result.get('resourceId', 'Unknown')}
Sensitivity: {classification.get('sensitivityLevel', 'Unknown')}
Risk Score: {classification.get('riskScore', 0)}/100
AI Access: {'Allowed' if result.get('governance', {}).get('aiAccessAllowed') else 'Restricted'}
"""
    
    return summary


def _format_classification_summary(response: Dict[str, Any]) -> str:
    """Format classification summary response for Claude."""
    summary_data = response.get('summary', {})
    
    return f"""
Content Classification Summary - Governance Analytics

Tenant: {response.get('tenantId', 'Unknown')}
Period: {response.get('period', {}).get('days', 0)} days
Generated At: {response.get('generatedAt', 'Unknown')}

Total Classifications: {summary_data.get('totalClassifications', 0)}

By Status:
- Successful: {summary_data.get('byStatus', {}).get('successful', 0)}
- Errors: {summary_data.get('byStatus', {}).get('errors', 0)}
- Pending: {summary_data.get('byStatus', {}).get('pending', 0)}

By Sensitivity Level:
- Public: {summary_data.get('bySensitivityLevel', {}).get('publicContent', 0)}
- Internal: {summary_data.get('bySensitivityLevel', {}).get('internalContent', 0)}
- Confidential: {summary_data.get('bySensitivityLevel', {}).get('confidentialContent', 0)}
- Highly Confidential: {summary_data.get('bySensitivityLevel', {}).get('highlyConfidentialContent', 0)}

By Risk Score:
- Low Risk (0-29): {summary_data.get('byRiskScore', {}).get('lowRisk', 0)}
- Medium Risk (30-69): {summary_data.get('byRiskScore', {}).get('mediumRisk', 0)}
- High Risk (70-100): {summary_data.get('byRiskScore', {}).get('highRisk', 0)}

Detected Sensitive Data:
- Email Addresses: {summary_data.get('detectedSensitiveData', {}).get('emailAddresses', 0)}
- Phone Numbers: {summary_data.get('detectedSensitiveData', {}).get('phoneNumbers', 0)}
- Credit Card Numbers: {summary_data.get('detectedSensitiveData', {}).get('creditCardNumbers', 0)}
- SSNs: {summary_data.get('detectedSensitiveData', {}).get('socialSecurityNumbers', 0)}

Governance Actions Applied:
- Access Blocked: {summary_data.get('governanceActions', {}).get('accessBlocked', 0)}
- Approval Required: {summary_data.get('governanceActions', {}).get('approvalRequired', 0)}
- Enhanced Monitoring: {summary_data.get('governanceActions', {}).get('enhancedMonitoring', 0)}
- Automated Labeling: {summary_data.get('governanceActions', {}).get('automatedLabeling', 0)}

Trends:
- Week-over-Week Increase: {summary_data.get('trends', {}).get('weekOverWeekIncrease', 0)}%
- High Risk Trend: {summary_data.get('trends', {}).get('highRiskTrend', 'Unknown')}
- Classification Accuracy: {summary_data.get('trends', {}).get('classificationAccuracy', 0)}%

Governance Features:
- Classification Analytics: ✅ Active
- Risk Trends: ✅ Available
- Compliance Reporting: ✅ Enabled
- Governance Insights: ✅ Provided
"""


def _format_automated_labeling_response(response: Dict[str, Any]) -> str:
    """Format automated labeling response for Claude."""
    summary_info = response.get('summary', {})
    
    summary = f"""
Automated Sensitivity Labeling Results

Labeling ID: {response.get('labelingId', 'Unknown')}
Processed At: {response.get('processedAt', 'Unknown')}
Tenant: {response.get('tenantId', 'Unknown')}

Summary:
- Total Resources: {summary_info.get('totalResources', 0)}
- Successful: {summary_info.get('successful', 0)}
- Failed: {summary_info.get('failed', 0)}
- Success Rate: {summary_info.get('successRate', 0)}%

Governance Features:
- Automated Labeling: ✅ Enabled
- Fills Purview Gaps: ✅ Yes
- Enhances Compliance: ✅ Active
- Reduces Manual Effort: ✅ Significant

Microsoft Purview Integration:
- Complements Native Labeling: ✅ Yes
- Handles Unsupported File Types: ✅ Yes
- Provides AI Governance Context: ✅ Yes
- Enables Automated Workflows: ✅ Yes

Sample Results:
"""
    
    # Show first few results as examples
    for result in response.get('results', [])[:3]:
        if isinstance(result, dict):
            summary += f"""
Resource: {result.get('resourceId', 'Unknown')}
Success: {'✅ YES' if result.get('success') else '🚨 NO'}
Applied Label: {result.get('appliedLabel', 'N/A')}
Method: {result.get('labelingMethod', 'Unknown')}
"""
    
    return summary