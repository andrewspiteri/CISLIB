---
title: "Configuration supplied technical intent"
status: Draft
scope: Workspace
last_reviewed: null
cis:
  stable_id: cislib:spec:configuration-supplied-intent
  technical_intent_schema: 1
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# CIS Configuration Module — Regenerated Specification

**Version:** 1.0 — regenerated baseline  
**Date:** 16 November 2026  
**Target:** `net10.0`, within the existing `CIS.Core` package and assembly  
**Status:** Discussion requirements consolidated with current integration guidance. Proposed refinements are explicitly identified; this is not an implementation or release-completion record.  
**Companion:** [Implementation handoff](CIS_Configuration_Implementation_Handoff_v1.0.md)

## 1. Purpose and source of authority

The Configuration module discovers configuration POCO classes through the existing Type Manager, creates one retained live instance for each concrete configuration type, loads common and application-specific values into that instance, and keeps it updated. Application components request the concrete POCO directly through DI. A reload changes its values, not its identity.

This document reconstructs the Configuration specification from the user’s messages in this conversation and the dated 16 November 2026 Type Manager/Configuration alignment documents [S1–S4]. It supplies the consolidated source missing from the earlier alignment review. It does not claim to have recovered a separate historical Configuration document.

**U01–U12 below are requirements established in the conversation. Current integration constraints come from the dated alignment documents. P01–P08 are recommended refinements where the conversation did not settle the exact contract.** The detailed sections describe those recommendations without representing them as earlier user approvals. Reconcile any conflicting canonical decision before freezing an affected public contract; do not restart a questionnaire about settled requirements.

This revision is documentation work. No repository checkout, source modification, native build, test execution, CIS import or release qualification was performed to produce it. Repository status statements below report the dated source documents, not a fresh audit of repository heads.

## 2. Confirmed requirements

| ID | Requirement established in this conversation |
|---|---|
| U01 | Configuration values map into ordinary configuration POCO classes. |
| U02 | The concrete POCO type is the configuration identity; callers do not supply an independent logical configuration name. |
| U03 | `DEF` represents the full configuration type name including its defining assembly. |
| U04 | `ABC` represents the running application’s host project name. |
| U05 | Each configuration type has one retained live instance per owning application/container. DI resolutions of that concrete type return that same instance across scopes. |
| U06 | Configuration classes implement `IConfigurationObject` and expose one event named `ConfigurationChanged`. The later spelling is authoritative for this specification; do not publish a second misspelled marker. |
| U07 | Configuration startup discovers the configuration types, retrieves applicable stored configuration, constructs the objects with their defaults and maps stored values when present. |
| U08 | Common configuration is shared across microservices; application-specific values override the corresponding common values. The logical locations are `CommonConfiguration/DEF` and `ABC/DEF`. |
| U09 | Background refresh uses polling or storage change notifications, according to backing-store support. |
| U10 | Retrieved configuration is checked using a checksum; detected changes cause remapping of the retained object. |
| U11 | Remapping uses a lock to protect the update, and `ConfigurationChanged` is raised after the change. The read-side requirements needed for atomic observation are addressed explicitly in §10. |
| U12 | Configuration queries the Type Manager for implementations of `IConfigurationObject`. Type Manager assembly preloading and catalog preparation complete before container construction. |

The same-instance requirement concerns the live root POCO delivered to consumers. Internal candidates and detached value copies are permitted; they must never replace the root registered in DI. Identity is process/host-local, not one shared object across multiple running microservice instances.

## 3. Latest integration constraints

The 16 November alignment documents supersede outdated implementation guidance in the Type Manager v0.1/v1.0 planning material [S1–S4].

| Area | Current integration requirement |
|---|---|
| Type Manager | Reuse the implemented `CIS.TypeManagement` API, completed catalog, package SDK and inventory generation. Do not restart its historical 36-task plan or scan assemblies again in Configuration. |
| Startup | Type Manager preparation precedes container construction. Stage 1 wires and constructs the scheduled root services. Stage 2 awaits registered `IStartable` work in DI registration order. Readiness follows successful Stage 2 completion. |
| Healthcheck | Preserve `HealthcheckService.MarkStartupCompleted()` and `BeginStopping()` ownership. Application-wide completion belongs to the startup coordinator, not Configuration. |
| Dependency availability | The reviewed sources did not establish an implemented generic startup coordinator or external Heartbeat scheduler. Treat their agreed behaviour as an integration contract, not proof that a callable API exists. |
| Packaging | Configuration is a logical module in the existing single `CIS.Core` shipping package/assembly, with explicit `CIS.*` namespaces. Preserve existing `CIS.Configuration` diagnostic types and avoid naming collisions. |
| Runtime dependencies | Follow the approved Core closure, including the existing logging and DI abstractions and approved `Microsoft.AspNetCore.App` policy. This is neither a new ban on those approved references nor blanket approval for Microsoft-prefixed packages. Add no new runtime dependency without an actual requirement and approval. |
| EnvironmentOptions | Preserve its accepted first-access defaults, recognized environment overrides, validation and bounded identity interning. Configuration neither reinitializes it nor adds storage, discovery or scheduling responsibilities to it. |
| Delivery | Use current CIS supplied-input/impact workflows and existing native C# MSTest. Keep tests, documentation and review with coherent implementation outcomes. Preserve completed work, valid results and finite authority limits. |

