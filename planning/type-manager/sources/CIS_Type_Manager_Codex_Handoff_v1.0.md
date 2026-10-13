# CIS Type Manager — Coding-Agent Handoff

**Version:** 1.0 · **Date:** 13 October 2026

Use this with `CIS_Type_Manager_Definition_v1.0.md` and `CIS_Type_Manager_Roadmap_and_Tasks_v1.0.md`. The definition is the behavioural contract; the roadmap gives task dependencies and acceptance evidence. This is an implementation instruction, not a report that the code already exists.

## Objective

Implement the Type Manager in the existing CIS core-library structure. It must automatically preload the host application's selected managed runtime assemblies, enumerate/cache their types, and return a complete manager **before DI container construction begins**. Configuration will query this catalog; do not implement Configuration or the deferred Startup Pipeline now.

## Working sequence

Start with TM-001: inspect the repository and existing public APIs. Reuse appropriate core facilities, preserve existing compatibility and quality gates, and record the actual namespace/toolchain/build conventions before editing. Do not assume paths or class names exist merely because a planning example uses them.

Then work in the six roadmap milestones. The first proof is a build-generated inventory containing an initially unused project library and its runtime dependencies. Use the pinned SDK's resolved runtime asset information. Do not substitute already-loaded assemblies, compile references, manual per-configuration registration or arbitrary recursive folder scans.

The build integration belongs to this delivery. A normal host enables it once; ordinary dependency discovery is automatic. A focused build-only helper is permitted. No MSBuild/test tooling becomes an application runtime dependency, and no general inventory-provider hierarchy is required.

Use `TypeManager.CreateAsync(options, cancellationToken)` to validate/copy inputs, read inventory, load/reuse assemblies in Default, enumerate once, build maps, precompute requested queries and publish the complete object. Runtime work remains direct/sequential. Do not add Task.Run, a startup scheduler or background initialization.

Implement queries with actual runtime Type identity and CLR assignability. Concrete, closed classes are returned regardless of visibility or constructor availability. Support inherited and closed-generic matches; reject open-generic wildcard contracts. Exact textual lookup only searches the catalog and cannot load an assembly on a miss.

Keep cache membership fixed, expose protected read-only results and use a simple private lock for first query computation/cache publication. Prepare configured bootstrap queries using the same matcher. Failed required loads/scans must prevent publication; preserve useful loader details and keep expected cancellation separate.

## Boundaries to preserve

The target is net10.0 with no added external runtime NuGet dependencies. Use existing repository test tooling separately from shipped runtime code. Published source/binary/behaviour/manifest compatibility is indefinite from the first release; maintain historical fixtures instead of replacing them.

`EnvironmentOptions` remains a value store. Type Manager neither initializes it nor reads configuration to discover configuration classes. Logging is optional host handling of the completed report/error, not a DI prerequisite.

Do not create configuration objects, map stored values, implement checksums/locks/events, register every discovered class automatically, or design IStartable ordering. Do not create a real public IConfigurationObject contract just for a demo when that contract does not yet exist; use the sample-only discovery marker.

Do not introduce plugin hot-loading, custom loading contexts, dynamic catalog events, open-generic synthesis, JIT warm-up, static-initializer execution, source-generator infrastructure or unsupported publishing support. Temporary selected-asset resolver handling, if necessary, must remain local to preparation and be removed on all paths.

## Code quality

A developer should be able to read the factory from top to bottom. Use ordinary loops, dictionaries and focused helper methods. One statement per line; descriptive names; short error-handling branches. Avoid dense LINQ for loading/error flow, large positional constructors, broad provider/strategy hierarchies and classes spread over numerous partial files.

Do not mock away the hard CLR behaviour. Use real compiled fixture assemblies and fresh-process tests for loading, identity, unreferenced libraries and failure cases. Ensure test setup has not already loaded the fixture class being discovered.

## Evidence and reporting

Implement tests with each task. Track A01–A30 and keep AT01–AT21 traceability. Complete all six verification categories: unit, integration, mutation, architecture, backward compatibility and business cases. Report failures/skips honestly; no blanket exclusions or placeholders.

At each milestone report completed task IDs, changed files, commands actually run, tests/results, any observed contract issue and the next dependency-ready task. Do not mark a task complete solely because a class or test file exists. Do not silently broaden/rewrite the baseline to bypass a failure.

Before completion, test the exact packed runtime/build assets in fresh supported Windows/Linux host consumers; archive runnable tests/fixtures, API/reference binaries, manifest fixtures, performance observations, tool versions and artifact hashes. Preserve compatibility for future releases. Prepare the completion report; publishing to an external package feed requires separate maintainer authorization.
