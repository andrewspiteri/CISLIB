# CIS Type Manager — Implementation Definition

**Document version:** 1.0  
**Date:** 13 October 2026  
**Status:** Implementation baseline prepared; implementation and release verification remain outstanding.  
**Supersedes:** Type Manager module definition draft v0.1, dated 11 October 2026.  
**Runtime target:** `net10.0`  
**Scope:** Type Manager only. Configuration is the next module; the wider Startup Pipeline remains deferred.

This document closes the Type Manager design for implementation. It incorporates the confirmed requirements and selects concrete defaults for the proposals left open in v0.1. New selections are recorded below; they are not presented as earlier user decisions. Document version 1.0 is not a claim that a software package has been implemented, tested or released.

## 1. Purpose and non-negotiable requirements

The Type Manager prepares the application's managed assembly set and retains a catalog of its CLR types. Other modules query that catalog instead of performing their own discovery scans.

**Preparation must finish successfully before DI container construction begins.** The host creates the manager directly, then registers that completed instance for the application's use. The module must not require DI, live configuration or an already-running startup service to initialize itself.

| ID | Requirement |
|---|---|
| TM01 | Complete application assembly preloading and initial type discovery before container construction. |
| TM02 | Automatically include deployed project and application dependency libraries, including eligible libraries not yet touched by application code. |
| TM03 | Retain actual CLR types, defining assemblies and reusable type mappings. |
| TM04 | Provide a general implementation query that Configuration can call with `IConfigurationObject`. |
| TM05 | Preserve type identity including the assembly; never identify a configuration candidate by a short class name. |
| TM06 | Publish a complete catalog, not a partially initialized object. |
| TM07 | Provide deterministic, read-only, thread-safe results without repeat assembly scans. |
| TM08 | Target `net10.0` without adding external runtime NuGet dependencies or a dependency on a DI container. |
| TM09 | Keep the implementation direct and human-readable. Do not introduce a discovery framework or a general plugin system. |
| TM10 | Preserve published source, binary and documented behavioural compatibility indefinitely, starting with the first published contract. |
| TM11 | Ship the full unit, integration, mutation, architecture, backward-compatibility and business-case verification suite, fixtures, instructions and results with each release. |

## 2. Closed decisions

| ID | Baseline decision | Change or clarification from v0.1 |
|---|---|---|
| D01 | Create and fully prepare the manager through `TypeManager.CreateAsync`, before constructing the container. | Retains the completed-object factory proposal; no public initialization state machine. |
| D02 | Generate a small assembly inventory during the host build/publish, from SDK-resolved runtime assets. Read it during bootstrap. | Replaces the unspecified inventory producer with a deliverable owned by this module's implementation. |
| D03 | Use the default application loading context; reuse matching loaded assemblies and load remaining selected assets through it. | Confirms the proposed single-context boundary. |
| D04 | Keep catalog membership fixed for the host lifetime. | No runtime rescan, assembly-added event, reload, disposal lifecycle or plugin discovery. |
| D05 | Return concrete, closed classes using CLR assignability, including inherited implementation. | Visibility and constructors remain the consumer's concern. |
| D06 | Accept closed class/interface queries; reject open-generic wildcard queries in v1. | Retain generic definitions in the full catalog. |
| D07 | Preserve full observed runtime assembly identity and exact assembly-qualified lookup. | Configuration's persisted key/version policy remains outside this module. |
| D08 | Treat every selected assembly as required; fail preparation rather than silently skip broken input. | No best-effort or partial-success switch. |
| D09 | Cache prepared and subsequent valid implementation queries for the manager's lifetime. | No TTL, eviction framework, cache-size policy engine or background task. |
| D10 | Support conventional untrimmed, framework-dependent .NET 10 build and folder-publish layouts on Windows x64 and Linux x64 initially. | Makes the first release verification matrix explicit. Other deployment modes are not claimed. |
| D11 | Keep bootstrap options as simple input values; the factory owns validation and preparation. | `EnvironmentOptions` remains a value store and has no loading or initialization responsibility. |
| D12 | Keep diagnostics to an immutable preparation report and useful exceptions. | No dependency on DI-resolved logging, Configuration, Heartbeat or Healthcheck. |