The reviewed Type Manager material records scoped package-consumer success, not complete release qualification. Consuming it does not authorize repeating every historical test campaign.

## 4. Recommended refinements requiring explicit contract closure

These recommendations make the specification usable for implementation planning. They are not silently promoted into historical user decisions.

| ID | Recommended v1 refinement | Why it matters |
|---|---|---|
| P01 | Complete initial Configuration loading before configuration-consuming constructors execute; use explicit async bootstrap after Type Manager and before container construction as the straightforward placement. Activate ongoing refresh through the accepted startup/lifetime integration. | The new two-stage startup constructs services before calling their start methods. Loading only in Stage 2 cannot guarantee populated values in Stage 1 constructors. |
| P02 | Persist the full type name plus assembly identity **without assembly version**, while retaining the complete CLR identity internally. | Including assembly version in a storage key makes an ordinary assembly-version change select a different configuration record. The user required the assembly, but did not resolve version participation. |
| P03 | Use presence-aware property overlays; merge nested POCO members recursively; replace collections/dictionaries; distinguish omission, explicit null and deletion. Use a structured JSON value model internally, not a mandated storage product. | Prevents default `false`/`0` values and removed overrides from being misinterpreted. |
| P04 | Guarantee consistent multi-property reads through a short synchronous shared-lock read helper. Support passive, nonthrowing data-property assignments; preserve the root, not every nested reference. | A writer-only lock does not make arbitrary unsynchronized POCO reads atomic. |
| P05 | Use a typed change event with a local revision, backed by a small explicit event-publication helper; an optional base class can hide the boilerplate. | An interface event supplies subscription syntax, not permission for an unrelated manager to invoke an arbitrary implementation’s event. |
| P06 | Separate source revision tokens from an effective-value checksum; use SHA-256 over deterministic mapped values as the proposed effective checksum. | Source changes hidden by overrides should not produce false effective-change events. |
| P07 | Initial load establishes a baseline without a change event. Invalid refreshes keep the last accepted values. Notifications follow successful commits and isolate subscriber exceptions. | Makes success, failure and event meaning unambiguous. |
| P08 | Automatically construct eligible passive POCOs through a usable parameterless constructor; allow a narrowly scoped explicit factory for exceptional types. Capture defaults once, and validate detached candidates before updating the live instance. | Preserves automatic discovery without activating arbitrary application services or recomputing defaults on every refresh. |

P01, P02 and P04/P05 affect consumer-facing guarantees and deserve explicit resolution before public contracts or persisted keys are frozen. Other refinements can be recorded with them in the existing coherent design task; they do not each require a separate approval workflow.

No production database, message broker, polling interval, stale-age threshold or extra configuration scope is selected here. Those values are not established by the source conversation.

## 5. Identity, scope and registration

### 5.1 Runtime identity and application identity

The in-process registry is keyed by the actual CLR `Type` returned by the completed Type Manager. Do not use short class names, string aliases or a named `Get<T>("DEF")` API to identify configurations.

The host supplies `ABC` once from its established host project/application identity. Do not derive it from the configuration’s library, machine name, replica, request tenant or whichever assembly happens to be executing. If the compiled assembly name differs from the host project name, bootstrap must supply the established host identity explicitly; the module cannot reconstruct a project filename from an arbitrary runtime library.

`EnvironmentOptions.ApplicationName` has recognized override behaviour in the current library. Do not silently substitute an overridden value for the user’s fixed host identity, or build another identity-resolution subsystem [S1, S3]. Capture the selected host identity at initialization; do not move to a different storage scope during reload.

The common namespace is reserved: a host identity colliding with `CommonConfiguration` must be rejected, not merge both levels into one location.

### 5.2 Persisted identity: recommendation, not a hidden decision

.NET’s `AssemblyQualifiedName` contains assembly identity information, including version in ordinary runtime representations [T3]. Type Manager retains that exact identity. It does not decide Configuration’s durable key policy.

Under P02, Configuration derives a stable key from the full type name and defining assembly name/culture/public-key token, omitting only the assembly version. For example:

```text
DEF = Shared.Configuration.PollerConfiguration, Shared.Contracts, Culture=neutral, PublicKeyToken=null
ABC = CustomerService
```

The logical records are then:

```text
CommonConfiguration/{DEF}
CustomerService/{DEF}
```

Pass scope and type identity as structured adapter inputs. A storage adapter handles physical escaping or separate database columns; slashes in the examples do not require a filesystem implementation.

Use deterministic, case-sensitive logical identities. Reject collisions between distinct discovered types. Do not silently try short names, both versioned and unversioned keys, or several scopes until something matches. If a prior persisted key scheme already exists, preserve it until an explicit migration is agreed. A type or assembly rename remains an identity change even under version-independent keys.

### 5.3 DI registration

