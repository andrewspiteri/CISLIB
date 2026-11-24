---
title: Configuration central snapshot contracts
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.central-snapshots
---

# Configuration central snapshot contracts

RM-08 adds public companion contracts in `CIS.Configuration` and integrates them
with the existing retained-root preparation, value pipeline and serialized
refresh owner. The [implementation plan](../../changes/CIS-0027/implementation-plan.md)
separates this Configuration behavior from authenticated bootstrap and client
transport integration. Authored tests are source until native verification is
recorded under the owning work story.

## Local enrollment and trusted target

`ConfigurationEnrollment` explicitly binds one concrete closed
`IConfigurationObject` type to a case-sensitive portable `ConfigurationId`,
canonical positive Int64 decimal `SchemaVersion`, and locally selected
`ConfigurationUpdateMode`. Constructing this descriptor performs no root
construction, defaults evaluation or I/O. `PersistedDefinition` reuses the
existing versionless Configuration identity. Discovery alone never enrolls a
configuration centrally.

`ConfigurationOptions.Enrollments` defaults to an empty list;
`SnapshotTarget` defaults to null. Existing stores and defaults-only options
acquire no compulsory central member or deployment identity. Preparation copies
and validates these inputs, including exact type/ID uniqueness and fixed
host scope, before delivering retained roots to consumers.

For central use, bootstrap independently establishes a
`ConfigurationSnapshotTarget`: authoritative namespace, application resource,
environment, admitted registration incarnation, fixed Configuration host scope
and optional trusted cohort. The target must come from the authenticated
bootstrap/session owner. A response's labels, a telemetry display-name override
or a supplied UUID cannot establish it. The request carries that independent
target alongside the selected enrollment. `MatchesTarget` compares target,
configuration ID and schema exactly; it supplies neither current-session
authority nor an execution permit.

Schema and assignment generation remain decimal strings through this boundary,
including values above JavaScript's exact integer range. UUIDs use canonical
nonempty lowercase text. Central environment and host names are bounded to 255
Unicode scalar values; ordinary legacy host-scope validation is unchanged.
Portable configuration/cohort identifiers use bounded
URI-unreserved ASCII with exact comparison and exclude complete dot segments.

## One paired observation

`IConfigurationSnapshotStore` is an optional capability beside
`IConfigurationStore`. It does not add a required member to the legacy port or
select another scheduler. Centrally enrolled entries require it. The public
paired port also permits noncentral reads without enrollment, target or
provenance; the current refresh owner deliberately routes noncentral entries
through their independent-record semantics.

`ConfigurationSnapshotRequest` contains the exact common and fixed-host keys,
an optional retained whole-pair token, and central selection/context when
applicable. It rejects mismatched definitions, host scopes and half-specified
central context. A pair token is an opaque equality stamp: it does not order
assignments, authenticate source content or renew Management authority.

`ConfigurationSnapshotReadResult` distinguishes three whole-pair outcomes:

- `Complete` owns common/application observations together. Each member must be
  `Found` or successfully confirmed `Missing`; per-member `Unchanged` and
  `Failure` cannot form a complete pair. Existing `ConfigurationReadResult`
  factories detach JSON from provider document lifetimes.
- `Unchanged` confirms only the exact already-retained matching target/session
  pair. It carries no replacement content. A caller without that retained pair
  must refuse it. A failed authoritative observation cannot return unchanged.
- `Failure` retains no pair, token or source references. After independently
  validating desired identity, an adapter can retain that
  `ConfigurationDesiredIdentity` and a bounded safe failure code for a visible
  identified rejection. An unidentified polling failure carries no invented
  desired identity and cannot clear previously accepted state.

For a complete central result, `ConfigurationSnapshotProvenance` holds desired
identity plus immutable common/application publication references. A reference
holds source ID, publication ID and exact lowercase SHA-256. Null references
mean the release explicitly selected absence and must agree with the complete
source observations. Both absent is the authorized defaults release. Null
sources on `Failure` or `Unchanged` never mean absence.

The central adapter validates the full wire target and source metadata,
strict decoded JSON and exact UTF-8 source digest before exposing a complete
candidate. Mapping the validated fields to these records does not by itself
authenticate an arbitrary custom provider. The source digest differs from
Configuration's private typed effective checksum.

## Actual acceptance and routing ownership

The runtime integration rechecks independently trusted current-session,
cancellation, stopping, supersession and generation facts immediately before
acceptance. Values, checksum, local revision and accepted provenance share the
existing retained-root value gate. Immutable data crosses that boundary;
provider callbacks and observers run outside it. Accepting unchanged effective
values still advances accepted desired/provenance facts without advancing local
revision or raising a value-change event. Initialization remains revision zero.