A normal host should enable the bundled build integration once. It should not maintain a list of its configuration classes or ordinary project dependencies. Only additional discovery-only assets outside the normal resolved runtime set require explicit build inclusion.

## 3. Scope and ownership

| Owner | Responsibility |
|---|---|
| Host bootstrap | Supply the host assembly and any explicit inventory location, await creation and retain the returned manager. |
| Type Manager build integration | Automatically produce the correct inventory for each supported host build/publish output. |
| Type Manager runtime | Validate inventory, preload selected assemblies, discover types, create maps, cache queries and report preparation. |
| DI integration in the host | Register the completed manager instance; use discovered types for registrations where appropriate. |
| Configuration module | Decide configuration construction eligibility, create/retain objects, load storage, apply overrides and manage changes. |
| Future Startup Pipeline | Define service ordering, readiness, shutdown and general lifecycle orchestration. |

The Type Manager does not create application/configuration instances, invoke `IStartable`, run property getters, instantiate attributes, map configuration, compute configuration checksums, raise `ConfigurationChanged`, register discovered classes automatically in DI, prepare serialization contracts or warm application resources.

Its core must not reference the Configuration implementation or marker interface. The expression `GetImplementations<IConfigurationObject>()` is a consumer example, not an internal dependency.

The module must not read or mutate `EnvironmentOptions`. Ordinary host code may use its existing bootstrap values when supplying inputs. Application name `ABC` and configuration persistence naming remain Configuration concerns; the Type Manager needs the actual host assembly, not an application-name inference framework.

## 4. Automatic assembly inventory

### 4.1 Selected assembly set

The inventory includes the host's executable managed assembly, referenced project runtime assemblies, transitive application/package runtime assemblies selected for the target deployment, and explicitly included discovery-only managed libraries.

It does not mean every project in a solution, every DLL on disk, all installed shared-framework assemblies, all RID alternatives in a package, native libraries, satellite resource assemblies, reference-only assemblies or build/analyzer tooling. Files selected as required runtime assemblies are never silently reclassified as optional because they fail to load.

Framework dependencies needed by these assemblies remain under normal .NET resolution. Framework assemblies are not automatically catalog members merely because the runtime loads them. The same explicitly selected application set is both preloaded and cataloged.

A library containing configuration classes must be included when it is a deployed runtime dependency even if the compiler emitted no direct use of one of its classes. A library not deployed or declared for discovery cannot be inferred from its source project being present in the solution.

### 4.2 Build integration is part of completion

Ship one small MSBuild integration with the module's build assets. Hosts enable it with `CisTypeManagerEnabled=true`; class libraries do not independently generate host manifests. Normal references are then automatic. An explicit `CisTypeManagerAssembly` build item covers an additional discovery-only managed asset and requires that it and its dependencies be deployable in the normal host context.

The integration consumes SDK-resolved runtime/copy-local/publish items after asset selection and conflict resolution. It does not scan arbitrary folders, use the compile-reference set as the runtime set, or reimplement NuGet/RID selection. Build and publish must each produce an inventory for their actual output, including changed/removed dependencies. `publish --no-build` must still produce the correct publish inventory from the available valid build outputs.

MSBuild target ordering and identity extraction have existing SDK/MSBuild mechanisms [R7, R8]. The first implementation milestone must prove the actual target hook and asset metadata against the pinned .NET 10 SDK in real sample hosts. That is a required implementation test, not a placeholder for a later component or a claim that the hook is already verified.

Prefer a small target plus a focused build-only helper when necessary to inspect metadata and write JSON safely. Such a helper may use SDK/MSBuild APIs at build time; it must not become a runtime dependency of applications or a public provider framework. It must not execute application code while generating the inventory.

The integration must handle `dotnet build`, `dotnet run` following a successful build, and the supported folder-publish profiles. It must not recurse into its own targets, rewrite unchanged output unnecessarily, emit manifests for unsupported design-time builds, or leave stale manifest entries after incremental removal of a reference.

### 4.3 Manifest contract

