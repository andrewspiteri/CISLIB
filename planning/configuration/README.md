---
title: Configuration requirements and proposed implementation direction
type: planning-reference
status: Draft
---

# Configuration requirements and proposed implementation direction

This record reconciles the supplied Configuration outcome and selected P01–P08
profile. Andrew Spiteri approved the four-task contract/allocation bundle on
17 November 2026 through CIS-0008. Its recorded outcome, not this planning file,
grants the shared finite implementation authority. The [acceptance map](acceptance.md)
links the implemented boundaries to their actual evidence; exact native results
and qualification gaps are recorded in [CIS-0008](../../docs/changes/CIS-0008/verification.md).

## Sources and precedence

| Source | Role |
| --- | --- |
| [Regenerated Configuration specification v1.0](sources/CIS_Configuration_Module_Specification_v1.0.md) | Supplies the restored conversation-derived requirements U01–U12, recommendations P01–P08 and acceptance CFG-A01–CFG-A28. It does not claim recovery of a separate historical document or completed implementation. |
| [Implementation handoff](sources/CIS_Configuration_Implementation_Handoff_v1.0.md) | Requests import/reconciliation and coherent remaining work; explicitly supplies no new execution budget. Embedded workflow instructions do not independently grant approval or allocation. |
| [Current decisions](../decisions.md) and [dependency policy](../../docs/architecture/csharp-dependency-policy.md) | Preserve the actual environment architecture, single Core package, approved dependencies, native development route and separate release obligations. Current explicit user direction takes precedence over older planning instructions. |
| [Type Manager Configuration boundary](../../docs/modules/type-management/configuration-boundary.md) and [retained Type Manager definition](../type-manager/sources/CIS_Type_Manager_Definition_v1.0.md) | Keep discovery/catalog identity separate from Configuration construction, retained objects, persistence, merging and refresh policy. Older source task lists are not new work instructions. |

The restored specification is available. Earlier alignment statements that the
entire Configuration source was missing no longer describe the current input.
Its references to the 16 November Type Manager v1.1/alignment documents remain
provenance references; absent copies need not be reconstructed before using this
supplied source. Preserve older sources and historical observations unchanged.

At intake, repository inspection found no existing retained-POCO Configuration module,
`IConfigurationObject`, `ConfigurationChanged` contract, storage adapter or adopted
durable key scheme. Existing configuration-named fixtures cover environment and
module-option validation. They do not settle this module's P choices or establish
previous persisted keys. Any subsequently identified adopted scheme must be
reconciled explicitly rather than migrated or guessed automatically.

## Existing implementation to reuse

| Existing boundary | Configuration consequence |
| --- | --- |
| [ITypeManager](../../implementations/csharp/src/CIS.Core/TypeManagement/ITypeManager.cs), [TypeManagerOptions](../../implementations/csharp/src/CIS.Core/TypeManagement/TypeManagerOptions.cs) and `TypeManager.CreateAsync` | Await actual preparation before the one root build. Reuse `AssemblyManifestPath`, `PrecomputedContracts` and implementation queries. Configuration performs no assembly scan or second preparation. Queries can return nonpublic classes without usable constructors; Configuration owns eligibility and useful rejection. |
| [Inventory build integration](../../implementations/csharp/build/type-manager/README.md) | Keep genuine Core SDK/targets and actual Console/Web consumer machinery. Do not restart TM-001–TM-036 or replace product inventory generation with test tooling. |
| [Startup](../../docs/modules/startup/README.md) | The actual `CIS.Startup.IStartable` and awaited Generic Host integration exist. All scheduled instances materialize before sequential start methods run. Constructor-ready values therefore require preparation before consumers are constructed. |
| [Heartbeat](../../docs/modules/heartbeat/README.md) | The actual shared scheduler and owner/drain contracts exist. Reuse them for selected polling; do not create timers, another scheduler or a placeholder production integration. |
| [Healthcheck](../../docs/modules/healthcheck/README.md) | Use existing monitor/status policy. Application-wide startup completion and stopping remain with the existing host/startup integration; Configuration does not call `MarkStartupCompleted()` or take over readiness. |
| [Configuration diagnostics](../../implementations/csharp/src/CIS.Core/Environment/Resolution/CoreConfigurationException.cs) | Preserve `CIS.Configuration.ConfigurationIssue`, `ConfigurationIssueCodes` and `CoreConfigurationException`. Check new names and diagnostic codes against this public surface before selecting them. |
| [EnvironmentOptions](../../implementations/csharp/src/CIS.Core/Environment/EnvironmentOptions.cs) | Preserve first-access defaults, recognized overrides and bounded identity interning. Configuration does not reinitialize it. Its overridden `ApplicationName` is not silently substituted for the fixed host-project identity used by storage. |

