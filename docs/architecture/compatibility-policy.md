# Permanent preservation of published Core contracts

Selected on 10 October 2026 for WP02-S04-T01. This is the maintainer's compatibility policy within the user-authorized roadmap, applying the recorded architecture, facade-name, dependency and truncation amendments. The source documents supply the design baseline; they are not independent user instructions. [The review inventory](../../compatibility/csharp/preservation-review.json) identifies the exact unpublished C# declarations and portable semantics reviewed.

## Publication boundary

Preserve every supported contract indefinitely from its first actual publication, including prereleases and contracts introduced by intermediate releases. A local version number, Git checkpoint, scaffold package, reference-only specification or successful compiler check does not create an actual publication baseline. The current declarations are proposals and can still be corrected with explicit review before publication. Do not label them archived historical releases or claim current logging runtime compatibility.

A package major version never waives preservation. Keep old supported APIs and behavior available and tested when adding a separately selected API/schema/configuration path. A separately named successor product cannot be described as a compatible replacement when it requires migration. Deprecation documentation may identify alternatives, but do not add warning-producing Obsolete attributes to supported calls: existing warnings-as-errors consumers must still build.

Published promises cannot later be reclassified as unsupported to avoid a regression. The policy covers documented guarantees and supported usage, not private-reflection dependence or undocumented implementation accidents, and does not promise execution on every future operating system/runtime. A defect fix affecting supported observable behavior requires a regression and explicit compatibility assessment.

## Public API and construction paths

The C# package and assembly are CIS.Core, with root namespace CIS. Preserve the published namespaces, public type/member identities, visibility, class/struct and sealed/static shape, implemented interfaces, nullability, generic constraints, property accessors and sync/async signatures. A module folder is not a separate package: CIS.Logging and CIS.Logging.MicrosoftLogger stay in Core. Configuration diagnostics use CIS.Configuration.

Preserve exact ordered parameter names/types as well as return types. Named arguments are part of the supported source contract. Keep existing overloads and optional-argument/default semantics. Never add a required parameter/property to an existing construction path. Test additions against prior null, conversion, named, extension and interface-implementation call patterns rather than assuming additive metadata guarantees binding. Do not add required members to implementable public interfaces; introduce a separate optional capability instead. Do not change published struct layouts or replace classes with structs.

| Reviewed surface | Construction/signature commitments |
| --- | --- |
| CIS.EnvironmentOptions | Static Initialize(setup), Current, IsInitialized and convenience getters; preserve the user-selected EnvironmentOptions spelling. There is no misspelled alias or Reset. |
| EnvironmentSetup, LoggingSetup | Parameterless caller-owned mutable named inputs; selected absent/null/inherited settings remain compatible. |
| EnvironmentSnapshot, LoggingOptions, EventMetadata | Sealed getter-only owned outputs from successful initialization; no public result constructors. |
| ConfigurationIssue | Constructor parameter names code, key, explanation; immutable properties. |
| CoreConfigurationException | Constructor parameter issues; immutable copied Issues collection and safe diagnostic categories. |
| LogEventData, OperationContextData | Parameterless mutable input objects with named properties; event severity comes only from the method. |
| LogValue | Explicit Null(), Boolean(value), String(value), Number(value), Integer(value), IntegerString(value), DecimalString(value), Uuid(value), Timestamp(value), Array(items), Map(properties); no arbitrary object factory or implicit conversion. Preserve Kind/IsTruncated and owned value graphs. |
| CoreLogRuntime | Create(environment), CreateLogger(component), BeginScope(context), FlushAsync(timeout, cancellationToken), StopAsync(cancellationToken), GetDiagnostics(), DisposeAsync(); implements IAsyncDisposable, with no synchronous Dispose or positional runtime-settings overload. |
| CoreLogger | IsEnabled(level), Write(level, data), WriteLazy(level, factory); distinct direct/lazy names avoid nullable overload ambiguity. |
| CoreLoggerProvider | Constructor runtime; CreateLogger(categoryName), SetScopeProvider(scopeProvider), Dispose(); Microsoft ILoggerProvider/ISupportExternalScope and inherited IDisposable. The proposed constructor is nonowning only. |
| Write/flush/stop/output/diagnostic types | Sealed, getter-only observations with owned collections and no public constructors; preserve result/status meanings and nullable accepted-only tickets. |
| CoreLogInitializationException | Safe OutputName/ErrorCode output-initialization failure; no public constructor in the proposed surface. |

