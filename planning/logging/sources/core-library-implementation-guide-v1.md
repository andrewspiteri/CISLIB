# Core Library Implementation Guide

**Specification version:** 1.1  
**Date:** 8 October 2026  
**Initial implementation:** C# and .NET  
**Purpose:** Build a reusable environment and logging core that gives developers and coding agents a tested, maintainable implementation to consume and a stable pattern to follow.

**Recovery review:** This revision carries forward the draft produced during the interrupted discussion. It preserves the agreed scope and clarifies relative paths, write-result precedence, file recovery, delivery observations, and bounded flush coordination. The implementation defaults and test thresholds below remain explicit project design choices; they are not additional requirements attributed to the user.

The first delivery is the working core library and its complete verification suite. Runnable examples follow the core implementation and become part of the release. Equivalent TypeScript implementations follow the established configuration and event contracts.

This guide defines the implementation to build. API examples describe the intended public surface; they are not a claim that the library, example applications, or test suites have already been implemented or executed.

The GMCore logging module and the environment example described in the carried-forward discussion are design references. The requirements below govern the new implementation. The public static class name is **`EnviormentsOptions`**, exactly as requested.

## 1 Requirements and implementation decisions

The core must reduce the number of decisions an agent has to invent: configuration resolution, application identity, event capture, output formatting, buffering, error handling, and shutdown must have one maintained implementation. Examples explain how and when to use that implementation.

| ID | Required outcome |
| --- | --- |
| REQ-001 | Implement the core functionality before creating the reference examples and language ports. |
| REQ-002 | Provide the static `EnviormentsOptions` entry point for validated startup configuration and application/runtime identity. |
| REQ-003 | Automatically attach application name, build version, deployment environment, instance, host, OS, and available runtime context to events. |
| REQ-004 | Configure human, coding-agent, and log-collector output through documented environment variables. |
| REQ-005 | Keep all shipping core code free of third-party NuGet dependencies, including transitive runtime dependencies. Prefer zero explicit package references in the core itself. |
| REQ-006 | Preserve published backwards compatibility indefinitely, including source, binary, configuration, wire format, and documented behavior. |
| REQ-007 | Execute and distribute the complete unit, integration, mutation, architecture, backwards-compatibility, and business-case verification material with every published version. |
| REQ-008 | Demonstrate readable C# and enforce responsibility and dependency boundaries automatically. |
| REQ-009 | Keep the core usable across applications without a dependency on CIS orchestration, CTEP persistence, a game engine, or a collector SDK. |
| REQ-010 | Preserve a language-neutral contract and fixtures that a later TypeScript implementation can pass. |

The implementation choices in this guide supply the details needed to begin: .NET 10 as the initial target, an immutable startup snapshot, independent console/file outputs, bounded asynchronous queues, cumulative compatibility baselines, and measurable test gates. These are project design choices, rather than thresholds imposed by an external standard.

Use `Core` and `Core.MicrosoftLogging` as folder and project shorthand in this guide. In the first repository commit, choose the final package ID, assembly name, and root namespace from the repository's naming convention. Freeze those identities before the first published release, including a published prerelease. Keep the requested static type name unchanged.

## 2 First release scope

The first published release contains:

- Environment/configuration resolution, validation, and immutable metadata.
- A small structured logging API and automatic metadata enrichment.
- Operation scopes, correlation, and exception capture.
- Human console rendering and JSONL rendering.
- Console and rotating file writers with independent filtering.
- Bounded admission, writer isolation, flush, stop, and delivery accounting.
- An optional Microsoft logging provider bridge.
- All six test families, reusable fixtures, examples, and release verification tooling.

Datadog-oriented deployment uses JSONL console or file collection. A collector agent is configured by the application deployment. The core does not need an HTTP exporter or a Datadog SDK for that capability. Configure tracing/APM separately if an application uses it; a trace-agent address is not a general log destination.

Health-check counters, service discovery, messaging transports, business calculations, persistence, and application lifecycle orchestration remain owned by their respective components. `EnviormentsOptions` can expose typed configuration relevant to the core, but it must not accumulate unrelated mutable application state.

The memory queue provides bounded asynchronous diagnostic logging. Successful queue admission is not proof of disk durability or collector ingestion. Business records requiring transactional or audit durability need a separately specified durable mechanism.

## 3 Dependency policy and package boundaries

### Shipping code

Use the .NET shared framework wherever it supplies the functionality. The initial `Core` project has **zero explicit `PackageReference` items** and no third-party runtime dependency. Use built-in facilities such as `System.Text.Json`, `System.Threading.Channels`, `System.Diagnostics`, `System.Runtime.InteropServices`, `System.IO`, and `TimeProvider`.

Do not add a larger framework reference merely to obtain one convenience API. In particular, the generic core must remain a normal .NET class library, without requiring the ASP.NET shared framework.

An allowlist must identify actual package IDs, versions, target-framework groups, and origin. A name beginning with `Microsoft.` or `System.` is not, by itself, permission to add it. Inspect the resolved graph and final package contents as well as the project file.

| Project area | Permitted dependency direction |
| --- | --- |
| `src/Core` | Selected .NET shared-framework assemblies only. |
| `src/Core.MicrosoftLogging` | `Core` and the specifically allowlisted Microsoft logging abstractions and their approved framework-specific dependency closure. |
| `tests` and verification tools | `Core`, adapters under test, pinned Microsoft test/build packages, and the explicitly identified development-only mutation CLI. |
| `examples` | Released candidate packages and application host packages required by that example. |
| Consumer applications | Core or the optional bridge; application infrastructure remains outside the core. |

### Standard Microsoft logging integration

Keep the core's event and configuration contracts independent of `Microsoft.Extensions.*` types. Provide an optional `CoreLoggerProvider` implementing the standard `ILoggerProvider` interface in `Core.MicrosoftLogging`. This lets applications continue using `ILogger<T>` without making every core consumer acquire those package dependencies.

`ILogger`, `ILoggerFactory`, and `ILoggerProvider` are supplied by `Microsoft.Extensions.Logging.Abstractions`. Its .NET 10 dependency group in version 10.0.12 includes `Microsoft.Extensions.DependencyInjection.Abstractions`; other target-framework groups have additional dependencies. Verify the exact restore graph for the pinned version rather than assuming the package is dependency-free. [R1]

The initial bridge allowlist for a .NET 10 build is:

- `Microsoft.Extensions.Logging.Abstractions`.
- `Microsoft.Extensions.DependencyInjection.Abstractions`, when brought in by the selected abstractions package.

Pin tested versions centrally. A later version or target with a different dependency closure must pass the same dependency gate before it is accepted. Do not copy third-party source or embed third-party assemblies to bypass the policy.

The host application can register the provider using its own Microsoft logging infrastructure. A convenience `AddCoreLogging` extension that requires additional host packages belongs in a separately justified integration layer. The basic provider must not require the core to depend on hosting or dependency injection.

### Verification tooling

The NuGet restriction applies to the shipping core and its dependency graph. Test/build tools are isolated from that graph and pinned independently.

Use MSTest with Microsoft test-platform and coverage tooling. Use small handwritten test doubles and standard assertions. Architecture inspection can use SDK project information and .NET metadata APIs. JSON fixtures and explicit comparers are sufficient for snapshot verification.

Use **Stryker.NET as an explicitly third-party, development-only CLI** for mutation testing. Install it from a pinned local tool manifest. It must never become a core package reference, a runtime asset, or a consumer installation requirement. Include its version, configuration, dependency inventory, and licensing information in the verification bundle.

This distinction preserves the user's core-library restriction while providing a mature mutation engine. A future policy banning third-party development tools as well would require a different mutation implementation; a handful of handcrafted fault injections must not be presented as equivalent mutation coverage.

## 4 Framework and platform baseline

Start with `net10.0`, normal managed execution, and Windows and Linux verification lanes. Confirm the first consuming application's target framework during repository setup. Add any necessary older target before the first published baseline rather than publishing an incompatible minimum and correcting it later.

As of this guide's date, .NET 10 is an LTS release supported through 14 November 2028. .NET 8 and .NET 9 reach the end of their listed support on 10 November 2026. Use a serviced, pinned SDK/runtime for implementation and record the actual versions used; do not copy a floating `latest` version into reproducibility files. [R2]

Maintain two explicit records:

| Record | Meaning |
| --- | --- |
| Contract target frameworks | Target assets and minimum framework requirements that a published core version promises to consumers. Retain them when adding new targets. |
| Tested execution matrix | Exact SDK, runtime, OS, and architecture combinations on which this release's suite ran. Update it as platforms evolve. |

Indefinite library compatibility does not extend Microsoft's runtime support lifecycle. Preserve the old contract and target assets, continue regression testing them, and test historical consumers on appropriate newer supported runtimes. If an old runtime remains part of the claimed execution contract, maintain an isolated historical execution lane and its reproducible toolchain as well.

Do not raise the minimum framework, drop a historical target asset, or require a consumer rewrite in any release of the compatible package. Adding a modern target is additive; it does not remove the previously shipped target.

OS metadata discovery should recognize macOS as well, but advertise macOS execution support only after the corresponding suite runs there. Similarly, Native AOT, trimming, browser .NET, and single-file application support require explicit validation before being claimed. Application-supplied build identity should remain available where assembly discovery is constrained.

## 5 Repository structure and responsibilities

Use one core assembly initially, separated internally by responsibilities. Each folder contains real types with a defined job; it is not another partial-file subdivision of one large class.

| Path | Responsibility |
| --- | --- |
| `src/Core/Environment` | Static facade, setup input, resolver, validation, and immutable snapshot. |
| `src/Core/Events` | Event input, immutable captured event, metadata projection, and bounded value normalization. |
| `src/Core/Context` | Operation scopes and correlation capture. |
| `src/Core/Logging` | Logger creation, filtering, fan-out, and public lifecycle operations. |
| `src/Core/Outputs` | Human/JSONL rendering, console/file writers, and file rotation. |
| `src/Core/Diagnostics` | Bounded internal diagnostics and delivery counters. |
| `src/Core.MicrosoftLogging` | Microsoft logging provider and scope/event mapping. |
| `tests/Core.UnitTests` | Deterministic behavior of individual responsibilities. |
| `tests/Core.IntegrationTests` | Real process, stream, filesystem, and lifecycle behavior. |
| `tests/Core.ArchitectureTests` | Project/package/assembly/type dependency rules and public surface rules. |
| `tests/Core.CompatibilityTests` | API, source, binary, configuration, and historical behavior replay. |
| `tests/Core.BusinessCases` | Named end-user and operator scenarios. |
| `tests/Core.TestSupport` | Test-only fake clocks, controllable writers, process harnesses, and comparers. |
| `verification/mutation` | Full-scope Stryker configuration and result validation. |
| `contracts` | Event schema, configuration contract, and language-neutral fixtures. |
| `compatibility` | Append-only historical package/API/consumer/behavior baselines. |
| `examples` | Runnable applications and explanations written after core behavior exists. |
| `eng` | Restore/build/test/package/verification scripts and artifact validation. |

