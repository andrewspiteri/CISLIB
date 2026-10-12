# EnvironmentOptions: Simplification Instructions for Codex

**Project:** CIS core library  
**Updated:** 11 October 2026  
**Status:** Corrected responsibility: EnvironmentOptions is a passive value store.

## 1. The correction that governs this task

The user's clarification is explicit:

> EnvironmentOptions is just a value store. It should not initialize anything.

The earlier handoff incorrectly made `EnvironmentOptions` responsible for initialization, validation, lifecycle, and snapshot publication. The subsequent review then tried to make that implementation more readable. Those requirements were the wrong starting point.

**This revision replaces those instructions.** Do not preserve a self-initializing options subsystem merely because the previous implementation or handoff contained one.

The required design is:

- `EnvironmentOptions` contains values and simple literal defaults.
- Ordinary application startup code supplies the initial values and applies available environment-variable overrides.
- A small separate helper may read/parse overrides if there is useful shared code.
- Logging and other modules consume the values and perform their own startup work.

The user has deferred design of the general startup pipeline. Do not introduce one to complete this correction.

## 2. Responsibilities

| Component | Responsibility |
| --- | --- |
| `EnvironmentOptions` | Hold application/runtime facts and any ordinary module-option values that belong in the central store. |
| Existing application startup/bootstrap code | Decide when to populate those values, provide application metadata, and arrange for consumers to start afterward. |
| Optional small environment-reading helper | Read known variables, interpret them, and apply valid overrides over supplied/default values. |
| Logging | Read the relevant values, construct its own event metadata, and initialize its own runtime. |
| Other modules | Read the settings they need and own their own initialization. |

Creating or assigning option values is different from starting a service. The reviewed archive showed construction of logging options and event metadata; it did not establish that the environment code starts logging workers. The correction is to remove preparation and lifecycle ownership from the value store as well.

### The value store must remain passive

It should not:

- Read environment variables, inspect assemblies, or query the host/runtime.
- Call a loader from a static constructor, property getter, or property initializer.
- Validate configuration or perform parsing in its setters.
- Own initialization state, readiness, locking, retries, or publication.
- Create logging event metadata or initialize a module.
- Throw from ordinary value getters because application startup has not run.
- Poll storage, subscribe to refresh events, or scan types.

Literal property defaults are fine. They do not justify an initialization workflow.

## 3. Example of the intended simplicity

This is a minimal example of the intended responsibility, not a complete replacement public API:

```csharp
public static class EnvironmentOptions
{
    public static string ApplicationName { get; set; } = string.Empty;

    public static string BuildVersion { get; set; } = "unknown";

    public static string ServiceVersion { get; set; } = "unknown";

    public static string EnvironmentName { get; set; } = "unknown";

    public static string? HostName { get; set; }

    public static string OperatingSystem { get; set; } = "unknown";
}
```

There is no `Initialize`, factory, readiness flag, or snapshot-management mechanism in this example.

Choose setter visibility to match the actual assignment location and established public contracts. In particular, do not make setters internal while expecting a different application assembly to assign them directly. This example does not prescribe unrestricted runtime mutation as an application policy.

### Assignments belong in application startup

Application startup supplies values before starting consumers. For example:

```csharp
EnvironmentOptions.ApplicationName = "CIS";
EnvironmentOptions.BuildVersion = suppliedBuildVersion;

string? nameOverride =
    System.Environment.GetEnvironmentVariable("CORE_SERVICE_NAME");

if (string.IsNullOrWhiteSpace(nameOverride))
{
    nameOverride =
        System.Environment.GetEnvironmentVariable("DD_SERVICE");
}

if (!string.IsNullOrWhiteSpace(nameOverride))
{
    EnvironmentOptions.ApplicationName = nameOverride.Trim();
}
```

This illustrates one override. The application/startup code is responsible for validating the selected name where required before starting consumers. Other existing settings follow the same straightforward pattern.

If several applications need the same environment-reading code, one small helper can remove repetition. It must not recreate a generic configuration engine or a renamed copy of the existing 429-line factory.

## 4. Instructions for the current implementation

The latest reviewed archive has 10 C# files and 774 physical lines. Its `EnvironmentSnapshotFactory` contains 429 lines, with a 147-line `Create` method. Those counts describe the supplied archive; inspect the current repository before editing.

The refactor must correct ownership first, then simplify the remaining loading code.

