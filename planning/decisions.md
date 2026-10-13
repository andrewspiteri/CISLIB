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

WP02-S01 was committed as ccb7237. WP02-S02 defines schema/value/bound contracts and executes C# development-only record/value validation. The user amended oversize behavior to configurable entry bytes (64 KiB default) and truncation. CORE_LOG_MAX_ENTRY_BYTES and the selected 16 KiBÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â€šÂ¬Ã…Â¡Ãƒâ€šÃ‚Â¬ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬Ãƒâ€¦Ã¢â‚¬Å“16 MiB range, marker/projection choices and validation limits are recorded in [event guidance](../docs/modules/logging/event-contract.md). Real bounded capture/truncation and output rendering remain WP04/WP05 work.

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

## WP03-S04-T03 publisher synchronization

WP03-S04-T02 is committed as `c345978`. [Publication concurrency](../docs/modules/logging/startup-publication-concurrency.md) retains the private monitor gate and adds an in-progress boolean with finally cleanup after actual Assembly.GetName callback controls reproduced replacement and failed-attempt publication. Same-thread nested non-null calls reject before capture; ordinary waiting contenders can retry after failures. This private administrative state and re-entry exception behavior are maintainer choices within the roadmap, with no public signature/dependency change. Ten genuine process scenarios and compiled shared-gate/order/cleanup controls execute on Windows; all ten process scenarios also run on Ubuntu using the same managed bytes. Full shared lifecycle/metadata qualification remains T04.

## WP03-S04-T04 startup qualification

WP03-S04-T03 is committed as 55a83a8. [Startup qualification](../docs/modules/logging/startup-qualification.md) binds all original metadata/lifecycle vectors to genuine Core builders, isolated public children and compiled guards, then compares actual UUIDs across separately initialized applications. Controlled discovery fixtures are explicitly separated from actual public source discovery; no test-only publisher/reset or shipping seam is added. These test layout and evidence choices complete the authorized startup story while preserving original vectors/contracts and historical evidence. Ubuntu uses the existing exact runtime and identical managed bytes; SDK/full-family and logging runtime qualification remain later work.

## WP04-S01-T01 value representations

WP03-S04-T04 is committed as 5240547. [Explicit values](../docs/modules/logging/log-values.md) use a typed readonly scalar union, private owned read-only containers and nine actual factories under CIS.Logging. Scalar/key validation uses allocation-free Unicode/ASCII checks; strings retain precision/text without arithmetic normalization or arbitrary object conversion. Complete collection ownership is implemented now; depth/node projection and truncation provenance follow in T03, while UUID/time factories follow in T02. The typed fixture adapter distinguishes 35 real Core constructor rows from two portable transport errors. These are maintainer choices within the authorized task, preserving original contracts/enum values and the user's later aggregate truncation requirement.

## WP04-S01-T02 typed scalar normalization

WP04-S01-T01 is committed as 7d8af14. [Value guidance](../docs/modules/logging/log-values.md) adds real Guid and DateTimeOffset factories using invariant lowercase UUID/UTC seven-digit timestamp strings with explicit kinds. All UUID bit patterns are valid attributes, distinct from startup instance identity constraints. The existing typed API excludes arbitrary object graphs; compiled direct-call guards qualify the absence of reflection serialization/arbitrary ToString, while the fixture adapter identifies malformed typed transports separately. Twelve additional neutral rows, three cultures and 59 actual identical-byte children per Windows/Ubuntu qualify this task. Reusing the existing IL/process readers and using unique native row identifiers are test implementation choices; original contracts, source plans and historical evidence remain unchanged. Traversal projection/provenance remains T03 and complete S01 qualification T04.

## WP04-S01-T03 bounded value projection

