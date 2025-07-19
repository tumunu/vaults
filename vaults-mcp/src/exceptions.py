"""Custom exceptions for Vaults MCP server."""

from typing import Optional


class VaultsException(Exception):
    """Base exception for Vaults MCP server."""
    
    def __init__(self, message: str, details: Optional[str] = None):
        super().__init__(message)
        self.message = message
        self.details = details


class AuthenticationError(VaultsException):
    """Raised when authentication fails."""
    pass


class APIError(VaultsException):
    """Raised when API call fails."""
    
    def __init__(
        self, 
        message: str, 
        status_code: Optional[int] = None, 
        details: Optional[str] = None
    ):
        super().__init__(message, details)
        self.status_code = status_code


class ConfigurationError(VaultsException):
    """Raised when configuration is invalid."""
    pass


class ValidationError(VaultsException):
    """Raised when input validation fails."""
    pass


class RateLimitError(VaultsException):
    """Raised when rate limit is exceeded."""
    
    def __init__(
        self, 
        message: str, 
        retry_after: Optional[int] = None, 
        details: Optional[str] = None
    ):
        super().__init__(message, details)
        self.retry_after = retry_after


class ServerError(VaultsException):
    """Raised when server encounters an internal error."""
    pass


class TimeoutError(VaultsException):
    """Raised when request times out."""
    pass


class NetworkError(VaultsException):
    """Raised when network connectivity issues occur."""
    pass