Create the retained root once and register **that object** under its concrete type. A factory invoked for each scope, a newly deserialized replacement, `IOptionsSnapshot<T>` or a wrapper in place of the concrete POCO does not satisfy U05.

All components using the same root container observe the same object. Separate application hosts have separate catalogs of configuration instances, even when hosted in one process. Do not use one process-global dictionary keyed only by `Type` to share live values across hosts.

Reject conflicting pre-existing registrations for a discovered configuration type instead of silently creating multiple effective singletons. Any optional interface alias must point to the same retained object. DI resolution is in-memory and does not read storage, validate an ETag, scan assemblies or start a worker.

## 6. Discovery, construction and defaults

Use the prepared dependency through its existing contract [S1, S3]:

```csharp
IReadOnlyList<Type> candidates =
    typeManager.GetImplementations<IConfigurationObject>();
```

The Type Manager’s general query includes inherited implementations, nonpublic classes and classes lacking a usable configuration constructor. Configuration owns eligibility; do not change Type Manager to hide unsupported candidates.

Under P08, each eligible type is a concrete closed class with passive mapped properties and an approved construction path. Ordinary types use a usable parameterless constructor. An explicit per-type factory can cover an exceptional class; it must return an instance of the exact discovered type with default values, not perform a second storage lookup or build/resolve a DI container. Do not silently omit an incompatible discovered configuration: identify the type and construction/mapping problem.

The initial live object’s constructor and property initializers establish its defaults. Capture a detached default value representation before applying stored overrides. Every candidate is rebuilt from those same captured defaults; a removed property must not retain an old override or acquire a newly generated timestamp merely because a refresh occurred.

Temporary typed candidates are private working objects. Never copy delegates, locks, event subscriptions, diagnostic state or runtime resources from them into the retained POCO. Configuration classes and their mapped child objects are data holders, not service containers: constructors and accessors must not perform I/O, start timers, publish events or resolve other configuration services.

Under the proposed simple mapping profile, use public instance read/write properties with supported data types. Exclude static properties, indexers, events and runtime bookkeeping. Detect unsupported required property shapes before making the object available to consumers. Init-only/get-only objects, polymorphic activation, executable setters and cyclic graphs require an explicit different contract; do not quietly build a generic object-mapping framework to support them.

## 7. Two-level composition and validation

There are exactly two stored levels. POCO defaults are fallback values, not a third remote scope.

```text
Effective values = captured POCO defaults
                   overlaid with CommonConfiguration/DEF
                   overlaid with ABC/DEF
```

### 7.1 Presence and replacement semantics — P03

| Input at the higher-precedence level | Proposed result |
|---|---|
| Property omitted | Preserve the value from the lower-precedence level. |
| Explicit `false`, `0`, empty string or empty collection | Override the lower-precedence value; do not treat it as absent. |
| Explicit `null` | Set null if the mapped property contract permits it; otherwise reject the candidate. Null is not inheritance syntax. |
| Nested POCO object | Merge explicitly present named members recursively. |
| Array, list or dictionary property | Replace the complete property value. No append, index merge or per-entry deletion language. |
| Override property removed | Rebuild from the current lower-precedence values and captured defaults. |
| Entire application record confirmed deleted | Rebuild from current common values and defaults. |
| Entire common record confirmed deleted | Rebuild from defaults and the current application record. |
| Both records confirmed absent | Use captured defaults, subject to normal validation. |

Parse each record as a structured property map so that omission remains distinguishable from an explicitly supplied default value. Do not deserialize each level into an ordinary default-initialized POCO and then guess which values were present.

Proposed property matching is ordinal using the declared serialization/property names. Reject duplicate keys and ambiguous mappings. Unknown properties are ignored with bounded name-only diagnostics, not included in effective-value change detection; this supports compatible rolling additions. Do not silently coerce incompatible values. Preserve any stricter already-adopted schema policy when reconciling an existing contract.

For a nested object supplied over a null lower-level value, create the candidate child from that declared child type’s data defaults and then apply present members. Do not merge against a stale child still referenced by a consumer. A malformed record or failed known-property conversion rejects the candidate; it is not treated as a missing record.

### 7.2 Worked example

| Property | POCO default | Common record | ABC record | Effective in ABC |
|---|---:|---:|---:|---:|
| `PollIntervalSeconds` | 30 | 60 | 15 | 15 |
| `TimeoutSeconds` | 5 | 10 | absent | 10 |
| `Enabled` | true | true | false | false |

If common interval changes to 90 while ABC still overrides it with 15, the effective interval stays 15. If ABC subsequently removes its override, the interval becomes 90, not 60 or 30.

### 7.3 Candidate validation

Convert and validate the complete detached candidate before entering the live-object write section. Include property-level type/range checks and any registered configuration-specific cross-property validation. Use simple existing validation facilities or a small per-type validator; no new validation language or rules engine.

A failed initial candidate prevents successful Configuration initialization. A failed runtime candidate leaves accepted live values, effective checksum and revision unchanged. Validation is allowed to report property paths and error categories, not secret values.