WP04-S01-T02 is committed as 9b4fca4. [Value guidance](../docs/modules/logging/log-values.md) defines actual per-root budgets, cached owned-shape reuse, recursive partial projection and inherited/new loss flags. Root/node occurrence accounting and prefix lookahead are implementation choices within the amended truncation task. Structural map retention uses its enumeration prefix to avoid unbounded sorting/copying; later byte allocation sorts retained keys ordinally. Observed invalid prefix data remains an error; omitted suffix/caller blocking callbacks are outside the captured projection. The common CIS.Text scalar decoder is reused by values, keys and existing startup identities without changing their policies. Private cached metadata/budgets add no public API/dependency. Ninety-six new native cases, 22 neutral descriptors, 81 identical-byte Windows/Ubuntu children and fresh copied-source replay qualify T03. T04 complete qualification and actual scopes/event/byte/runtime handling remain later work.


## WP04-S01-T04 value-model qualification

WP04-S01-T03 is committed as 2d847f4. [Complete value qualification](../docs/modules/logging/log-values.md) adds 71 neutral numeric/error/Unicode/mutation rows, twelve actual-Core compiler controls and an executable typed consumer without changing shipping behavior. The [acceptance map](../conformance/logging/value-qualification-map.json) preserves source descriptions and separately applies the user's structural truncation amendment and typed/cooperative boundary. Eight malformed UTF-16 cases declare a native code-unit capability for future-language applicability. All four S01 criteria are qualified within value construction; later logger InvalidEvent accounting and event/scope/byte capture are not claimed. [Windows/Ubuntu evidence](logging/evidence/wp04-s01-t04-windows-linux.json) retains 293 new native cases, 152 child scenarios per platform and copied-source replay. The silent-compiler log-creation harness correction retains its initial failed record. Next is WP04-S02-T01 exception projection.


## WP04-S02-T01 exception capture foundation

WP04-S01-T04 is committed as c373e20. [Exception guidance](../docs/modules/logging/exception-projection.md) selects internal CapturedException/ExceptionProjection types for later event capture, actual runtime full-name identity, owned strings/ordered children, null-message empty-string handling and transparent accessor failure propagation. Initial bounds/local and aggregate loss metadata are included so introducing the helper never creates an unbounded secondary graph. Cooperative getters and retained-prefix validation are documented implementation boundaries; all-limit/provenance/shared fixture qualification remains T02/T03/T04. [Windows/Ubuntu evidence](logging/evidence/wp04-s02-t01-windows-linux.json) retains 27 new native cases, six child scenarios per platform and copied-source replay. The initial solution-name and type-name guard failures are preserved without changing source contracts, runtime dependencies or the public API. Next is WP04-S02-T02.


## WP04-S02-T02 exception bounds

WP04-S02-T01 is committed as bc9b88d. [Exception bounds qualification](../docs/modules/logging/exception-projection.md) exercises the already-implemented capture guards at exact/adjacent scalar/node/depth boundaries, mixed aggregate preorder, repeated occurrences, concurrent budgets and omitted accessors. Linked test-only sources, a literal exact-tree Python comparator and 34 genuine children per platform are implementation choices within the authorized task. Shipping Core and dependency identities remain unchanged. Ordered-child and bound behavior is qualified for the internal cooperative projection; detailed marker/exclusion and complete shared fixtures remain T03/T04, with encoded entry bytes/runtime qualification later.


## WP04-S02-T03 exception markers and excluded content

WP04-S02-T02 is committed as 4500e56. [Detailed exception qualification](../docs/modules/logging/exception-projection.md) separates local marker placement from descendant loss across sixteen shapes, excludes arbitrary/Data/ambient capture, and observes real BCL mutation and collection of caller graphs with live owned models. Linked test-only sources, literal native and independent Python trees, direct compiled member guards and weak-reference targets are maintainer qualification choices within the authorized task. Shipping capture, public API and dependencies remain unchanged. Claude identified a missing last-branch node-loss isolation case; its required correction and the widened guard's initial failures are retained before passing native/process/source-replay evidence. Read counters and IL scope limits are explicit. Complete shared fixtures/final S02 qualification remain T04; event encoded bytes/runtime accounting remain later work.


## WP04-S02-T04 shared exception qualification

