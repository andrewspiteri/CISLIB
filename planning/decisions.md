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

## WP02-S04-T01 preservation review

WP02-S03 was committed as 875053e. Within the user-authorized roadmap, the maintainer selected [permanent preservation of published contracts](../docs/architecture/compatibility-policy.md), including prereleases and intermediate releases, with no major-version, obsolete-warning, rewritten-baseline or target-removal waiver. The [C# review](../compatibility/csharp/preservation-review.json) records exact unpublished proposals and remaining prepublication qualification. This is a policy selection, not separate user confirmation, a publication baseline or proof of runtime compatibility.

## WP02-S04-T02 publication format

WP02-S04-T01 is committed as `69ebd90`. The [C# publication format](../compatibility/csharp/publications/README.md) selects a strict ledger plus immutable hashed record/artifact references, including prereleases/intermediate versions, package/API/contract metadata, unchanged consumer origins and exact execution/toolchain materialization. The actual ledger contains no publication rows. Selected canonical version/path syntax and trusted previous-snapshot comparison are development choices within the roadmap; schema/hash controls do not attest publication or qualify historical compatibility. T03/T04 and the later package harness remain pending.

## WP02-S04-T03 replay definition

WP02-S04-T02 is committed as `365b3b4`. The [four-layer replay definition](../docs/architecture/csharp-compatibility-replay.md) specifies cumulative API/source/unchanged-binary/configuration-wire-behavior checks, isolated version/lockfile substitutions, two integration-aware binary scenarios, separate original/current runtime evidence, named validated normalization and an honest zero-history path. These are maintainer choices within the roadmap, reflecting the provider-inside-Core amendment. The [structured protocol](../compatibility/csharp/replay-rules.json) is definition-only; no harness/runtime/publication or historical compatibility pass is claimed. T04 and WP07-S04 remain pending.

## WP02-S04-T04 initial fixtures

WP02-S04-T03 is committed as `d7e6f4d`. [Initial fixture guidance](../docs/architecture/csharp-compatibility-samples.md) binds six compiler patterns, eight deliberate API changes and 20 deterministic wire samples to actual selected contracts. Reference-only compilation and pure wire comparison execute without adding shipping placeholders or claiming a historical/package runtime pass. Existing configuration vectors supply defaults/bounds expectations; actual resolver/harness work remains pending. Raw wire fixtures retain exact bytes, including deliberate framing failures.

## WP03-S01-T01 application identity

WP02-S04-T04 is committed as `2b18579`. [Application identity discovery](../docs/modules/logging/application-identity-discovery.md) now implements supplied/application/entry identity precedence in internal `CIS.Configuration` types. It excludes Core's assembly, preserves informational strings and explicit build IDs, and handles expected optional discovery failures per fact. The internal namespace, reader seam and exception filter are implementation choices within the authorized roadmap. Public facade publication, full identity validation, process facts and Linux process execution remain pending.

## WP03-S01-T02 process and runtime facts

WP03-S01-T01 is committed as `5e91e6b`. [Process/runtime discovery](../docs/modules/logging/runtime-metadata-discovery.md) reads actual process start, visible host, OS family/description, separate OS/process architectures, runtime description and PID through typed internal seams. It isolates expected native failures per fact and never substitutes initialization time or collector/deployment settings. Null for undefined architecture/nonpositive PID, trimmed optional text and `Win32Exception` handling are implementation choices within the authorized task; new platform support, public snapshot publication and Linux process execution remain pending.

## WP03-S01-T03 versions and initialization identity

WP03-S01-T02 is committed as `0ded9ac`. [Version/initialization behavior](../docs/modules/logging/initialization-identity.md) separates actual BuildVersion from the selected CORE/DD/setup telemetry label, retains the selected source for later diagnostics, and generates UUID/time through internal callbacks. Empty-ID rejection and timestamp capture after UUID generation are maintainer choices within the task. Factories propagate required-source failures without fallback; the later publisher owns winning validation/publication and run stability. Full static initialization, configuration validation and Linux process execution remain pending.

## WP03-S01-T04 discovery qualification

WP03-S01-T03 is committed as `07d3400`, with the separate test cleanup fix `e4c82d5`. [Discovery fixtures](../docs/modules/logging/discovery-fixtures.md) combine 20 portable rows and seven real child-process integration cases. A registered nonshipping fixture has fixed application/assembly/file-version decoys and friend access to internal Core discovery, while Core's runtime dependency closure stays unchanged. Exact-runtime process scenarios execute on Windows and Ubuntu 24.04.4 x64 under WSL2 using a task-local copy of the existing Microsoft .NET 10.0.12 runtime. Fixture layout, controlled absence modes and runtime reuse are implementation choices within the authorized roadmap. This qualifies discovery only; Linux SDK/full-suite and macOS execution remain unqualified.

## WP03-S02-T01 capture and selection

WP03-S01-T04 is committed as `ceb028b`. [Configuration input behavior](../docs/modules/logging/configuration-inputs.md) selects an ordinal owned snapshot of all 18 CORE names plus three DD aliases and a shared raw-token/typed-candidate precedence mechanism. Registry/provenance representation, required-read failure propagation and explicit absence are implementation choices within the roadmap. The existing version helper reuses selection, and the child fixture passes one capture. Complete option parsing, validation and public publication remain later tasks.

## WP03-S02-T02 typed settings

WP03-S02-T01 is committed as `fe121ef`. [Typed resolution](../docs/modules/logging/configuration-resolution.md) implements all 18 selected settings, their defaults and independent output decisions. Internal nullable setup inputs, getter-only parse results/provenance, nullable unresolved enablement and separate parse states are implementation choices within the roadmap. Selected invalid input never falls through; invalid inherited thresholds do not invent a fallback. Parsed integer domains and schema selection are implemented, while option ranges, identity/path validation, aggregated safe errors and public snapshot publication remain later tasks. The user-selected 64 KiB default is resolved; runtime truncation remains pending.

## WP03-S02-T03 normalization qualification

WP03-S02-T02 is committed as `5711027`. [Normalization guidance](../docs/modules/logging/configuration-normalization.md) qualifies the already-implemented capture, candidate, selector and token behavior across all 18 settings. C# whitespace handling and preserved Unicode sequences are documented from executed controls; zero-width/BOM characters remain nonblank selected data for the later validator. Exact catalog requests, ordinal snapshot lookups, generic value guards and plain object formatting are distinguished from OS source lookup semantics and future public safe diagnostics. No redundant production normalization path is added. Shared configuration fixtures and full option/identity/path validation remain pending.

## WP03-S02-T04 shared configuration qualification

WP03-S02-T03 is committed as `a697386`. [Configuration fixtures](../docs/modules/logging/configuration-fixtures.md) select a neutral full-snapshot format, one linked nonshipping C# adapter, an extension of the existing process host and a compiled configuration-call guard. These are maintainer implementation choices within the roadmap. All 26 cases execute on Windows and Ubuntu using the same managed host/Core/input bytes, with exact existing runtime selection and controlled host labels. Terminal/debugger fixture context is distinguished from actual observed child state and compiled independence evidence. No hosting-framework package, new runtime dependency or public placeholder is added; complete validation and publication remain later work.

## WP03-S03-T01 option validation

WP03-S02-T04 is committed as `a74985b`. [Option validation](../docs/modules/logging/configuration-option-validation.md) implements all seven inclusive numeric ranges, selected parser-state diagnostics, setup-only EnvironmentKind and the enabled file/entry size relationship. Short validator helpers, internal owned safe problem descriptors, parent-error suppression and catalog ordering are implementation choices within the roadmap. One unpublished entry-size overflow expectation is corrected to OutOfRange, consistent with the existing numeric vectors; original plans/historical evidence are preserved. Windows native tests and fresh source replay qualify the logging-option subset. Public exception/identity/path aggregation and startup publication remain later tasks.

## WP03-S03-T02 identities and immutable diagnostics

WP03-S03-T01 is committed as `1554e62`. [Identity validation](../docs/modules/logging/configuration-identity-validation.md) implements scalar/control/Unicode limits, copies explicit build identifiers into the local resolution and aggregates selected option/identity problems through actual public immutable diagnostics in `CIS.Configuration`. Fixed exception formatting, null argument guards, empty-sequence acceptance, caller-responsible issue explanations, metadata diagnostic keys and duplicate inherited BuildVersion suppression are prepublication implementation choices. Windows native tests and fresh source replay qualify 60 identity rows and focused controls; original plans/historical evidence remain unchanged. Path syntax, public startup/snapshot and complete metadata/lifecycle fixtures remain later tasks.

## WP03-S03-T03 captured file paths

WP03-S03-T02 is committed as `c56d7f7`. [File-path validation](../docs/modules/logging/configuration-file-paths.md) captures one required fully qualified directory, uses native two-argument GetFullPath with literal tilde protection against Windows short-name filesystem lookups, inserts safe path problems in catalog order and returns immutable local validated path state for the later publisher. Capture/source failure policy, native syntax exception filter, explicit phase-helper names and local state composition are implementation choices within the roadmap. Windows native families/replay and 13 actual path/cwd children per Windows/Ubuntu qualify all 14 declared path rows across their applicable platforms. Explicit JSON variable-source fixtures enable NUL syntax cases; they do not claim native environment transport. Public startup/snapshot, runtime output ownership and Linux full-suite qualification remain later work.

## WP03-S03-T04 complete validation qualification

WP03-S03-T03 is committed as `2618ed0`. [Complete validation fixtures](../docs/modules/logging/configuration-validation-fixtures.md) select a neutral combined-result protocol, injected discovery facts/JSON source, a test-only application context for omitted facts in 23 original option-only rows, source-blocked stored reads and direct compiled infrastructure-call guards. These are maintainer implementation choices within the roadmap. All selected-setting validation acceptance criteria now have native/process evidence; complete public facade/snapshot reads remain S04. No shipping production behavior changes, new dependencies, publication or full Linux/release qualification are introduced.

## WP03-S04-T01 public startup facade

WP03-S03-T04 is committed as `c563221`. [EnvironmentOptions](../docs/modules/logging/environment-options.md) implements the actual public facade and required sealed owned snapshot/options/metadata types. A private monitor gate synchronizes all published-reference reads/writes; successful construction precedes publication, failure publishes nothing, null always uses an argument guard and later non-null calls reject replacement. These are maintainer choices within the authorized roadmap, preserving the user-selected CIS namespace and EnvironmentOptions spelling. Basic Windows native/process and fresh-source checks qualify T01; the later three tasks retain focused copying, concurrency and full lifecycle qualification. No logging runtime, public reset, new dependency or publication is introduced.

## WP03-S04-T02 input ownership qualification

WP03-S04-T01 is committed as `c1aae7c`. [Startup input ownership](../docs/modules/logging/startup-input-ownership.md) qualifies the existing owned capture/publication mechanics against all 24 actual mutable input properties, captured explicit assembly identity, invalid caller repair, reuse of local builders, actual public post-publication mutation/source changes and collection of caller objects. The selected public shape has one mutable nested LoggingSetup and no collections. The readonly stored-graph guard, non-inlined weak-reference fixture and four native Windows/Ubuntu cases are maintainer test choices within the roadmap. Only public XML documentation changes in shipping source; the copy algorithm already supplied by T01 remains unchanged. Concurrency/full lifecycle qualification stays with T03/T04.