The event contracts must not depend on concrete outputs. Environment resolution must not depend on log writers. Logging consumes the immutable environment snapshot. Output implementations consume already-captured events. The optional Microsoft bridge depends on the core, and the core never references the bridge.

Internal testability seams are allowed for time, variable reads, runtime discovery, and writer coordination. Keep them internal unless an actual consumer requires an extension point. Every public interface becomes an indefinite compatibility obligation.

## 6 C# maintainability standard

Commit a short maintainability standard and enforce the mechanical parts in the build.

- Use one statement per line and braces for control flow.
- Prefer explicit names and guard clauses. Avoid compressed multi-condition expressions and nested conditional operators.
- Use named setup properties for collections of settings. Avoid constructors with long positional parameter lists.
- Keep side effects visible. Property getters read already-resolved state; initialization and I/O use explicit methods.
- Use a class per coherent responsibility. Do not distribute a handwritten implementation across partial files as a substitute for decomposition.
- Reserve partial types for a documented source-generator requirement, such as Microsoft-generated logging methods in an example.
- Treat nullable warnings and compiler warnings as errors. Enable SDK code analyzers and code-style enforcement.
- Run `dotnet format --verify-no-changes` in verification using the pinned SDK.
- Document public behavior, ownership, cancellation, exceptions, and thread-safety. Explain invariants and unusual decisions in comments.
- Review a method exceeding roughly 60 lines, a type exceeding roughly 300 lines, or cyclomatic complexity exceeding 15. These are decomposition review triggers; splitting code mechanically does not satisfy the review.

No broad warning suppression or complexity exclusion may be introduced to make a build green. Record narrow, justified exceptions next to their reason. Generated code is identified separately from handwritten code.

A typical project property baseline is:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <Deterministic>true</Deterministic>
  <EnablePackageValidation>true</EnablePackageValidation>
</PropertyGroup>
```

SDK selection, analyzer settings, package versions, and restore sources must be committed. Do not introduce a custom code-analysis framework when SDK analyzers and a small architecture harness provide the required checks.

## 7 EnviormentsOptions contract

### Public shape

Implement the following conceptual surface with ordinary readable C#:

- `EnviormentsOptions.Initialize(EnvironmentSetup setup)` returns the resolved `EnvironmentSnapshot`.
- `EnviormentsOptions.Current` returns that same snapshot.
- `EnviormentsOptions.IsInitialized` reports whether publication succeeded.
- Convenience getters expose `ApplicationName`, `BuildVersion`, `ServiceVersion`, `EnvironmentName`, `HostName`, `OperatingSystem`, `Logging`, and `EventMetadata`.

`EnvironmentSetup` is the caller's startup input. The resolver copies it. `EnvironmentSnapshot`, resolved logging options, and `EventMetadata` are immutable outputs with no public setters and no retained references to caller-owned mutable collections.

Do not use a positional record as an expanding public options contract. Preserve constructors and public parameter names once released. Prefer a sealed setup class with named properties and a sealed read-only resolved snapshot.

### Initialization semantics

1. Read the recognized configuration inputs once.
2. Resolve application/build/runtime metadata through a dedicated resolver.
3. Validate the complete result, including relationships between options.
4. Construct the immutable snapshot locally.
5. Publish it atomically.

Before successful initialization, `Current` and convenience getters throw a documented `InvalidOperationException`. A failed validation publishes nothing. A second initialization after successful publication throws a documented `InvalidOperationException`; it cannot replace the running snapshot. Concurrent initialization has exactly one successful publisher.

Expose no production `Reset` method. Test the resolver directly using supplied inputs. Test static initialization scenarios in isolated child processes.

Keep the CLR static constructor trivial. Do not parse collector addresses, create directories, subscribe global exception events, start workers, or contact infrastructure from it. .NET does not retry a failed static constructor, which is another reason to put validation in an explicit startup method. [R3]

### Captured metadata

| Property | Type and rule |
| --- | --- |
| `ApplicationName` | Nonempty string. Configured logical service name, falling back to the supplied application's name. |
| `BuildVersion` | String from supplied build metadata or application informational version; assembly version string is a fallback, then `unknown`. |
| `ServiceVersion` | Configurable telemetry release label; defaults to `BuildVersion`. |
| `EnvironmentName` | Nonempty deployment label; preserve custom values after trimming. Use `unknown` when no source supplies one. |
| `EnvironmentKind` | Explicit enum with frozen numeric values: Unknown=0, Console=1, Service=2, Web=3, Function=4. |
| `InstanceId` | A new UUID string per successful application initialization, stable for that run. |
| `ProcessStartedAtUtc` | Nullable UTC `DateTimeOffset` for actual process start. Do not substitute initialization time under this name. |
| `InitializedAtUtc` | UTC `DateTimeOffset` for successful core initialization. |
| `HostName` | Observed process-environment host name, or null when unavailable. |
| `OperatingSystem` | Stable lowercase family string such as `windows`, `linux`, `macos`, or `unknown`. |
| `OperatingSystemDescription` | Optional runtime-reported descriptive text. |
| `OSArchitecture` | Optional normalized architecture string. |
| `ProcessArchitecture` | Optional normalized process architecture string. |
| `RuntimeDescription` | Optional runtime-reported description, separate from application version. |
| `ProcessId` | Nullable integer. |
| `CommitId` and `BuildId` | Optional build-supplied strings; do not guess them from filenames or parse arbitrary version text. |

Use the explicitly supplied application assembly for fallback identity discovery. `Assembly.GetEntryAssembly()` may be used as a fallback when it is available. Never use the core assembly's own version as the application's version.

Preserve informational versions as strings, including prerelease/build information. .NET distinguishes assembly identity, file version, and informational version; informational version can carry source revision information. [R4]

Use standard runtime APIs for OS and process metadata. `RuntimeInformation` supplies OS description, OS architecture, process architecture, and framework description. Host name means the name visible to the application; it is not a guarantee about the physical host behind a container. [R5]

### Configuration precedence

For configurable values, apply the following order from highest to lowest:

1. The corresponding nonblank `CORE_*` process environment variable.
2. A supported nonblank compatibility alias, such as the corresponding `DD_*` identity variable.
3. Explicit application setup/configuration, including the host's already-resolved environment name where supplied.
4. Discovered fallback metadata.
5. The documented default.

Treat empty or whitespace-only values as absent. A present but invalid higher-priority value fails validation; do not silently fall back to a lower-priority value. Environment variable names are documented uppercase names; normalized configuration tokens are case-insensitive. Identity values retain their case after trimming.

Detected facts such as OS architecture and actual build metadata do not follow arbitrary deployment overrides. `CORE_SERVICE_VERSION` changes `ServiceVersion`; it does not overwrite `BuildVersion`.

Use the host's resolved framework environment as input. `CORE_ENVIRONMENT=live` may be a telemetry deployment label while a framework uses `Production`. The core must not silently mutate the process environment or alter the host's behavior after host creation.

When an application also enables a Datadog tracer, align service/env/version from the resolved configuration or validate conflicting equivalent settings during integration setup. Preserve `DD_AGENT_HOST` as a collector destination setting if an external integration uses it; never use it as application host metadata.

### Validation and diagnostics

Return all configuration issues through a `CoreConfigurationException` containing an immutable collection of issues. Each issue has a stable code, configuration key, and safe explanation. Error message prose can improve, but codes and their meanings remain stable.

Validate recognized settings only. Do not enumerate and dump all process variables. Unknown `CORE_*` names remain ignored by the runtime for forward-compatible deployment configuration; a separate validation command may report suspicious names without changing startup semantics.

Parse booleans explicitly as `true` or `false`, case-insensitively. Parse integers using invariant culture and explicit bounds. Give durations and byte counts explicit units. Do not let debugger attachment override configured behavior.

Metadata discovery failures can produce an unavailable optional field. Required configuration errors fail startup. Resolution must not require the logging pipeline to be running to report an error.

## 8 Environment variable contract

These keys, defaults, interpretation rules, and precedence become part of the permanent compatibility contract when v1 is released.

| Variable | Default | Meaning |
| --- | --- | --- |
| `CORE_SERVICE_NAME` | Application name fallback | Logical service/application identity; supports `DD_SERVICE` as a fallback. |
| `CORE_ENVIRONMENT` | Supplied host environment or `unknown` | Deployment label; supports `DD_ENV` as a fallback. |
| `CORE_SERVICE_VERSION` | Build version | Telemetry release label; supports `DD_VERSION` as a fallback. |
| `CORE_LOG_LEVEL` | `Information` | Fallback threshold for outputs without their own threshold. |
| `CORE_LOG_CONSOLE_FORMAT` | `human` | `human` or `jsonl`. |
| `CORE_LOG_CONSOLE_LEVEL` | Inherits `CORE_LOG_LEVEL` | Console threshold; `None` disables console output. |
| `CORE_LOG_CONSOLE_STREAM` | `stderr` | `stderr` or `stdout`; the application chooses explicitly. |
| `CORE_LOG_HUMAN_DETAIL` | `normal` | `normal` or `verbose`; affects human presentation only. |
| `CORE_LOG_FILE_PATH` | Absent | Absolute or relative input path, resolved once to an absolute path during initialization; absent disables file output. |
| `CORE_LOG_FILE_FORMAT` | `jsonl` | `jsonl` or `human`. |
| `CORE_LOG_FILE_LEVEL` | Inherits `CORE_LOG_LEVEL` | File threshold; `None` disables file output. |
| `CORE_LOG_QUEUE_CAPACITY` | `1024` | Capacity per enabled output, integer 1 through 65536. |
| `CORE_LOG_FLUSH_INTERVAL_MS` | `1000` | Regular file-buffer flush scheduling interval, integer 10 through 60000. The timer runs when idle too; blocked I/O can delay completion. |
| `CORE_LOG_SHUTDOWN_TIMEOUT_MS` | `5000` | Default graceful stop deadline, integer 1 through 300000. |
| `CORE_LOG_FILE_MAX_BYTES` | `10485760` | Maximum bytes per file segment, integer 65536 through 1073741824. |
| `CORE_LOG_FILE_RETAINED_FILES` | `10` | Maximum retained closed segments, integer 1 through 1000, plus the active segment. |
| `CORE_LOG_SCHEMA_VERSION` | `1` | JSON contract version. v1 accepts only `1`; future versions preserve this selection. |

Valid levels are `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, and `None`, with explicit enum values 0 through 6 respectively. `None` never emits an event. Reject unknown numeric enum values as invalid arguments. Do not change accepted tokens or defaults in a later update; add new behavior through an explicit new option when required.

`CORE_LOG_LEVEL` is an inheritance default, not a global upper bound. For example, console Warning and file Debug must admit Debug events to the file even when the fallback is Information.

Validate provided recognized options even if their output is disabled, so a typo has predictable behavior. A missing file path disables the file writer; invalid provided file settings still produce a configuration issue.