WP04-S02-T03 is committed as `a3d28c8`; CIS initialization follows as `4783207`. Shared semantic descriptors use explicit per-runtime exception type tags and declare malformed UTF-16 code-unit capability. Literal expected prefixes, separate C#/Python text expansion, complete native field/type comparison and explicit optimization-safe checks are qualification choices within the task. Source graphs carry real mutable cyclic base Data behind throwing accessors and armed metadata traps; existing BCL mutation/weak-reference/compiled guards remain intact. The original four S02 criteria are preserved separately from their scoped applied meaning. Immutable owned raw text and cached shape/loss remain measurable by later aggregate capture, while escaped/envelope/LF event bytes and runtime accounting remain pending. Shipping Core, public API, dependencies, source plans and historical evidence are preserved.

Claude review corrections add later-child malformed text, scalar clipping through combining sequences, empty aggregates at the depth limit and Unicode noncharacters. Native diagnostic parameters and failure accessor ordering are explicitly C# adapter observations, without imposing them on future languages. Fabricated capture-call counters are removed; actual per-node reads qualify capture behavior. These are task implementation corrections, not further user directions.


## WP04-S03-T01 operation scope foundation

WP04-S02-T04 is committed as 1024d40. The actual public OperationContextData input matches the preserved reference shape. Internal CapturedOperationContext/OperationScopeFrame/OperationScopeStore types implement owned bounded context and per-instance AsyncLocal storage without introducing placeholder runtime APIs. Reusing LogValue.Map, immutable parent links, an early depth guard and stale-parent protection are implementation choices. Actual runtime admission/public wiring, merging and full disposal/shared-story qualification remain later tasks. Inherited frame identity does not prove the creating logical flow; the stronger cross-flow rejection wording in the preserved runtime specification is an explicit T03 design/qualification issue, not silently claimed as implemented. Original sources/contracts and prior evidence remain unchanged.

Claude identifies two specific T03 ownership cases to resolve: a child inheriting the active frame can currently dispose the parent handle in its own context, and that shared disposed flag can make a later foreign call appear to be a harmless repeat. Concurrent disposal across flows also needs an explicit ownership/atomicity decision. T01 tests the existing absent-frame rejection without claiming universal cross-flow detection; these inherited-frame and repeated-foreign cases remain required T03 work.

## WP04-S03-T02 merged scope view

WP04-S03-T01 is committed as 467b9f2. Internal CapturedScopeContext and OperationScopeStore.Capture implement the captured merged view required by T02. First-encounter candidate selection is bounded at 1023 keys; later scopes still replace existing candidates before the final independent value projection. Scope input loss survives replacement. These are implementation choices under the preserved last-ordinal-key-wins and bound/provenance contracts. No original contract, public runtime placeholder, dependency policy or historical evidence changes. T03 full owning-flow disposal and T04 shared-story qualification remain open.

T02 advisory corrections qualify captured dictionary enumeration as .NET-specific observed behavior, sort the development observer case-key list, and make its negative control change membership. A later duplicate of an omitted key is explicitly qualified. Candidate-cap mutations can be output-equivalent yet change resource use; WP07-S03 must evaluate that difference and must not silently classify them as contract-equivalent.

## WP04-S03-T03 creator-owned disposal

WP04-S03-T02 is committed as `3b0e684`. On 11 October 2026 the user explicitly kept BeginScope and the creator-disposes rule, with documented detection limits. The [amendment](logging/plan-amendments.md) withdraws our later unpublished universal cross-flow rejection promise and preserves the original supplied plan. The [scope guidance](../docs/modules/logging/operation-scopes.md) distinguishes guaranteed valid behavior, precise detectable-state guards and prohibited misuse observations. No heuristic owner token, thread/task/ExecutionContext identity check, new dependency, public runtime placeholder or published-contract change is introduced. The existing stack algorithm is retained; its absent-frame message now describes current context rather than claiming flow detection. The selected rule resolves the inherited-frame and repeated-foreign cases raised during T01 as documented prohibited misuse with unspecified behavior, rather than detected rejections. T03 qualification is recorded in implementation status and its retained evidence; T04 full shared scope qualification remains next.

## WP04-S03-T04 barrier-driven scope qualification

