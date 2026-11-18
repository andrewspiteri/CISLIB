# CisLib

CisLib implements Logging, Type Manager, Healthcheck, Heartbeat, Startup, Configuration, portable Metrics, typed RestClient and sequential Workflows in the C# `CIS.Core` package and assembly. The optional `CIS.Metrics.OpenTelemetry` package adds bounded Prometheus collection and native HTTP integration; `CIS.RestClient.Resilience` adds explicitly selected retry policies. It targets .NET 10; Core uses the approved Microsoft runtime dependencies and `Microsoft.AspNetCore.App`. Applications own runtime startup, shutdown and resources. The library does not depend on Change Impact Studio.

The single maintained runnable tutorial is [CountryData](implementations/csharp/examples/CountryData/README.md), using real CIS REST and normal CISApplication startup. Small dedicated test fixtures retain assembly, protocol, ownership and package boundaries; ordinary Git history retains retired application source.

Run commands from `implementations/csharp` so `global.json` selects SDK **10.0.401**. The selected qualification runtime is **10.0.12**; install its ASP.NET Core shared framework as well.

The full Compatibility suite also needs **.NET 8.0.31** for the pinned ApiCompat tool. `CIS_COMPAT_API_DOTNET` explicitly selects its host. Without an override, the fixture uses a complete repository-local `.artifacts/tool-host-8.0.31/` installation when present, then ordinary `dotnet`; the exact runtime pin remains enforced. This machine has that private installation, so IDE runs can use it automatically. SDK and product commands continue to use ordinary `dotnet`.

Frozen Type Manager consumers run with a separate generated runtime configuration supplying Core's approved .NET/ASP.NET shared frameworks and supplemental NuGet dependencies. Their original binaries, source, dependency manifests and runtime configuration stay unchanged. Negative cases deliberately omit deployment components and must still fail complete metadata discovery.

```powershell
cd implementations/csharp
dotnet tool restore --configfile eng/Tools.NuGet.config
dotnet restore CisLib.slnx --locked-mode
dotnet build CisLib.slnx -c Release --no-restore
dotnet test CisLib.slnx -c Release --no-build --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress"
```

This ordinary regression selection excludes performance observations and extended stress tests. An unfiltered solution run or IDE Run All includes those categories; performance/stress and full release qualification remain separate open work.

Open the same solution in the IDE. To run a focused test selection:

```powershell
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --filter FullyQualifiedName~Healthcheck
```

Pack the library and its Type Manager build assets directly:

```powershell
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release -o ../../.artifacts/packages
dotnet pack src/CIS.Metrics.OpenTelemetry/CIS.Metrics.OpenTelemetry.csproj -c Release -o ../../.artifacts/packages
dotnet pack src/CIS.RestClient.Resilience/CIS.RestClient.Resilience.csproj -c Release -o ../../.artifacts/packages
```

A local development package is not a published release. Scoped checks and current cleanup results are recorded in [cleanup disposition](planning/cis/cleanup-disposition.md). Direct coverage/mutation commands are documented in [native verification](implementations/csharp/verification/mutation/README.md). Complete platform, coverage, mutation, compatibility and release qualification remain separate obligations in [current decisions](planning/decisions.md). The removed preparation, custody, replay and recovery command protocols are retired; they do not require reimplementation in CIS.

