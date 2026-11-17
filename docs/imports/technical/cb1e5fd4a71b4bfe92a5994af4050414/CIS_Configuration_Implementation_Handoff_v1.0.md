---
title: "Configuration supplied implementation handoff"
status: Draft
scope: Workspace
last_reviewed: null
cis:
  stable_id: cislib:spec:configuration-supplied-handoff
---

# CIS Configuration — Implementation Handoff

**Date:** 16 November 2026  
**Source:** [Regenerated Configuration specification v1.0](CIS_Configuration_Module_Specification_v1.0.md)  
**Purpose:** Import/reconcile the restored specification and organize the remaining Configuration work. This document does not itself grant a new execution budget or claim that implementation has run.

## Outcome

Deliver type-identified, retained configuration POCOs directly through DI. Discover them using the existing Type Manager, apply common values followed by host-application overrides, and refresh the same instances using checksums, coordinated updates and `ConfigurationChanged`.

The regenerated specification supplies the conversation-derived Configuration source that the 16 November alignment documents reported missing. Do not keep treating the entire source as unavailable. Preserve historical imported sources and reconcile a real conflict if one is found; do not invent earlier approvals for new recommendations.

## Start from existing work

Read the current applicable `AGENTS.md`, accepted decisions, dependency policy, public contracts and CIS records. Inspect the actual branch once. Use the installed CIS supplied-input workflow to register/reconcile this source and generate or update only materially affected work. Preserve completed tasks, actual costs/attempts, limits, accepted baselines and reusable evidence. Use actual supported commands, not fabricated CLI syntax.

Reuse `CIS.TypeManagement.TypeManager.CreateAsync`, `TypeManagerOptions(hostAssembly)`, `AssemblyManifestPath`, `PrecomputedContracts` and `ITypeManager.GetImplementations(Type)`/the generic extension. Reuse the `CIS.Core` SDK/inventory integration and existing Console/Web package-consumer examples. Do not reimplement discovery, restart TM-001–TM-036 or remove genuine product build integration as test tooling.

Retain the existing `CIS.Core` package/assembly, explicit `CIS.*` namespaces, `net10.0`, approved logging/DI abstractions and existing dependency policy. Inspect existing `CIS.Configuration` diagnostics before choosing new public names. Preserve EnvironmentOptions first-access defaults and recognized overrides; Configuration neither reinitializes nor redesigns it.

## Resolve the actual contract choices together

The main document distinguishes confirmed U requirements from P recommendations. Reconcile P01–P08 in the existing design/implementation task, using existing authority. The principal consumer-facing choices are:

**Initial-load timing (P01).** The recommended placement loads initial values after Type Manager but before constructing consumers, normally before container construction. Stage 1 then sees populated objects; Stage 2 activates/reconciles refresh under the accepted lifecycle. Loading only in the first Stage 2 start is a different contract: constructors see defaults and must not consume stored values. Do not promise both, hide async storage in a DI factory or build a temporary container.

**Persisted identity (P02).** Full type name and defining assembly are required. Assembly-version participation was not settled in the conversation. The specification recommends retaining assembly name/culture/token but omitting version in storage keys, while keeping exact CLR identity internally. Preserve any existing adopted storage keys until an explicit migration is authorized. Do not add fallback key guessing.

**Read/event contract (P04/P05).** A writer-only lock cannot guarantee atomic arbitrary POCO reads. Implement the selected shared-lock read helper and passive property contract, or surface the actual conflict before changing consumer semantics. An interface event cannot simply be invoked by an unrelated manager. The recommended small event helper/accessor pattern must support direct-interface POCOs without mandatory base-class inheritance, field-reflection tricks or proxies.

The remaining proposals define presence-aware merging, effective-value checksums, failure/event semantics and passive construction. Record their disposition once. Do not add one approval cycle for each test, documentation update or small supporting task.

## Deliver in coherent work packages

Use CFG-W1–CFG-W6 in §15 as an ownership map, not an instruction to create duplicate tasks. Keep executable tasks at most medium and incorporate their focused tests, documentation and review. After shared contracts, merge/mapping work and storage/refresh coordination can run in parallel with named, non-overlapping files. Queue shared solution/project/package changes through one owner and coordinate output directories. The controller arranges actual parallel execution; do not claim CIS does it automatically.

Demonstrate early:

- One real discovered type is registered once and reused across scopes and after a valid reload.
- A common property masked by an application override emits no event, but its latest value appears when the override is removed.
- A synchronized reader cannot observe a partial property update.

Then complete the real provider/scheduler/startup boundaries and affected package consumers. No production storage backend was selected. No polling interval was agreed. Use an approved existing value or explicit host/provider input rather than inventing a backend, environment variable or timer.

The reviewed sources did not establish an implemented generic startup coordinator or external Heartbeat scheduler. Verify actual availability; use their accepted contracts. Core operations can be tested against controlled boundaries while integration is pending, but a test double must not be described as production polling or startup completion. Do not develop a second scheduler or broaden this module into those dependencies.

## Verification and stopping rules

Use existing native C# MSTest and normal `dotnet`/IDE routes. Run affected builds/tests during edits, affected regression and existing review at coherent boundaries, and applicable mutation/platform/package/compatibility checks at the actual candidate/release boundary. Preserve all six library verification categories without rebuilding their tooling.

Do not introduce a Python C# test harness, custom assertion/runner framework, new mutation engine, giant evidence archive or unbounded retry loop. Reuse results whose material inputs remain current. Documentation/administrative refreshes alone are not test invalidation.

Checkpoint on a genuine contract conflict, missing mandatory integration, repeated failure without a new diagnosis, or an actual overall authority/resource limit. Report the concrete issue and completed work. Do not create a preliminary planning-approval chain or repeatedly ask questions already answered in the source.

The completion report identifies selected P decisions, U/CFG-A coverage, exact changed source, actual native results and reused evidence, package/integration status, and any precise remaining external dependency. It must not claim release readiness from documentation checks, test doubles or historic Type Manager results alone.