Using `JsonSerializer.Deserialize<T>` and swapping its result into DI is not a retained-instance implementation. Likewise, `JsonObjectCreationHandling.Populate` does not by itself implement this module’s merge/replacement rules; its documented collection behaviour can accumulate values [T4]. Build the candidate separately, then perform the controlled in-place root update.

## 8. Storage boundary

Configuration owns a small storage abstraction. Its actual interface names are to be reconciled with existing `CIS.Configuration` names before publication; the following is a behavioural contract, not a claim of installed APIs.

A provider can read the common/application records for a structured `(scope, configuration identity)` request, accepts cancellation, and returns an owned immutable payload or a detached copy. Results distinguish:

| Result | Meaning |
|---|---|
| Found | A complete readable record, optionally with a source checksum/version/ETag. |
| Missing | The store successfully confirmed that no record exists. |
| Unchanged | A conditional read confirms the exact cached source record remains current. Valid only when that cached record is available. |
| Failure | Timeout, authorization, unavailable storage, parse/read failure or another unsuccessful operation. Never equivalent to Missing. |

A provider reports whether it supports usable change notifications. Notifications identify affected keys or invalidate a scope; they request a reread rather than directly supplying authoritative live-object assignments. Creation and deletion are changes, too. A store-wide/common notification must be routed only to relevant discovered types.

Opaque source tokens are compared for equality unless the provider explicitly supplies ordered revision semantics. Serialize refreshes to avoid locally racing old reads, but do not claim this repairs a store that returns stale snapshots. Provider consistency guarantees must be documented and tested by its adapter.

The two scopes are not automatically one cross-record transaction. Without a provider snapshot guarantee, the effective candidate combines the successfully read versions observed in that refresh and converges through subsequent refreshes. This module does not promise atomic changes across common/application records, multiple configuration types or replicas.

No concrete production store is selected by this specification. Core behaviour and adapter contract tests can proceed with a controlled in-memory test double. A production adapter is a separately explicit dependency/deliverable, not something an agent should guess. Storage writes, admin UI, migration engines, audit databases and secret-management services are outside this read/refresh module.

Bootstrap credentials, addresses and application identity must be available without first loading the configuration that depends on them. Avoid the cycle “connect to configuration storage using a configuration obtained from that storage.”

## 9. Initial loading and the accepted startup sequence

The latest startup agreement constructs scheduled root services in Stage 1 and invokes their asynchronous startup methods in registration order in Stage 2 [S1, S3]. Registration order cannot make a Stage 2 load happen before a constructor that has already executed.

**Recommended placement under P01:**

| Boundary | Configuration-related work |
|---|---|
| Bootstrap: Type Manager | Await the existing Type Manager preparation, before constructing the container. |
| Bootstrap: initial Configuration preparation | Query types, create retained objects/capture defaults, read both scopes, compose and validate candidates, apply initial values and establish checksums. No consumer receives a partial result. |
| Registration/container construction | Register the already-prepared Type Manager, Configuration runtime and each retained concrete POCO; construct one root container. |
| Stage 1 | Construct the scheduled services. Their constructors can receive already-populated configuration objects. |
| Stage 2 | Invoke registered startables in DI order. Configuration’s lifetime integration establishes refresh subscriptions/scheduling and reconciles changes since bootstrap before dependent start work that needs that guarantee. |
| Readiness | The startup coordinator marks completion only after all required Stage 2 work succeeds, preserving other Healthcheck conditions. |

Initial preparation is explicit asynchronous host work, not I/O hidden in a constructor or a DI factory. It does not require an extra container, a competing startup coordinator or changes to Type Manager. The host supplies a bootstrap-capable storage adapter and owns externally supplied dependencies.

Activate notifications with a subscribe-and-reconcile handshake: establish observation, reread current records and process any invalidation received during that reread. An event between the first load and activation must not be lost. In polling mode, reconcile on activation rather than waiting a full polling interval for startup changes. Heartbeat admission still follows its accepted readiness/lifecycle rules; a direct startup reconciliation is not a second periodic scheduler.

The earlier assistant suggestion “load only after container construction, then resolve all dependent services” is not compatible with blindly constructing all startables first. It is not silently carried forward. If the chosen integration instead loads in Configuration’s first Stage 2 start, the public contract must explicitly say constructors get defaults and may only retain the reference; loaded values are guaranteed for later start methods, not constructors. **That alternative is not implemented as a second automatic mode by this proposal.** Resolve P01 once.

Initial loading establishes a baseline; P07 proposes no initial `ConfigurationChanged` event. A later effective change detected during activation does produce the normal event. Neither an initial event nor a subscriber is required for a component to read its initialized values.

Any initial storage, mapping or validation failure prevents successful startup under the proposed baseline. Confirmed absence is different: valid defaults may be used. Do not silently enter a defaults-only/offline mode after a failed read. Cleanup disposes only acquired resources owned by Configuration; no application-ready signal is emitted.

## 10. Retained-instance updates and atomic reads

### 10.1 Scope of the guarantee

A C# lock coordinates threads acquiring that same lock; it does not stop unrelated code reading ordinary properties [T1]. Therefore copying several properties under a writer lock alone cannot guarantee a consistent multi-property observation by all consumers.