| Product | Current reference |
| --- | --- |
| Application | [CISApplication](docs/modules/application/core.md), [CountryData](implementations/csharp/examples/CountryData/README.md) |
| Environment and Logging | [EnvironmentOptions](docs/modules/logging/environment-options.md), [runtime ownership](docs/modules/logging/runtime-contract.md), [incoming frontend](docs/modules/logging/incoming-pipeline.md), [processing policies](docs/modules/logging/processing-policies.md), [interpolation](docs/modules/logging/interpolation.md) |
| Type Manager | [Definition](planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md), [inventory build integration](implementations/csharp/build/type-manager/README.md), [Configuration boundary](docs/modules/type-management/configuration-boundary.md) |
| Healthcheck | [Usage and transports](docs/modules/healthcheck/README.md), [technical intent](planning/healthcheck/technical-intent.md), [contracts](planning/healthcheck/contract-catalog.md) |
| Heartbeat | [Owners, scheduling and hosting](docs/modules/heartbeat/README.md), [CountryData](implementations/csharp/examples/CountryData/README.md), [implementation record](docs/changes/CIS-0006/verification.md) |
| Startup and DI | [Sequential startup and CountryData](docs/modules/startup/README.md), [enrollment and lifetime contract](docs/modules/startup/core.md), [requirements](planning/startup/README.md) |
| Configuration | [Preparation and hosting](docs/modules/configuration/hosting.md), [retained reads/events](docs/modules/configuration/core.md), [mapping](docs/modules/configuration/mapping.md), [refresh lifetime](docs/modules/configuration/refresh.md), [requirements](planning/configuration/README.md) |
| Metrics | [Portable API](docs/modules/metrics/core.md), [collection policy](docs/modules/metrics/collection.md), [host lifetime](docs/modules/metrics/hosting.md), [HTTP policy](docs/modules/metrics/http.md), [real example](docs/modules/metrics/example.md), [requirements and evidence](planning/metrics/README.md) |
| RestClient | [Core API and ownership](docs/modules/rest-client/core.md), [HTTP content](docs/modules/rest-client/http.md), [extensions](docs/modules/rest-client/extensions.md), [optional retry](docs/modules/rest-client/resilience.md), [Metrics](docs/modules/rest-client/metrics.md), [actual example and consumers](docs/modules/rest-client/example.md), [requirements and evidence](planning/rest-client/README.md) |
| Workflows | [Sequential pipelines](docs/modules/workflows/pipeline.md), [CountryData lookup](implementations/csharp/examples/CountryData/CountryLookupPipeline.cs), [requirements and integration](planning/workflows/README.md) |
| Shared behavior | [Logging contracts](contracts/logging/README.md), [Healthcheck contracts](contracts/healthcheck/v1/README.md), native fixtures under `conformance/` |
| Architecture | [Dependencies](docs/architecture/csharp-dependency-policy.md), [compatibility](docs/architecture/compatibility-policy.md), [assembly identity](docs/architecture/csharp-assembly-policy.md) |

`contracts/` and `conformance/` hold portable specifications and behavioral fixtures. C# source, tests, examples and build assets live under `implementations/csharp/`. `planning/` retains authoritative definitions and concise decisions; historical campaign payloads are available only where Git history still contains them. Existing historical reports establish no current acceptance claim.

Minimal CIS import is installed alongside the native workflow. The [product BRD](docs/specs/business-requirements.md), [technical intent](docs/specs/technical-intent-spec.md), [overall solution design](docs/architecture/overall-solution-design.md), [component sheet](docs/references/component-sheet.md) and [no-UI direction](docs/design/ui-direction.md) are Active and approved by Andrew Spiteri. [CIS-0001](docs/changes/CIS-0001/proposal.md) retains its approved [plan](docs/changes/CIS-0001/plan.md), durable [WORK-001 contract](docs/changes/CIS-0001/agent-tasks/WORK-001.md) and authorized finite allocation.