JSONL and human format selection are explicit. Terminal detection may affect an explicitly enabled human color feature in a future compatible extension, but must never switch the serialization format or the output stream.

The table specifies initial project defaults, including queue and rotation limits. Measure them in the first implementation's burst and file tests before freezing v1. Changing a value before the first published baseline is a design adjustment; changing it after release requires preserving the old default behavior.

## 9 Structured event contract

### Capture model

Create each immutable event from three inputs:

1. The process metadata snapshot.
2. Current operation context.
3. Event-specific facts.

The logger automatically attaches the process metadata. Callers provide an event name, severity, message, and relevant business/operation attributes. A shared immutable metadata object can be referenced internally by many events.

Use an explicit allowlisted projection. Do not serialize `EnviormentsOptions`, `EnvironmentSetup`, or `LoggingOptions` wholesale. Configuration paths, destinations, credentials, arbitrary environment variables, and live runtime state are not event metadata.

This separation is compatible with the OpenTelemetry distinction between resource information, trace context, and event attributes. The JSON layout below is the core's own versioned representation, rather than a claim that it is OTLP JSON. [R6]

### Required schema fields

| Field | v1 meaning |
| --- | --- |
| `schema_version` | Integer `1`. |
| `timestamp` | UTC event-creation instant in a documented ISO 8601 representation with `Z`. |
| `status` | Stable lowercase severity token. Map Information to `info`, Warning to `warn`, and preserve the documented mappings for other levels. |
| `event_name` | Stable name of the event type, such as `experiment.replay.completed`. |
| `message` | Human-readable description of the occurrence. |
| `service`, `env`, `version` | Resolved application name, deployment label, and service version. |
| `component` | Logger category/component name. |
| `resource` | Allowlisted build, instance, host, OS, runtime, process, and startup metadata. |
| `attributes` | Event-specific structured values; an empty object is valid. |

Optional top-level fields include `operation_id`, `trace_id`, `span_id`, `message_template`, and `exception`. Omit absent optional fields. Explicit null values inside attributes remain JSON null. Trim optional identifier values and treat blank identifiers as absent; preserve message/template text as supplied. Reject an empty event name or component. Format UTC timestamps with invariant `yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'` precision after converting the value to UTC.

Severity mappings are fixed: Trace=`trace`, Debug=`debug`, Information=`info`, Warning=`warn`, Error=`error`, Critical=`critical`. `None` has no serialized status because it produces no event.

The `resource` projection is fixed by the following mapping. Required captured values are always emitted; optional unavailable values are omitted.

| Snapshot property | Resource field | JSON type and availability |
| --- | --- | --- |
| `BuildVersion` | `build_version` | Required string; `unknown` is an explicit fallback. |
| `InstanceId` | `instance_id` | Required UUID string. |
| `InitializedAtUtc` | `initialized_at` | Required UTC timestamp string. |
| `ProcessStartedAtUtc` | `process_started_at` | Optional UTC timestamp string. |
| `EnvironmentKind` | `environment_kind` | Required lowercase string: unknown, console, service, web, or function. |
| `HostName` | `host_name` | Optional string. |
| `OperatingSystem` | `os_family` | Required lowercase string, including unknown. |
| `OperatingSystemDescription` | `os_description` | Optional string. |
| `OSArchitecture` | `os_architecture` | Optional normalized lowercase string. |
| `ProcessArchitecture` | `process_architecture` | Optional normalized lowercase string. |
| `RuntimeDescription` | `runtime_description` | Optional string. |
| `ProcessId` | `process_id` | Optional integer JSON number. |
| `CommitId` | `commit_id` | Optional string. |
| `BuildId` | `build_id` | Optional string. |

Application name, deployment environment, and service version appear at the existing top-level `service`, `env`, and `version` fields. A separate configuration object is never embedded.

An exception is a JSON object with required `type` and `message` strings, optional `stack_trace` string, optional `inner_exceptions` array of the same shape, and `truncated: true` only when its bounded projection omitted content. A single inner exception is an array of one. Preserve `AggregateException` children in their original order. Do not emit arbitrary exception properties or `Exception.Data`.

Use `resource.host_name` for the host observed by the application. Do not blindly overwrite a collector's own physical-host attribution with a container-visible name. Keep severity in `status`; a business outcome such as `success`, `cancelled`, or `rejected` belongs in attributes.

All standard process metadata fields must be available in machine output when captured. Human normal output may show a concise selection; verbose output expands available context. A compact renderer must not remove the application/build/instance attribution from standalone structured records.

### Illustrative event

The actual JSONL writer emits the following as one physical line:

```json
{
  "schema_version": 1,
  "timestamp": "2026-10-08T18:45:12.0830000Z",
  "status": "info",
  "event_name": "experiment.replay.completed",
  "message": "Replay completed",
  "service": "ctep.research-worker",
  "env": "qa",
  "version": "2.4.0",
  "component": "ReplayCoordinator",
  "resource": {
    "build_version": "2.4.0+8c42d91",
    "instance_id": "5fbab327-67ab-47d1-b814-b3a98c16e41f",
    "initialized_at": "2026-10-08T18:40:00.0000000Z",
    "environment_kind": "service",
    "host_name": "research-worker-03",
    "os_family": "linux",
    "process_architecture": "x64",
    "process_id": 2841
  },
  "operation_id": "op-0189",
  "attributes": {
    "experiment_id": "exp-42",
    "duration_ms": 821,
    "records_processed": 12000,
    "outcome": "success"
  }
}
```

### Values and bounds

Define a small supported attribute-value model: null, boolean, string, finite floating-point number, safe-range integer, bounded arrays, and bounded string-keyed maps. Normalize UUIDs and timestamps to documented strings. An integer-number constructor accepts only the inclusive range -9007199254740991 through 9007199254740991 and rejects values outside it. This range follows ECMAScript's safe-integer boundary. [R18] A separate explicit integer-string representation accepts larger integers. Decimal values always use an explicit invariant decimal-string representation. Never switch a field automatically from number to string as its value grows; the caller selects and keeps a consistent representation in that event schema.

Reject unsupported arbitrary objects rather than using unrestricted reflection serialization or calling arbitrary `ToString()` implementations. A helper may convert supported .NET scalar types into the core's value model. Input maps/arrays are copied recursively into owned data before admission; a read-only wrapper around a caller's dictionary is insufficient.

Use fixed, documented v1 bounds: maximum 64 KiB serialized JSONL record including its line terminator, attribute nesting depth 8, and a total of 1024 attribute nodes. Count each scalar, array, and map as one node, with the attributes root at depth 1. Event names, component names, attribute keys, and operation IDs are at most 255 Unicode scalar values. Messages and templates are each at most 8192 scalar values. Exception projection permits at most 16 exception nodes, inner depth 4, 4096 scalar values per exception message, and 16384 per stack trace. Operation scopes nest at most 32 levels.

Enforce bounds while traversing and copying, before creating an arbitrarily large secondary representation. Exception text/children may be clipped at their stated limits with the explicit truncation marker; excessive ordinary input or an event that still cannot fit the aggregate envelope is rejected and accounted for. Do not split an oversized JSON object into several log records or emit invalid truncated JSON.

Identity validation keeps application/environment names within 255 Unicode scalar values and version/build identifiers within 512. Reject control characters in identities, event/component names, attribute keys, and operation IDs. Preserve valid Unicode; count encoded bytes when enforcing record size. The aggregate record cap still applies after escaping. Human rendering escapes control sequences in supplied message/attribute text rather than allowing that text to inject terminal commands or false record boundaries.

### Correlation and exceptions

Use `OperationContextData` with a nullable typed `OperationId` and a supported attribute map. Request, user, tenant, and experiment identifiers can be deliberate scoped attributes where appropriate; they never become mutable static environment properties. The runtime copies scope input when `BeginScope` is called. Nested scope values override outer scope values; explicit event attributes override scoped attributes with the same key. `LogEventData.OperationId`, when nonblank, overrides the innermost scoped operation ID. Reserved identity and correlation fields cannot be overwritten by a free-form attributes map.

Scopes are isolated by core runtime instance and asynchronous execution context. Scope disposal is LIFO within the owning logical flow. Repeated disposal in that flow is harmless; out-of-order disposal raises a documented `InvalidOperationException` without altering the current scope. Scope handles must not be passed to another concurrent flow for disposal. Enforce the scope-depth limit and document normal execution-context inheritance for child tasks.

Capture scope values and `Activity.Current` correlation when the event is created, before buffering. In v1, trace and span IDs come only from a valid W3C `Activity.Current`: 32 lowercase hexadecimal characters for trace ID and 16 for span ID, excluding all-zero IDs. [R17] Omit both when no valid W3C activity is available. Scopes and free-form state cannot override them. Record source-provided operation IDs separately. Test asynchronous flow, child activities, nested scopes, cancellation, and concurrent operations.

Capture exception type, safe message, stack trace, and bounded inner-exception information at event creation. Do not retain a mutable `Exception.Data` dictionary or arbitrary exception objects in the queued event. Do not automatically capture HTTP bodies, headers, environment variables, or credentials. The logging examples must explain that messages themselves can contain sensitive data; a field filter cannot guarantee removal of every secret embedded in arbitrary prose.

A later forwarded domain/message event preserves its original producer metadata. A consuming application's own log event uses the consumer's resource context and the appropriate correlation identifiers.

## 10 Logging API and Microsoft provider behavior

### Small core surface

The initial public API needs a runtime/pipeline owner, a logger, structured input, and result objects. Implement these method shapes; names represent the guide's working API before its first publication:

- `CoreLogRuntime.Create(EnvironmentSnapshot environment)` initializes the configured outputs.
- `CoreLogger runtime.CreateLogger(string component)` returns a logger tied to that runtime.
- `bool logger.IsEnabled(CoreLogLevel level)` checks whether an enabled, running output is interested.
- `LogWriteResult logger.Write(CoreLogLevel level, LogEventData data)` captures and attempts admission.
- `LogWriteResult logger.WriteLazy(CoreLogLevel level, Func<LogEventData> factory)` checks interest before invoking the factory, at most once.
- `IDisposable runtime.BeginScope(OperationContextData context)` creates a copied, bounded operation scope.
- `Task<LogFlushResult> runtime.FlushAsync(TimeSpan timeout, CancellationToken cancellationToken)` requests a bounded flush.
- `Task<LogStopResult> runtime.StopAsync(CancellationToken cancellationToken)` starts or joins bounded shutdown.
- `CoreLogDiagnostics runtime.GetDiagnostics()` returns an immutable counter/status snapshot.
- `ValueTask runtime.DisposeAsync()` joins the same shutdown operation through `IAsyncDisposable`.

`LogEventData` contains EventName, Message, optional MessageTemplate, optional OperationId, supported Attributes, and optional Exception. It has no severity property. The method's `level` is the single authority for filtering and event severity in both direct and lazy calls. Apply this decision order: validate method-level required arguments and enum values; if admission has closed, return Stopped; otherwise return Filtered for None or no configured threshold interest; return Rejected when every interested output is faulted; otherwise capture once and attempt admission. Stopped, Filtered, and the all-faulted early rejection do not traverse event content or invoke the factory. A valid None call after admission closes therefore returns Stopped.

