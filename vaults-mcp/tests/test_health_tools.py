"""Tests for health monitoring tools."""

import pytest
from unittest.mock import AsyncMock

from src.tools import health_tools


@pytest.mark.asyncio
async def test_health_check_success(mock_client, sample_health_response):
    """Test successful health check."""
    mock_client.get.return_value = sample_health_response
    
    result = await health_tools.health_check(mock_client)
    
    assert len(result) == 1
    content = result[0].content
    assert "System Health Status: HEALTHY" in content
    assert "CosmosDB: healthy" in content
    assert "Version: 1.0.0" in content
    
    mock_client.get.assert_called_once_with("/api/health")


@pytest.mark.asyncio
async def test_health_check_failure(mock_client):
    """Test health check with API failure."""
    mock_client.get.side_effect = Exception("Connection failed")
    
    result = await health_tools.health_check(mock_client)
    
    assert len(result) == 1
    content = result[0].content
    assert "Health check failed: Connection failed" in content


@pytest.mark.asyncio
async def test_get_service_bus_health_success(mock_client):
    """Test successful Service Bus health check."""
    mock_response = {
        "overall": {
            "status": "healthy",
            "available": True,
            "monitoringEnabled": True
        },
        "queues": {
            "inviteQueue": {
                "name": "invite-queue",
                "status": "healthy",
                "available": True,
                "metrics": {
                    "activeMessages": 0,
                    "deadLetterMessages": 0,
                    "totalMessages": 0
                }
            }
        }
    }
    mock_client.get.return_value = mock_response
    
    result = await health_tools.get_service_bus_health(mock_client)
    
    assert len(result) == 1
    content = result[0].content
    assert "Service Bus Health Status: HEALTHY" in content
    assert "invite-queue: healthy" in content
    
    mock_client.get.assert_called_once_with("/api/admin/servicebus/health", requires_auth=True)


@pytest.mark.asyncio
async def test_get_queue_metrics_success(mock_client):
    """Test successful queue metrics retrieval."""
    mock_response = {
        "queueName": "invite-queue",
        "timestamp": "2025-06-18T10:30:00Z",
        "messages": {
            "active": 5,
            "deadLetter": 0,
            "total": 5
        },
        "size": {
            "currentBytes": 1024,
            "maxMegabytes": 1024,
            "utilizationPercent": 0.1
        }
    }
    mock_client.get.return_value = mock_response
    
    arguments = {"queue": "test-queue"}
    result = await health_tools.get_queue_metrics(mock_client, arguments)
    
    assert len(result) == 1
    content = result[0].content
    assert "Queue Metrics: invite-queue" in content
    assert "Active: 5" in content
    assert "Current Size: 1024 bytes" in content
    
    mock_client.get.assert_called_once_with(
        "/api/admin/servicebus/metrics", 
        params={"queue": "test-queue"}, 
        requires_auth=True
    )


@pytest.mark.asyncio
async def test_get_queue_metrics_default_queue(mock_client):
    """Test queue metrics with default queue name."""
    mock_client.get.return_value = {"queueName": "invite-queue"}
    
    arguments = {}
    result = await health_tools.get_queue_metrics(mock_client, arguments)
    
    mock_client.get.assert_called_once_with(
        "/api/admin/servicebus/metrics", 
        params={"queue": "invite-queue"}, 
        requires_auth=True
    )