"""Configuration management for Vaults MCP server."""

import os
from typing import Optional
from pydantic import validator
from pydantic_settings import BaseSettings


class Config(BaseSettings):
    """Configuration settings for Vaults MCP server."""
    
    # Production Function App Configuration
    base_url: str = "https://your-function-app.azurewebsites.net"
    function_key: Optional[str] = None
    
    # HTTP Client Configuration
    timeout: int = 30
    max_retries: int = 5
    retry_delay: float = 1.0
    
    # Logging Configuration
    log_level: str = "INFO"
    
    # MCP Server Configuration
    server_name: str = "vaults-mcp"
    server_version: str = "1.0.0"
    
    class Config:
        """Pydantic configuration."""
        env_prefix = "VAULTS_"
        env_file = ".env"
        case_sensitive = False

    @validator("base_url")
    def validate_base_url(cls, v: str) -> str:
        """Validate base URL format."""
        if not v.startswith(("http://", "https://")):
            raise ValueError("base_url must start with http:// or https://")
        return v.rstrip("/")
    
    @validator("timeout")
    def validate_timeout(cls, v: int) -> int:
        """Validate timeout is positive."""
        if v <= 0:
            raise ValueError("timeout must be positive")
        return v
    
    @validator("max_retries")
    def validate_max_retries(cls, v: int) -> int:
        """Validate max_retries is non-negative."""
        if v < 0:
            raise ValueError("max_retries must be non-negative")
        return v
    
    @validator("retry_delay")
    def validate_retry_delay(cls, v: float) -> float:
        """Validate retry_delay is non-negative."""
        if v < 0:
            raise ValueError("retry_delay must be non-negative")
        return v
    
    @validator("log_level")
    def validate_log_level(cls, v: str) -> str:
        """Validate log level."""
        valid_levels = {"DEBUG", "INFO", "WARNING", "ERROR", "CRITICAL"}
        if v.upper() not in valid_levels:
            raise ValueError(f"log_level must be one of: {', '.join(valid_levels)}")
        return v.upper()


def get_config() -> Config:
    """Get configuration instance."""
    return Config()