---
title: "CISLIB repository profile"
type: repository-profile
status: Active
owner: Repository maintainer
review_cadence: on repository change
cis:
  stable_id: cislib:reference:repository-profile
---

# Repository profile

This inventory maps physical source directories to graph components. The registered project inventory comes from `implementations/csharp/CisLib.slnx`. Only CIS.Core is the shipping library; test, example, specification and development-tool components do not become runtime dependencies. C# is the current implementation. Future languages belong under `implementations/<language>/` and need their own declared capabilities.

## Components

### cis-core

- Root: `implementations/csharp/src/CIS.Core`
- Languages: csharp
- Frameworks: dotnet
- Roles: shared-library
- Capabilities: configuration, logging-values, exception-projection
- Confidence: high
- Evidence:
  - implementations/csharp/src/CIS.Core/CIS.Core.csproj

### cis-core-unittests

- Root: `implementations/csharp/tests/CIS.Core.UnitTests`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-suite
- Capabilities: verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj

### cis-core-integrationtests

- Root: `implementations/csharp/tests/CIS.Core.IntegrationTests`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-suite
- Capabilities: verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj

### cis-core-architecturetests

- Root: `implementations/csharp/tests/CIS.Core.ArchitectureTests`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-suite
- Capabilities: verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj

### cis-core-compatibilitytests

- Root: `implementations/csharp/tests/CIS.Core.CompatibilityTests`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-suite
- Capabilities: verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/CIS.Core.CompatibilityTests/CIS.Core.CompatibilityTests.csproj

### cis-core-businesscases

- Root: `implementations/csharp/tests/CIS.Core.BusinessCases`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-suite
- Capabilities: verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/CIS.Core.BusinessCases/CIS.Core.BusinessCases.csproj

### cis-core-testsupport

- Root: `implementations/csharp/tests/CIS.Core.TestSupport`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-support
- Capabilities: verification-support
- Confidence: high
- Evidence:
  - implementations/csharp/tests/CIS.Core.TestSupport/CIS.Core.TestSupport.csproj

### cis-core-discoveryfixture

- Root: `implementations/csharp/tests/fixtures/DiscoveryHost`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-fixture
- Capabilities: isolated-process-verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/fixtures/DiscoveryHost/CIS.Core.DiscoveryFixture.csproj

### cis-dependencychecker

- Root: `implementations/csharp/eng/CIS.DependencyChecker`
- Languages: csharp
- Frameworks: dotnet
- Roles: development-tool
- Capabilities: dependency-verification
- Confidence: high
- Evidence:
  - implementations/csharp/eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj

### consolehuman

- Root: `implementations/csharp/examples/logging/ConsoleHuman`
- Languages: csharp
- Frameworks: dotnet
- Roles: example
- Capabilities: source-reference-scaffold
- Confidence: high
- Evidence:
  - implementations/csharp/examples/logging/ConsoleHuman/ConsoleHuman.csproj

### logging-wire-examples

- Root: `implementations/csharp/eng/CIS.DependencyChecker/JsonlInputSelector.cs`
- Languages: csharp
- Frameworks: dotnet
- Roles: example-tool
- Capabilities: stopped-jsonl-input-selection
- Confidence: high
- Evidence:
  - implementations/csharp/eng/CIS.DependencyChecker/JsonlInputSelector.cs

### cis-typemanager-build

- Root: `implementations/csharp/eng/CIS.TypeManager.Build`
- Languages: csharp
- Frameworks: dotnet
- Roles: build-tool
- Capabilities: type-inventory-generation
- Confidence: high
- Evidence:
  - implementations/csharp/eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj

### typemanager-examples

- Root: `implementations/csharp/examples/type-manager`
- Languages: csharp
- Frameworks: dotnet, aspnetcore
- Roles: example
- Capabilities: console-host-integration, web-host-integration
- Confidence: high
- Evidence:
  - implementations/csharp/examples/type-manager/Console/Console.csproj
  - implementations/csharp/examples/type-manager/Web/Web.csproj

### typemanager-fixtures

- Root: `implementations/csharp/tests/fixtures/TypeManager`
- Languages: csharp
- Frameworks: dotnet
- Roles: test-fixture
- Capabilities: inventory-runtime-verification
- Confidence: high
- Evidence:
  - implementations/csharp/tests/fixtures/TypeManager/Host/Host.csproj

### cis-discovery-testlogger

- Root: `implementations/csharp/eng/CIS.Discovery.TestLogger`
- Languages: csharp
- Frameworks: dotnet
- Roles: development-tool
- Capabilities: native-test-discovery
- Confidence: high
- Evidence:
  - implementations/csharp/eng/CIS.Discovery.TestLogger/CIS.Discovery.TestLogger.csproj

### csharp-tooling

- Root: `implementations/csharp`
- Languages: csharp, powershell
- Frameworks: dotnet
- Roles: development-tool
- Capabilities: compiler-controls, conformance-verification
- Confidence: high
- Evidence:
  - implementations/csharp/eng/verify.ps1
  - implementations/csharp/verification/contracts/startup/

This fallback owns supporting files outside the specific project directories. More specific component roots take precedence. Some compiler-control sources intentionally do not compile in isolation; graph diagnostics for those files do not establish a defect in shipping Core. Native compiler reports remain their qualification evidence.