WP04-S03-T03 is committed as `c8b897c`. Shared semantic rows, literal expected views, readiness/release barriers, joined worker cleanup and independent C#/Python comparators are qualification choices within the original T04 task. The user-selected creator-disposes contract remains authoritative. Native diagnostic names/messages are adapter bindings; no universal owner detection, simultaneous cross-flow atomicity or public runtime replacement is introduced. Shipping Core and dependencies remain unchanged. The acceptance map preserves original criterion text while declaring the internal scope-state phase and pending public integration. Task execution and completion are recorded separately in retained evidence/status.


## WP04-S04-T01 owned event projection

WP04-S03-T04 is committed as `369607e`. The actual public LogEventData follows the preserved named input shape; immutable internal CapturedEventFacts and EventProjection hold projected allowlisted facts without configuration/caller/scope references. Explicit severity/resource maps, bounded candidate merging, existing typed values/exception projection, and scalar-safe message/template clipping are implementation choices under the selected truncation amendment. None skips projection; undefined native severity fails before content. Supplied malformed message/template suffixes remain invalid. This does not introduce public logger placeholders or redefine S04 completion: T02 detailed precedence, T03 event UTC/W3C capture, T04 byte encoding and T05 complete structural fixtures remain required. Shared observations are development transport with controlled metadata and independently observed actual process identities. Original supplied plans and historical evidence remain unchanged.

## WP04-S04-T02 event precedence qualification

WP04-S04-T01 is committed as 4fe61b7. Existing shipping merge/capture code already implements whole-value last-ordinal-key precedence and trimmed optional IDs. T02 qualifies these rules with literal shared/native inputs, all typed winners, protected nested spellings and independent comparator controls. Candidate retention remains the implemented first-encounter bounded policy; explicit matching keys replace retained scope candidates, while new event keys may be omitted under bounds. This is a maintainer qualification choice, not a new user request. Actual W3C pair capture and complete reserved-correlation/queued evidence remain T03; complete S04 wire/byte qualification remains T04/T05. No shipping/source-contract/historical-evidence changes or runtime placeholders are introduced.


## WP04-S04-T03 event time and correlation

WP04-S04-T02 is committed as 3761d66. [Event correlation](../docs/modules/logging/event-correlation.md) adds immutable UTC time and paired validated W3C IDs to the existing owned facts. Internal TimeProvider injection, textual nonzero validation, pre-clock ID sampling and separate independently observed native current-ID bindings are maintainer choices within the original T03 task. Test-only private CLR faults are explicitly native controls, with no shipping reflection or portable public-API claim. Named DTO references are read before callbacks; concurrent caller mutation is not an atomic snapshot contract. Shipping encoding/bytes, admission and complete S04 qualification remain later tasks. Original plans, contracts and historical evidence are preserved.

## User-directed EnvironmentOptions responsibility correction

On 11 October 2026 the user's revised EnvironmentOptions-Codex-Simplification (1).md replaces the earlier simplification: EnvironmentOptions is a passive value store and initializes nothing. Remove the unpublished facade lifecycle, factory, discovery interfaces and redundant setup/snapshot types. Application startup supplies values and calls any overrides; logging prepares settings and captures metadata. Public setters allow assignments from another application assembly. No compatibility bridge is justified by the empty publication ledger. Current tests explicitly migrate removed mechanics and diagnostic owners while preserving input validation and release gates. [Implementation and review record](environment-options-simplification.md) reports the final ownership and evidence. The logging roadmap remains paused and prior uncommitted WP04-S04-T03 work/evidence remains preserved.

## User-requested explicit default population

The user asked to close the missing heuristics/default-population gap after the passive-store correction. EnvironmentDefaults.Apply(applicationAssembly) now supplies common assembly, host, runtime/process, UUID and UTC facts through one explicit call. The store remains passive. Caller startup applies defaults, then explicit assignments, then known environment overrides before starting consumers. Environment name/kind, build/commit identifiers and logging values remain caller-controlled. Repeated helper calls reapply defaults rather than using lifecycle/readiness/publication state. This follow-up does not resume the paused logging roadmap, publish an API or introduce a general startup pipeline. The current guide and separate environment-defaults implementation record describe exact behavior and executed evidence.

