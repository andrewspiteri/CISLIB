---
title: "Test Suite Profile"
type: test-suite-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-11-22"
review_cadence: "on test framework, command, classification, or CI-tier change"
---

# Test suite profile

These six selections cover the retained native MSTest/VSTest families, separating
Docker-free IntegrationTests from the real-service lane in the same project.
Bindings are configuration, not execution evidence. Earlier results retain their
original scope; the small unit-only CIS adoption route in the
[repository README](../../README.md) retains its separate execution/accounting.

Run from `implementations/csharp` so `global.json` selects SDK **10.0.401**. Each
command builds its real dependencies in Release. First perform the locked restore
documented in the [canonical testing guide](../../implementations/csharp/tests/README.md);
`--no-restore` requires compatible assets. Install the selected **.NET/ASP.NET Core
10.0.12** frameworks. Fast bindings exclude `RequiresDocker`; service tests require
the supported Docker environment and automatically provision their owned services.
All listed commands exclude `PerformanceObservation` and `ExtendedStress`.

Result, coverage, mutation, and retained artifact paths are derived state under `.cis/local/`.
Suite-specific sanitized runtime evidence belongs under `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/` and is hash-correlated during `cis test reconcile`.

| Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path | Prerequisites | Applies when | CI tier | Artifacts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| cis-core-architecturetests | cis-core-architecturetests | architecture | dotnet-test | dotnet test tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --no-restore --filter "TestCategory!=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-architecturetests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-architecturetests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |
| cis-core-businesscases | cis-core-businesscases | business | dotnet-test | dotnet test tests/CIS.Core.BusinessCases/CIS.Core.BusinessCases.csproj -c Release --no-restore --filter "TestCategory!=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-businesscases.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-businesscases.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |
| cis-core-compatibilitytests | cis-core-compatibilitytests | api-compatibility | dotnet-test | dotnet test tests/CIS.Core.CompatibilityTests/CIS.Core.CompatibilityTests.csproj -c Release --no-restore --filter "TestCategory!=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-compatibilitytests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-compatibilitytests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets; ApiCompat 10.0.401; tool-host runtime 8.0.31 | C# production changes | pr | retain-on-failure |
| cis-core-integrationtests | cis-core-integrationtests | integration | dotnet-test | dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --no-restore --filter "TestCategory!=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-integrationtests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-integrationtests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets; no Docker needed | C# production changes | pr | retain-on-failure |
| cis-core-service-integrationtests | cis-core-integrationtests | integration | dotnet-test | dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --no-restore --settings tests/CIS.Core.IntegrationTests/service-tests.runsettings --filter "TestCategory=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-service-integrationtests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-service-integrationtests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets; reachable local Linux Docker and pinned PostgreSQL/RabbitMQ/Ryuk images | Runtime, native providers, schema, infrastructure, dependency or pin changes | pr | retain-on-failure |
| cis-core-unittests | cis-core-unittests | unit | dotnet-test | dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --no-restore --filter "TestCategory!=RequiresDocker&TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-unittests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-unittests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |

Native commands resolve `../../.cis/local/testing/results/<suite>.trx` from the
declared working directory; `Result path` is repository-relative. Coverage and
mutation paths remain unset. The CI tier alone does not establish enforcement or
passing execution. The [service workflow](../../.github/workflows/service-tests.yml)
defines a stable Linux job and invokes the small native TRX guard after the service
selection. It requires a successful native exit and run summary, nonzero results
and no skipped/inconclusive/failed selected case. Use that same result check for
manual service acceptance; a table entry alone does not run it. Observed commit,
runner, counts and cleanup belong in the current verification record. Branch-check
enforcement remains repository configuration.

For Compatibility only, restore the pinned native tools using `dotnet tool restore --configfile eng/Tools.NuGet.config` from `implementations/csharp`; ApiCompat requires .NET **8.0.31**. A private installation can use the existing `CIS_COMPAT_API_DOTNET` override; keep its machine-specific value local. No release-only tool installation is required for the selected unit demonstration.

Fixtures, examples, shared support and `CIS.TypeManager.Build` remain their existing test/consumer/product-build dependencies, not additional shipping modules or test suites. Ordinary `dotnet` and IDE execution remain available, and CIS is not a shipping runtime dependency. Performance/stress and full release qualification remain separate open work; see [native test guidance](../../implementations/csharp/tests/README.md).
