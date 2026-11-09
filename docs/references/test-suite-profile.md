---
title: "Test Suite Profile"
type: test-suite-profile
status: Active
owner: "Repository maintainers"
last_reviewed: "2026-11-09"
review_cadence: "on test framework, command, classification, or CI-tier change"
---

# Test suite profile

These five bindings configure the retained native MSTest/VSTest families. They are **configured, not executed or verified by this adoption**. Earlier cleanup results retain their original scope. The small unit-only incremental demonstration is a separate selected route in the [repository README](../../README.md); these rows do not establish its execution, accounting or reuse.

Run from `implementations/csharp` so `global.json` selects SDK **10.0.401**. Each command builds the selected project and its real dependencies in Release. `--no-restore` requires compatible restored assets: use the ordinary restore/build instructions in the [repository README](../../README.md) after a checkout or dependency/project change. Install the pinned **.NET/ASP.NET framework 10.0.12**. The commands preserve the ordinary `PerformanceObservation` and `ExtendedStress` exclusions; unfiltered native runs and IDE Run All still include those categories.

Result, coverage, mutation, and retained artifact paths are derived state under `.cis/local/`.
Suite-specific sanitized runtime evidence belongs under `.cis/local/testing/diagnostics/<suite-id>/<run-id>/attempt-<number>/` and is hash-correlated during `cis test reconcile`.

| Suite ID | Component | Layer | Framework | Command | Working directory | Result format | Result path | Coverage path | Mutation path | Prerequisites | Applies when | CI tier | Artifacts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| cis-core-architecturetests | cis-core-architecturetests | architecture | dotnet-test | dotnet test tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-architecturetests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-architecturetests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |
| cis-core-businesscases | cis-core-businesscases | business | dotnet-test | dotnet test tests/CIS.Core.BusinessCases/CIS.Core.BusinessCases.csproj -c Release --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-businesscases.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-businesscases.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |
| cis-core-compatibilitytests | cis-core-compatibilitytests | api-compatibility | dotnet-test | dotnet test tests/CIS.Core.CompatibilityTests/CIS.Core.CompatibilityTests.csproj -c Release --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-compatibilitytests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-compatibilitytests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets; ApiCompat 10.0.401; tool-host runtime 8.0.31 | C# production changes | pr | retain-on-failure |
| cis-core-integrationtests | cis-core-integrationtests | integration | dotnet-test | dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-integrationtests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-integrationtests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |
| cis-core-unittests | cis-core-unittests | unit | dotnet-test | dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress" --logger "trx;LogFileName=cis-core-unittests.trx" --results-directory ../../.cis/local/testing/results | implementations/csharp | trx | .cis/local/testing/results/cis-core-unittests.trx | - | - | SDK 10.0.401; .NET/ASP.NET 10.0.12; compatible restored assets | C# production changes | pr | retain-on-failure |

Native commands resolve `../../.cis/local/testing/results/<suite>.trx` from the declared working directory; `Result path` is repository-relative. Coverage and mutation paths remain unset because this step does not qualify either route. The CI tier is a suite classification, not evidence of an installed or executed CI gate.

For Compatibility only, restore the pinned native tools using `dotnet tool restore --configfile eng/Tools.NuGet.config` from `implementations/csharp`; ApiCompat requires .NET **8.0.31**. A private installation can use the existing `CIS_COMPAT_API_DOTNET` override; keep its machine-specific value local. No release-only tool installation is required for the selected unit demonstration.

Fixtures, examples, shared support and `CIS.TypeManager.Build` remain their existing test/consumer/product-build dependencies, not additional shipping modules or test suites. Ordinary `dotnet` and IDE execution remain available, and CIS is not a shipping runtime dependency. Performance/stress and full release qualification remain separate open work; see [native test guidance](../../implementations/csharp/tests/README.md).