| Current code | Required change |
| --- | --- |
| `EnvironmentOptions.Initialize` | Remove initialization ownership from the store. Move necessary assignments to existing application startup or one small independent helper. See the compatibility rule below before removing a shipped method. |
| `EnvironmentOptions.Current`, `IsInitialized`, `Gate`, `current`, and `initializing` | Remove mandatory lifecycle/publication machinery from the target value-store design. A data store does not inherently need a current-snapshot facade or readiness guard. |
| `EnvironmentSnapshotFactory` | Remove the factory-based subsystem. Retain only the ordinary metadata reads, override parsing, and genuinely required checks in the code that prepares the values. Do not move the class unchanged or split it into replacement services. |
| `EnvironmentSnapshot` | Reassess whether a second data representation is needed. Do not retain it solely to enforce initialization. A retained data type must be passive. |
| `EnvironmentSnapshot` constructing `EventMetadata` | Let logging construct its metadata from the stored facts. The data store should not construct a logging projection. |
| `EnvironmentSetup` | Keep only if it remains a useful input model or a supported public type. Avoid redundant setup/store/snapshot copies without a concrete requirement. |
| Logging options | They may remain ordinary values in the store. Parsing and runtime setup belong to the code preparing/consuming them. |
| Source callbacks, issue-count validity tests, and long helper contracts | Replace with direct values, straightforward parsing, and clearly named results. Do not carry this bookkeeping into a renamed loader. |
| Historical README and implementation tasks | Rewrite to describe a passive store populated by its caller. Remove instructions to recreate self-initialization and snapshot publication. |

The revised shape must be understandable by a human without learning an internal framework. Concentrate the store and any small shared loading helper in one or two coherent classes, while retaining necessary simple public data types for compatibility.

## 5. Loading behaviour belongs to the caller/helper

This responsibility correction does not require gratuitous changes to the meaning of existing environment variables.

When applying overrides, retain the current effective precedence where applicable:

**Default < discovered value < explicit supplied value < existing alias < canonical environment variable.**

- Missing, empty, or whitespace-only variables leave the initial value unchanged.
- Select the highest-priority nonblank value first.
- A valid selected value replaces the initialized value.
- Preserve rejection of malformed selected overrides in the loading/validation code where currently required; do not silently substitute a lower-priority value.
- Validate the selected value, rather than an unused lower-priority value.
- Read each needed variable at most once per loading operation. An unused alias need not be read.
- Keep actual `BuildVersion` separate from the service-version override.
- Resolve the effective global log level before deriving unspecified console/file levels from it.

These rules can be implemented with ordinary conditionals and small parsing methods. They do not require candidates, selection-state objects, parse-state hierarchies, providers, registries, or general-purpose binding.

Application startup should populate values before consumers begin. This task does not introduce concurrent refresh, atomic multi-property update requirements, or a process-wide initialization state machine. Preparing some values locally before assigning them is acceptable; a snapshot-publication framework is not required.

### Validation and diagnostics

Keep checks where a value is prepared or used. Preserve useful existing error codes/source keys when they are supported contracts, using plain errors or an ordinary list as appropriate.

Do not make exact issue-position bookkeeping or a generic validation engine mandatory merely because the previous internal tests exercised it. Preserve actual supported behaviour and required release gates, while adapting tests of removed mechanics.

Path normalization, logging limits, and file-size relationships are concerns of preparing/using logging settings. The store must not perform them. Reuse any required existing path behaviour rather than replacing it blindly, but do not retain a family of path-resolution objects.

## 6. Existing variables: reference for migration

This table records the supplied implementation. It is a migration reference for the loading code, not a runtime settings catalogue or a requirement to put parsing in the store.