## User-selected readonly static environment construction

The latest direct user request makes obvious runtime/identity facts readonly and populates defaults followed by overrides in the constructor, with common identity strings interned. The user explicitly selected keeping the static container with a static constructor. This supersedes the passive-store attachment and explicit EnvironmentDefaults.Apply candidate where they conflict. First access discovers entry-assembly and current runtime/process facts and applies existing alias/canonical variables. Fifteen properties are getter-only; EnvironmentKind, CommitId and BuildId remain ordinary caller policy, and the getter-only Logging reference contains mutable module-owned options. Four bounded validated identity strings are interned after override selection. No explicit initializer, reset/readiness state, snapshot transport, runtime startup or new dependency is introduced. CLR static failure semantics are documented and exercised. Controlled test transports preserve useful metadata/validation vectors without mutating readonly production facts. The nine migrated mechanic bindings are recorded explicitly; all 41 Planned release cases remain unchanged. The broader logging roadmap remains paused and the changes remain uncommitted.

## Resume logging implementation (12 October 2026)

The user requested continuing logging after committing the environment simplification and UTC/W3C work as `248f2f74`. Resume at WP04-S04-T04, then complete T05 before moving to runtime work. Keep solutions within their intended task scope; introduce abstractions only for a concrete requirement. The current constructor decision remains authoritative.

## WP04-S04-T05 complete event qualification

WP04-S04-T04 is committed as `9f43728`. The [shared qualification](../docs/modules/logging/event-qualification.md) executes real capture and JSONL encoding for every S04 criterion, applying the existing user-selected configurable-cap/truncation amendment separately from preserved source wording. Controlled test metadata/time, real activity observations, readiness/release barriers with joined cleanup, independent wire/type comparators and producer schema validation are implementation choices within the requested task. Review sharpened delayed scope/trace changes and comparator controls without changing shipping code, API, dependencies or introducing queue/output substitutes. The task completes S04 internal capture/encoding qualification; logger filtering/write outcomes start at WP04-S05-T01, with output/runtime/provider/release obligations still pending. Source plans and historical evidence remain unchanged.

## WP04-S05-T01 runtime-bound logger foundation

WP04-S04-T05 is committed as `9277ef84`. The [logger foundation](../docs/modules/logging/logger-foundation.md) binds the actual named methods and immutable results to owned metadata, per-runtime scopes, frozen thresholds/cap and one internal nonblocking admission interface. A bounded gate surrounds closure/fanout but never caller work. Output implementations supply their own tickets and counters; controlled doubles are explicit admission controls rather than simulated delivery. Public runtime creation/lifecycle remain absent until their real WP05 composition, without placeholder stubs. These are concrete task implementation choices under the selected architecture and truncation policy. Claude corrections separate owned encoder defects from caller-invalid capture and qualify original bytes with strict decoding/corruption controls. Complete ordering/outcome/error/counter qualification and output/lifecycle/provider work remain later tasks; no dependency, startup-framework, publication or original-source-plan change is introduced.

## WP04-S05-T02 exact logger decision ordering

The T01 implementation already satisfies the [exact order](../docs/modules/logging/logger-decision-order.md); T02 adds direct observations of argument precedence, early skipped caller work, interested-output fault selection, capture-before-admission and closure recheck. Cooperative tripwires and bounded joined cross-thread closure probes use actual Core with explicit non-I/O admission controls. Claude found missing mixed-fault cases; both orientations now require healthy interested admission, and follow-up review passed. Captured truncation survives late closure; invalid capture returns before that check. These are qualification choices, without shipping API/dependency changes or a scope spy/lifecycle/output-delivery claim. Complete S05 and real runtime/output/provider/release work remain subsequent tasks.

## WP04-S05-T03 lazy capture, outcomes and global loss counters