Statements in the supplied sources that Startup or Heartbeat were not established
describe their earlier review. They are superseded for integration planning by
these actual implementations. Configuration remains a new logical module within
the existing `CIS.Core` package/assembly, using explicit `CIS.*` namespaces,
`net10.0` and the approved dependency closure.

## Selected P01–P08 profile

The following profile was selected in one explicit decision together with the
bounded outcome/allocation. It is a new 17 November decision, not an invented
historical approval. Concrete signatures are implemented and checked in W1 under
that scope; supporting tests, documentation and corrections need no separate
approval while remaining within the same contract and overall limits.

| Proposal | Recommended selection and observable boundary |
| --- | --- |
| P01 — Initial load | Explicit async Configuration preparation after completed Type Manager and before consumer/container construction. Create retained objects, capture defaults, load/validate/apply the initial baseline, then register those exact objects. Stage 2 establishes refresh observation and directly reconciles the bootstrap-to-activation gap before dependent start work. Do not hide storage I/O in DI factories or add a second container. |
| P02 — Persisted identity | Keep exact CLR `Type`/assembly identity internally. Persist full type name plus defining assembly name, culture and public-key token, omitting assembly version. Use deterministic ordinal identities, reject collisions and keep common/application scope separate from the type key. No fallback lookup or automatic migration. No prior adopted key choice was found in the inspected tree. |
| P03 — Composition | Overlay captured defaults, then common, then application values using a presence-aware structured value model. Omission inherits; explicit false/zero/empty values override; allowed null is a value. Merge nested POCO members, replace collections/dictionaries, and rebuild from current lower levels after deletion. Reject duplicate/ambiguous keys and invalid known values; ignore unknown properties with bounded name-only diagnostics. |
| P04 — Reads and commits | Passive, nonthrowing data setters and one private per-entry shared synchronization gate. A short synchronous reader helper copies a consistent view of the exact retained object. Prepare/validate detached values outside the gate; commit prepared assignments/checksum/revision inside it. Raw POCO reads have no cross-property atomicity guarantee; nested references may be replaced. |
| P05 — Notification access | One `ConfigurationChanged` event, proposed typed arguments carrying CLR type/local revision, and a small per-instance helper. Direct interface implementations forward add/remove through documented helper accessors; an optional base class provides the same pattern. No mandatory inheritance, backing-field reflection, proxy or second event bus. Incompatible event wiring must be diagnosed before delivery. |
| P06 — Change detection | Separate source tokens/payload bookkeeping from a deterministic effective-value SHA-256 checksum. Canonicalize supported mapped values independently of culture, property order and event machinery. A masked source change refreshes source state without remapping/event/revision; later override removal exposes the latest common value. Hashes are not authenticity checks or routine diagnostic output. |
| P07 — Outcomes and events | Initial population establishes a neutral baseline with no change event. Invalid refreshes retain accepted live values/checksum/revision. One successful effective commit produces one ordered after-lock notification; cancellation after commit cannot suppress it. Snapshot handlers in subscription order and isolate synchronous subscriber exceptions. Arbitrary async-void or blocking handlers receive no forced-abort/durable-delivery guarantee. |
| P08 — Construction/defaults | Use eligible passive concrete closed classes with a usable parameterless constructor; permit an explicit per-type factory for exceptional types. Capture defaults once, build detached candidates and validate before commit. Reject incompatible discovered types usefully rather than silently skipping them. Factories do not load storage, construct another provider or recompute defaults during refresh. |