The bounded CIS adoption unit result is verified: **2 passed, 0 failed, 0 skipped**, with zero pending unit batches. The retained native pass verifies through the corrected CIS reader without another test launch; the original interrupted attempt remains inconclusive and charged. CIS-0001/WORK-001 is Complete under its approved plan, with no active reservation or recovery hold. The [adoption checkpoint](planning/cis/cleanup-disposition.md#practical-workflow-repair-continuation) records controlled disposal, retry, actual accounting and the reader correction. Original namespace proof is not a prerequisite for retrying disposable unit work. That result's source, test selection, profile, toolchain, approvals and finite limits are unchanged. Its canonical plan contains only the completed WORK-001. No engineering-quality policy or full release qualification is adopted. Other unimplemented modules and languages remain specifications rather than active scaffolding.

The separate Type Manager package-integration task is [CIS-0002/WORK-001](docs/changes/CIS-0002/agent-tasks/WORK-001.md), under its own approved [plan and allocation](docs/changes/CIS-0002/plan.md). Its current status and [verification record](docs/changes/CIS-0002/verification.md) cover clean Console/Web package consumers and actual Windows/Ubuntu execution. This product task preserves the completed CIS-0001 result and accounting. Broader release, coverage, mutation and performance obligations remain separate. Use the [inventory build guide](implementations/csharp/build/type-manager/README.md) and [CountryData](implementations/csharp/examples/CountryData/README.md) for current SDK/package usage. The Console/Web observations above remain historical inputs, not maintained tutorial paths.

The owner-safe Heartbeat implementation is tracked under [CIS-0006](docs/changes/CIS-0006/verification.md). Its scoped Windows observations, actual source/package checks and remaining qualification limits are recorded separately from the historical owners above.

## CIS commands

Use a verified CIS build containing [`464dcb8255f9a0c4a01652f290bae680e2e7afc0`](https://github.com/andrewspiteri/CIS/commit/464dcb8255f9a0c4a01652f290bae680e2e7afc0). Here `cis` means that installed command, or `dotnet` followed by its verified `cis.dll`. Preserve the native commands and toolchain above; CIS is not a shipping runtime dependency. Agent skill references are in [AGENTS.md](AGENTS.md#cis-integration). The [incremental test manual](https://github.com/andrewspiteri/CIS/blob/464dcb8255f9a0c4a01652f290bae680e2e7afc0/docs/manual/cis_test_incremental.md) defines current result reuse and verification.

The verified build in this checkout is installed under ignored `.cis/local/tools/cis/`.
From the repository root, replace `cis` below with `dotnet .cis/local/tools/cis/cis.dll`.
Fresh checkouts need their own verified installation; generated binaries are not committed.

From the repository root:

```text
cis repo doctor --repo . --root docs --format agent
cis definition status --workspace . --format json
cis brd validate --workspace . --format json
cis change list --repo . --format json
cis technical-intent status --workspace . --format json
cis test inventory --repo . --format agent
cis graph build --repo . --format agent
cis test incremental plan --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --layer unit --format agent
cis test incremental status --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --layer unit --format agent
cis test incremental verify --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --layer unit --format json
```

The completed CIS-0001 profile selects two existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests` cases covering real Unicode sanitization and explicit-message projection. It pins this installation's absolute executable/tool paths locally; on another installation, create a complete local profile using the reviewed CIS schema and actual tools. CIS-0002 uses a separate scoped Type Manager package profile; its task record owns that result. Subsequent owners retain their own scoped results and currentness; for Metrics, read [CIS-0009 verification](docs/changes/CIS-0009/verification.md). A configured suite or an earlier checkpoint is not proof that another scope passed. The canonical [suite bindings](docs/references/test-suite-profile.md) do not turn fixtures/examples into independent product modules.

`definition status` preserves selected working direction separately from approved product records. `brd validate` reports current canonical approval readiness. `change list` and `technical-intent status` locate current work and foundation state. The completed CIS-0001 unit result needs only read-only plan/status/verification in its original Windows execution context; that owner has used both authorized attempts. A different effective environment can correctly report pending work. New native work uses its own legitimate approved owner and remaining finite allocation. Use `cis test incremental run` only within that authority, with **`--layer unit`**, the real `--change`, `--task`, `--authority`, and authorized `--actor`, preserving any inherited `--parent-execution`. Do not use `--force`. Inspect read-only plan/status for current results and pending work, and `cis artifacts inventory` with that same association. Profile limits do not allocate work. Security, standards and optional capability setup remain explicit follow-up work; no full starter reconciliation or release qualification is implied.

CisLib uses the [MIT License](LICENSE.md). Andrew Spiteri maintains the official [repository](https://github.com/andrewspiteri/CISLIB); external contributions and publication are governed by [maintenance policy](CONTRIBUTING.md).

Consumer tests can use the optional [CIS.CommonTesting helpers](docs/modules/common-testing.md). Core and runtime applications do not reference this package; ordinary MSTest/IDE execution and nonpackable repository process fixtures remain available.
