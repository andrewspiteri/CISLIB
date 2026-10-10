# Recorded foundation decisions

Recorded 10 October 2026 from the user's directions in the preparation conversation.

| Decision | State | Direction |
| --- | --- | --- |
| Repository home | Confirmed by user | A separate repository named `cislib`, independent of CIS. |
| Implementation layout | Confirmed by user | Language-specific code, tests, examples and tooling live under `implementations/<language>/`. |
| Language scope | Confirmed by user | Plan for different languages; begin implementation with C#. No later language or runtime is fixed yet. |
| Library scope | Confirmed by user | Core will contain multiple modules; logging is the first. |
| Library naming | Confirmed by user | Library/project/assembly/package name `CIS.Core`; root namespace `CIS`, with module namespaces such as `CIS.Logging`. |
| C# packaging | Confirmed by user | Core contains Logging and its Microsoft logger integration as folders/namespaces in one project. |
| Runtime dependencies | Confirmed by user | Microsoft-maintained NuGets are allowed with explicit provenance/graph checks. Third-party dependencies belong in separate optional packages. |
| First consumer | Confirmed by user | A simple example application owns and exercises the runtime. |
| Bootstrap | Authorized by user | Initialize the layout, basic solution/projects and local Git repository. |

## Development choices used in this scaffold

- C# target: `net10.0`; local development SDK pinned to stable `10.0.401`, with roll-forward disabled.
- C# solution: `implementations/csharp/CisLib.slnx`.
- Library project/assembly/package ID: `CIS.Core`; root namespace: `CIS`; development version `0.1.0-dev.1`.
- First example: `implementations/csharp/examples/logging/ConsoleHuman`, targeting `net10.0` with a development project reference to Core.
- Approved initial runtime package: `Microsoft.Extensions.Logging.Abstractions` 10.0.12. Its restored .NET 10 dependency closure is checked against the explicit allowlist.
- Git initialization creates a local repository on `main`. No remote or initial commit is part of this scaffold.

These are development choices, not a published compatibility baseline. During WP02-S01 the user selected `EnvironmentOptions`, correcting the original unpublished `EnviormentsOptions` spelling. The proposed C# facade is `CIS.EnvironmentOptions`; no misspelled alias is required.

## Assembly and platform decisions

Selected 10 October 2026 while carrying out the user's request to document `WP01-S01-T03` and `WP01-S01-T04`. These are implementation decisions within the authorized task; the user has not separately confirmed each policy value.

| Decision | Selected direction | Record |
| --- | --- | --- |
| Assembly identity | `CIS.Core`, neutral culture, stable `AssemblyVersion` `1.0.0.0` throughout this package's compatible line. | [Assembly policy](../docs/architecture/csharp-assembly-policy.md) |
| Release identity | Package version, numeric file version and informational version identify releases independently of assembly identity. Current development values are `0.1.0-dev.1`, `0.1.0.1` and SDK-generated informational metadata. | [Assembly policy](../docs/architecture/csharp-assembly-policy.md) |
| Strong-name signing | Disabled for the managed .NET-only baseline; public-key token is null. No signing key is created. | [Assembly policy](../docs/architecture/csharp-assembly-policy.md) |
| Contract target | One portable `net10.0` asset; preserve published target assets and minimum framework requirements, including prereleases. | [Contract targets](../implementations/csharp/support/contract-targets.json) |
| Initial platform lanes | Windows 11 x64 and Ubuntu 24.04 x64, normal managed, framework-dependent execution. Both require full release qualification before support is advertised. | [Platform policy](../docs/architecture/csharp-platform-policy.md) |
| Executed platform evidence | Windows scaffold checks are recorded separately from full qualification; Linux remains planned and unexecuted. | [Execution matrix](../implementations/csharp/support/execution-matrix.json) |
| Historical runtimes | Preserve unchanged consumer fixtures and retained toolchains; explicitly record each original-runtime or newer-runtime replay and its roll-forward policy. | [Platform policy](../docs/architecture/csharp-platform-policy.md) |

Current task completion is tracked in [implementation status](logging/implementation-status.md). The original supplied plans remain unchanged.

## WP01-S02 implementation

The user selected WP01-S02 next and supplied `https://github.com/andrewspiteri/CISLIB`. The local `origin` points to that repository; no commit or push was performed.

Five test-family projects, nonpackable shared support and a development-only metadata checker are in the solution. Evaluated project/package/assembly directions and exact test dependencies are recorded in the [dependency policy](../docs/architecture/csharp-dependency-policy.md). Strong shipping isolation is preserved: Core still resolves only its original two approved runtime packages. Basic MSTest/VSTest setup is complete; full toolchain qualification remains WP01-S03 work.

## Commit checkpoint and WP01-S04

At the user's request, the foundation through WP01-S03 was committed as `ea8e9cf` on `main`. The historical setup records above describe the earlier uncommitted state. WP01-S04 now adds structured verification/source replay, initial six-family case inventories and deterministic test support. Release qualification remains pending; [verification guidance](../docs/architecture/csharp-verification.md) records the bootstrap scope.

## Remaining publication decisions

Resolve licence, publishing destination, distribution signing (NuGet author signing/Authenticode, if used), and the remaining public API/behavior compatibility policy before publishing any release, including a prerelease. The assembly identity and target-retention decisions above are selected; complete both platform lanes and qualify test, coverage, mutation and compatibility tools against real behavior before claiming a release is verified.

## WP01-S03 implementation

The user selected WP01-S03 and expanded severity abbreviations during implementation. The retained internal parser and tests recognize `TRC`, `DBG`, `INF`/`INFO`, `WRN`/`WARN`, `ERR` and `CRT`/`CRIT` alongside full names. [Toolchain qualification](../docs/architecture/csharp-toolchain.md) records exact CLI/runtime pins, fresh restore, real Microsoft coverage and isolated mutation, API compatibility controls, native exclusions, source identity and retention limits. [Windows evidence](logging/evidence/wp01-s03-windows.json) records the completed smoke run. Linux, historical replay and the full logging release remain unqualified.

## WP02-S01 contract definition

WP01-S04 was committed as `b7b8b78` at the user's request. The user selected the corrected unpublished facade name `EnvironmentOptions`. Portable startup/configuration semantics remain under shared contracts; C# signatures remain under `implementations/csharp`. A reference-only specification and compiled consumers qualify shape without inserting placeholder startup APIs into Core. [Contract guidance](../docs/modules/logging/startup-contract.md) and [Windows evidence](logging/evidence/wp02-s01-windows.json) record selected details and execution limits. Runtime resolution and publication remain WP03 work.

## WP02-S02 event contracts

WP02-S01 was committed as ccb7237. WP02-S02 defines schema/value/bound contracts and executes C# development-only record/value validation. The user amended oversize behavior to configurable entry bytes (64 KiB default) and truncation. CORE_LOG_MAX_ENTRY_BYTES and the selected 16 KiB–16 MiB range, marker/projection choices and validation limits are recorded in [event guidance](../docs/modules/logging/event-contract.md). Real bounded capture/truncation and output rendering remain WP04/WP05 work.