Under P04, each retained configuration entry owns one private read/write synchronization gate. Consumers needing consistent settings read them through a short synchronous helper using that gate. An illustrative proposed helper shape is:

```csharp
public interface IConfigurationReader
{
    TResult Read<TConfiguration, TResult>(
        TConfiguration configuration,
        Func<TConfiguration, TResult> reader)
        where TConfiguration : class, IConfigurationObject;
}
```

The caller still injects the concrete POCO. The reader supplies synchronization, not a replacement named configuration lookup:

```csharp
var values = configurationReader.Read(
    configuration,
    current => (current.PollIntervalSeconds, current.TimeoutSeconds));

// Perform asynchronous work using the copied values after the read completes.
```

Only the exact retained instance belonging to that reader’s Configuration runtime is accepted. A detached candidate or object from another host is an error. Do not lock publicly accessible `Type`, string or POCO objects, and do not hold the gate while calling storage, awaiting, logging or invoking events.

The read callback must not modify configuration, perform I/O, request a synchronous refresh, acquire another configuration’s read lock or return a live mutable child for later use. Copy needed scalars or detach a collection inside the callback. The guarantee is one configuration at a time, not an atomic read across different types.

Ordinary uncoordinated property access remains syntactically possible, but it has no promised cross-property consistency or synchronization/visibility guarantee during refresh. Do not advertise transparent atomic POCO reads. A stronger transparent guarantee would require a different property/backing-state design and explicit authorization, not a proxy or source generator introduced unnoticed.

### 10.2 Commit behaviour

Prepare all converted values and detached child/collection replacements before acquiring the gate. Under the gate, copy mapped data into the retained root and update its accepted checksum and local revision. Release the gate before notifying subscribers.

The module is the only supported writer. Nested references may be replaced; event subscriptions on the root remain intact. Consumers must not mutate properties/collections or cache nested references expecting automatic replacement tracking.

Supported mapped setters must be passive and nonthrowing. Validating a temporary candidate does not make arbitrary user-defined setters transactional. An unexpected live setter failure is an unsupported/broken configuration contract: do not report success, advance the revision, raise a successful-change event or claim the old values were necessarily restored. Stop that entry’s updates and surface the fault through existing diagnostics/health integration. Do not disguise a partial assignment with an unverified best-effort rollback. No custom IL-analysis or transactional property framework is required to support executable setters; they are outside the simple POCO contract.

## 11. Checksums and refresh algorithm

### 11.1 Source state versus effective state — P06

Retain source tokens/payloads separately from the last accepted effective-value checksum. A source may change without changing the values used by this application. Refresh bookkeeping must retain the latest successful common record even when an application override masks its change.

Compute the proposed effective checksum over the complete mapped, validated candidate, not source JSON formatting, source revision identifiers or the live object’s event machinery. The deterministic representation includes property identity, typed value, explicit nulls and collection order; object/dictionary keys are consistently ordered. Equivalent mapped values must have the same representation. Specify numeric/date/enum representation through the fixed mapping contract and cover it with tests; do not depend on reflection enumeration order, ambient culture or default `GetHashCode`.

SHA-256 is the proposed algorithm, not a previously selected user requirement. It is a change-detection mechanism, not proof of authenticity or authorization. Do not expose secret-derived payloads or hashes unnecessarily in diagnostics.

A configuration type’s mapping/checksum format is fixed for the running host. A compatible deployment may recompute its initial baseline; a checksum is not the persisted configuration identity.

### 11.2 One refresh path

Polling, storage notifications, reconnect reconciliation and any explicitly authorized refresh request use the same path:

1. Admit one refresh for the configuration type; coalesce concurrent triggers into a pending-change flag.
2. Retrieve/revalidate both source records. Unchanged results are usable only with the corresponding retained source state; a failed read cannot become an empty override.
3. Build the complete detached candidate from captured defaults and the current successful source records.
4. Convert and validate all mapped values. Compute its deterministic effective checksum.
5. If effective values are unchanged, retain successful source bookkeeping and freshness information without remapping, advancing the revision or raising an event.
6. Otherwise acquire the entry’s gate; check stopping/cancellation at the commit boundary; copy prepared values and accept the new checksum/revision together.
7. Release the gate and publish one notification for that accepted revision.
8. Finish the serialized attempt. If another trigger arrived during it, perform/schedule a further reconciliation through the same existing execution path.

Serialization covers retrieval through commit and ordered notification, not just the copy operation. An older local refresh cannot overwrite a newer local result. A late signal cannot be lost between “check pending” and returning idle; implement that handoff under the entry’s coordination state.

Bound pending work by discovered entries rather than queuing one job per notification. Coalescing may skip intermediate store revisions; the contract is convergence to valid current values, not replay of every stored edit.

A failed refresh retains the last accepted live values and records a diagnostic. A new invalid source token must not be marked as successfully applied: later conditional reads of the same invalid source cannot incorrectly clear the error without validation. Retry on subsequent legitimate triggers/refreshes; do not create an immediate unbounded retry loop.

