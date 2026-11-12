# CisLib

CisLib implements Logging, Type Manager and Healthcheck in one C# `CIS.Core` package and assembly. It targets .NET 10; Core uses the approved Microsoft runtime dependencies and `Microsoft.AspNetCore.App`. Applications own runtime startup, shutdown and resources. The library does not depend on Change Impact Studio.

Run commands from `implementations/csharp` so `global.json` selects SDK **10.0.401**. The selected qualification runtime is **10.0.12**; install its ASP.NET Core shared framework as well.

The full Compatibility suite also needs **.NET 8.0.31** for the pinned ApiCompat tool. For a private installation, set `CIS_COMPAT_API_DOTNET` to that runtime's `dotnet.exe`; this machine's verified host is `.artifacts/tool-host-8.0.31/dotnet.exe` under the repository root. SDK and product commands continue to use ordinary `dotnet`.

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
```

A local development package is not a published release. Scoped checks and current cleanup results are recorded in [cleanup disposition](planning/cis/cleanup-disposition.md). Direct coverage/mutation commands are documented in [native verification](implementations/csharp/verification/mutation/README.md). Complete platform, coverage, mutation, compatibility and release qualification remain separate obligations in [current decisions](planning/decisions.md). The removed preparation, custody, replay and recovery command protocols are retired; they do not require reimplementation in CIS.

| Product | Current reference |
| --- | --- |
| Environment and Logging | [EnvironmentOptions](docs/modules/logging/environment-options.md), [runtime ownership](docs/modules/logging/runtime-contract.md), [incoming frontend](docs/modules/logging/incoming-pipeline.md), [processing policies](docs/modules/logging/processing-policies.md), [interpolation](docs/modules/logging/interpolation.md) |
| Type Manager | [Definition](planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md), [inventory build integration](implementations/csharp/build/type-manager/README.md), [examples](implementations/csharp/examples/type-manager/README.md) |
| Healthcheck | [Usage and transports](docs/modules/healthcheck/README.md), [technical intent](planning/healthcheck/technical-intent.md), [contracts](planning/healthcheck/contract-catalog.md) |
| Shared behavior | [Logging contracts](contracts/logging/README.md), [Healthcheck contracts](contracts/healthcheck/v1/README.md), native fixtures under `conformance/` |
| Architecture | [Dependencies](docs/architecture/csharp-dependency-policy.md), [compatibility](docs/architecture/compatibility-policy.md), [assembly identity](docs/architecture/csharp-assembly-policy.md) |

`contracts/` and `conformance/` hold portable specifications and behavioral fixtures. C# source, tests, examples and build assets live under `implementations/csharp/`. `planning/` retains authoritative definitions and concise decisions; historical campaign payloads are available only where Git history still contains them. Existing historical reports establish no current acceptance claim.

Minimal CIS import is installed alongside the native workflow. The [product BRD](docs/specs/business-requirements.md), [technical intent](docs/specs/technical-intent-spec.md), [overall solution design](docs/architecture/overall-solution-design.md), [component sheet](docs/references/component-sheet.md) and [no-UI direction](docs/design/ui-direction.md) are Active and approved by Andrew Spiteri. [CIS-0001](docs/changes/CIS-0001/proposal.md) retains its approved [plan](docs/changes/CIS-0001/plan.md), durable [WORK-001 contract](docs/changes/CIS-0001/agent-tasks/WORK-001.md) and authorized finite allocation.

The bounded CIS adoption unit result is verified: **2 passed, 0 failed, 0 skipped**, with zero pending unit batches. The retained native pass verifies through the corrected CIS reader without another test launch; the original interrupted attempt remains inconclusive and charged. CIS-0001/WORK-001 is Complete under its approved plan, with no active reservation or recovery hold. The [adoption checkpoint](planning/cis/cleanup-disposition.md#practical-workflow-repair-continuation) records controlled disposal, retry, actual accounting and the reader correction. Original namespace proof is not a prerequisite for retrying disposable unit work. That result's source, test selection, profile, toolchain, approvals and finite limits are unchanged. Its canonical plan contains only the completed WORK-001. No engineering-quality policy or full release qualification is adopted. Later languages and modules remain specifications rather than active scaffolding.

The separate Type Manager package-integration task is [CIS-0002/WORK-001](docs/changes/CIS-0002/agent-tasks/WORK-001.md), under its own approved [plan and allocation](docs/changes/CIS-0002/plan.md). Its current status and [verification record](docs/changes/CIS-0002/verification.md) cover clean Console/Web package consumers and actual Windows/Ubuntu execution. This product task preserves the completed CIS-0001 result and accounting. Broader release, coverage, mutation and performance obligations remain separate. See the [package setup](implementations/csharp/examples/type-manager/README.md#package-consumers) for the versioned SDK and ordinary consumer commands.

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

The completed CIS-0001 profile selects two existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests` cases covering real Unicode sanitization and explicit-message projection. It pins this installation's absolute executable/tool paths locally; on another installation, create a complete local profile using the reviewed CIS schema and actual tools. CIS-0002 uses a separate scoped Type Manager package profile; its task record owns that result. Other suites are configured and unverified through CIS. The canonical [suite bindings](docs/references/test-suite-profile.md) do not turn fixtures/examples into independent product modules.

`definition status` preserves selected working direction separately from approved product records. `brd validate` reports current canonical approval readiness. `change list` and `technical-intent status` locate current work and foundation state. The completed CIS-0001 unit result needs only read-only plan/status/verification in its original Windows execution context; that owner has used both authorized attempts. A different effective environment can correctly report pending work. New native work uses its own legitimate approved owner and remaining finite allocation. Use `cis test incremental run` only within that authority, with **`--layer unit`**, the real `--change`, `--task`, `--authority`, and authorized `--actor`, preserving any inherited `--parent-execution`. Do not use `--force`. Inspect read-only plan/status for current results and pending work, and `cis artifacts inventory` with that same association. Profile limits do not allocate work. Security, standards and optional capability setup remain explicit follow-up work; no full starter reconciliation or release qualification is implied.

CisLib uses the [MIT License](LICENSE.md). Andrew Spiteri maintains the official [repository](https://github.com/andrewspiteri/CISLIB); external contributions and publication are governed by [maintenance policy](CONTRIBUTING.md).