Keep complex inputs in named options/data properties. Use distinct method names for genuinely different operations rather than adding ambiguous nullable overloads. Fix the complete signature set, argument names, exceptions, return semantics, and ownership rules before v1 is frozen.

`IsEnabled` is false after admission closes and for outputs already faulted. It is not a reservation or a guarantee of free queue capacity. If every interested output is faulted, Write/WriteLazy returns Rejected with faulted-output results without capturing content or invoking the factory. If the runtime has stopped accepting, it returns Stopped.

`LogWriteResult` has immutable `Outcome`, `Outputs`, and nullable `ErrorCode` properties. Freeze outcome values: Accepted=0, PartiallyAccepted=1, Filtered=2, Rejected=3, Stopped=4, InvalidEvent=5. Each output result has `OutputName`, `Status`, and an optional accepted sequence number. Output statuses are Accepted=0, Filtered=1, QueueFull=2, Faulted=3, Stopped=4. Sequence numbers exist only for successful admission. Invalid event capture rejects the event globally before any output receives it; that result has an error code and no output admission entries.

Accepted means every interested output admitted the event. PartiallyAccepted means at least one accepted and at least one interested output rejected it, including an output already faulted. Rejected means no interested output accepted. Filtered means no output is interested, including the case where all normal outputs are disabled; the Outputs collection may then be empty. Acceptance is never a delivery acknowledgment.

`LogFlushResult` and `LogStopResult` expose immutable `Completion`, `Succeeded`, `LossObserved`, a runtime-wide invalid-event count, and per-output results. Completion values are Completed=0 and TimedOut=1. Per-output results identify the captured accepted-sequence boundary, written/failed/abandoned/pending counts through that boundary, whether the writer flush succeeded, and cumulative rejection diagnostics as observed when the boundary was captured. Cumulative counters are never reset by flushing; pending/in-flight values are gauges for the reported boundary. Capture the runtime-wide invalid-event count with the operation's initial observations. `Succeeded` means all covered admitted work was written and each writer flush succeeded; `LossObserved` additionally exposes any global capture rejection, output rejection, failure, or abandonment in the reported cumulative observations. Completed with Succeeded=false is a valid failure result, not a successful drain. Succeeded=true and LossObserved=true can coexist when all admitted work drained but earlier events were rejected before admission.

Null required arguments and programmer misuse have documented argument exceptions. Input size/type rejection is represented in the write result and diagnostics. Output I/O failures do not escape into business code through a normal logging call. A caller's own lazy factory can throw its own exception; do not conceal that as successful logging.

### Provider bridge

The Microsoft provider maps `LogLevel`, category, event ID/name, state values, formatted message, exception, and scopes into the same core event path. Preserve `EventId.Name` as the event name when present; use the documented fallback `log.message` otherwise and retain a supplied numeric event ID as an attribute. Preserve `{OriginalFormat}` as `message_template` when available.

Apply filtering before calling the supplied formatter or copying expensive state. Do not implement another message-template parser in the core. The provider uses the formatter supplied by Microsoft logging and copies structured state through the core's supported value normalization.

Host-side filtering can discard an event before the provider sees it. Configure the provider's host filter broadly enough for the most verbose enabled core output, then let the core perform its independent output thresholds. Include a regression case with a Debug file output and a Warning console output. Microsoft recommends using the host's logging objects and appropriate categories for integrated applications. [R7]

The provider consumes host scopes and applies the same copy, bounds, and precedence rules. A structured host scope's recognized string `operation_id` maps to the typed operation ID; other permitted fields become attributes. Core identity and Activity-derived trace/span fields remain reserved. Exercise external-scope-provider integration as well as provider-created scopes.

The provider must clearly state whether it owns the runtime. The initial default is **non-owning**: the application explicitly stops/disposes the core after its background work ends. Provider disposal must not unexpectedly stop a runtime shared by other application components.

### Illustrative startup

```csharp
var environment = EnviormentsOptions.Initialize(
    new EnvironmentSetup
    {
        ApplicationAssembly = typeof(Program).Assembly,
        ApplicationName = "ctep.research-worker",
        EnvironmentKind = EnvironmentKind.Service
    });

await using var runtime = CoreLogRuntime.Create(environment);
var logger = runtime.CreateLogger("ReplayCoordinator");

logger.Write(CoreLogLevel.Information, new LogEventData
{
    EventName = "application.started",
    Message = "Research worker started"
});

// The host finishes business work, then inspects the bounded stop result.
var stopResult = await runtime.StopAsync(CancellationToken.None);
```

The implementation must make disposal idempotent and compatible with an earlier explicit stop. The example application will handle unsuccessful drain results visibly. This sketch does not replace the runnable examples or their tests.

## 11 Output formatting and file behavior

### Human output

Normal human output includes timestamp, severity, component, event name, and message. Verbose human output expands available application/build/instance context, operation fields, structured attributes, and exception details. Level filtering remains independent of detail selection.

Treat the human renderer as a display format. Versioned JSONL is the machine contract. The human renderer must still preserve promised information availability and configured stream behavior, although spacing and visual layout may improve.

### JSONL output

Use `System.Text.Json` to write valid UTF-8 JSON with no BOM, one record per physical line, and LF termination. Escape embedded newlines, control characters, and exception text. Emit no ANSI escape sequences, banners, raw stack traces, or progress prose in that stream.

Document field names, field types, casing, null policy, and timestamp representation. Readers ignore unknown optional fields. Preserve existing field types and meanings forever within schema version 1. JSON member order is not a consumer contract; canonical fixture comparers compare structure and explicitly normalize nondeterministic fields.

CLI examples keep command results on stdout and logs on stderr by default. Container/service examples can explicitly select stdout for collection. Human and file outputs can run simultaneously with different levels and formats.

### File lifecycle and bounds

`CORE_LOG_FILE_PATH` accepts an absolute or relative active file path. Capture the process working directory during environment initialization and resolve a relative input against that captured directory once. The immutable logging snapshot exposes the resulting absolute path; later working-directory changes cannot redirect output. Resolve and validate syntax at startup; create directories and open files only when the file writer is initialized. Do not create directories from environment property getters.

Require exclusive writer ownership for that active-file/rotation group. A second process targeting the same group must fail output initialization clearly; it must not silently interleave records or corrupt rotation. Deployments with concurrent instances provide distinct active paths. Test ownership and rotation behavior on every advertised OS.

Append to an existing valid active segment under the exclusive ownership rule. Rotate before a new complete encoded record would exceed `FileMaxBytes`. Keep records intact. Retain at most `FileRetainedFiles` closed segments plus the active segment, using a deterministic naming and ordering rule that continues across restarts.

Before appending to a nonempty active segment, establish its format and encoding from the writer's managed segment inventory. In v1, missing or incompatible format metadata fails initialization clearly before changing existing content. Do not infer JSONL validity merely from a final newline, or append JSONL to a prior human-format segment. A deployment switching formats at the same path must select a new rotation group or explicitly archive the old group first. Test both format mismatch and missing inventory; creation of a new empty group records its format under the same ownership rule.

For JSONL startup recovery, inspect the owned active segment's final record boundary under exclusive ownership. If the file ends in a partial record, preserve it as a uniquely named damaged segment in the same managed rotation group and start a fresh active segment; never append new JSON directly after a partial record. Mark damaged segments distinctly in the managed segment inventory so normal collector examples exclude them. They count toward retention and remain subject to that documented retention policy. Surface the recovery through safe diagnostics.

If deleting a closed segment fails and continuing would exceed the hard retained-file bound, pause/fault the file output before creating further segments. A human-format record that exceeds the configured segment byte limit is recorded as a terminal render-too-large failure for that output; do not rotate repeatedly in search of a segment that can hold it. Other outputs remain independent.

Retention only deletes files belonging to the configured writer's exact rotation group. It must not use a broad directory wildcard or remove unrelated files, another active instance's files, or an archive outside the configured path. Test collisions, permissions, full-disk/write failure, missing directories, Unicode paths, and restart recovery after an interrupted rotation.

The configured size/count bounds apply to one rotation group. A deployment creating new groups indefinitely must manage those groups' lifecycle; per-writer limits are not a global filesystem quota.

If startup cannot initialize an enabled file writer, runtime creation fails with a clear output initialization error, stops newly created workers, and releases acquired handles and ownership. Startup rollback never deletes pre-existing active or retained segments; only newly created empty temporary artifacts may be removed. Borrowed console streams are never disposed. If a writer fails after startup, preserve other outputs, account for undelivered events, and expose the fault through diagnostics. Do not silently redirect a file configured for collection to an unrelated default directory.

## 12 Buffering isolation flush and shutdown

### Admission and independent workers

Use one bounded queue and one sequential worker per enabled output. Fan out the same immutable event to interested outputs. A blocked file writer must not block console admission or make the other output lose its own ordering.

Use a bounded `Channel` with full mode `Wait`, but perform admission with nonblocking `TryWrite`. Set `SingleReader=true`, `SingleWriter=false`, and `AllowSynchronousContinuations=false` for the single-worker/multiple-producer design. When full, **reject the incoming event for that output** and increment its queue-full counter. Preserve events already admitted. This is the initial overflow policy and deliberately differs from retaining only the newest events by evicting old accepted records. .NET's Channels APIs provide the bounded queue and admission primitives for this implementation. [R16]

Each output preserves its own admission order. Concurrent producers are ordered by successful admission, not by caller wall-clock timestamp. No total delivery order across independent outputs is promised. Keep queues and worker bookkeeping bounded.

Do not invoke a message factory when all enabled outputs filter the event. If at least one output is interested, evaluate a lazy factory once, capture the event once, and then attempt output admission. Full queues do not justify invoking the factory repeatedly.

### Delivery accounting

Maintain monotonic cumulative counters and ordered completion information per output for:

- Filtered events.
- Admission attempts and accepted events.
- Queue-full and unavailable-output rejections.
- Successfully written events.
- Failed writes.
- Abandoned accepted events that were never attempted after an output fault.

Report pending/in-flight work and work unresolved at a deadline as nonnegative gauges, captured consistently with the cumulative counters and the observation boundary. These gauges increase and decrease as work progresses; they are not monotonic counters.

Maintain a separate runtime-wide cumulative InvalidEvent count for capture rejections before fan-out. Include it in diagnostics and the captured flush/stop loss observations. These rejected events have no accepted sequence and must not create invented output admissions or disappear from loss reporting.

All accepted events must be accounted for as written, failed, abandoned, or still pending at an observation boundary. Thus Accepted = Written + Failed + Abandoned + Pending for a consistently captured output boundary. Failed includes a terminal rendering failure or an attempted write that did not confirm complete success; partial bytes may exist and must never be reported as a confirmed complete record. Do not call an event delivered merely because it was dequeued. Do not increment a sequence for a rejected admission in a way that prevents flush completion forever.