Cancellation before commit leaves the live object unchanged. Cancellation after commit does not undo the accepted change or silently suppress its notification. Forced thread termination, rollback after process failure and durable event delivery are not promised.

## 12. Change notification contract

### 12.1 Proposed public event — P05

The only event required on a configuration object is `ConfigurationChanged`. A proposed signature is:

```csharp
public interface IConfigurationObject
{
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
}
```

The proposed event arguments carry the configuration CLR type and a monotonically increasing **local** revision. They do not imply a global store version or contain secret values, full before/after snapshots or a lifecycle state machine. Final names/signatures must be checked against existing Core public names before release.

Declaring an interface event does not allow the Configuration service to invoke an arbitrary class’s private event publisher [T2]. This is a real implementation requirement, not something to solve by locating compiler-generated backing fields.

P05 recommends a small library event helper with per-instance handler storage. A direct POCO can forward its event’s `add`/`remove` accessors to that helper. An optional `ConfigurationObject` base class can implement the same delegation. The module publishes through its internal side of the helper. Use weak/per-instance ownership as appropriate so subscriptions do not create a process-global lifetime leak.

This keeps the interface’s single event, permits direct interface implementation without compulsory inheritance and permits subscriber-by-subscriber exception isolation. It does require the documented accessor/base-class pattern: an arbitrary field-like event with no publication connection is not automatically compatible. Detect/report incompatible event wiring during integration rather than discovering objects that can never notify. Settle this small contract before freezing the marker; no dynamic proxies, event-field reflection, source generator or second event bus.

### 12.2 Event semantics — P07

The sender is the retained POCO. Publish exactly one event per successfully applied effective change, after releasing the value gate. No event is emitted for an unchanged effective result, invalid candidate, failed read or cancelled pre-commit refresh. Initial population establishes the baseline rather than a reload event.

Take a subscriber snapshot safely, invoke in subscription order and catch/report each synchronous subscriber exception independently. One failed handler must not roll back committed values or prevent later handlers from being attempted. Subscribers unsubscribe through normal event semantics.

Notifications for one configuration are ordered with its serialized refreshes; no ordering is promised across configurations. A handler may use the synchronized reader. It must not synchronously wait for another refresh of the same entry. A trigger raised by a handler marks pending work rather than entering refresh recursively.

Handlers must be brief and synchronous; `async void` continuation failures and indefinitely blocking subscriber code cannot be made safe by a surrounding synchronous catch. Asynchronous follow-up belongs to the consumer’s existing work mechanism. The module does not create unbounded per-handler tasks or promise to forcibly abort handlers.

An event means “an accepted effective change occurred; read the current configuration.” It is not a durable message, acknowledgment protocol or historical-value snapshot. A subscriber that uses values later must capture them appropriately. Components remain responsible for applying settings to their own running timers, clients or caches; mutating a POCO does not automatically reconfigure those resources.

## 13. Background refresh, health and shutdown

A notification-capable provider uses its existing storage subscription mechanism. A polling-only provider uses the accepted Heartbeat integration. Both observe common and application records, including record creation and deletion, and invoke §11 rather than maintaining different merge/update implementations.

Do not add `Timer`/`PeriodicTimer` loops, a second scheduler or one permanent thread per configuration merely because the external Heartbeat implementation is not yet available. Implement/test the refresh operation and its integration boundary using existing controlled test facilities; report the exact pending runtime dependency honestly. A test double is not proof that production polling works.

No default polling interval was agreed. Reuse an established approved setting or require an explicit positive host/provider interval where polling is selected; do not invent an environment variable, timeout policy or retry campaign. Event mode must disclose whether its watch is reliable/replayable or needs an agreed periodic reconciliation. Reconnect always reconciles; an adapter cannot silently promise eventual convergence after missed events without a recovery mechanism.

Use the existing non-overlap, cancellation and capacity behaviour of Heartbeat when that integration is available. Configuration maintains per-type refresh serialization; this is distinct from the application-wide scheduler’s concurrency limit. Provider callbacks signal bounded pending work and do not block while remapping objects.

Expose compact status through existing diagnostics/Healthcheck integration: initial-load outcome, last successful reconciliation, last accepted change/revision and current refresh failure. Never log complete configuration records or credentials. Use existing duplicate suppression for repeated failures.

The application/service retains its agreed health-criticality policy. This document does not invent a universal stale-age threshold or mark the application ready from Configuration. A successful unchanged read can restore refresh freshness; an unchanged-but-invalid source cannot. Recovery of one module does not override other Healthcheck failures.

On stopping, close refresh admission, detach owned subscriptions, cancel owned polling registration and drain owned in-flight work through the accepted bounded lifetime mechanism. Coordinate stop and commit admission so no new commit starts after stopping wins that boundary. A commit already accepted completes its notification. Dispose only owned resources, do not dispose host-owned adapters or user POCOs unexpectedly, and never replace a retained POCO with null/defaults at shutdown. This module cannot guarantee a finite drain of arbitrary noncooperating provider or subscriber code; preserve and report the host’s existing timeout behaviour.

## 14. Acceptance scenarios