The [capture/outcome qualification](../docs/modules/logging/logger-capture-outcomes.md) checks actual Core with per-write factory counts, global rejection, original factory exception identity and output-owned ticket passthrough. Distinct prior-ticket controls and valid-invalid-valid/admission-rejection sequences expose invented tickets; contiguity remains the output implementation's responsibility. A direct atomic increment saturates the two global loss counts at Int64.MaxValue instead of wrapping, fulfilling the existing nonnegative-counter contract; a saturated count is a lower bound. These are prepublication implementation/qualification choices, without new public APIs, dependencies or a metrics framework. Scheduling probes are observations rather than deterministic lost-update proofs; CAS correctness is also source-inspected. Generic attribute/exception callback capture remains UnsupportedValue; no every-draft-code claim is made. Claude final review passed after retaining current real evidence and sharpening per-write/qualification wording. T04 full table/S05 mapping and actual output/lifecycle/provider/release work remain open.

## WP05-S01-T02 bounded human views

The [human output guide](../docs/modules/logging/human-output.md) describes actual internal normal/verbose renderers over the already owned v1 JSONL. Reading those bytes avoids recapture and retaining caller objects or a second fact graph. Quoted scalar escapes, visible Unicode format/control handling, reserved protected context, scalar-clipped message and whole typed payload omission are prepublication display choices under the user's size/truncate rule. No new public API, dependency, worker, terminal heuristic or template parser is introduced. The renderer's loss flag includes capture plus display loss without changing JSONL; runtime aggregation/composition remains pending. Human layout is not a permanent wire contract. Original source plans and previous evidence are preserved.

## WP05-S01-T03 explicit format selection

The [presentation selector](../docs/modules/logging/presentation-separation.md) takes owned capture plus explicit format/detail/cap, reuses original JSONL memory and forwards human rendering loss without retaining settings, destination or caller objects. A readonly byte-view value avoids another JSONL buffer. Unknown choices fail rather than guessing; a selected cap below the captured JSONL byte length is an internal composition error. Runtime composition uses one prepared cap, while a different valid selector cap that still fits those bytes is accepted. These are maintainer implementation choices within T03, without public APIs, dependencies, message-template parsing or runtime/output composition. Qualification crosses actual logger thresholds with all presentation choices and replaces ambient/caller state before rendering. Direct shipping-IL dependency guards and canonical inspection qualify no inference, without claiming attached-debugger/TTY runs or arbitrary transitive proof. Runtime output/stream and render-loss integration remain pending.

## WP05-S01-T04 rendering qualification

The [rendering story qualification](../docs/modules/logging/rendering-qualification.md) extends an existing test fixture to render each original admitted capture through all explicit format/detail choices. Literal neutral structures, complete human information assertions, order/spacing controls with no normalization, and current supplemental presentation/human runs qualify all four S01 criteria at the internal seam. These are qualification choices, with no shipping source/API/dependency or runtime output/composition framework change. Operational composition and release remain pending; historical checkpoints and original plans are unchanged.

T04 comparer review follows the existing v1 contract: both Integer and Number project as JSON numbers, and wire values cannot prove their constructor kind. C# semantic numeric comparison stays unchanged; Python now accepts equivalent numeric notation while preserving distinct Boolean/string kinds. Positive integer-with-.0 and exponent-notation controls plus a Boolean-to-number corruption control qualify that policy. Safe ASCII spaces after display `=` are accepted consistently. This adds no lexical-number wire contract, shipping code or field normalization.

## WP05-S02-T01 bounded output queue foundation

The [queue foundation](../docs/modules/logging/output-queues.md) uses actual bounded Channels, one awaited worker per enabled output, trusted internal writer callbacks and suppressed creator context. Primitive closure and observable callback faults avoid retained work; they do not complete public stop/flush deadlines or S03 failure/abandonment accounting. Shared controlled MemoryStream cases are actual queue/writer probes, without claiming physical destination composition, runtime tickets/outcomes, full S02, normal destination I/O isolation or release. These are scoped implementation choices under the original task; public contracts, dependencies, original plans and historical checkpoints remain unchanged.

## WP05-S02-T02 successful ticketed runtime fanout