Each output follows Running, Faulted, Stopping, and Stopped lifecycle states. A terminal I/O failure atomically faults that output, rejects new admission, records the in-flight outcome, and marks accepted queued events that will not be attempted as abandoned. A stopped output retains its last fault information in diagnostics. v1 does not automatically retry a possibly partial write or recover a faulted output in the background. Recovery uses an explicitly recreated runtime after the previous output can be safely closed. Other outputs continue normally.

Keep internal diagnostics separate from normal routing to prevent a failing writer from recursively logging through itself. Counter/status inspection is always available. A bounded, rate-limited diagnostic output may write a safe error code and counts; it must use valid framing, preserve CLI stdout, and never create an unbounded task or retry queue when that diagnostic output also blocks.

### Flush semantics

`FlushAsync` takes an explicit positive finite timeout and captures a boundary for each enabled output: all events admitted before that boundary are covered. Events admitted later do not keep the flush waiting indefinitely. Caller cancellation cancels that caller's wait with OperationCanceledException; it does not erase already-requested bounded worker coordination. The flush timeout produces a TimedOut result when its boundary remains unresolved.

Measure flush and shutdown deadlines with a monotonic elapsed-time source through the testable `TimeProvider` seam. Use UTC time only for event and observation timestamps. A wall-clock correction cannot extend, shorten, or restart an established timeout.

For each covered output, wait for covered work to reach a terminal delivery outcome and ask the writer to flush its userspace buffer. Return a structured result indicating whether the boundary was fully reached, how many events were written or failed, and what remained unresolved. Include the relevant rejection/drop diagnostics so an empty queue cannot masquerade as lossless delivery.

An explicit flush is not a disk `fsync` durability guarantee and is not a collector acknowledgment. State that limitation in API documentation and examples. If durable flush is added later, it needs a separately named behavior and tests.

Implement the boundary with carefully synchronized accepted/completed sequence bookkeeping or non-droppable control coordination. Register the boundary/control request under the same synchronization that captures admitted tickets, before later admission can overtake that request. Serialize writer flush operations with writes, and never extend an existing target because more events arrive. Test it under concurrent admission and a saturated queue. A flush marker that can be dropped by the same overflow policy is invalid.

Coalesce output coordination for the same accepted boundary. Retain at most one coordination record per outstanding accepted-sequence boundary plus the completed boundary currently being flushed; do not keep a new worker control object for every caller. Caller-specific waits preserve their original boundaries and rejection observations, and detach on completion, timeout, or cancellation. Repeated cancelled or timed-out flush calls against blocked I/O must not accumulate an unbounded internal waiter/control list. Add a blocked-writer stress case that checks the retained coordination bound, rather than checking only event-queue capacity.

The regular file-buffer timer must schedule a flush after the configured interval even when only one event was written and traffic becomes idle. The interval is a scheduling target; in-flight blocked I/O can delay completion and must remain observable.

### Stop and disposal

The first `StopAsync` request atomically closes new admission and starts one shared shutdown operation with the configured overall deadline. Subsequent calls join that same operation and do not restart its deadline. The cancellation token cancels only that caller's wait, with OperationCanceledException; it does not cancel or reopen the shared shutdown operation. A stop request initiates shutdown even when its caller's wait token is already cancelled. New writes after closure return Stopped.

`DisposeAsync` joins the same shared shutdown operation using its original deadline, without starting another drain window. It does not report an unsuccessful drain as an exception from a business logging call; the final stop result remains available through diagnostics. Applications needing to act on loss inspect `StopAsync` or diagnostics explicitly before disposal. Repeated disposal is safe and never reopens outputs.

On the shared shutdown deadline, abandon queued work that has not started, request cancellation of in-flight I/O, and report any unresolved in-flight work as pending. Stop/flush results are immutable observations with `ObservedAtUtc`; later diagnostics may show an in-flight operation eventually completing. Cancellation requests do not prove that an operating-system I/O operation or arbitrary writer has stopped. Keep at most the already-bounded outstanding work; never spawn replacement writes indefinitely. Do not dispose a resource underneath an unresolved write without a documented coordination strategy.

Graceful shutdown tests must distinguish a successfully drained queue from a deadline result. Abrupt process termination can lose memory-buffered events, so tests assert the stated loss boundary rather than inventing an exactly-once or crash-durable guarantee.

## 13 Indefinite backwards compatibility

### Compatibility promise

Every supported public contract remains usable in future versions of the same core package. Compatibility covers:

| Surface | What must remain compatible |
| --- | --- |
| Source | Existing calls, named arguments, supported language patterns, overload resolution, and warnings-as-errors builds. |
| Binary | Previously compiled consumers can load and use the candidate core without recompilation on a compatible execution environment. |
| Behavior | Documented results, exception categories/codes, side effects, filtering, initialization, ordering, cancellation, and lifecycle semantics. |
| Configuration | Existing keys, aliases, accepted values, precedence, default values, and output stream selection. |
| Event schema | Field names, types, meanings, correlation, null rules, severity mapping, and supported schema-version selection. |
| Package identity | Package/assembly names, namespace/type identity, signing identity where used, and retained target assets. |
| Operational contract | Bounded resources, file ownership/retention rules, and the distinction between accepted, written, flushed, and durable. |

An incremented major version is not permission to remove an old contract. A new behavior that cannot preserve the old semantics requires a separately selected contract while the old API/path remains implemented and tested. A separately named successor product may require a migration, but it cannot be advertised as the compatible upgrade to the existing core.

The commitment applies to published guarantees and supported usage. It cannot guarantee every future OS/runtime, private-reflection dependency, or undocumented implementation accident. This boundary must not be used to reclassify an inconvenient published promise as unsupported. A defect fix that affects an observable supported case needs a regression and explicit compatibility assessment.

### Rules for evolving the API

- Keep existing public types, members, parameters, enum values, and constants.
- Keep the exact `EnviormentsOptions` spelling and namespace after publication.
- Never add a required property or parameter to an existing construction path.
- Preserve original overloads, and test that additions do not change binding for old calls, including null, numeric conversions, and named arguments.
- Avoid adding members to public implementable interfaces. Add a separate optional capability/interface where necessary.
- Do not change a public struct's layout or replace a class with a struct. Prefer sealed classes for contracts that may need additive information.
- Do not introduce warning-producing `[Obsolete]` attributes on supported calls: old consumers can treat warnings as errors. Documentation can identify a newer option while retaining the old one without compiler disruption.
- Preserve sync/async behavior, cancellation meaning, exception types/codes, and ownership decisions.
- Add optional JSON fields only under the published unknown-field rule. Do not change an existing field's type or reinterpret its value.
- Preserve the default schema and configuration behavior. New defaults apply only through an explicit new selection that existing applications do not accidentally activate.

.NET's compatibility guidance explicitly identifies parameter renames, changed defaults, changed constant/enum values, and some overload additions as compatibility hazards. A library-specific policy must therefore look beyond removed methods. [R8]

Keep package version, file version, and informational version available for release identity. Choose assembly identity/signing once before the initial baseline. For this managed .NET-only core, the draft's baseline choice is a stable `AssemblyVersion` for the permanent compatibility line, while package/file/informational versions advance. This is a deliberate project choice, not a requirement of binary compatibility: Microsoft's current general package guidance recommends incrementing assembly versions for ordinary packages and requires a compatible replacement to have an equal or higher version. [R19] Assess the intended loader scenarios before freezing the first publication. Validate the chosen policy with old binaries and actual loaded assembly hashes; a stable version by itself proves nothing about behavior or which patched bytes loaded. Never decrease below a previously published assembly version, change a signing key, or introduce signing later without satisfying existing assembly identity contracts.

### Cumulative historical baselines

Every release adds an immutable baseline entry containing:

- The actual released package and assembly hashes.
- Its public API metadata snapshot.
- Representative consumer source and its build settings.
- Already-compiled consumer binaries, dependencies, runtime configuration, and hashes.
- Configuration, event, and behavioral contract fixtures introduced by that release.
- Supported target assets and the execution environment used for baseline capture.

Keep every historical entry. Comparing only the first and latest releases can miss an API introduced in an intermediate release. Baseline manifests are append-only; do not regenerate old fixtures with candidate output or rebuild old binaries with the new SDK and continue calling them historical binaries.

Store baselines and verification bundles under durable retention with hashes. Expiring CI attachments and a public package-feed URL are not sufficient for indefinite replay.

### Four mandatory compatibility layers

**Layer A — package and API validation.** Run .NET package validation/APICompat against every historical released package belonging to the same supported package/contract identity, including published prereleases. Determine membership from the immutable historical manifest, never from the targets or APIs that happen to remain in the candidate. Check framework/runtime asset consistency and explicitly enable the parameter-name and relevant attribute checks supported by the pinned tool. `PackageValidationBaselineVersion` identifies one baseline; the release harness must iterate the historical manifest or invoke the tool for each package. Normal compatibility mode permits compatible additions. Strict mode checks API equality and must not be enabled blindly for additive releases. [R9] [R10]

Do not use baseline-TFM ignore switches, regenerated baselines, or suppressions of real incompatibilities to make a candidate pass. A tool false positive needs a narrow documented explanation and an independent regression proving the historical contract; it does not remove that contract from the manifest.

**Layer B — historical source compilation.** Restore each preserved consumer source fixture against the candidate package using its historical contract TFM and unchanged declared language, nullable, and warnings-as-errors settings. Do not retarget all old fixtures to the newest framework. Cover constructors, named arguments, null calls, extension methods, overload conversions, and supported custom interface implementations. Compare required behavior after compilation as well as compiler success. Add current-toolchain/current-runtime cases separately so they cannot replace historical-target coverage.

**Layer C — unchanged historical binaries.** Copy each frozen consumer into an isolated replay directory and retain its consumer DLL hashes. Run a package replacement case using the old consumer, old bridge where present, and candidate core. Separately run a complete upgrade case using the old consumer plus candidate bridge/core and their resolved permitted dependency closure. Never recompile the consumer. Assert which candidate assemblies actually loaded using their paths, metadata, and SHA-256. Record any isolated probing or dependency-manifest changes needed by the upgrade harness; never overwrite the archived original manifests or treat a rebuilt consumer as a binary replay.

**Layer D — behavior, configuration, and wire replay.** Run all accumulated historical fixtures against the candidate. Compare parsed event meaning, defaults, configuration precedence, stream separation, public errors, lifecycle outcomes, and other documented behavior. Normalize only explicitly nondeterministic fields such as generated instance IDs and timestamps. A broad ignore-list is not compatibility testing.

Old applications do not automatically roll forward to a new .NET major under the default host policy. A current-runtime replay lane must explicitly select and record any permitted roll-forward setting while preserving the original consumer binary and distinguishing that lane from a same-runtime package replacement test. [R11]

The zero-history exception applies only when the immutable publication manifest is empty. That first published release still exercises the baseline harness, source/binary consumers, schema/configuration contracts, and negative controls that deliberately demonstrate detection of an incompatible sample. Record the absence of historical releases honestly, then preserve the first package for every following release. A first stable release must compare against any earlier published prereleases.

