"""Pytest configuration and fixtures for Vaults MCP Server tests."""

import pytest
import asyncio
from unittest.mock import AsyncMock, MagicMock

from src.config import Config
from src.client import VaultsClient


@pytest.fixture
def config():
    """Test configuration."""
    return Config(
        base_url="https://test.example.com",
        function_key="test-key",
        timeout=10,
        max_retries=3,
        log_level="DEBUG"
    )


@pytest.fixture
def mock_client():
    """Mock HTTP client for testing."""
    client = AsyncMock(spec=VaultsClient)
    return client


@pytest.fixture
def event_loop():
    """Create an instance of the default event loop for the test session."""
    loop = asyncio.get_event_loop_policy().new_event_loop()
    yield loop
    loop.close()


@pytest.fixture
def sample_health_response():
    """Sample health check response."""
    return {
        "status": "healthy",
        "timestamp": "2025-06-18T10:30:00Z",
        "duration": 1250.5,
        "message": "Overall health: healthy",
        "components": {
            "CosmosDB": {
                "status": "healthy",
                "duration": 45.2,
                "message": "CosmosDB is accessible"
            }
        },
        "version": "1.0.0",
        "environment": "production"
    }


@pytest.fixture
def sample_admin_stats():
    """Sample admin stats response."""
    return {
        "TenantId": "tenant-123",
        "TotalUsers": 50,
        "ActiveUsers": 40,
        "TotalPolicies": 5,
        "ActivePolicies": 4,
        "HighRiskPolicies": 2,
        "TotalInteractions": 1000,
        "InteractionsWithPii": 5,
        "PolicyViolations": 3,
        "LastSyncTime": "2025-06-18T10:30:00Z",
        "ProcessedAt": "2025-06-18T10:30:00Z"
    }