These are behavioural scenarios, not a mandate for one test or task per row. U requirements are established; tests exercising P refinements apply when those refinements are selected. Use real native C# implementation tests and existing fixture/test tooling.

| ID | Observable acceptance |
|---|---|
| CFG-A01 | A deployed, initially unused library’s direct and inherited configuration types are obtained through the existing Type Manager; Configuration performs no assembly scan. |
| CFG-A02 | Two consumers and two DI scopes receive the same root for one type; a different application host gets a different root. |
| CFG-A03 | The full type/assembly identity distinguishes same-named types in different assemblies; the agreed version-policy test is explicit. |
| CFG-A04 | The supplied host identity selects only its application override; an EnvironmentOptions display/override value cannot silently redirect it. |
| CFG-A05 | Neither source present, common only, application only and both present produce the specified default/override values. |
| CFG-A06 | Explicit false, zero, empty string, empty collection and allowed null override correctly; omission inherits. |
| CFG-A07 | Nested POCO overlays and collection/dictionary replacement follow the chosen contract without accumulating old items. |
| CFG-A08 | Removing an override/property/record restores current lower-level values; captured defaults do not change between refreshes. |
| CFG-A09 | Unknown-property/duplicate-key/unsupported-value handling follows the agreed mapping policy and does not expose secret data. |
| CFG-A10 | An invalid candidate or unsuccessful storage read preserves accepted root values, checksum and revision; an initial failure blocks successful startup. |
| CFG-A11 | A hidden common change emits no event but is reflected when the overriding application property is later removed. |
| CFG-A12 | Formatting/property-order-only differences and identical mapped values cause no remap/event; a real mapped change does. |
| CFG-A13 | Existing references and event subscriptions survive accepted updates; the root is never replaced by a candidate or a deserializer. |
| CFG-A14 | Coordinated readers cannot observe mixed old/new property sets; a deliberately slowed copy proves the synchronization boundary. No atomicity claim is made for raw reads. |
| CFG-A15 | Concurrent triggers cannot let an older local read overwrite newer accepted values; a trigger during refresh receives a subsequent reconciliation. |
| CFG-A16 | Subscription activation/reconnect covers changes in the initial-read/watch gap, including deletion and invalidation during reconciliation. |
| CFG-A17 | Polling and notification triggers feed the same refresh logic; production integration uses the actual accepted scheduler/provider rather than a hidden replacement. |
| CFG-A18 | One accepted change produces one after-commit event; unchanged/failed/pre-commit-cancelled attempts and initial baseline produce none under P07. |
| CFG-A19 | One throwing synchronous subscriber does not prevent later subscribers; a handler can read safely and cannot reenter refresh synchronously. |
| CFG-A20 | Event publication works for the documented direct-interface accessor pattern and optional base; unsupported arbitrary publishers are not silently accepted. |
| CFG-A21 | Under P01, a Stage 1 constructor observes populated configuration; Stage 2 registration order and application-wide Healthcheck completion remain unchanged. |
| CFG-A22 | Cancelling before commit leaves state unchanged; cancellation after commit cannot remove an accepted notification. |
| CFG-A23 | Stopping prevents newly admitted commits/triggers, releases owned registrations and does not dispose host-owned dependencies or change live values. |
| CFG-A24 | An invalid source followed by Unchanged conditional reads remains invalid until a candidate is successfully validated; failed reads never act as deletion. |
| CFG-A25 | Per-type/host isolation, bounded trigger coalescing and detached collection reads hold under concurrent activity without unbounded queued jobs. |
| CFG-A26 | Conflicting registrations, unusable constructors, unsupported mapping/event shapes and key collisions fail with useful type-specific diagnostics. |
| CFG-A27 | Existing Core package consumers, namespace/dependency policy and compatibility baselines remain valid; Type Manager and EnvironmentOptions behaviour is not regressed. |
| CFG-A28 | Production adapter and Heartbeat/startup integration status is reported separately from core/test-double success; no unrun integration is described as passed. |

Tests should coordinate operations deterministically using the existing native facilities, not long sleeps, enormous data sets or invented execution frameworks. Secret-safe diagnostics, actual supported-platform/package checks and relevant compatibility fixtures belong at their existing acceptance/release boundaries.

## 15. Completion roadmap and implementation boundaries

The following six work packages are an ownership/dependency map, not six approvals or six new CIS records regardless of existing work. Reconcile existing tasks first. Keep executable tasks at most medium; split only where real scope or independent ownership requires it. Supporting tests, documentation and review stay with the change.