## 14 Full test suite

All six families are required. Separate them by project, category, or harness so the release can prove that each executed. Mutation evaluates the tests by changing production code; it need not duplicate the test suite in a sixth C# project.

### Unit tests

Unit tests focus on observable behavior of a responsibility using deterministic inputs. Required cases include:

| Area | Required coverage |
| --- | --- |
| Configuration | Missing/blank/valid/invalid values, whitespace/case policy, bounds, every precedence layer, invalid higher-priority input, booleans including `false`, and one-time relative-path resolution despite later working-directory changes. |
| Identity | Correct application assembly, informational version preservation, optional unavailable metadata, distinct instance IDs, UTC timestamps, and stable snapshot values. |
| Immutability | Mutation of setup dictionaries, nested attributes, exception data, or scope inputs after a logging call cannot alter captured events. |
| Filtering | Every level/threshold combination, `None`, disabled outputs, per-output overrides, and union-based `IsEnabled`. |
| Deferred work | No factory invocation when filtered; at most one invocation when interested; documented factory exception behavior. |
| Serialization | Unicode, quotes, control characters, line breaks, nulls, bounds, safe numbers, decimal strings, and valid exception projection. |
| Context | Nested scopes, event overrides, reserved keys, asynchronous flow, and concurrent isolation. |
| Delivery | Queue-full rejection, accepted/completed sequencing, writer failure, global invalid-event loss, non-monotonic pending gauges, flush boundaries/control bounds, wall-clock corrections, stopped-plus-None precedence, stop races, cancellation, and idempotent disposal. |
| File policy | Encoded size boundaries, segment naming, retention ownership, format/encoding mismatch, missing inventory, ordering, interrupted-rotation recovery, and preservation of pre-existing files during startup rollback. |

Test the static facade in child processes. Most environment tests should call the internal resolver with an explicit variable source, clock, application metadata, and runtime metadata.

### Integration tests

Launch real small consumer processes that restore and use the candidate package. Control their environment and capture stdout/stderr separately. Test actual temporary file output and rotation. Drain redirected streams concurrently to avoid creating test-harness deadlocks.

Required scenarios include human normal/verbose output, JSONL, CLI stdout results with stderr logs, simultaneous console/file outputs, application restart identity, output initialization failure, writer failure isolation, exclusive file ownership, rotation/retention across restart, Unicode paths, burst load beyond capacity, graceful stop, and deadline reporting.

Use barriers, task-completion signals, and controllable streams/writers for concurrency. Use finite deadlines to detect hangs. Do not use arbitrary sleeps as proof that a race has been exercised. For OS-specific failures that cannot be made deterministic with a real filesystem, combine a controlled I/O seam with at least one real integration case.

JSON contract tests do not prove Datadog ingestion. The initial claim is collector-oriented structured output. If a release additionally claims tested ingestion, add a provisioned Agent fixture and retain actual ingestion evidence; do not require a paid hosted account for basic core verification.

### Mutation tests

Run full Stryker mutation over all handwritten shipping code with meaningful bodies, including the environment resolver, value capture, filtering, JSON writer decisions, queue/flush logic, and file policy. Include the optional bridge when it is part of the release. Exercise every materially distinct conditional compilation path. Define an explicit mutation matrix with one Stryker invocation and report for each shipping project and materially distinct target implementation; a run against Core alone does not cover the bridge.

Use the unit suite and fast deterministic business/integration cases that Stryker can execute. Mutation builds run in isolated copies of source/output directories. They must never overwrite the candidate package or its release staging directory.

A release run must disable changed-files-only and stale incremental-result shortcuts. Archive the complete JSON/HTML reports, configuration, tool version, raw mutant counts, exclusions, and source commit. An empty mutation population, pending work, unexplained runtime errors, or an undiscovered test project blocks release.

Investigate surviving and no-coverage mutants. Add a test when the mutant exposes a meaningful missing assertion. Equivalent-mutant exclusions require a precise source location, operator, source hash, and semantic explanation. They become invalid when the affected code changes. Do not exclude logging methods or entire difficult classes merely to raise the score.

### Architecture tests

Use multiple inspection levels:

1. Evaluated project references and framework references.
2. The locked restored package graph for each target.
3. Dependency groups and assets in the produced NuGet package.
4. Compiled assembly references and public/protected API types.
5. Type/namespace dependency rules inside the core, using .NET metadata/IL inspection where needed.

Required rules include zero third-party core dependencies; no references from core to tests, examples, host integration, persistence, or collector SDKs; no mutable static configuration fields; no exposed caller-owned collections in resolved metadata; no output implementation types in environment/event contracts; and no unexpected content, build-transitive scripts, or test assemblies in the package.

Source-file naming is not evidence of an architecture boundary. Include a negative fixture that violates a rule and prove the architecture harness detects it. Use a small .NET-only checker rather than bringing an architecture-test framework into the product.

### Backwards compatibility tests

Execute all four layers in section 13 against the cumulative historical manifest. Publish results by baseline version, target, and test layer. One aggregate success count must not conceal a missing historical consumer or a failed asset-selection case.

A change to the compatibility harness also needs its negative controls. For example, remove a method or change a JSON field type in a deliberately incompatible fixture and assert that the appropriate gate rejects it.

### Business-case tests

Express scenarios as named, executable Given/When/Then cases using MSTest and fixture data. Assert the outcome a developer, agent, or operator needs; do not mirror private method bodies. The catalog in the next section supplies the initial required cases.

Every accepted requirement must map to at least one observable acceptance case and its supporting technical tests. Cases that are genuinely inapplicable to an OS need an explicit applicability entry and another applicable lane proving the promised behavior. Unexpected skips remain failures.

## 15 Business case acceptance catalog

| ID | Given and when | Required observable result |
| --- | --- | --- |
| BC-001 | A developer selects verbose human output and an operation fails. | Output identifies application/build/environment, explains the event, and includes the permitted exception/context detail. |
| BC-002 | An agent invokes a CLI command configured for JSONL diagnostics. | Stdout is a valid command result; every stderr log line is independently parseable JSON with stable identity. |
| BC-003 | An operator changes output settings through environment variables. | The same application build selects the configured stream/file/format without source changes, preserving event meanings. |
| BC-004 | Console is Warning and file is Debug. | Debug events reach the file and are absent from console; expensive work is skipped only when no output is interested. |
| BC-005 | Two experiments execute concurrently across asynchronous boundaries. | Each event retains its own operation/experiment identifiers and the shared process identity. |
| BC-006 | A recognized setting contains an invalid value. | Startup fails with the setting name and accepted values, without dumping unrelated environment data. |
| BC-007 | A queue fills or a writer fails. | Resource use remains bounded, unaffected outputs continue, and rejection/failure counters are truthful. |
| BC-008 | An application stops gracefully with queued events. | Accepted events reach the documented boundary or the stop result explicitly reports unresolved work and deadline/cancellation. |
| BC-009 | An unchanged older source consumer upgrades its package reference. | It builds under its preserved settings and retains its supported behavior. |
| BC-010 | An already-compiled older consumer receives the candidate core asset. | It runs without recompilation and proves that the candidate assembly was loaded. |
| BC-011 | The application restarts and file rotation continues. | Instance identity changes, existing owned segments are handled consistently, retention is bounded, and unrelated files survive. |
| BC-012 | A caller mutates a dictionary immediately after logging. | Queued events retain the values captured at the logging call. |
| BC-013 | An application uses the Microsoft provider and its scopes. | Category, event name, structured state, exception, and correlation map into the core contract; provider ownership rules hold. |
| BC-014 | A collector reads generated structured records. | Records retain the specified service/env/version and metadata fields, with no raw multiline or ANSI contamination. This proves the output contract, not hosted ingestion. |
| BC-015 | An event contains boundary-size Unicode, a decimal value, and an unsupported object. | Supported values follow the exact type/encoding rules; unsupported/oversized input is explicitly rejected and accounted for. |
| BC-016 | All normal outputs are disabled. | Logging calls return filtered/stopped outcomes as specified, do not invoke deferred factories, and produce no ordinary log records. |

The later TypeScript implementation must run the shared fixture catalog, with explicit host-specific applicability. Do not mark a TypeScript implementation case as passed in the C# release before that port exists.

## 16 Measurable release gates

These are the proposed binding starting thresholds for this project. Establish them in the repository before implementation spreads across applications.

| Gate | Required release result |
| --- | --- |
| Test family completeness | All six families executed; every required case ID discovered; no unexpected skips. |
| Functional correctness | Zero failing unit, integration, architecture, compatibility, or business-case tests. |
| Business outcomes | 100% of required applicable business cases pass. |
| Historical compatibility | 100% of applicable historical API/source/binary/behavior fixtures pass. |
| Dependencies and architecture | Zero violations in source graph, restore graph, packed assets, and compiled references. |
| Code coverage | At least 95% line coverage and 90% branch coverage for handwritten shipping code, measured per shipping assembly and reported in aggregate. |
| Mutation | At least 90% conservative full-scope mutation score for each materially distinct shipping implementation, alongside the native Stryker score. |
| Critical invariants | No unresolved meaningful surviving/no-coverage mutants for identity immutability, precedence, output separation, escaping, filtering, or delivery accounting. |
| Build quality | Release build, SDK analyzers, formatting checks, and public documentation checks pass. |
| Distribution integrity | Tested package/assembly hashes match the released bytes; complete verification bundle and manifests exist. |

Coverage excludes only identified generated code and genuinely non-executable material. Publish exclusion rules and denominators. Do not raise a percentage by dropping tests, excluding a class, changing the source scope, or omitting an assembly from aggregation.

Stryker's native score counts both killed and timed-out mutants as detected and includes survived and uncovered mutants in its valid population. Publish those raw counts and the native score. [R12]

The additional conservative project gate is:

```text
Conservative mutation score =
    Killed / (Killed + Timeout + Survived + NoCoverage)
```

Report compile errors, runtime errors, ignored/equivalent mutants, and pending mutants separately. Use documented zero-denominator handling: no valid mutants is a failed or explicitly non-mutatable implementation classification, never an automatic 100% for a nonempty codebase.

Re-run timeout mutants in isolation with a relaxed timeout. A reproduced test assertion failure can be recorded as a kill with its evidence. Confirmed nontermination remains visible as detected in the native report but receives no numerator credit in the conservative score. An unexplained timeout or harness error blocks release.

Keep the original Stryker report immutable. Any reclassification is a separate attestation identifying the mutant, source hash, original status, rerun settings, result, and evidence hash. The conservative gate consumes the raw report plus those attestations; it never rewrites raw history to hide a timeout.

A surviving noncritical mutant still requires disposition even when the threshold passes. Equivalent exclusions are specific, source-bound, and reported with denominator changes. No retry-until-green policy may hide flaky tests. Fix a failure or diagnose it; do not weaken a required gate to release a patch.

## 17 Test toolchain and reproducibility

