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

Minimal CIS import is installed alongside the native workflow. The governed unit demonstration and unchanged-result reuse still require a legitimate approved adoption task and finite work/storage allocation; this setup alone does not complete CIS adoption. See the [prepared adoption handoff](planning/cis/cleanup-disposition.md#minimal-cis-adoption-preparation--9-november-2026) for the actual governance blocker. Later languages and modules remain specifications rather than active scaffolding.

## CIS commands

Use a verified CIS build containing `1eab89f68c635513a20bca8aa1a7ce6ae02cb8e5`. Here `cis` means that installed command, or `dotnet` followed by its verified `cis.dll`. Preserve the native commands and toolchain above; CIS is not a shipping runtime dependency. Agent skill references are in [AGENTS.md](AGENTS.md#cis-integration).

From the repository root:

```text
cis repo doctor --repo . --root docs --format agent
cis change list --repo . --format json
cis technical-intent status --workspace . --format json
cis test inventory --repo . --format agent
cis graph build --repo . --format agent
cis test incremental plan --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --format agent
cis test incremental status --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --format agent
```

The current ignored local profile selects two existing `HealthDiagnosticRuntimeAcceptanceBoundaryTests` cases covering real Unicode sanitization and explicit-message projection. It pins this installation's absolute executable/tool paths locally; on another installation, create a complete local profile using the reviewed CIS schema and actual tools. Other suites are configured and unverified through CIS. The canonical [suite bindings](docs/references/test-suite-profile.md) do not turn fixtures/examples into independent product modules.

`change list` and `technical-intent status` locate existing work and expose the current foundation prerequisite; their absence is not permission to invent approvals. Only after a legitimate approved change/task association and finite allocation exist, use `cis test incremental run` with this profile, **`--layer unit`**, the real `--change`, `--task`, `--authority`, and authorized `--actor`, preserving any inherited `--parent-execution`. Do not use `--force`. Then inspect read-only plan/status for current results and zero pending unit work, and `cis artifacts inventory` with that same association. Profile limits do not allocate work; absent governance must be resolved through its owning authority rather than an unaccounted diagnostic run. Security, standards and optional capability setup remain explicit follow-up work; no full starter reconciliation or release qualification is implied.

CisLib uses the [MIT License](LICENSE.md). Andrew Spiteri maintains the official [repository](https://github.com/andrewspiteri/CISLIB); external contributions and publication are governed by [maintenance policy](CONTRIBUTING.md).
