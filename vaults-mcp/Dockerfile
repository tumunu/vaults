# CopilotVault MCP Server Dockerfile
FROM python:3.11-slim

# Set working directory
WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y \
    --no-install-recommends \
    build-essential \
    && rm -rf /var/lib/apt/lists/*

# Copy requirements first for better caching
COPY requirements.txt .

# Install Python dependencies
RUN pip install --no-cache-dir -r requirements.txt

# Copy application code
COPY src/ ./src/
COPY pyproject.toml .
COPY README.md .
COPY CLAUDE.md .

# Install the package
RUN pip install -e .

# Create non-root user
RUN useradd --create-home --shell /bin/bash mcp
RUN chown -R mcp:mcp /app
USER mcp

# Environment variables
ENV PYTHONPATH=/app
ENV COPILOT_VAULT_LOG_LEVEL=INFO

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD python -c "import asyncio; from src.client import CopilotVaultClient; from src.config import get_config; \
    asyncio.run(CopilotVaultClient(get_config()).get('/api/health'))" || exit 1

# Expose port (if needed for future HTTP interface)
EXPOSE 8000

# Run the MCP server
CMD ["python", "-m", "src.server"]