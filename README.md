# Vaults - Microsoft 365 Copilot Governance Platform

**Open-source enterprise platform for Microsoft 365 Copilot governance, auditing, and risk management**

*Developed by [Tumunu](https://tumunu.com) - Security and digital forensics tools that demonstrate real capability*

[![Production Ready](https://img.shields.io/badge/status-production%20ready-green)](docs/DEPLOYMENT.md)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![React 18](https://img.shields.io/badge/React-18.0-blue)](https://reactjs.org/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.0-blue)](https://www.typescriptlang.org/)
[![Azure Functions](https://img.shields.io/badge/Azure%20Functions-v4-blue)](https://docs.microsoft.com/azure/azure-functions/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Overview

Vaults is a comprehensive governance platform that addresses Microsoft's acknowledged gaps in Copilot oversight. Built for enterprise administrators, CISOs, and compliance teams to monitor, govern, and audit Microsoft 365 Copilot usage across their organisation.

### Key Features

**Real-time Governance Controls**
- Permission right-sizing and violation prevention
- Content classification for non-Office files
- AI response filtering and risk assessment

**Executive Risk Scorecard**
- AI risk scoring with trend analysis
- Governance maturity assessment
- ROI tracking and business justification

**Security and Compliance**
- Microsoft Graph Security API integration
- Identity Protection and risky user detection
- GDPR, SOC2, and ISO27001 compliance reporting

**Enterprise Billing**
- Seat-based licencing and management
- Stripe integration for payment processing
- Usage analytics and cost optimisation

## Quick Start

### Prerequisites

- **Microsoft 365 Copilot Licence** for your organisation
- **Azure AD Premium P2** (recommended for full features)
- **Azure Subscription** for infrastructure deployment
- **.NET 8 SDK** and **Node.js 18+** for local development

### 1. Clone the Repository

```bash
git clone https://github.com/your-org/vaults.git
cd vaults
```

### 2. Azure Infrastructure Setup

Deploy the required Azure resources using the provided Bicep templates:

```bash
cd vaults-infrastructure
az deployment group create \
  --resource-group your-resource-group \
  --template-file main.bicep \
  --parameters @parameters.json
```

### 3. Configure Azure AD Permissions

Grant these permissions to your Azure AD application:

| Permission | Type | Purpose |
|------------|------|---------|
| `AiEnterpriseInteraction.Read.All` | Application | Copilot interaction data |
| `Reports.Read.All` | Application | Usage metrics |
| `SecurityEvents.Read.All` | Application | Security alerts |
| `IdentityRiskEvent.Read.All` | Application | Risky users |
| `InformationProtectionPolicy.Read.All` | Application | Compliance policies |

### 4. Local Development

**Backend Azure Functions:**
```bash
cd vaults-function-app
cp local.settings.example.json local.settings.json
# Configure local.settings.json with your Azure credentials
dotnet restore
func start
```

**Frontend React Dashboard:**
```bash
cd vaults-web
cp .env.example .env.local
# Configure .env.local with your environment settings
npm install
npm run dev
```

**MCP Server for Claude Integration:**
```bash
cd vaults-mcp
cp .env.example .env
# Configure .env with your Function App credentials
python -m venv venv
source venv/bin/activate
pip install -r requirements.txt
python -m src.server
```

## Architecture

### Core Components

- **`vaults-function-app/`** - .NET 8 Azure Functions backend API
- **`vaults-web/`** - React 18 + TypeScript web dashboard
- **`vaults-mcp/`** - Python MCP server for Claude integration
- **`vaults-infrastructure/`** - Bicep templates for Azure deployment

### Technology Stack

**Backend:**
- Azure Functions v4 (.NET 8 isolated)
- Microsoft Graph API integration
- Azure Cosmos DB for data storage
- Application Insights for monitoring

**Frontend:**
- React 18 with TypeScript
- Material-UI component library
- Azure AD MSAL authentication
- Vite build system

**Infrastructure:**
- Azure Function Apps
- Azure Cosmos DB
- Azure Storage Accounts
- Azure Key Vault for secrets
- Application Insights

## Key Features

### Governance-First Architecture

Addresses Microsoft's acknowledged gaps in Copilot governance:

- **Permission Right-sizing**: Prevent over-privileged access before it becomes a risk
- **Content Classification**: AI-powered classification for non-Office files
- **Real-time Monitoring**: Live threat detection and automatic response
- **Policy Enforcement**: Automated governance controls and violation prevention

### Executive Dashboard

Purpose-built for CISOs and executive teams:

- **Risk Scorecard**: AI-powered risk assessment with trend analysis
- **ROI Tracking**: Quantified business value and investment returns
- **Compliance Reporting**: Board-ready reports for SOC2, ISO27001, GDPR
- **Business Justification**: Data-driven insights for budget decisions

### Enterprise Security

Production-ready security features:

- **Multi-tenant Architecture**: Complete data isolation per organisation
- **Azure AD Integration**: Enterprise SSO and role-based access
- **Microsoft Graph APIs**: Official Copilot data sources
- **Audit Logging**: Comprehensive activity tracking

### Demonstration Capabilities

Designed for vendor evaluations and executive presentations:

- **Problem Visualization**: Rapid identification of over-privileged access risks within 30 seconds
- **Solution Demonstration**: Live governance controls showcase within 60 seconds
- **ROI Validation**: Quantified risk reduction and business value presentation within 30 seconds

## API Documentation

Vaults provides 40+ REST API endpoints organized into logical categories:

### Health & Monitoring
- `GET /api/health` - Comprehensive system health check
- `GET /api/health/live` - Liveness probe for Kubernetes
- `GET /api/health/ready` - Readiness validation

### Microsoft Graph Integration
- `GET /api/vaults/usage/summary` - Official Copilot usage metrics
- `GET /api/vaults/security/alerts` - Security alerts from Graph API
- `GET /api/vaults/security/risky-users` - Identity Protection data

### Governance & Compliance
- `GET /api/policies` - Audit policy management
- `POST /api/governance/classify-content` - AI content classification
- `GET /api/governance/risk-assessment` - Real-time risk scoring

### Enterprise Billing
- `POST /api/v1/stripe/payment-links` - Seat-based billing
- `GET /api/v1/stripe/seats/{tenantId}` - Seat allocation status
- `GET /api/v1/stripe/billing/{tenantId}` - Billing information

## Development

### Project Structure

```
vaults/
├── vaults-function-app/           # Backend API (.NET 8)
│   ├── Functions/                 # Azure Function endpoints
│   ├── Core/Services/             # Business logic services
│   ├── Tests/                     # Unit and integration tests
│   └── Documentation/             # API documentation
├── vaults-web/                    # Frontend dashboard (React)
│   ├── src/pages/dashboard/       # Dashboard pages
│   ├── src/components/            # Reusable UI components
│   ├── src/hooks/                 # API integration hooks
│   └── src/utils/                 # Utility functions
├── vaults-mcp/                    # MCP server (Python)
│   ├── src/tools/                 # MCP tool implementations
│   ├── tests/                     # Python tests
│   └── docs/                      # MCP documentation
├── vaults-infrastructure/         # Azure infrastructure (Bicep)
├── docs/                          # Project documentation
└── .private-config/               # Local configuration (not in git)
```

### Development Commands

**Backend Operations:**
```bash
cd vaults-function-app
dotnet test                    # Execute test suite
dotnet build                   # Build application
func start                     # Start local development server
```

**Frontend Operations:**
```bash
cd vaults-web
npm test                       # Execute test suite
npm run build                  # Generate production build
npm run dev                    # Start development server
```

**MCP Server Operations:**
```bash
cd vaults-mcp
pytest                         # Execute test suite
python -m src.server           # Start MCP server
python validate_tools.py       # Validate tool implementations
```

### Testing

- **Unit Tests**: Comprehensive test coverage for all components
- **Integration Tests**: End-to-end API testing
- **Security Tests**: Authentication and authorization validation
- **Performance Tests**: Load testing for enterprise scale

## Deployment

### Production Deployment

1. **Infrastructure**: Deploy Azure resources using Bicep templates
2. **Backend**: Deploy Function App using GitHub Actions or Azure DevOps
3. **Frontend**: Build and deploy to Azure Static Web Apps or CDN
4. **Configuration**: Set up Azure Key Vault for secrets management

### Environment Configuration

Required environment variables for production:

```bash
# Azure AD Authentication
AZURE_TENANT_ID=your-tenant-id
AZURE_CLIENT_ID=your-client-id
AZURE_CLIENT_SECRET=your-client-secret

# Database and Storage
COSMOS_DB_CONNECTION_STRING=your-cosmos-connection
AZURE_STORAGE_CONNECTION_STRING=your-storage-connection

# External Services
STRIPE_SECRET_KEY=your-stripe-key
SENDGRID_API_KEY=your-sendgrid-key
```

See [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) for complete deployment instructions.

## Documentation

Comprehensive documentation is available in the [`docs/`](docs/) directory:

- **[API Documentation](docs/API_DOCUMENTATION.md)** - Complete API reference
- **[Deployment Guide](docs/DEPLOYMENT.md)** - Production deployment instructions
- **[Azure Setup Guide](docs/AZURE-SETUP-GUIDE.md)** - Azure AD and infrastructure setup
- **[Testing Guide](docs/TESTING-GUIDE.md)** - Testing strategies and procedures
- **[MCP Integration](docs/CLAUDE_MCP_SERVER_IMPLEMENTATION.md)** - Claude Code integration

## Contributing

We welcome contributions to the Vaults platform. Please see our [Contributing Guide](CONTRIBUTING.md) for detailed guidelines and procedures.

### Development Setup

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

### Code Standards

- **Backend**: Follow .NET coding conventions and use EditorConfig
- **Frontend**: Use TypeScript strict mode and follow React best practices
- **Python**: Follow PEP 8 and use type hints
- **Documentation**: Update relevant documentation for any changes

## Security

### Reporting Security Issues

Please report security vulnerabilities to security@vaults.com. Do not create public GitHub issues for security problems.

### Security Features

- **Enterprise Authentication**: Azure AD integration with MFA support
- **Data Encryption**: End-to-end encryption for all data transmission
- **Access Controls**: Role-based access with principle of least privilege
- **Audit Logging**: Comprehensive activity tracking and monitoring
- **Secret Management**: Azure Key Vault integration for sensitive data

## Licence

This project is licenced under the MIT Licence - see the [LICENCE](LICENSE) file for details.

## Support

### Community Support

- **GitHub Issues**: Bug reports and feature requests
- **Discussions**: Community discussions and questions
- **Documentation**: Comprehensive guides and API references

### Enterprise Support

For enterprise customers requiring dedicated support, please contact [Tumunu](mailto:project@tumunu.com).

## Roadmap

### Current Focus (Q4 2024)

- **Governance-First Architecture**: Real-time permission controls (Completed)
- **Executive Dashboard**: Risk scorecard and ROI tracking (Completed)
- **Microsoft Graph Integration**: Official Copilot APIs (Completed)
- **Production Readiness**: 39/40 endpoints operational (Completed)

### Upcoming Features (Q1 2025)

- **Advanced Analytics**: Machine learning risk prediction (In Development)
- **Mobile App**: Flutter mobile dashboard (In Development)
- **API Gateway**: APIM integration for enterprise scale (In Development)
- **Multi-Cloud**: AWS and GCP deployment options (In Development)

---

**Built for enterprise Microsoft 365 Copilot governance by Tumunu**

Tumunu develops practical security and digital forensics tools that demonstrate real capability. For questions, feedback, or support, please visit our [GitHub Discussions](https://github.com/your-org/vaults/discussions) or contact [Tumunu](mailto:project@tumunu.com).