The [actual runtime fanout](../docs/modules/logging/queued-fanout.md) uses a readonly event/ticket slot and one shared admission/dequeue/closure gate, with capture and writer callbacks outside it. An internal creation entry freezes only the values this composition consumes; it is not a public placeholder or new startup framework. Positive successful tickets advance only on admission; Int64 exhaustion terminally rejects without wrapping. Existing filtered/unavailable hooks are recorded, while complete S03 diagnostics/failure/abandonment accounting and public physical-output/lifecycle/provider/release remain pending. Controlled MemoryStream/IOException cases are actual runtime/queue probes, without a persistence or full-story claim. These are scoped implementation choices under the original T02 task; supplied sources and historical checkpoints remain preserved.

## WP05-S02-T03 incoming overflow qualification

The [incoming overflow cases](../docs/modules/logging/queue-overflow.md) qualify the existing real Channel admission and Core result composition rather than adding another shipping mechanism. Literal per-output outcomes, accepted caller/byte preservation and controlled one-sided recovery/fault/filter cases distinguish admission from delivery and retain only per-destination order. Reusing the existing fixture and an explicit native catalog profile are scoped verification choices; complete S03 output counters/diagnostics, T04 bookkeeping, physical outputs/lifecycle/provider and release remain pending. Original supplied plans and historical evidence remain unchanged.

## WP05-S02-T04 bounded cohort qualification

The [seeded producer cohorts](../docs/modules/logging/queue-bursts.md) qualify all four S02 criteria at the existing internal Core/Channel/memory-writer seam. Literal cohort counts and independently checked caller/ticket bytes avoid fixing scheduling-dependent winners. Sampled held-writer queue maxima, atomic callback maxima and canonical single-worker inspection distinguish accepted worker bounds from caller/pre-admission memory and later flush-control obligations. Explicit shared transport ticket checks and reused native transport validation are verification choices, with no shipping/API/dependency change. S03 accounting/diagnostics, physical output/lifecycle/provider and release remain pending; original plans and historical records are unchanged.

## WP05-S03-T01 cumulative output accounting

The [output accounting foundation](../docs/modules/logging/output-accounting.md) adds direct fields and a readonly internal value under the existing admission/dequeue gate. BufferedCount preserves the older queue-only hook, while Pending includes unresolved in-flight work. Successful callbacks, throwing callbacks and terminal queued cleanup supply written/failed/abandoned transitions without I/O under the gate or a new counter framework. Independent rejection/filter counts and their aggregate sums saturate; accepted/tickets stop terminally, preserving the exact accepted disposition identity. This limit policy is a maintainer implementation choice for the unpublished contract, not separate user confirmation. Literal controlled-writer checkpoints, unchanged historical transport semantics, independent comparers and private unit limit/concurrency controls qualify the scoped implementation. Claude required and approved explicit exhaustion partitions; all current verification and graph/evidence records are retained. Broader physical partial/uncertain-write boundaries, full states/safe fault information, public diagnostics and lifecycle/provider/release remain later tasks. Original plans and historical evidence remain unchanged.

## WP05-S03-T02 live accounting boundaries and write confirmation

The [live observation implementation](../docs/modules/logging/delivery-observations.md) adds accepted sequence to the existing output value and a small readonly runtime value containing nullable output slots and global counts under the shared gate. The atomic global invalid read fixes the observation point while output/truncation/closure fields remain stable. Existing awaited callbacks confirm complete records; actual controlled Stream writes qualify unresolved partial/full bytes, ignored cancellation requests and terminal IOException/OCE without retry or false confirmation. These are direct task implementation/qualification choices, not a new public snapshot framework or separately approved policy. Fixed historical target registration/coalescing remains the explicit WP05-S08 scope. Claude identified a real Unicode fixture mismatch from a Windows default-encoding read; explicit UTF-8 source reads and ASCII Unicode escapes resolve it, with independent actual-byte and final verification evidence. Historical garbled-fixture reports remain identified as pre-correction evidence, while unrelated prior Unicode source issues remain outside this task. Full fault states/safe metadata/public diagnostics, physical destination ownership/lifecycle/provider and release remain pending; original plans and historical evidence are preserved.

