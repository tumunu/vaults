// Vaults Global Using Directives
// This file makes the following namespaces available across all C# files
// without explicit using statements. This improves code readability and
// reduces repetitive imports across the Function App.
//
// When adding new global usings:
// 1. Ensure they are used in 80%+ of files
// 2. Add them alphabetically within their category
// 3. Update this documentation

// Core .NET namespaces
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Net;
global using System.Text.Json;
global using System.Threading;
global using System.Threading.Tasks;

// Azure Functions Worker namespaces
global using Microsoft.Azure.Functions.Worker;
global using Microsoft.Azure.Functions.Worker.Http;

// Microsoft Extensions (Logging, Configuration, DI)
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;

// Azure SDK namespaces (commonly used)
global using Microsoft.Azure.Cosmos;

// Vaults Core namespaces
global using VaultsFunctions.Core.Extensions;
global using VaultsFunctions.Core.Helpers;
global using VaultsFunctions.Core.Models;
global using VaultsFunctions.Core.Services;

// Third-party libraries (commonly used)
// Note: Newtonsoft.Json removed to avoid ambiguity with System.Text.Json
// Use explicit namespace if Newtonsoft.Json is needed in specific files