The inventory enumerates all 37 proposed public types, including twelve enums, and records every reviewed method, constructor, property, constant and parameter. PublicSurface.cs, EventSurface.cs, RuntimeSurface.cs and ProviderSurface.cs remain authoritative full reference declarations; the startup/runtime JSON catalogs and their captured hashes anchor the review. Actual parameterless-constructor defaults/validation and final shipping annotations must be recorded and tested during implementation before publication: extern declarations do not prove runtime behavior.

Preserve all published numeric enum values and constant values, including inlined strings used by already compiled consumers. The reviewed enums cover EnvironmentKind, levels, output formats/streams/detail, value kinds, admission outcomes/statuses, completion, runtime/output state and writer-flush status. ConfigurationIssueCodes and LogErrorCodes are named string categories, not raw diagnostic payload. The inventory contains the exact values; keep additions from changing the meaning of old values or accepted tokens.

## Configuration, defaults and targets

Preserve every published key/alias, recognized value, default, inclusive bound, validation category, absence rule and precedence decision. The current precedence is nonblank CORE variable → supported DD alias → explicit setup → discovered fallback → documented default. Invalid higher-priority input is rejected without falling through. Trimming/token case rules, user-selected severity abbreviations, independent output thresholds and individual validation of disabled-output settings are part of the contract.

The reviewed defaults and bounds are:

| Key | Default and selected bounds |
| --- | --- |
| `CORE_SERVICE_NAME` | discovery: applicationName |
| `CORE_ENVIRONMENT` | unknown |
| `CORE_SERVICE_VERSION` | metadata: BuildVersion |
| `CORE_LOG_LEVEL` | Information |
| `CORE_LOG_CONSOLE_FORMAT` | human |
| `CORE_LOG_CONSOLE_LEVEL` | inherit: Logging.Level |
| `CORE_LOG_CONSOLE_STREAM` | stderr |
| `CORE_LOG_HUMAN_DETAIL` | normal |
| `CORE_LOG_FILE_PATH` | absent; file output disabled |
| `CORE_LOG_FILE_FORMAT` | jsonl |
| `CORE_LOG_FILE_LEVEL` | inherit: Logging.Level |
| `CORE_LOG_QUEUE_CAPACITY` | 1024; range 1–65536 |
| `CORE_LOG_FLUSH_INTERVAL_MS` | 1000; range 10–60000 |
| `CORE_LOG_SHUTDOWN_TIMEOUT_MS` | 5000; range 1–300000 |
| `CORE_LOG_FILE_MAX_BYTES` | 10485760; range 65536–1073741824 |
| `CORE_LOG_FILE_RETAINED_FILES` | 10; range 1–1000 |
| `CORE_LOG_SCHEMA_VERSION` | 1; range 1–1 |
| `CORE_LOG_MAX_ENTRY_BYTES` | 65536; range 16384–16777216 |

DD_SERVICE, DD_ENV and DD_VERSION remain the respective aliases for service, environment and service version. ServiceVersion defaults to actual application BuildVersion, rather than Core's version. Application discovery is a fallback, never the Core assembly identity. Resolve relative output paths once against the captured initialization directory. Default stdout/stderr separation and independent output enablement cannot silently change on upgrade.

The user-selected entry policy is 65536 encoded bytes by default, overridden through CORE_LOG_MAX_ENTRY_BYTES / LoggingSetup.MaxEntryBytes / LoggingOptions.MaxEntryBytes in the selected 16384–16777216 range. Oversized supported payload is truncated at valid boundaries and marked; it is not ordinary invalid capture. An enabled file requires FileMaxBytes >= MaxEntryBytes. Preserve the marker, deterministic projection, global truncated count and LossObserved meaning with this contract after publication.

