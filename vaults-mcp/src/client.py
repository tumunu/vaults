"""HTTP client for Vaults Azure Function App."""

import logging
from typing import Any, Dict, Optional, Union
import httpx
from tenacity import (
    retry,
    stop_after_attempt,
    wait_exponential,
    retry_if_exception_type,
    before_sleep_log,
)

from .config import Config
from .exceptions import (
    APIError,
    AuthenticationError,
    NetworkError,
    RateLimitError,
    ServerError,
    TimeoutError,
)

logger = logging.getLogger(__name__)


class VaultsClient:
    """Async HTTP client for Vaults Azure Function App."""
    
    def __init__(self, config: Config):
        """Initialize the client with configuration."""
        self.config = config
        self.base_url = config.base_url
        self.function_key = config.function_key
        self.timeout = config.timeout
        
        # Create async HTTP client
        self.client = httpx.AsyncClient(
            base_url=self.base_url,
            timeout=httpx.Timeout(self.timeout),
            headers=self._get_default_headers(),
            follow_redirects=True,
        )
    
    def _get_default_headers(self) -> Dict[str, str]:
        """Get default headers for requests."""
        return {
            "Content-Type": "application/json",
            "User-Agent": f"Vaults-MCP-Server/{self.config.server_version}",
            "Accept": "application/json",
        }
    
    def _get_auth_headers(self) -> Dict[str, str]:
        """Get authentication headers for Function-level endpoints."""
        headers = {}
        if self.function_key:
            headers["x-functions-key"] = self.function_key
        return headers
    
    def _handle_response_error(self, response: httpx.Response) -> None:
        """Handle HTTP response errors."""
        status_code = response.status_code
        content_type = response.headers.get("content-type", "").lower()
        
        try:
            error_data = response.json()
            error_message = error_data.get("error", f"HTTP {status_code}")
            error_details = error_data.get("details")
        except Exception:
            # Check if this is an HTML error page
            if "text/html" in content_type:
                error_message = f"Received HTML error page instead of JSON (HTTP {status_code})"
                error_details = "The server returned an HTML error page, likely due to an unhandled exception"
            else:
                error_message = f"HTTP {status_code}: {response.text[:200]}..."  # Truncate long responses
                error_details = None
        
        if status_code == 401:
            raise AuthenticationError(error_message, error_details)
        elif status_code == 429:
            retry_after = response.headers.get("Retry-After")
            raise RateLimitError(
                error_message, 
                retry_after=int(retry_after) if retry_after else None,
                details=error_details
            )
        elif 400 <= status_code < 500:
            raise APIError(error_message, status_code, error_details)
        elif 500 <= status_code < 600:
            raise ServerError(error_message, error_details)
        else:
            raise APIError(error_message, status_code, error_details)
    
    @retry(
        stop=stop_after_attempt(5),
        wait=wait_exponential(multiplier=1, min=1, max=10),
        retry=retry_if_exception_type((NetworkError, ServerError, TimeoutError)),
        before_sleep=before_sleep_log(logger, logging.WARNING),
    )
    async def _make_request(
        self,
        method: str,
        path: str,
        params: Optional[Dict[str, Any]] = None,
        json_data: Optional[Dict[str, Any]] = None,
        requires_auth: bool = False,
    ) -> Dict[str, Any]:
        """Make HTTP request with retry logic."""
        url = path if path.startswith("http") else path
        headers = self._get_auth_headers() if requires_auth else {}
        
        try:
            logger.debug(f"Making {method} request to {url}")
            
            response = await self.client.request(
                method=method,
                url=url,
                params=params,
                json=json_data,
                headers=headers,
            )
            
            logger.debug(f"Response status: {response.status_code}")
            
            if not response.is_success:
                self._handle_response_error(response)
            
            # Handle empty responses
            if not response.content:
                return {}
            
            # Check content type before parsing JSON
            content_type = response.headers.get("content-type", "").lower()
            if "text/html" in content_type:
                # Server returned HTML instead of JSON - likely an error page
                raise ServerError(
                    f"Received HTML response instead of JSON (HTTP {response.status_code})",
                    "The server returned an HTML error page, likely due to an unhandled exception"
                )
            
            try:
                return response.json()
            except Exception as e:
                # If JSON parsing fails, provide helpful error message
                raise APIError(
                    f"Failed to parse JSON response (HTTP {response.status_code}): {str(e)}"
                )
            
        except httpx.TimeoutException as e:
            logger.error(f"Request timeout: {e}")
            raise TimeoutError(f"Request timed out after {self.timeout}s")
        except httpx.NetworkError as e:
            logger.error(f"Network error: {e}")
            raise NetworkError(f"Network error: {e}")
        except httpx.HTTPStatusError as e:
            logger.error(f"HTTP error: {e}")
            self._handle_response_error(e.response)
        except Exception as e:
            logger.error(f"Unexpected error: {e}")
            raise APIError(f"Unexpected error: {e}")
    
    async def get(
        self, 
        path: str, 
        params: Optional[Dict[str, Any]] = None, 
        requires_auth: bool = False
    ) -> Dict[str, Any]:
        """Make GET request."""
        return await self._make_request("GET", path, params=params, requires_auth=requires_auth)
    
    async def post(
        self,
        path: str,
        json_data: Optional[Dict[str, Any]] = None,
        params: Optional[Dict[str, Any]] = None,
        requires_auth: bool = False,
    ) -> Dict[str, Any]:
        """Make POST request."""
        return await self._make_request(
            "POST", path, params=params, json_data=json_data, requires_auth=requires_auth
        )
    
    async def put(
        self,
        path: str,
        json_data: Optional[Dict[str, Any]] = None,
        params: Optional[Dict[str, Any]] = None,
        requires_auth: bool = False,
    ) -> Dict[str, Any]:
        """Make PUT request."""
        return await self._make_request(
            "PUT", path, params=params, json_data=json_data, requires_auth=requires_auth
        )
    
    async def delete(
        self,
        path: str,
        params: Optional[Dict[str, Any]] = None,
        requires_auth: bool = False,
    ) -> Dict[str, Any]:
        """Make DELETE request."""
        return await self._make_request("DELETE", path, params=params, requires_auth=requires_auth)
    
    async def close(self) -> None:
        """Close the HTTP client."""
        await self.client.aclose()
    
    async def __aenter__(self):
        """Async context manager entry."""
        return self
    
    async def __aexit__(self, exc_type, exc_val, exc_tb):
        """Async context manager exit."""
        await self.close()