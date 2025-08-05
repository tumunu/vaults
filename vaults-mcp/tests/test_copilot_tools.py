"""Unit tests for copilot_tools.py module."""

import pytest
from unittest.mock import AsyncMock, MagicMock
from httpx import Response
from pydantic import ValidationError
from typing import Sequence
from mcp.types import TextContent

from src.tools.copilot_tools import (
    get_copilot_root,
    get_copilot_users,
    get_interaction_history,
    copilot_retrieve_content,
    get_copilot_usage_summary,
    get_copilot_user_count,
    get_security_alerts,
    get_high_risk_users,
    get_policy_violations
)

@pytest.fixture
def mock_client():
    """Fixture providing a mocked VaultsClient."""
    client = AsyncMock()
    client.get = AsyncMock()
    client.post = AsyncMock()
    return client

@pytest.mark.asyncio
async def test_get_copilot_root_success(mock_client):
    """Test successful get_copilot_root tool execution."""
    # Setup mock response
    mock_response = {
        "id": "copilot-root",
        "displayName": "Microsoft 365 Copilot API",
        "description": "Root endpoint for Copilot API",
        "users": {"href": "/api/copilot/users"},
        "interactionHistory": {"href": "/api/copilot/interactions"},
        "searchFunction": {"href": "/api/copilot/search"}
    }
    mock_client.get.return_value = mock_response

    # Execute tool
    result = await get_copilot_root(mock_client, {})

    # Verify results
    assert isinstance(result, Sequence)
    assert len(result) == 1
    content = result[0]
    assert isinstance(content, TextContent)
    assert "Microsoft 365 Copilot API" in content.text
    assert "/api/copilot/users" in content.text
    mock_client.get.assert_awaited_once_with("/api/copilot", requires_auth=True)

@pytest.mark.asyncio
async def test_get_copilot_root_error(mock_client):
    """Test get_copilot_root with API error."""
    mock_client.get.side_effect = Exception("API unavailable")

    result = await get_copilot_root(mock_client, {})

    assert len(result) == 1
    assert "API unavailable" in result[0].text

@pytest.mark.asyncio
async def test_get_copilot_users_success(mock_client):
    """Test successful get_copilot_users execution."""
    mock_response = {
        "users": [
            {
                "id": "user1",
                "displayName": "Test User",
                "email": "test@example.com",
                "copilotEnabled": True,
                "lastActivity": "2025-05-01T12:00:00Z"
            }
        ]
    }
    mock_client.get.return_value = mock_response

    result = await get_copilot_users(mock_client, {"tenant_id": "tenant123"})

    assert len(result) == 1
    content = result[0]
    assert "Test User" in content.text
    assert "test@example.com" in content.text
    assert "Yes" in content.text  # copilotEnabled
    mock_client.get.assert_awaited_once_with(
        "/api/vaults/users",
        params={"tenantId": "tenant123"},
        requires_auth=True
    )

@pytest.mark.asyncio
async def test_get_copilot_users_missing_tenant(mock_client):
    """Test get_copilot_users with missing tenant_id."""
    with pytest.raises(ValidationError):
        await get_copilot_users(mock_client, {})

@pytest.mark.asyncio
async def test_get_interaction_history_success(mock_client):
    """Test successful get_interaction_history execution."""
    mock_response = {
        "value": [
            {
                "id": "interaction1",
                "userId": "user1",
                "sessionId": "session1",
                "createdDateTime": "2025-05-01T12:00:00Z",
                "userPrompt": "What's the weather?",
                "aiResponse": "The weather is sunny."
            }
        ],
        "@odata.nextLink": "next-page-link"
    }
    mock_client.get.return_value = mock_response

    result = await get_interaction_history(mock_client, {
        "tenant_id": "tenant123",
        "top": 5,
        "filter": "createdDateTime gt 2025-01-01"
    })

    assert len(result) == 1
    content = result[0]
    assert "interaction1" in content.text
    assert "next-page-link" in content.text
    mock_client.get.assert_awaited_once_with(
        "/api/vaults/interactionHistory",
        params={
            "tenantId": "tenant123",
            "$top": 5,
            "$filter": "createdDateTime gt 2025-01-01"
        }
    )