Default filename: `<host-simple-assembly-name>.types.json` under `AppContext.BaseDirectory`. A supplied `AssemblyManifestPath` selects an alternative file conforming to the same schema; it does not select an alternative discovery algorithm.

The UTF-8 JSON schema is versioned independently of package versions:

```json
{
  "schemaVersion": 1,
  "hostAssembly": "CustomerService, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
  "assemblies": [
    {
      "assemblyName": "CustomerService, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
      "relativePath": "CustomerService.dll"
    },
    {
      "assemblyName": "Shared.Contracts, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
      "relativePath": "Shared.Contracts.dll"
    }
  ]
}
```

This is an illustrative excerpt; a real manifest includes every selected dependency, including the core library where applicable. Values come from build output metadata, not assumptions based on filenames.

| Field/rule | Contract |
|---|---|
| `schemaVersion` | Integer `1`. Unsupported versions fail clearly. |
| `hostAssembly` | Full observed assembly name of the intended host. Must match the supplied host assembly. |
| `assemblies` | Nonempty list including the host; each selected item is required. |
| `assemblyName` | Full assembly identity selected for deployment, including version/culture/token as emitted. |
| `relativePath` | Normalized path relative to the manifest directory. No rooted path, URI or traversal outside that directory. |
| Duplicates | Identical identity/path entries deduplicate. Conflicting identity/path or same-simple-name entries fail. |
| Unknown properties | Ignored within supported schema version 1 for additive compatibility. Required fields and duplicate JSON property names are validated. |
| Ordering | Generator writes deterministic ordinal ordering by full assembly identity, then path. |

Paths describe where trusted deployment assets are located; they are not type identities. The manifest is a deployment artifact, not a security sandbox, remote plugin feed or authenticity signature. The deployment must remain stable during preparation. The module does not promise file-integrity verification against a hostile local actor.

### 4.4 Missing, stale or unsupported inventory

Missing/unreadable manifest, unsupported schema, host mismatch, invalid path, missing required asset or conflicting selection fails preparation. The error must identify the manifest and relevant entry.

Do not fall back to `AppDomain.GetAssemblies()`, recursive directory scans or an empty list. Currently loaded assemblies alone do not establish the intended deployment inventory; .NET's host already uses dependency information for its own probing [R1].

An explicitly supplied, correctly formed inventory is supported for controlled test/custom hosts. Its author owns completeness. It is not an excuse to omit automatic inventory generation from the normal host delivery.

## 5. Preparation and loading

### 5.1 Public creation contract

The host passes its own assembly explicitly, such as `typeof(Program).Assembly`, rather than letting the library guess from the currently executing assembly or a test runner.

```csharp
var options = new TypeManagerOptions(typeof(Program).Assembly)
{
    PrecomputedContracts = new[] { typeof(IConfigurationObject) }
};

ITypeManager typeManager = await TypeManager.CreateAsync(
    options,
    cancellationToken);

// Only now perform DI setup and build the container.
// Register this exact typeManager instance in the host's DI integration.
```

The configuration interface in this example is supplied by the consumer. A standalone Type Manager sample uses a test/sample discovery contract until Configuration's public contract exists.

`TypeManagerOptions` has one required constructor argument, `Assembly hostAssembly`, a get-only `HostAssembly` property, an optional init-only `string? AssemblyManifestPath` (default null), and an init-only `IReadOnlyList<Type> PrecomputedContracts` (default empty). These are data only. The factory validates them and copies mutable input collections before its first asynchronous suspension. Null option objects/elements and invalid precomputed contracts produce argument errors before loading begins.

The factory signature is:

```csharp
public static Task<ITypeManager> CreateAsync(
    TypeManagerOptions options,
    CancellationToken cancellationToken = default);
```

The asynchronous work includes reading the manifest. Assembly loading, reflection and catalog construction remain straightforward sequential operations; do not wrap everything in `Task.Run` or introduce worker pools. No partial manager is returned and no work continues in a background initialization task after success.

### 5.2 Direct preparation sequence