| Canonical variable | Alias | Existing fallback / rule |
| --- | --- | --- |
| `CORE_SERVICE_NAME` | `DD_SERVICE` | Supplied application name, then discovered name; required selected application identity. |
| `CORE_ENVIRONMENT` | `DD_ENV` | Supplied environment name, otherwise `unknown`. |
| `CORE_SERVICE_VERSION` | `DD_VERSION` | Supplied service version, otherwise actual build version. |
| `CORE_LOG_LEVEL` | None | Supplied level, otherwise `Information`. |
| `CORE_LOG_CONSOLE_FORMAT` | None | Supplied format, otherwise `Human`; preserve `human`/`jsonl` tokens. |
| `CORE_LOG_CONSOLE_LEVEL` | None | Supplied console level, otherwise effective global level. |
| `CORE_LOG_CONSOLE_STREAM` | None | Supplied stream, otherwise standard error; preserve `stderr`/`stdout`. |
| `CORE_LOG_HUMAN_DETAIL` | None | Supplied detail, otherwise normal; preserve `normal`/`verbose`. |
| `CORE_LOG_FILE_PATH` | None | Supplied path, otherwise absent; preserve required logging path behaviour. |
| `CORE_LOG_FILE_FORMAT` | None | Supplied format, otherwise JSON Lines. |
| `CORE_LOG_FILE_LEVEL` | None | Supplied file level, otherwise effective global level. |
| `CORE_LOG_QUEUE_CAPACITY` | None | Default 1024; range 1–65,536. |
| `CORE_LOG_FLUSH_INTERVAL_MS` | None | Default 1000; range 10–60,000. |
| `CORE_LOG_SHUTDOWN_TIMEOUT_MS` | None | Default 5000; range 1–300,000. |
| `CORE_LOG_FILE_MAX_BYTES` | None | Default 10,485,760; range 65,536–1,073,741,824; at least maximum entry bytes for enabled file output. |
| `CORE_LOG_FILE_RETAINED_FILES` | None | Default 10; range 1–1000. |
| `CORE_LOG_SCHEMA_VERSION` | None | Default 1; only 1 currently supported. |
| `CORE_LOG_MAX_ENTRY_BYTES` | None | Default 65,536; range 16,384–16,777,216. |

Keep existing token interpretation, integer parsing, and identity constraints where required by their consumers. Verify the complete repository because the archives omit the logging and text implementations.

## 7. Compatibility and scope

The long-term compatibility requirement remains relevant, but a draft implementation is not evidence of a released contract.

- Check the actual public API baseline and consumers before removing public members.
- If the lifecycle API has not shipped, remove it as part of this design correction.
- If an API has demonstrably shipped, keep only the minimum compatibility bridge needed and report the exact limitation. Compatibility code must not dictate a new initialization framework.
- Do not claim complete behavioural compatibility if a supported getter's pre-start behaviour or another supported contract changes.
- Do not preserve the old initialization machinery simply because internal tests or the previous handoff required it.

No additional external NuGet dependencies are needed. Retain the existing framework target and relevant repository quality/release requirements.

Do not design the future startup pipeline, Type Manager, storage-backed POCO Configuration module, polling, events, or live refresh in this task. Do not add heartbeat or other new module functionality.

## 8. Implementation and verification

1. Inspect the repository instructions, real consumers, and public compatibility baseline.
2. Make `EnvironmentOptions` passive, with ordinary values and defaults.
3. Move the necessary assignments/overrides into the existing startup location or a small independent helper; remove unnecessary factory and lifecycle code.
4. Let logging read the stored values and construct its own metadata.
5. Adapt tests to verify default values, explicit assignments, override precedence, selected-value validation, relevant consumer behaviour, and actual compatibility obligations.
6. Remove tests whose only purpose is to preserve a deleted internal framework. Retain required unit, integration, mutation, architecture, compatibility, and business-case coverage where applicable.
7. Update documentation and report the simplified ownership, removed abstractions, changed call sites, and verification results.

Human-readability rules:

- One statement per line and ordinary control-flow blocks.
- Concrete names and visible defaults.
- Short private helpers where they reduce repetition.
- No long positional argument lists, nested conditional chains, source-selection callbacks, or issue-count validity inference.
- No mechanical concatenation into fewer files and no hidden framework in nested/partial classes.
- Allow more lines when clearer formatting helps.

## 9. Acceptance criteria

- [ ] `EnvironmentOptions` stores values; it performs no discovery, loading, validation, or service startup.
- [ ] Its ordinary property access does not invoke initialization or environment reads.
- [ ] The target store has no mandatory readiness, retry, locking, or publication state.
- [ ] Caller/bootstrap code owns preparation and decides when consumers begin.
- [ ] Available valid overrides apply over initialized values, with the established precedence.
- [ ] Logging constructs its own metadata and owns its own runtime initialization.
- [ ] The factory/transport/lifecycle subsystem has been removed rather than renamed.
- [ ] Any retained public compatibility bridge is justified by an actual released contract.
- [ ] A human can read one setting's preparation without learning a framework.
- [ ] No new startup or general configuration framework has been introduced.

**The central requirement is responsibility: EnvironmentOptions holds the values; other code decides what to put there and when to use them.**