P01 avoids a real timing conflict: Stage 2 loading alone cannot make earlier
Stage 1 constructors observe stored values. If a different contract is selected,
its constructor/default-value guarantee must be stated explicitly instead of
promising both timings. P02 does not alter Type Manager's exact assembly-qualified
lookup. P04/P05 must not be reduced to writer-only locking or an event the manager
cannot legitimately publish.

## Proposed runtime and ownership shape

The application supplies and captures `ABC`, its established host project/application
identity, once. Reject collision with reserved `CommonConfiguration`; do not
derive the scope from a library, machine, tenant or overridden display identity.
The two logical records are `CommonConfiguration/DEF` and `ABC/DEF`. An adapter
owns physical escaping/columns; the notation does not select filesystem storage.

Each owning host retains one live root per discovered type. Register that object
under its concrete type and reject conflicting registrations. Aliases, when
explicitly supported, refer to the same object. Separate hosts retain separate
roots; no process-global live-object registry is proposed. DI reads remain
in-memory. Existing subscriptions survive reload because candidates never replace
the root. Consumers copy synchronized values and own reconfiguration of their
timers, clients and caches.

A small provider boundary distinguishes Found, confirmed Missing, conditional
Unchanged with matching cached state, and Failure. Failed reads never imply
deletion. Opaque source tokens have equality semantics unless a provider explicitly
guarantees ordering. The two records are not a cross-record transaction without
a real provider snapshot guarantee.

One per-entry refresh path covers retrieval, composition, validation, commit and
ordered notification. Coalesce triggers into bounded pending state per discovered
entry and protect the pending-to-idle handoff. Retain successful lower-level source
changes even when masked. An invalid source cannot become healthy/applied merely
because a later conditional read returns Unchanged. Do not hold the value gate
during storage, awaits, logging or notifications.

Activation subscribes and reconciles, including invalidations received during
the reread; polling also reconciles directly during startup instead of awaiting
the first recurring callback. Heartbeat stays inactive under its existing startup
contract until the complete Startup operation succeeds. Runtime triggers use the
selected real provider/Heartbeat boundary and the same serialized refresh logic.
Stop closes trigger/commit admission, detaches owned observation and joins actual
in-flight refresh/notification work before dependency disposal. Accepted commits
finish notification. Configuration disposes only its resources, not host-owned
adapters or user POCOs. Noncooperating user work cannot be declared settled by a
wait timeout.

No production backend, polling interval, new environment variable, universal
health criticality or stale-age policy has been selected. Polling requires explicit
positive host/provider input unless an applicable adopted value is found. A
controlled provider is useful test evidence; it is not a delivered production
adapter. Report any actual adapter gap separately from core and scheduler success.

## Coherent delivery and early demonstrations

Use the specification's CFG-W1–CFG-W6 as an ownership map, not six compulsory new
tasks. Keep focused tests, usage documentation and independent review with each
implementation outcome. Establish shared contracts/default capture first; merge/
mapping and provider/refresh work can then proceed in parallel only with named,
non-overlapping files. One owner coordinates public/project/package edits and
shared native output directories. Host/lifetime composition integrates those
results; qualification is recorded at its actual scope, without a new test harness.

The first useful demonstrations should prove:

1. One type discovered through the real prepared catalog supplies the same root
   to two consumers/scopes before and after a valid reload.
2. A masked common change emits no event, and deleting the application override
   exposes that latest common value on the retained root.
3. A controlled commit boundary prevents a synchronized reader from returning a
   mixed old/new property set; no equivalent promise is made for raw reads.

The [acceptance map](acceptance.md) assigns all supplied U/CFG-A identifiers to
existing native project families. Use controlled signals and bounded observation
waits, actual Type Manager/DI/Startup/Heartbeat/Healthcheck integration, and compact
results. Preserve the six library verification categories and applicable package,
platform, coverage/mutation and compatibility obligations at their real boundary;
narrow checks do not establish release readiness.

Earlier native allocations exclude Configuration; completed owners, costs,
attempts, reserves and reports remain historical. The separate
[CIS-0008 authority and allocation](../../docs/changes/CIS-0008/proposal.md) now
cover the four concrete owners using one shared six-hour/eight-attempt pool and
the existing physical six-GiB ceiling, with included 600-second/64-MiB reserve.
Implementation and actual verification are tracked there. These planning files
do not claim package publication or a merge.