1. Check arguments and cancellation; capture input values.
2. Read and validate the manifest, paths, required file presence and selected identities.
3. Establish the complete selected asset map before loading any newly selected assets.
4. Reuse matching assemblies in `AssemblyLoadContext.Default`; load remaining selected assets through that same context.
5. Enumerate each selected assembly once and collect its defined runtime types and relationships.
6. Build immutable catalog collections and private lookup maps.
7. Prepare the requested implementation queries through the same matcher used by normal queries.
8. Finalize diagnostics, check cancellation at the publication boundary and return the completed manager.

Do not create custom load contexts, load raw bytes into isolated contexts, or use `Assembly.LoadFile` as a convenient shortcut. Runtime type compatibility depends on the actual loaded assembly instance [R2].

Validate the actual loaded identity against the selected inventory entry. A different already-loaded version of a selected assembly is an identity conflict, not an implicit catalog substitution. Normal runtime binding of transitive references is not reimplemented: older reference versions resolving to the runtime-selected asset remain the runtime's responsibility. Selecting two different versions under the same simple name in one inventory is invalid for this baseline.

Identity-based default resolution handles ordinary deployed dependencies. Explicit selected paths may be loaded through `AssemblyLoadContext.Default.LoadFromAssemblyPath` when needed; their resolved identities must still be checked. If bootstrap requires a temporary resolving handler for selected discovery assets, it may resolve only entries from the completed manifest map, must avoid recursive self-resolution, and must always be removed in `finally`. It must never search directories or remain attached after creation. Prefer ordinary host probing wherever it suffices [R1].

Assemblies outside the selection that load as a runtime consequence do not silently extend the catalog. Inputs in another loading context, dynamic assemblies, metadata-only assemblies and reference assemblies are outside the supported inventory.

### 5.3 Publication, cancellation and lifetime

The returned manager has fixed catalog membership for the owning host's lifetime. A second explicit factory call creates another independent manager; there is no static process-global catalog, reset function or automatic merging of hosts.

Several callers awaiting the same creation task receive its result or failure. The host is responsible for retaining and registering one successful result.

Cancellation is cooperative and checked between units of work. It cannot interrupt an already executing synchronous runtime call. A final cancellation check occurs immediately before publication; cancellation observed there prevents a successful result. Cancellation after that check does not revoke a published manager.

A failed operation returns no manager, but assemblies already loaded into the default context are not rolled back. No public `Dispose`, `StartAsync`, `StopAsync`, `IsReady`, refresh or event API is required by this module. It owns no timers, polling loops, persistent file handles or background subscriptions.

## 6. Catalog and query contract

### 6.1 Catalog contents and identity

Retain the actual CLR `Type` and `Assembly` objects. Enumerate all defined types, including nonpublic/nested types, interfaces, structs, abstract classes and open generic definitions. A forwarded type belongs to the target defining assembly, not a second definition in the forwarding assembly [R3].

The full catalog does not generate every possible constructed generic type. `Type` itself supplies metadata such as visibility, class kind and generic status; avoid a parallel public object model mirroring reflection. Cache assembly membership, exact-name lookup and implementation relationships/results where needed.

The exact-name map uses each available `Type.AssemblyQualifiedName` with ordinal case-sensitive comparison. Preserve the actual assembly identity, including version; do not remove parts or invent aliases [R5]. A type without a usable assembly-qualified name can remain in the full catalog but is not given a fabricated textual identity.

Order assemblies by observed full assembly identity using ordinal comparison. Order types and implementation results by assembly-qualified name where available, using assembly identity, type name and metadata token as a deterministic fallback for unnamed/unrepresentable entries. That fallback is for ordering only, not lookup identity. Duplicate textual identities for distinct actual definitions are errors, never last-write-wins.

Configuration decides how to persist an identity across its own assembly-version changes. Nothing in this module drops assembly identity or resolves storage keys by a class-name suffix.

### 6.2 Public read interface

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

public interface ITypeManager
{
    IReadOnlyList<Assembly> Assemblies { get; }

    IReadOnlyList<Type> Types { get; }

    TypeManagerPreparationReport PreparationReport { get; }

    IReadOnlyList<Type> GetDefinedTypes(Assembly assembly);