| Work package | Deliverable and exit condition | Dependencies / safe parallel work |
|---|---|---|
| CFG-W1 — Contracts and retained objects | Reconcile current source/public names and P decisions; provide discovery eligibility, retained-instance registration, default capture, read/event primitives and focused native tests. Public assumptions and existing dependencies are explicit. | First. One owner handles shared public contracts/project edits. |
| CFG-W2 — Effective-value pipeline | Implement presence-aware merge, mapping/validation, deterministic effective checksums and prepared value commits. Demonstrate defaults, override removal, hidden common changes and reader consistency. | W1; can proceed beside W3 with separate files. |
| CFG-W3 — Storage and serialized refresh | Implement provider read results, source bookkeeping, bounded trigger admission and the common serialized refresh path against the agreed contract. Exercise failures, conditional reads and race scenarios. | W1; can proceed beside W2 using a controlled mapping seam. |
| CFG-W4 — Bootstrap and DI integration | Combine W1–W3, demonstrate the selected initial-load guarantee, concrete singleton identity and correct two-stage startup/Healthcheck boundary in the existing consumer setup. | W1–W3. No new container framework. |
| CFG-W5 — Runtime refresh/lifetime integration | Bind notification and accepted Heartbeat triggers, activation/reconnect reconciliation, health status and stop cleanup. Demonstrate real integration where dependencies exist; identify exact external gaps otherwise. | W1/W3; adapter work can overlap W2/W4 under named ownership. |
| CFG-W6 — Qualification and handoff | Reconcile acceptance coverage, affected regression and actual package consumers; document contracts and unresolved external integration accurately; remove disposable work output. | Integrates prior work; acceptance test design can accompany earlier tasks. |

No production store adapter is implicit in W3. Implement an actual adapter only when its backend and authority are known; a required missing adapter is reported as a dependency, not silently replaced by a chosen database or filesystem.

Use current CIS import, planning, impact and validation commands discovered from the installed version. Preserve source identities, completed work, limits, costs and accepted results. Documentation/administrative changes alone are not a reason to relaunch unchanged tests. Parallel workers require named non-overlapping file ownership and coordinated shared build outputs; CIS’s existence is not proof that it automatically schedules every independent task.

Preserve the library’s unit, integration, mutation, architecture, backward-compatibility and business-case obligations. During edits run affected builds and native MSTest cases; at coherent boundaries run affected regression/review; use release-level gates at their actual feature/release boundary. Do not implement a Python C# harness, replacement test runner, new mutation framework or large evidence archive. Retain compact results and necessary diagnostics. Public compatibility begins with the actual first published contract; do not invent a historical release baseline.

**Definition of complete:** agreed requirements and selected P decisions are implemented; retained identity, mapping, failure and synchronization scenarios pass; actual required storage/scheduler/startup integration is demonstrated or explicitly held as an external dependency; documentation and package integration match shipped behaviour; applicable gates are satisfied with current evidence. A specification, fake-provider test or successful Type Manager check alone does not establish Configuration completion.

## 16. Explicit exclusions

This module does not implement Type Manager again, extend its fixed catalog, construct a second DI container, orchestrate the application startup pipeline, create another scheduler, dynamically generate proxies, implement arbitrary object graphs/setter transactions, add customer/environment/tenant scopes, update remote configuration, migrate keys automatically, rotate secrets, create an administration UI or provide a distributed configuration transaction/event log.

The initial profile follows Core’s currently qualified .NET 10 deployment boundaries. Do not claim trimming, Native AOT, plugin hot-loading or unsupported publishing modes merely because the basic POCO example compiles.

## 17. References and provenance

### Project sources used

- **S0 — This conversation’s user requirements.** Concrete POCO identity; defining assembly; host project identity; same-instance DI; `IConfigurationObject`; initial defaults/storage mapping; two scopes; checksum/lock/event refresh; Type Manager before container construction.
- **S1 — `CIS_Type_Manager_Definition_v1.1.md`, 16 November 2026.** Implemented dependency, current boundaries, two-stage startup, EnvironmentOptions and dependency policy. Particularly §§2, 3 and 12.
- **S2 — `CIS_Type_Manager_Roadmap_and_Tasks_v1.1.md`, 16 November 2026.** Current status reconciliation, examples and proportionate verification rather than restarting historical tasks.
- **S3 — `CISLIB_Configuration_Alignment_Handoff_2026-11-16.txt`.** Retained Configuration ownership; current Type Manager APIs; application identity; startup/Healthcheck integration; CIS execution guidance; missing historical detailed-source statement.
- **S4 — `CIS_Type_Manager_Codex_Handoff_v1.1.md`, 16 November 2026.** Actual dependency surface, current packaging, native MSTest and bounded work organization.

S1/S3 report their review against CIS `75283190777ad691811ade6a7eff247a74105728`, CISLIB main `060b2f593b3c99e39d3682286311cd2d9d400a9f`, and continuation `099a25f202935d14ee4c396abd51ebbff6c1e4f1`. Those are the source documents’ pins, not commits re-reviewed here. Current implementation work must inspect its actual branch without resetting legitimate later changes.

### Primary .NET references

- **T1 — C# lock semantics:** https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock
- **T2 — Interface event implementation:** https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/events/how-to-implement-interface-events
- **T3 — `Type.AssemblyQualifiedName`:** https://learn.microsoft.com/en-us/dotnet/api/system.type.assemblyqualifiedname?view=net-10.0
- **T4 — System.Text.Json populate behaviour:** https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/populate-properties

These sources substantiate runtime/API behaviour. They do not approve the proposed CIS-specific defaults, persistence policy, public names or startup placement.