@pytest.mark.asyncio
async def test_copilot_retrieve_content_success(mock_client):
    """Test successful copilot_retrieve_content execution."""
    mock_response = {
        "results": [
            {
                "id": "result1",
                "title": "Test Document",
                "url": "https://example.com/doc1",
                "lastModified": "2025-05-01T12:00:00Z",
                "content": "This is test content"
            }
        ]
    }
    mock_client.post.return_value = mock_response

    result = await copilot_retrieve_content(mock_client, {
        "query": "test query",
        "filters": {"type": "document"}
    })

    assert len(result) == 1
    content = result[0]
    assert "Test Document" in content.text
    assert "https://example.com/doc1" in content.text
    mock_client.post.assert_awaited_once_with(
        "/api/vaults/retrieve",
        json_data={
            "query": "test query",
            "filters": {"type": "document"}
        }
    )

@pytest.mark.asyncio
async def test_get_copilot_usage_summary_success(mock_client):
    """Test successful get_copilot_usage_summary execution."""
    mock_response = {
        "value": [{
            "reportDate": "2025-05-01",
            "reportRefreshDate": "2025-05-02",
            "reportPeriod": "D7",
            "copilotEnabledUsers": 100,
            "copilotActiveUsers": 75,
            "utilizationRate": 75.0
        }]
    }
    mock_client.get.return_value = mock_response

    result = await get_copilot_usage_summary(mock_client, {
        "period": "D7"
    })

    assert len(result) == 1
    content = result[0]
    assert "100" in content.text  # copilotEnabledUsers
    assert "75%" in content.text  # utilizationRate
    mock_client.get.assert_awaited_once_with(
        "/api/vaults/usage/summary",
        params={"period": "D7"},
        requires_auth=True
    )

@pytest.mark.asyncio
async def test_get_security_alerts_success(mock_client):
    """Test successful get_security_alerts execution."""
    mock_response = {
        "value": [{
            "id": "alert1",
            "displayName": "Suspicious Login",
            "category": "Malware",
            "severity": "high",
            "status": "active",
            "createdDateTime": "2025-05-01T12:00:00Z",
            "assignedTo": "security@example.com"
        }]
    }
    mock_client.get.return_value = mock_response

    result = await get_security_alerts(mock_client)

    assert len(result) == 1
    content = result[0]
    assert "Suspicious Login" in content.text
    assert "HIGH" in content.text  # severity uppercased
    mock_client.get.assert_awaited_once_with(
        "/api/v1/copilot/security/alerts",
        requires_auth=True
    )

@pytest.mark.asyncio
async def test_get_high_risk_users_success(mock_client):
    """Test successful get_high_risk_users execution."""
    mock_response = {
        "value": [{
            "id": "user1",
            "userDisplayName": "Risk User",
            "userPrincipalName": "risk@example.com",
            "riskLevel": "high",
            "riskState": "atRisk",
            "riskDetail": "unfamiliarLocation",
            "riskLastUpdatedDateTime": "2025-05-01T12:00:00Z"
        }]
    }
    mock_client.get.return_value = mock_response

    result = await get_high_risk_users(mock_client)

    assert len(result) == 1
    content = result[0]
    assert "Risk User" in content.text
    assert "HIGH" in content.text  # riskLevel uppercased
    mock_client.get.assert_awaited_once_with(
        "/api/v1/copilot/security/risky-users",
        requires_auth=True
    )

@pytest.mark.asyncio
async def test_get_policy_violations_success(mock_client):
    """Test successful get_policy_violations execution."""
    mock_response = {
        "value": [{
            "id": "violation1",
            "displayName": "Data Policy Violation",
            "partnerTenantId": "partner123",
            "partnerState": "active",
            "lastHeartbeatDateTime": "2025-05-01T12:00:00Z"
        }]
    }
    mock_client.get.return_value = mock_response

    result = await get_policy_violations(mock_client)

    assert len(result) == 1
    content = result[0]
    assert "Data Policy Violation" in content.text
    assert "partner123" in content.text
    mock_client.get.assert_awaited_once_with(
        "/api/v1/copilot/compliance/violations",
        requires_auth=True
    )
