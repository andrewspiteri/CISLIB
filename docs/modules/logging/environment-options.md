# C# EnvironmentOptions implementation

Implemented in WP03-S04-T01, 11 October 2026, against the unpublished [startup contract](startup-contract.md). The shipping assembly/package is `CIS.Core`; the public facade is `CIS.EnvironmentOptions`, with logging types in `CIS.Logging`. Original plans remain unchanged and the user-selected corrected spelling takes precedence.

## Application use

Initialize once during application startup, before constructing the later logging runtime:

```csharp
using CIS;
using CIS.Logging;

EnvironmentSnapshot snapshot = EnvironmentOptions.Initialize(new EnvironmentSetup
{
    ApplicationName = "Example.App",
    BuildVersion = "1.2.3+build.5",
    EnvironmentKind = EnvironmentKind.Console,
    Logging = new LoggingSetup { Level = CoreLogLevel.Information }
});

LoggingOptions logging = EnvironmentOptions.Logging;
EventMetadata metadata = EnvironmentOptions.EventMetadata;
// snapshot and EnvironmentOptions.Current are the identical stored object.
```

This initializes configuration and captured metadata. The logger, workers and Microsoft provider are later roadmap work; this call does not create them. `LoggingOptions.MaxEntryBytes` exposes the user-selected 65536-byte default and CORE_LOG_MAX_ENTRY_BYTES override. Bounded capture/truncation remains later work.

## Explicit lifecycle

Before success, `IsInitialized` returns false, and `Current` plus all eight convenience getters throw `InvalidOperationException`. The convenience getters are ApplicationName, BuildVersion, ServiceVersion, EnvironmentName, HostName, OperatingSystem, Logging and EventMetadata.

`Initialize(null)` throws `ArgumentNullException` with parameter `setup`, even after initialization. Selected invalid inputs throw actual `CoreConfigurationException` with immutable safe ordered issues and publish nothing. A corrected call can succeed. Required source failures propagate without a manufactured snapshot. Optional unavailable discovery facts remain null (OS family defaults to unknown).

A successful call returns the identical snapshot subsequently exposed by `Current`. Every later non-null initialization throws `InvalidOperationException` before reading that new setup or environment; it cannot replace the winner. There is no public reset. Application restart establishes a new initialization lifecycle.

The CLR static constructor only allocates a private gate; it reads no configuration or metadata and performs no output work. Explicit static construction avoids the beforefieldinit flag. See Microsoft's [static constructor documentation](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors).

## Resolution and stored ownership

The private builder copies mutable startup fields, captures one initialization directory and all 21 recognized variables, discovers application identity, resolves precedence/defaults, and completes validation. It then discovers runtime facts, generates the initialization UUID/UTC time, and constructs getter-only sealed LoggingOptions, EventMetadata and EnvironmentSnapshot. No caller-owned setup object is retained. Do not mutate setup concurrently with Initialize; later caller mutation is permitted.

BuildVersion records the actual application build. ServiceVersion independently selects CORE/DD/setup telemetry identity and cannot rewrite BuildVersion. FilePath stores an absolute path resolved against the captured directory. EventMetadata carries exactly the 17 allowed application/process/initialization facts, with no logging settings, collector destinations or global operation context. It shares those same immutable facts with EnvironmentSnapshot. Stored getters do not rediscover them.

All access to the private published reference, including IsInitialized/Current, uses the same private monitor gate. The full build and successful publication occur inside that gate. This implements exclusive initialization and synchronized visibility without partial publication; getters can wait while initialization is running. See Microsoft's [lock statement documentation](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock). T03 [concurrent publication qualification](startup-publication-concurrency.md) verifies the shared gate and adds a private in-progress guard for same-thread metadata re-entry.

Initialization validates syntax without opening or probing log outputs. The application owns the future logging runtime. Compiled direct-call guards cover the startup builder/facade/projections and internal resolution/discovery classes, including output/network/worker calls and global exception subscriptions. These guards inspect direct Core calls; they do not claim to inspect every transitive framework implementation.

## Executed scope

[Windows evidence](../../../planning/logging/evidence/wp03-s04-t01-windows.json) retains 24 new unit cases, six fresh-process integration cases and one new architecture case, plus unchanged historical evidence and a fresh isolated source replay. Integration children use exactly .NET 10.0.12 with roll-forward disabled, and verify public preinitialization reads, null guards, defaults/explicit/CORE overrides, failure followed by retry, later-call rejection, real process facts, all convenience getters, matching metadata projections and stored paths after actual child cwd changes. Test processes write their result protocol; Core emits no output and owned output directories stay empty.

T01 includes genuine supporting types and safe ownership/publication mechanics because the facade must return a complete usable result. T02 now qualifies [caller mutation/copy ownership](startup-input-ownership.md). T03 now qualifies [concurrent publishers and failure/retry](startup-publication-concurrency.md); T04 still owns the complete shared lifecycle/metadata fixtures and cross-process identities. Those remaining tasks are not marked complete by the T01 prerequisites. No Linux native-suite, macOS, historical package compatibility, logging runtime or release qualification is claimed here.