Preserve every published target asset and minimum framework requirement, including those published in prereleases. The selected first C# asset is portable managed lib/net10.0/CIS.Core.dll with minimum runtime family 10.0. Do not silently remove a target, raise its minimum, retarget an old consumer, use a baseline-TFM ignore switch or change its signing identity to make an upgrade pass. New targets are additive after their own qualification. Windows x64 and Ubuntu 24.04 x64 remain distinct required release lanes; current Windows foundation evidence does not qualify full release or Linux behavior.

Preserve assembly name/culture, stable selected AssemblyVersion 1.0.0.0 and the unsigned/null public-key-token identity. Package/file/informational versions identify advancing releases independently. Distribution signing is separate and still unresolved. Microsoft-maintained dependencies remain explicitly approved with verified closure; third-party runtime integrations use separate optional packages. Exact dependency versions may evolve only with historical binding/public-interface/target compatibility evidence and the allowed dependency policy.

## Ownership, behavior and wire meaning

Startup copies named inputs and publishes one complete immutable snapshot, with specified pre-init, invalid/repeated/concurrent initialization behavior and no Reset/output creation/environment mutation. Application identity, telemetry release, instance identity, process start and initialization instant remain distinct. Do not substitute collector addresses for host identity or project arbitrary configuration/secrets into event metadata.

The application owns runtime shutdown/disposal. Core loggers and the Microsoft provider borrow it; provider disposal cannot stop a shared runtime. File workers/handles are owned; console streams are borrowed. Scopes belong to their runtime and logical async flow, copy bounded inputs, dispose LIFO and preserve context precedence. Caller mutation cannot change captured events/results. These ownership rules, thread-safety promises and safe exceptions/codes are behavioral compatibility obligations once published.

Preserve argument validation before state checks, Stopped before None/Filtered, all-faulted rejection before capture, method-only severity, lazy-at-most-once behavior and per-output independent bounded admission. Preserve accepted-only tickets, initial invalid/truncated/rejection observations and the boundary equation accepted = written + failed + abandoned + pending. Completed does not imply Succeeded; successful drains can still report prior loss. Pending is unresolved work, and must not be called written or abandoned merely because cancellation was requested.

Flush captures a fixed nondroppable boundary with bounded/coalesced coordination. Caller cancellation cancels that wait only. First Stop closes admission and starts one monotonic overall deadline, including with an already cancelled caller token; repeated Stop/Dispose join it. Do not reopen admission, restart deadlines, dispose under unresolved writes or rewrite immutable observations after later completion. Preserve output failure isolation and lack of automatic retry. Accepted, written and userspace flushed remain distinct from crash durability and collector acknowledgement; there is no cross-output total order guarantee.

Preserve schema-version selection, field names/types/meaning, required service/env/version and resource identities, exact supported numeric/string forms, UTC/UUID representations, optional omission versus attribute null, Unicode/escaping/LF rules, bounded projection and truncation provenance. Add optional wire fields only under the published reader unknown-field policy; never reinterpret an old field or silently activate a new schema/default. Producer projection remains allowlisted even though readers tolerate additions. JSON member order is not a guarantee. Later language implementations declare capabilities and execute portable cases; C# CLR signatures and binary replay do not automatically transfer to another language.

File formatting, segment ownership/naming/rotation/retention/restart recovery and Microsoft category/event/state/formatter/scope mappings must receive their final documented semantics and tests in WP05/WP06 before publication. This review records those obligations and existing guide intent; it does not fabricate unspecified implementation behavior.

## Review and remaining qualification

For each proposed change, identify every affected published contract and historical publication membership; preserve the old path, add a separately selected path when needed, and prove compatibility with the release harness. Never overwrite/regenerate historical baselines or substitute candidate-produced expectations for old ones. Actual publication history is not inferred from candidate targets or current APIs, and empty history never turns first-release qualification into a pass.

WP02-S04-T01 completes this policy and source-bound review only. Append-only publication/baseline format is T02; detailed four-layer replay/substitution/normalization and zero-history rules are T03; compiling consumer patterns and incompatible controls are T04. Actual cumulative compatibility execution and durable replay remain WP07/WP09. No publication or historical upgrade success is claimed here. Licence, destination/signing, final shipping contracts, both platform release lanes and full runtime evidence remain publication prerequisites.
