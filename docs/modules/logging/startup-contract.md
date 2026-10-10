# Startup, identity and configuration contract

WP02-S01 specifies the proposed v1 contract. The user selected **`EnvironmentOptions`**, correcting the supplied plan's `EnviormentsOptions` spelling before publication. The [amendments](../../../planning/logging/plan-amendments.md) take precedence over the preserved source documents.

This story defines signatures, types and expected behavior. The [reference-only C# specification](../../../implementations/csharp/verification/contracts/startup/PublicSurface.cs) and compiled consumers validate the proposed API shape. They are outside `CIS.Core`, are nonpackable and supply no executable initialization implementation. WP03 implements metadata discovery, configuration resolution/validation and publication. These artifacts are neither a released API baseline nor passing evidence for those runtime behaviors.

The [shared startup contract](../../../contracts/logging/v1/startup-contract.json) records portable semantics and metadata field kinds. C# type names and signature details remain in the implementation directory, so later languages can declare their own appropriate runtime surface.

## C# public shape

| Namespace | Proposed types |
| --- | --- |
| `CIS` | `EnvironmentOptions`, `EnvironmentSetup`, `EnvironmentSnapshot`, `EnvironmentKind` |
| `CIS.Logging` | `LoggingSetup`, `LoggingOptions`, `EventMetadata`, `CoreLogLevel`, `LogOutputFormat`, `ConsoleOutputStream`, `HumanOutputDetail` |
| `CIS.Configuration` | `ConfigurationIssue`, `ConfigurationIssueCodes`, `CoreConfigurationException` |

`EnvironmentOptions.Initialize(EnvironmentSetup setup)` returns `EnvironmentSnapshot`. `Current` returns the identical published instance; `IsInitialized` reports successful publication. Getter-only convenience properties expose `ApplicationName`, `BuildVersion`, `ServiceVersion`, `EnvironmentName`, `HostName`, `OperatingSystem`, `Logging` and `EventMetadata` from that instance. The parameter name `setup` participates in named-argument consumer checks.

`EnvironmentSetup` is a sealed caller-owned input class with a parameterless constructor and named, mutable properties: `ApplicationAssembly`, `ApplicationName`, `BuildVersion`, `ServiceVersion`, `EnvironmentName`, `EnvironmentKind`, `CommitId`, `BuildId` and `Logging`. Nullable strings/assembly/logging inputs are absent when not supplied; `EnvironmentKind` defaults to Unknown. `ApplicationAssembly` identifies the consuming application. `EnvironmentName` accepts the host's already-resolved framework environment; Core cannot change that host setting.

`LoggingSetup` is the sealed named input for optional level, format, stream, detail, file path, queue capacity, flush/shutdown milliseconds, file byte limit, retained segment count and schema version. Nullable typed inputs distinguish absence from explicit values; non-null invalid enum/numeric setup values must fail validation just as selected environment tokens do. Setup fixtures use canonical symbolic enum tokens and flattened property paths such as `Logging.ConsoleFormat`; a C# resolver adapter maps those tokens into the documented enums, rather than inventing string setters.

`EnvironmentSnapshot`, `LoggingOptions` and `EventMetadata` are sealed, getter-only outputs without public constructors or setters. The resolver owns their construction and copies setup/nested logging inputs before publication. No resolved object retains caller-owned mutable state. Public options use named classes rather than expanding positional records. `LoggingOptions.FlushInterval` and `ShutdownTimeout` are `TimeSpan`; setup/environment inputs explicitly use milliseconds. `FileMaxBytes` is `long`; its v1 range is still fixed by the configuration catalog. `ConsoleEnabled` and `FileEnabled` expose resolved enablement.

The [C# surface](../../../implementations/csharp/verification/contracts/startup/surface.json) records every property's C# type and all enum values. `EnvironmentKind` is Unknown=0, Console=1, Service=2, Web=3, Function=4. `CoreLogLevel` is Trace=0, Debug=1, Information=2, Warning=3, Error=4, Critical=5, None=6. Presentation enums have explicit values: Human=0/JsonLines=1, StandardError=0/StandardOutput=1, Normal=0/Verbose=1. These selected values become permanent when published; there is no published compatibility baseline yet.

## Publication and errors

Before a successful call, `IsInitialized` is false, while `Current` and convenience getters throw `InvalidOperationException`. `Initialize(null)` throws `ArgumentNullException` for `setup`. Each valid attempt captures recognized inputs once, resolves and validates a complete local result, then atomically publishes it. Validation failure throws `CoreConfigurationException`, publishes nothing and leaves a later valid attempt possible. After publication, subsequent non-null initialization attempts throw `InvalidOperationException` and preserve the original instance.

Concurrent valid calls produce exactly one successful publisher; losing callers receive `InvalidOperationException`. An invalid attempt cannot publish or replace state. No reader can see partial options. Caller setup is not a synchronization object and callers must not mutate it concurrently with initialization. Mutation after initialization returns cannot change the published snapshot. Only the private synchronization and published-reference fields needed for this behavior may be static; individual option values and operation context are not mutable globals.

There is no production Reset API. Tests of the eventual static implementation use isolated child processes, while the internal resolver accepts explicit variable, clock, UUID and metadata sources. The CLR static constructor stays trivial. Resolving or reading configuration must not create directories, open outputs, start workers, subscribe global exception events, contact collectors or change process environment variables. [Lifecycle fixtures](../../../conformance/logging/startup-lifecycle-cases.json) describe the required future observations; they have not executed against a runtime.

`CoreConfigurationException(IEnumerable<ConfigurationIssue> issues)` owns an immutable copied collection exposed as `IReadOnlyList<ConfigurationIssue>`. Each sealed issue has getter-only `Code`, `Key` and `Explanation`. Constructor parameter names are `issues`, and `code`, `key`, `explanation` for `ConfigurationIssue`. A read-only view over a caller-owned list is insufficient. Collect all independent selected-setting errors in one exception, ordered by the configuration catalog, followed by metadata/setup checks. Diagnostics identify a recognized setting or setup field and give a safe explanation, without raw supplied values, unrelated environment variables, paths, credentials or collector addresses.

| Stable code | Meaning |
| --- | --- |
| `InvalidToken` | Selected token or invariant integer syntax is unsupported. |
| `OutOfRange` | Integer overflows its parsing domain or exceeds the inclusive option bounds. |
| `UnsupportedSchemaVersion` | A syntactically representable schema integer is not 1. |
| `InvalidPath` | Selected path cannot be resolved syntactically into an absolute path. |
| `InvalidIdentity` | Selected identity exceeds its scalar bound, contains controls or invalid Unicode. |
| `InvalidEnvironmentKind` | Setup supplies an undefined EnvironmentKind value. |
| `MissingApplicationName` | Neither configuration/setup nor application assembly discovery provides an application name. |

An invalid selected alias names that alias in the issue. Missing application identity names `CORE_SERVICE_NAME`; invalid typed kind names `EnvironmentSetup.EnvironmentKind`. Error message prose can improve without changing code meanings. Validation fails safely without needing an active logger.

## Configuration selection

The [catalog](../../../contracts/logging/v1/configuration.json) defines all 17 guide section 8 keys plus the user-requested CORE_LOG_MAX_ENTRY_BYTES entry limit, three DD identity aliases, defaults, tokens, units and bounds. Selection is: nonblank CORE variable, supported nonblank alias, explicit non-null/nonblank setup, discovered fallback, documented default. Trim surrounding whitespace; blank strings are absent. Identity strings retain their case. Tokens use ordinal case-insensitive recognition. Integer tokens use invariant ASCII decimal digits, optionally signed, without grouping, fraction or exponent notation. Numeric severity tokens are invalid. No boolean environment option exists in section 8.

The first selected nonblank invalid input fails; it cannot fall through. Lower-priority values shadowed by a valid higher-priority choice are not the selected input. Validate every selected recognized option, including settings for disabled outputs. Ignore unknown CORE keys for forward-compatible deployments and read only the exact uppercase recognized names/aliases. Never enumerate and dump arbitrary process variables.

Severity tokens retain the user's aliases: TRC, DBG, INF/INFO, WRN/WARN, ERR and CRT/CRIT, plus the full seven names. Per-output levels inherit the fallback only when absent; the fallback is not a global cap. Console Warning and file Debug therefore preserve file interest in Debug. Console None disables console output. A missing file path or file None disables file output. Terminal/debugger state cannot change format, stream or detail.

Capture the working directory once during initialization, resolve a selected relative path against it and store an absolute path. Later directory changes cannot redirect output. Resolution validates syntax without filesystem writes or output ownership checks; opening/locking outputs belongs to runtime creation. Path expectations declare Windows or Linux applicability, preserving Unicode rather than promising common absolute-path syntax across languages/platforms.

[Configuration vectors](../../../conformance/logging/configuration-cases.json) contain valid, absent/blank, invalid, precedence and applicable boundary expectations for every key, including every severity token, alias fallback, numeric endpoints/overflow, identity scalar limits, disabled-output validation, host isolation and multiple safe errors. These are specified test inputs for WP03, not executed resolver results.

## Captured facts and ownership

`BuildVersion` is actual application build identity: supplied build information, explicit application assembly informational version, that assembly's version, then `unknown`. Only when no explicit assembly is supplied may available entry-assembly metadata be used. Never substitute the Core assembly's version. Preserve prerelease/build strings and case. `ServiceVersion` is the deployment/telemetry release label selected from CORE_SERVICE_VERSION, DD_VERSION, explicit setup, then BuildVersion; it cannot alter BuildVersion. `CommitId` and `BuildId` come only from explicit build inputs, not guessed filenames or version suffixes.

Application/environment names are at most 255 Unicode scalars; service/build versions and build identifiers at most 512. Selected values are trimmed, control-free and valid Unicode. Optional unavailable facts remain null. The metadata projection omits unavailable optionals when serialized later. HostName is the name observed by this application; DD_AGENT_HOST remains a collector destination and cannot supply it.

Use standard process/runtime discovery for OS family, optional OS description/architecture, process architecture, framework description, optional process ID and actual process start. OS family is lowercase windows/linux/macos/unknown. Optional architectures are normalized lowercase runtime names. Actual `ProcessStartedAtUtc` is nullable and converted to UTC; never substitute initialization time. `InitializedAtUtc` is the successful initialization instant in UTC. A new UUID string identifies each successful application initialization and stays stable for that run. Runtime discovery limitations do not advertise untested platform support.

`EventMetadata` owns the same allowlisted identity/process facts as the snapshot. It excludes logging settings, file paths, collector destinations, arbitrary environment values, credentials and operation context. Later event projection must use this allowlist. [Metadata fixtures](../../../conformance/logging/metadata-cases.json) preserve informational strings, application assembly precedence, optional unavailable facts, distinct timestamps/release labels, new instance IDs and copied ownership. WP03-S01-T01 implements and tests the [application build-identity discovery portion](application-identity-discovery.md); the complete snapshot vectors have not executed against a metadata resolver.

## Verification

Developer verification compiles the reference-only specification, the positive typed/named-argument consumer and six negative consumers. Negative controls reject snapshot/logging/metadata writes, mutation through the issue list, Reset and the superseded misspelling. Architecture checks inspect the reference-assembly marker, enum constants, sealed read-only outputs, input hashes and fixture completeness. The shipping Core project and package contain none of these specification declarations.

Run `pwsh -File eng/verify.ps1` from the repository root. For isolated compiler controls, run `pwsh -File implementations/csharp/eng/check-startup-contract.ps1`. Stable reports live under `.artifacts/csharp/verification/Developer/contracts/startup/`; architecture tests require those controls as well as the usual Release build/package prerequisites. No contract-shape success qualifies runtime publication, historical binary compatibility or the complete release profile.

During WP02-S02 the user requested configurable entry bytes and truncation. CORE_LOG_MAX_ENTRY_BYTES / LoggingSetup.MaxEntryBytes / LoggingOptions.MaxEntryBytes default to 65536 bytes; selected valid range is 16384–16777216. Resolution/precedence remains WP03; bounded payload truncation remains WP04/WP05. Entry size is distinct from file segment rotation size.

An enabled file output requires FileMaxBytes >= MaxEntryBytes, so an admitted bounded entry can fit a complete segment; a smaller segment setting reports OutOfRange. This cross-option rule is a prepublication implementation choice prompted by the configurable entry cap. Disabled outputs still validate each selected option individually.