    IReadOnlyList<Type> GetImplementations(Type contractType);

    bool TryGetType(
        string assemblyQualifiedTypeName,
        [NotNullWhen(true)] out Type? type);
}
```

Provide `GetImplementations<TContract>()` as a thin extension calling `GetImplementations(typeof(TContract))`; do not create different generic matching semantics or duplicate the implementation.

The concrete manager is sealed. Public namespace/package placement must follow the existing core repository conventions and be recorded in the first API baseline; the names and behaviour above are the implementation contract. Do not rename existing published APIs merely to match a proposed directory layout.

### 6.3 Implementation matching

A valid query contract is an ordinary closed runtime interface or class from the normal host type universe. Reject null, open generic definitions, generic parameters, arrays, pointer/by-reference/function-pointer types, value-type contracts and unsupported dynamic/metadata-only/custom-context contracts with documented argument errors.

Candidates are cataloged classes satisfying all three conditions:

```csharp
candidate.IsClass
    && !candidate.IsAbstract
    && !candidate.ContainsGenericParameters
```

Matching then uses `contractType.IsAssignableFrom(candidate)`. This includes equality for a concrete contract present in the catalog, inherited interface implementation and class inheritance [R4]. Apply CLR generic variance for closed contracts; a direct-interface-name index alone is not an adequate matcher.

No filter is applied for public visibility, a public parameterless constructor, DI registration or configuration attributes. Do not instantiate a candidate to determine whether it matches. Static classes are excluded by the abstract filter. Structs/interfaces remain in `Types` but are not returned by this class-implementation query.

A contract may come from a framework assembly outside catalog membership. Its type is only a query key; passing it must not cause that assembly's types to be cataloged. A valid query with no matches returns an immutable empty result.

Closed generic contracts such as `IHandler<Order>` are supported. `IHandler<>` is not wildcard syntax. Generic definitions remain in the full catalog; creating closed implementations or solving generic constraints is outside v1.

### 6.4 Lookup and argument outcomes

| Operation | Outcome |
|---|---|
| `GetDefinedTypes` with null | `ArgumentNullException`. |
| `GetDefinedTypes` with assembly outside catalog | `ArgumentException`; no scan or loading. |
| `GetImplementations` with null | `ArgumentNullException`. |
| `GetImplementations` with unsupported contract | `ArgumentException`; no cache entry. |
| Valid implementation query with no matches | Read-only empty list; cache the valid result. |
| `TryGetType` with null | `ArgumentNullException`. |
| `TryGetType` with empty/whitespace string | `ArgumentException`. |
| `TryGetType` with unknown nonempty identity | `false`, with `type = null`. |
| `TryGetType` with exact cataloged identity | `true`, with that actual runtime `Type`. |

Do not trim or normalize valid identity strings into alternative names. A miss does not call `Type.GetType`, which can itself cause loading [R6]. It also does not probe files, try partial identities or extend the catalog.

### 6.5 Caching and concurrency

After preparation, assembly/type lists and lookup maps are fixed and safely published. Use private owned storage with nonmutating public wrappers. Merely declaring a mutable array as `IReadOnlyList` is not sufficient protection.

Implementation queries are cached by actual contract `Type`, including valid empty results. Precomputed and subsequent queries use the same code. Use one private lock for first-time query computation and cache publication unless measurement demonstrates a need for more complexity. There is no I/O, user callback or instance construction within that lock. Completed read results remain safe to enumerate concurrently.

Cache results for the manager lifetime. The API accepts trusted application type requests; it does not create a type from arbitrary user text. Cache-size telemetry/eviction machinery is not part of this release. Measure representative memory usage in verification.

Uncached queries may evaluate the existing in-memory type collection. No query repeats `Assembly.GetTypes`, starts discovery, scans the filesystem or subscribes to runtime assembly-load events. Returned collection reference equality is not a public guarantee; membership, order and immutability are.

## 7. Side effects and diagnostics

Discovery must not deliberately construct application objects, execute attributes, invoke lifecycle methods, read application static/property values, force type initializers or prepare methods for JIT. Assembly loading still follows runtime initialization rules, so there is no absolute promise that runtime/module initialization cannot have effects [R9].

`TypeManagerPreparationReport` is an immutable data class containing `TimeSpan Elapsed`, `int SelectedAssemblyCount`, `int TypeCount`, `int PreparedContractCount`, and read-only `IReadOnlyList<string>` properties `SelectedAssemblyIdentities`, `LoadedAssemblyIdentities` and `ReusedAssemblyIdentities`. It describes preparation, not live cache counters. Count relationships must be internally consistent; classify loaded/reused relative to the selected assemblies observed before the load phase, including loads triggered while processing another selection.

Use monotonic elapsed time. No wall-clock timestamp is required for correctness. A normal successful log needs only a summary; per-assembly identities can be inspected in the report when needed. No per-type log spam.

The host can pass the report/exception to the established logging module. Type Manager must not obtain a logger from DI, initialize logging, wait for Healthcheck or mutate environment metadata. Full asset paths may be included in local diagnostic errors; do not add configuration content, credentials or arbitrary environment dumps.

## 8. Failure semantics

Use `TypeManagerInitializationException` for operational preparation failure. Preserve the original exception as `InnerException` and expose the failure kind, relevant manifest/asset location, relevant assembly identity and a protected read-only collection of loader exceptions where applicable. Avoid long positional constructors; implement readable internal creation helpers if needed.

Stable `TypeManagerFailureKind` values are: `InvalidInventory = 1`, `AssemblyLoadFailure = 2`, `AssemblyIdentityConflict = 3`, `TypeDiscoveryFailure = 4`, `UnsupportedDeployment = 5`. Argument errors remain argument errors. Cancellation remains `OperationCanceledException`, not a wrapped preparation failure.

| Failure | Behaviour |
|---|---|
| Required asset cannot load or has invalid executable metadata | Fail; do not omit it. |
| Actual loaded identity conflicts with selected identity | Fail; show selected and actual identities. |
| Type enumeration/relationship inspection is incomplete | Fail; preserve available loader details. |
| Required precomputed query cannot be prepared due to operational metadata failure | Fail before publication, categorized as type discovery. |
| Invalid caller option/precomputed contract | Argument error before new loading begins. |
| Unsupported declared deployment profile | Fail clearly; do not silently weaken discovery. |

`ReflectionTypeLoadException` exposes partial results and loader exceptions [R3]. Partial results are diagnostic only. All relevant loader errors in that exception must be retained, but the module need not continue processing other assemblies after a fatal failure to accumulate a global report.

After a bootstrap failure, the example/integration host must not reach its container-construction step. Retry policy is the host's concern; there is no internal automatic retry or fallback catalog.

## 9. Deployment and performance boundaries

The first supported matrix is framework-dependent, untrimmed .NET 10 on Windows x64 and Linux x64, with conventional build output and folder publishing. Console and ASP.NET Core sample hosts exercise the same Type Manager API. The Type Manager runtime itself does not require the ASP.NET shared framework.

The build integration rejects explicitly requested unsupported profiles when enabled. Single-file, Native AOT, trimming, self-contained deployment, custom/collectible contexts and runtime plugins are not advertised or implemented in v1. This is a declared scope boundary, not a test waiver for supported builds. Do not silently set host publishing properties to turn a requested unsupported profile into a supported one.

Preloading moves selected assembly-loading and discovery work into bootstrap. It does not promise zero first-request latency: JIT, static initialization, serialization metadata and application resources are separate concerns [R9, R10].

Measure fresh-process preparation, first uncached query, repeated/precomputed query behaviour and retained managed memory on representative catalogs. Label process-fresh measurements accurately; do not claim the operating system's disk cache was cold. No invented milliseconds, percentage improvement or cross-machine timing threshold is a release gate. Correctness gates include exactly one enumeration per selected assembly per successful preparation and no enumeration on later queries.

## 10. Implementation structure and readability

Use one public read interface, one concrete manager, a small input options class, the preparation report, the failure contract and a thin generic extension. A focused internal inventory reader and loader helper are appropriate where they make the direct flow clearer. The build integration is separate from the runtime assembly.

Do not create inventory-provider hierarchies, public catalog builders, generic execution pipelines, message buses, service locators, dynamic plugin systems, custom assembly binders or parallel startup schedulers. Do not split one large class into dozens of partial files. Tests are not a reason to turn every private operation into a public interface.

Prefer full method bodies, early validation, one statement per line, descriptive local names and short comments explaining nonobvious assembly-loading rules. Avoid dense LINQ chains for the load/scan/error flow. Let `Type` carry reflection metadata instead of recreating it in many descriptors. Use normal loops and dictionaries before adding specialized data structures without evidence.

The test matrix can be broad while production code stays small. Module-specific helper classes must have a concrete responsibility visible in the creation flow; there is no arbitrary class-count target that encourages unsafe compression.

## 11. Acceptance and compatibility

The companion roadmap contains the full acceptance matrix A01–A30 and task ownership/dependencies. Every original v0.1 acceptance scenario is mapped to that matrix; none is silently dropped.

Completion requires working automatic inventory, a complete pre-container catalog, correct queries, visible errors, consumer examples, the full required verification bundle and compatibility evidence. All implementation tasks begin as not started in this pack; no repository audit or test execution is implied by writing this definition.

Preserve published public API names/signatures, overloads, default behaviour, exception categories, query matching, ordering and manifest compatibility. Avoid adding required interface members later; prefer extensions or separate interfaces. Public data classes should allow additive evolution without forcing consumers to reconstruct large positional records.

For the first release, create and retain a frozen API/reference build and a compiled compatibility consumer before building the release candidate. Verify that consumer against the candidate without recompiling it. Archive the first released binary, manifest and behaviour fixtures as the historical baseline. Subsequent releases run the supported historical fixtures and consumers rather than replacing them.

Runtime package inspection must show zero new external runtime dependencies. Development-only test and build tooling is kept separate. Existing repository quality gates are retained; do not copy unrelated project thresholds or weaken gates to get the module green.

## 12. Boundary for Configuration and Startup Pipeline

Configuration receives one completed `ITypeManager` and calls `GetImplementations<IConfigurationObject>()`. It gets the actual concrete, closed candidate types, including those discovered from initially unloaded libraries and inherited marker implementations.

Configuration owns its own POCO validation, construction, singleton identity through DI, common/service-specific merge, locks/checksums and notifications. A Type Manager sample must not freeze a new public configuration marker/event contract merely to demonstrate discovery.

The Startup Pipeline definition remains deferred. This module requires only an explicit host bootstrap call that completes before container construction; catalog order conveys no service startup order.

**No additional functional decision is required to begin Type Manager implementation against this baseline.** The highest-risk technical point—the build/publish inventory hook—is proven first and blocks later claims of completeness until its tests pass.

## Technical references

Checked on 13 October 2026. These sources explain .NET/MSBuild behaviour, not the CIS design choices. The build integration must additionally be verified against the repository's pinned .NET 10 SDK.

- **R1 — Default assembly probing:** https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing
- **R2 — Assembly loading contexts and type identity:** https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext
- **R3 — Assembly.GetTypes and incomplete enumeration:** https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.gettypes?view=net-10.0
- **R4 — Type.IsAssignableFrom:** https://learn.microsoft.com/en-us/dotnet/api/system.type.isassignablefrom?view=net-10.0
- **R5 — Type.AssemblyQualifiedName:** https://learn.microsoft.com/en-us/dotnet/api/system.type.assemblyqualifiedname?view=net-10.0
- **R6 — Type.GetType can load assemblies:** https://learn.microsoft.com/en-us/dotnet/api/system.type.gettype?view=net-10.0
- **R7 — MSBuild targets:** https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-targets?view=visualstudio
- **R8 — MSBuild GetAssemblyIdentity task:** https://learn.microsoft.com/en-us/visualstudio/msbuild/getassemblyidentity-task?view=visualstudio
- **R9 — Static initialization:** https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/static-constructors
- **R10 — ReadyToRun and JIT work:** https://learn.microsoft.com/en-us/dotnet/core/deploying/ready-to-run