`ConfigurationAcceptanceStatus` captures actual disposition, identified desired
state, last accepted provenance, local revision and whether the accepted state
is intact. `Applied` and `AppliedUnchanged` require matching accepted desired
facts. `Rejected` and `PendingRestart` can retain a different accepted release.
`Faulted` makes last acceptance historical and never claims intact live state.
Subscriber diagnostics are separate from candidate failure and cannot undo an
accepted commit. These records contain no source payload or effective checksum.

`HotReload` uses the retained-root runtime commit. `RestartRequired` applies
normally during initial live loading, then stages valid effective runtime
changes while keeping active values. Returning to active values accepts current
provenance and clears pending restart without a value-change event. A restart
fetches the current live authorized release. Intact accepted state can survive
polling failure, identified rejection and pending restart; a terminal live-state
fault cannot preserve readiness.

Current central acceptance is available through the additive `Acceptance`
property on each `ConfigurationEntryRefreshStatus`. The original positional
status constructor and noncentral fields remain intact. A latest failed poll
can make `IsValid` false while actual accepted state stays intact. Readiness is
not a synonym for latest-poll validity or desired-equals-applied.

The internal hosting seam carries a retained reconciliation epoch. Initial
loading consumes the initial epoch. An actual connectivity/authority gap can
require one new epoch, and a short control callback joins that retained owner
instead of sweeping all definitions or rearming its own incomplete work. An
unidentified failed poll cannot finish an outstanding epoch; an authoritative
identified rejection or pending restart can finish it with intact roots.
The short trigger queues that single coalesced owner, so even synchronously
completing stores do not execute the sweep on the control callback. Startup
awaits the same actual operation sequentially. Healthy readiness copies a
retained aggregate without locking every root; actual completion work refreshes
it. A permanent live-setter failure closes shared admission atomically under
the actual value gate before any delayed observer could report the fault.

After a fresh authenticated admission, the hosting owner can rebind a new
registration/cohort without mutating captured options. Rebind preserves fixed
authority/application/environment/host, fences old candidates under each value
gate, cancels and joins old provider work, and discards the previous session's
conditional pair under the existing serialized read owner before its next
fetch. The assignment-map generation high-water survives that handoff. For the
same cohort, bounded observed release and source-reference comparison facts also
survive: a different registration ID cannot make the same map generation name
another release or change its pinned sources. Those comparisons exclude only
the old registration ID, retain no additional payload/cache, and do not rewrite
accepted provenance before a fresh current-session observation is accepted.
If only an identified failed desired release was known, its source references
can still be established by later valid complete content for that same release.
A different cohort may legitimately resolve another release at the same
generation; a lower generation still refuses. Snapshots never initiate this
rebind or renew Management authority.

## Bounded ownership

Central preparation and mapping check depth 16, at most 65,536 logical value
nodes, 4,096 elements per collection, 256 declared members per object and 64 KiB
UTF-8 per scalar before expansion or replacement construction. Source records
are capped at 256 KiB decoded JSON. Every source is structurally checked before
overlay, including masked known values and unknown subtrees. Completion counts
the defaults that missing supplied children would expand before constructing
their replacement graphs.

The one runtime shares a 16 MiB logical allowance across retained default/source
buffers, admitted paired reads and detached mapping work. It reserves a bounded
read before fetching, accounts source ownership until replacement or actual
stop, and releases temporary candidate ownership after its attempt settles.
The central client adapter retains exact decoded source text beside parsed JSON
in that same pair owner and charges both. This preserves digest-significant
surrounding whitespace without an unaccounted second adapter cache. The internal
request borrows the retained pair only for that read's exact conditional check.
Logical mapping accounting includes a conservative node/text/escaping allowance;
it can refuse a candidate before the maximum individual shape limits. This
does not cap arbitrary constructor, getter, factory or validator allocation,
the CLR's object overhead or application-owned live data. Those callbacks remain
subject to the existing passive-data contract. No persistent snapshot cache is
introduced.

The pure central adapter uses `ConfigurationNotificationMode.None` and the
host's explicit positive polling interval. A narrow routed composite preserves
noncentral watch/reconnect behavior. When a legacy store is `Reliable` but the
central side requires polling, the composite can declare
`RequiresReconciliation`: watches still forward only noncentral keys and the
explicit interval reconciles central work. Hints only coalesce reread requests.
Configuration passes only noncentral keys to watch acquisition, owns and
detaches acquired subscriptions, while the host retains
the supplied store's lifetime. No per-POCO network, validation, acknowledgement
or polling loops are introduced.

See the existing [reader/event boundary](core.md), [value mapping](mapping.md),
[serialized refresh owner](refresh.md) and [hosting ownership](hosting.md).