Use a pinned stable MSTest/VSTest toolchain initially, with Microsoft coverage and Microsoft's API compatibility tools. At the guide's date, Stryker.NET documents VSTest as its default and its MTP runner as preview. MSTest supports explicit VSTest selection. Re-evaluate tooling in a dedicated tested change rather than introducing a preview runner into the first mandatory release gate. [R13] [R14]

A conventional `Microsoft.NET.Sdk` test project with explicit Microsoft test references is a clear starting point. If `MSTest.Sdk` is used, select the intended runner explicitly and avoid enabling unrelated testing extensions by default.

Use Microsoft's `Code Coverage` collector or `dotnet-coverage` with verified settings and Cobertura as the initial machine-readable line/branch report format. The collector named `XPlat Code Coverage` belongs to Coverlet and must not be mistaken for the Microsoft collector. Verify nonzero branch denominators on a known branching fixture; fix collection/conversion rather than silently dropping the branch gate. [R15]

Commit the following:

- `global.json` with an exact SDK selection and documented roll-forward policy.
- Central package version pins and lockfiles for test/adapter projects.
- A local tool manifest with exact Stryker/API compatibility/coverage tool versions as used.
- NuGet source configuration, restore-source mapping where appropriate, and an approved dependency manifest.
- Coverage, mutation, architecture, and test-case manifests.
- Scripts with explicit exit-code handling, test-discovery checks, and predictable output paths.

Resolve the actual serviced tool versions during repository setup and record them. This guide intentionally does not invent an SDK patch number or a tool-version combination that has not been restored and tested together.

Core test doubles should be small and explicit. Use a controllable `TimeProvider`, deterministic UUID/metadata inputs inside tests, completion barriers, and temporary directories. Preserve random seeds and failing inputs if randomized stress tests are added.

The verification command must reject a missing test family, a missing report, zero discovered required tests, an unrecognized report schema, or a package hash mismatch. The absence of a failure report is not success.

## 18 Verification bundle shipped with every version

Every published version, including hotfixes, receives a matching **complete verification bundle** alongside its normal runtime package. Tests are distributed as release material; consumer applications do not install test dependencies to use the library.

The bundle must contain:

| Area | Required contents |
| --- | --- |
| Product | Exact runtime and optional bridge packages, symbols where produced, XML API documentation, release notes, and package/assembly hashes. |
| Source | Exact matching source snapshot, solution/project/build files, commit identity, and relevant submodule content if used. |
| Tests | All unit, integration, architecture, compatibility, and business-case projects; test support; mutation configuration and harness. |
| Contracts | JSON schema, environment/configuration contract, event catalogs, acceptance mapping, and language-neutral fixtures. |
| History | All cumulative API/package/config/schema/behavior baselines and historical source/binary consumer fixtures, with hashes. |
| Examples | Complete runnable examples, expected outputs, explanations, and collector configuration samples. |
| Tooling | Pinned SDK/tool manifests, restore configuration, package lockfiles, build/test/replay scripts, and version inventory. |
| Evidence | Raw test reports by family and lane, discovery/skip summaries, line/branch coverage, mutation JSON/HTML, architecture results, and compatibility results by baseline. |
| Integrity | Verification manifest, file checksums, dependency inventories, and licenses/notices for distributed tooling or packages. |

The release manifest must identify package version, source commit/tree hash, package SHA-256, loaded assembly hashes, test-suite version, tool versions, actual OS/runtime/architecture lanes, each family's discovered/executed/passed/failed/skipped counts, historical baseline IDs, coverage denominators, mutation counts, exclusions, and every required evidence path/hash.

A source archive plus lockfiles may still depend on a feed that later removes a package. Retain the exact permitted test/package bytes and required toolchains in durable, immutable storage. The verification bundle must include either those redistributable bytes or a content-addressed retained dependency archive with a materialization script and hashes. A feed URL alone is insufficient. Document licensing constraints before redistributing toolchain binaries.

Provide a documented clean-machine replay path. Once prerequisites are materialized, tests must not depend on a developer's package cache, undisclosed credentials, a live production database, or a paid collector account. Replaying may require the appropriate supported OS/SDK lane; record that requirement precisely.

Internal unpublished CI candidates can use faster feedback during development. Every published version, including prereleases and hotfixes, must receive the full suite and bundle. A change to source, tests, harnesses, dependency closure, compiler inputs, verification configuration, or package bytes invalidates the associated results. Regenerate the candidate as needed and repeat all six full release suites for the final set of inputs.

## 19 Build and release pipeline

Implement a repository-owned verification driver so local execution and CI use the same rules. A small .NET console driver with thin shell/PowerShell launchers is acceptable and avoids a dependency-heavy build framework.

| Stage | Work and required evidence |
| --- | --- |
| 1 Reproducible restore | Select the pinned SDK/tools; restore locked dependencies; record source and tool identities. |
| 2 Static verification | Format/analyzers/documentation checks; evaluated project dependency rules. |
| 3 Fast correctness | Build Release and run deterministic unit tests plus relevant fast behavior tests; prove required discovery. |
| 4 Full mutation | Run mutation in an isolated source/output tree; retain complete reports and validate scores/completeness. |
| 5 Clean candidate | Build unmutated Release output afresh, pack it once, apply the configured signing steps, and then compute the final package/assembly hashes. |
| 6 Package verification | Inspect package contents and dependency groups; run API/asset validation; stage the package in a local test feed. |
| 7 Full package suites | Run all normal test families, integration applications, business cases, and all historical compatibility layers against the candidate and promised platform lanes. |
| 8 Evidence validation | Confirm coverage, mutation linkage to the same source/configuration, discovery, historical completeness, outcomes, and exact candidate hashes. |
| 9 Bundle assembly | Create the complete verification bundle, manifests, checksums, and release notes. Replay from an extracted bundle in a clean verification workspace. |
| 10 Promotion | Publish the identical already-tested package and bundle under the repository's existing release authorization process; retrieve and verify the published payload/hash under the feed's documented signing/storage behavior. |

Unit tests may use project references for internal seams, but the uninstrumented shipping assembly they exercise must match the assembly packed into the candidate. Coverage/mutation instrumentation has its own recorded copies; do not compare instrumented bytes to the release DLL and misinterpret the difference. Black-box integration and compatibility tests restore the actual candidate package.

Use an isolated NuGet package cache and candidate-only source mapping for the package under test. A cached same-version package or a restored package from another feed must not satisfy a candidate test accidentally. For archived source fixtures, preserve original project/lockfiles and create the candidate-reference substitution plus its temporary lockfile only in the replay workspace, recording that change. Record the package path and the loaded core/bridge assembly hashes from the actual test processes.

Clean bundle replay extracts the hashed input into a read-only verification workspace, materializes its pinned prerequisites, and runs all six families for the selected recorded lane, including the corresponding mutation matrix entries. Generated builds and reports go to a separate working/output directory. Verify the input checksums before and after replay; do not write replay reports into the archived hashed bundle and then treat changed hashes as a valid release. Platform-wide certification still requires the complete recorded lane matrix, not one local replay.

The release pipeline can run its full verification directly from an extracted source/test input kit, then attach the resulting evidence. That satisfies clean replay without repeating an identical expensive mutation run solely to test archive packaging. A final external release index references the input digest, evidence digests, and completed bundle digest; it does not attempt to put an archive's own hash inside that same archive.

If the feed adds a repository-signature envelope, archive the fetched signed package and both package digests. Verify that all non-signature payload entries and the tested assembly bytes are unchanged. Feed-side signature metadata is not permission to rebuild or alter the tested runtime payload.

The full suite is a release requirement, not merely a nightly aspiration. A nightly full run is useful for early feedback; it does not replace the run tied to the final candidate and its complete verification inputs. A changed package, dependency graph, test suite, or harness cannot reuse a green report for an older input set.

Implement these script contracts:

```text
eng/verify --profile developer
eng/verify --profile release --version <candidate-version>
eng/replay --bundle <verification-bundle> --lane <recorded-lane>
eng/check-release --manifest <verification-manifest>
```

These command shapes are implementation requirements for the repository driver, not pre-existing commands. Each must return nonzero on failure, emit structured results, avoid hidden restore/build changes, and document prerequisites. Pin the driver to the release it verifies.

A typical local test runner command, once the pinned VSTest-based project exists, is:

```bash
dotnet test tests/Core.UnitTests/Core.UnitTests.csproj \
  --configuration Release \
  --no-build \
  --no-restore \
  --logger "trx;LogFileName=unit.trx" \
  --results-directory artifacts/test-results/unit
```

Use the driver to select the correct pinned runner and coverage/mutation settings. Do not rely on this one command as evidence that all six families ran.

## 20 Implementation work packages

Complete these work packages in order, with small reviewable commits. Tests are built alongside the relevant core behavior. Reference examples follow working core behavior rather than being used to simulate implementation completion.

| Work package | Implementation work | Exit evidence |
| --- | --- | --- |
| WP-01 Repository foundation | Fix naming, inspect initial consumer target, pin .NET/tools, create core/test projects, dependency manifests, formatting/analyzers, and verification entry points. | A minimal core package has no explicit package dependencies; test discovery and dependency negative controls work. |
| WP-02 Public contracts | Define setup/snapshot/event/value/result types, initialization semantics, configuration table, schema v1, and compatibility rules. | Public surface reviewed for indefinite stability; initial contract fixtures and safe API examples compile. |
| WP-03 Environment resolution | Implement precedence, parsing, metadata discovery, immutable publication, convenience getters, and diagnostics. | Unit cases for every variable and isolated-process static initialization cases pass. |
| WP-04 Event capture | Implement logger creation, levels, scopes, metadata projection, supported values, exception capture, and deferred evaluation. | Snapshot/correlation/immutability tests pass; event schema fixtures validate. |
| WP-05 Outputs and lifecycle | Implement JSONL/human renderers, console/file outputs, rotation, independent queues, accounting, flush, stop, and bounded diagnostics. | Real stream/file integration, overload/failure/stop tests, and output business cases pass. |
| WP-06 Microsoft bridge | Implement provider-only mapping and explicit ownership; validate host filtering and approved transitive graph. | Standard ILogger/scopes integration cases pass with no dependency leaking into core. |
| WP-07 Verification completion | Complete all six families, architecture inspection, negative controls, mutation gate, cumulative compatibility harness, and candidate-package replay. | Required coverage/mutation thresholds and all applicable acceptance cases pass. |
| WP-08 Reference examples | Build and document the example catalog using the completed core API; restore the candidate packages. | Every example runs in CI and matches its asserted outputs and explanation. |
| WP-09 First release | Freeze public contracts/defaults/schema, capture first historical consumers/baselines, produce complete bundle, and replay it cleanly. | All release gates pass for the exact package; artifacts are ready for authorized publication. |

WP-01 should validate the mutation runner/toolchain early using a small genuine core behavior, so a tooling incompatibility is discovered before the whole implementation depends on it. WP-07 completes the full mutation and compatibility coverage after functionality exists.