## WP05-S03-T03 actual output lifecycle and safe faults

The [output state implementation](../docs/modules/logging/output-states.md) adds the already selected enum and direct lifecycle/fault fields to the actual worker. Terminal callback failure atomically closes admission, classifies the in-flight failure and abandons all queued unstarted work under the existing gate. Primitive stop tracks worker completion separately from terminal fault disposition, preserving both original completion exceptions and safe retained category/system UTC time. AdmissionExhausted is an internal maintainer-selected category for representational limits; it abandons queued work immediately while unresolved in-flight work remains pending. Later callback failure updates the last fault without retry. These are scoped implementation choices, not new user policy or public lifecycle placeholders. Public GetDiagnostics, fixed flush coordination, physical destination ownership/deadlines/provider and release remain later tasks; supplied sources and historical evidence are preserved.

## WP05-S03-T04 concrete public live diagnostics

The [public diagnostic implementation](../docs/modules/logging/diagnostics.md) uses sealed getter-only values and an owned read-only collection projected from actual kernel observations under the same gate. First real admission closure is stamped once, live runtime state derives from closure and worker states, and the system UTC observation label does not invoke a caller clock. Diagnostic reads perform no routing, output I/O, retries or new tasks. The draft LastStopResult member and dependent LogStopResult remain tracked for the real bounded shutdown operation in WP05-S10; this dependency ordering does not remove the final v1 requirement or change preserved reference catalogs. Additional internal kernel guard behavior, literal composed fixtures, saturation/ownership/concurrency and compiled call-boundary controls are task implementation choices. All four S03 criteria are mapped at the actual controlled stream/queue seam; public physical creation/lifecycle, provider and release remain later work. User instructions and historical source plans remain intact.

## WP05-S04-T01 configured console composition

WP05-S03-T04 is committed as 5a83a1d7. [Configured console composition](../docs/modules/logging/console-composition.md) freezes concrete module options before selecting a stream, prepares the interested healthy console bytes before admission, and carries the owned memory in the existing queue slot. Write/global loss includes additional human omission once, while the shared JSONL capture stays unchanged. These are scoped implementation choices under the selected static environment and truncation amendments; no public placeholder/snapshot framework or new dependency is introduced. Opt-in strict UTF-8 console-child decoding corrects the host-code-page mismatch exposed by actual console records, preserving existing decoding for other helper callers. Complete borrowed ownership/startup/environment-consumer/public lifecycle and release qualification remain later tasks; preserved source/reference files and all 41 Planned release rows are unchanged.

## WP05-S04-T02 borrowed console ownership

WP05-S04-T01 is committed as 92006ca8. [Borrowed console qualification](../docs/modules/logging/console-ownership.md) makes the existing stream lifetime and awaited worker serialization explicit and verifies actual held/chunked/failing Streams, concurrent ticket/byte order, idle closure, sequential reuse and physical destination selection and continued application output. These are implementation/qualification choices within the authorized roadmap, with no new public API/dependency or global coordination framework. Failed-start rollback and full environment consumers remain T03/T04; public bounded lifecycle and release remain later work.

## WP05-S04-T03 console initialization and owned-worker rollback

WP05-S04-T02 is committed as 7667b5e4. [Console startup](../docs/modules/logging/console-startup.md) checks concrete writability before publication, exposes the already selected safe CoreLogInitializationException, and closes/joins newly acquired idle workers on validation or later acquisition failure. ConsoleInitializationFailed, ConsoleStreamNotWritable and WorkerInitializationFailed are fixed maintainer-selected categories for real output startup failures. They refine the preserved reference catalog's broad OutputInitializationFailed category; the later public LogErrorCodes catalog must reconcile these actual categories before publication. The historical reference list remains unchanged. A trusted internal launcher supplies deterministic pre-acquisition failure while launching actual workers; production remains Task.Run. These are direct task implementation/qualification choices, not a new user policy or general startup framework. Borrowed streams and another runtime remain intact; physical file resources/public lifecycle/full S04 consumers and release remain later work.