Code review for each package should explain the behavior implemented, the responsibility boundary, the public contract impact, and the evidence. Do not make a single large commit that combines environment resolution, writers, all tests, and generated examples.

The first consuming-application migration is a later, separately scoped adoption task. It should use the published candidate API and compare existing observability behavior. This guide does not authorize changing CTEP's business logic, persistence boundaries, or its current delivery scope.

## 21 Reference example catalog

Each example must contain runnable code, exact configuration, expected structured output or asserted observations, the reason for each log level, what information belongs in the event, and the information intentionally excluded. Include one clear failure or boundary case where relevant.

| Example | Teaching purpose |
| --- | --- |
| `examples/ConsoleHuman` | Initialize once; normal versus verbose detail; readable startup/completion/failure events. |
| `examples/AgentCli` | Keep stdout machine results clean and diagnostics as stderr JSONL; show useful operation IDs and exit behavior. |
| `examples/WorkerWithFileCapture` | Configure identity and rotating JSONL files; explain collection configuration and graceful stop. |
| `examples/IndependentOutputLevels` | Demonstrate Debug file plus Warning console and correctly deferred expensive work. |
| `examples/ConcurrentOperations` | Demonstrate two experiments/jobs with separate scopes across asynchronous work. |
| `examples/FailureAndShutdown` | Demonstrate an unavailable writer, queue-full accounting, flush boundaries, and stop deadlines. |
| `examples/MicrosoftLogging` | Use ILogger categories, structured state, scopes, and explicit provider/runtime ownership. |
| `examples/UpgradeConsumer` | Preserve a small application as a future source and frozen-binary upgrade fixture. |

Use a small example event catalog:

- `application.started`: application ready, relevant capabilities, and metadata.
- `operation.started`: operation identity and safe inputs needed to understand the work.
- `operation.completed`: outcome, duration, useful counts, and correlation.
- `operation.failed`: failure category, exception, retryability where known, and operation identity.
- `operation.cancelled`: expected cancellation distinguished from a failure.
- `dependency.retry_scheduled`: dependency, attempt, delay, and cause without payload/secret dumps.

Explain logging ownership: an exception should usually be recorded at the boundary that handles it or can add useful context. Do not teach every layer to catch, log, and rethrow the same exception. High-frequency progress belongs at a suitable level or aggregation interval; it must not become an uncontrolled stream of repeated Information events.

Application identity is supplied by the core, so examples must not manually repeat service/build/host fields on every call. Applications supply business facts and operation context. This division is what the agent should copy.

## 22 TypeScript portability contract

Prepare the shared contract now; implement the TypeScript library after the C# core and examples are stable.

Preserve environment variable names, precedence, defaults, level meanings, schema fields/types, identity semantics, value bounds, scope behavior, and lifecycle outcomes. Reuse language-neutral configuration/event fixtures and business-case input/output pairs.

A Node implementation can discover process/host/runtime information through Node's own runtime APIs. A browser implementation receives public application/build/deployment identity from its build or startup configuration and omits unavailable process/host fields. Never expose the server's entire environment in a browser bundle.

Keep process metadata separate from operation context in both languages. Preserve decimal/large-integer strings and timestamp/identifier encodings. Do not assume JavaScript numbers can represent every C# integer or decimal without loss.

The first port does not have to reproduce the C# internal class structure or every discovery API. It must preserve the documented observable contract and pass the applicable shared fixtures. Runtime-specific output mechanisms and lifecycle differences need explicit capabilities and tests; they must not be hidden under a claim of identical behavior.

## 23 Completion criteria

The first published implementation is ready when:

- `EnviormentsOptions` resolves and publishes the specified immutable environment snapshot.
- Logging produces correctly enriched human and JSONL output, with independent console/file filtering and tested lifecycle/failure behavior.
- The shipping core has zero explicit NuGet package references and zero third-party runtime dependencies; the optional Microsoft bridge matches its audited allowlist.
- The public surface, environment contract, defaults, schema, target assets, and ownership rules are frozen in the initial baseline.
- All six verification families execute and pass the applicable gates.
- All reference examples run against the actual candidate package.
- The complete source/test/fixture/tooling/evidence bundle is retained with the version, and a clean replay succeeds.
- Release notes identify actual behavior and tests, without claiming untested collectors, platforms, durability, or ports.

The guide itself is a specification. The release must contain actual implementations, test results, and artifacts; scaffolding, placeholder tests, generated empty reports, or a future-testing checklist do not satisfy these criteria.

## 24 Implementation brief for a coding agent

The following brief can be placed in the repository task that starts implementation. It references this guide as the detailed contract.

```text
Implement the environment and logging core described in
core-library-implementation-guide-v1.md.

Start with WP-01 and proceed through the work packages in order. Build core
functionality and its tests before the reference examples. Keep commits small
and organized around a coherent behavior and its verification.

Preserve the exact public static class name EnviormentsOptions. Resolve
configuration once, publish an immutable snapshot, and attach its allowlisted
application/build/host/OS metadata automatically to events. Keep operation
context separate and capture it before asynchronous queuing.

The Core project must have zero explicit PackageReference items and no
third-party runtime dependency. Keep the optional Microsoft logging provider
in its separate assembly with an exact audited Microsoft dependency allowlist.
Stryker is a pinned development-only tool and cannot enter runtime artifacts.

Implement the guide's configuration defaults, independent output filtering,
bounded queues, JSONL framing, file ownership/rotation, delivery accounting,
flush boundaries, and graceful stop semantics. Do not silently substitute a
different behavior because it is easier to code.

Treat backwards compatibility as indefinite across source, binary, behavior,
configuration, schema, and target assets. Retain cumulative historical package,
source, binary, and behavior fixtures. A major version or an Obsolete warning
does not permit breaking existing supported consumers.

Build and execute unit, integration, mutation, architecture, compatibility,
and business-case verification. Validate discovery as well as pass/fail.
Use actual candidate-package consumers and record the loaded assembly hash.
Never remove a fixture, weaken a threshold, regenerate historical expected
output, or suppress a breaking change merely to obtain a green build.

Create a complete verification bundle for each release candidate intended for
publication, containing exact source, packages, tests, fixtures, pinned tools,
reports, manifests, and checksums. Report implemented behavior and actual
commands/results candidly. A missing capability remains incomplete.

Follow the repository's existing release authorization and publication rules.
This task prepares a reviewable implementation and its evidence; it does not
authorize changes to unrelated consuming applications or business behavior.
```

## 25 References and provenance

The source references below support specific .NET/tooling facts. The architecture, limits, thresholds, work packages, and compatibility policy are the project decisions specified in this guide. Sources were checked on 8 October 2026; pin and validate exact versions during implementation.

| Reference | Primary source and use |
| --- | --- |
| R1 | [Microsoft.Extensions.Logging.Abstractions 10.0.12](https://www.nuget.org/packages/Microsoft.Extensions.Logging.Abstractions/10.0.12) and [Microsoft.Extensions.DependencyInjection.Abstractions 10.0.12](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12) — logging interface ownership and target-specific dependency groups. |
| R2 | [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core) — current .NET release lifecycles. |
| R3 | [C# static constructors](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors) — failed static initialization behavior. |
| R4 | [.NET versioning guidance](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/versioning) — assembly/file/informational version distinctions. |
| R5 | [RuntimeInformation](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.runtimeinformation) — OS/process/runtime metadata APIs. |
| R6 | [OpenTelemetry logs data model](https://opentelemetry.io/docs/specs/otel/logs/data-model/) — resource, trace context, and event attribute meanings. |
| R7 | [Logging guidance for .NET library authors](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/library-guidance) — ILogger usage, categories, and deferred work considerations. |
| R8 | [.NET compatibility change rules](https://learn.microsoft.com/en-us/dotnet/core/compatibility/library-change-rules) — source/binary/behavior compatibility hazards. |
| R9 | [.NET package validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/overview) and [baseline validator](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/baseline-version-validator) — package and historical baseline checks. |
| R10 | [API compatibility tools](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/overview) — APICompat and strict equality mode. |
| R11 | [.NET runtime version selection](https://learn.microsoft.com/en-us/dotnet/core/versions/selection) — runtime selection and roll-forward behavior. |
| R12 | [Stryker mutant states and metrics](https://stryker-mutator.io/docs/mutation-testing-elements/mutant-states-and-metrics/) — native mutation score and mutant classifications. |
| R13 | [Stryker.NET configuration](https://stryker-mutator.io/docs/stryker-net/configuration/) — runner selection, scope, timeouts, and reporting settings. |
| R14 | [MSTest SDK configuration](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-sdk) — MTP default and explicit VSTest selection. |
| R15 | [.NET code coverage guidance](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-code-coverage) and [dotnet-coverage](https://learn.microsoft.com/en-us/dotnet/core/additional-tools/dotnet-coverage) — coverage tooling and collector distinctions. |
| R16 | [.NET Channels](https://learn.microsoft.com/en-us/dotnet/core/extensions/channels) — bounded channels and admission primitives used by the proposed implementation. |
| R17 | [W3C Trace Context](https://www.w3.org/TR/trace-context/) — trace and parent/span identifier representations. |
| R18 | [ECMAScript Number.MAX_SAFE_INTEGER](https://tc39.es/ecma262/multipage/numbers-and-dates.html#sec-number.max_safe_integer) — exact integer representation boundary. |
| R19 | [NuGet package compatibility rules](https://learn.microsoft.com/en-us/dotnet/standard/library-guidance/nuget-package-compatibility-rules) — current assembly-version recommendations, compatible replacements, and target-specific asset compatibility; checked during the recovery review. |

Reference provenance recorded in the original draft, retained here as historical metadata rather than newly verified attachment hashes:

- `Logging.zip`, SHA-256 `a6f7361d7858d55cdd1e382aadce6710c0ac3aa3f16e22c8138099a8f621ecc7`.
- `Pasted text.txt`, SHA-256 `62e151db284e032a7274e97a4aa713c1f7f2c81c723a94c6b68528142c765b0c`.

Attachments actually available and checked during this continuation:

- `Logging(1).zip`, SHA-256 `23cf028a49b1d93f9721b905152ddaa21db3185d9bf58a1f09c7d75fdfeb57c1`: 17 C# reference files, with no solution/project manifests or test suite.
- `Pasted text(1).txt`, SHA-256 `6bde0b2fbdd100519b428a5b34180aabfc15bc9a6b20f4ab91d96c28311b3927`: carried-forward conversation ending during guide preparation.

The current transcript preserves the environment requirements and proposed public API, but does not reproduce the entire separately pasted environment-module source. The recovery review therefore verifies that contract against the transcript and reviews the available logging source; it does not claim to have re-inspected that earlier environment source or run the reference code. The recovered draft before these corrections had SHA-256 `5b713aa3b452eea1b24169ac991dedcc46efa8dea96402bcb049f17667225a5f`.

The reference material informs the new design. Using it as a starting point does not require API compatibility with GMCore or its engine-specific adapters. The permanent compatibility obligation begins with this core's first published supported contract.

