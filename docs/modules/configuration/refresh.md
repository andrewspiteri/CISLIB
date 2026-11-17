---
title: Configuration refresh and lifetime
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.refresh
---

# Configuration refresh and lifetime

Configuration retains the same concrete root object while its internal refresh
coordinator reconciles common and fixed-host records. It uses the existing
retained-object catalog and value pipeline. Storage and watch behavior come from
the host-supplied `IConfigurationStore`; this module selects no backend or default
polling interval. The [requirements register](../../../planning/configuration/README.md)
and [acceptance map](../../../planning/configuration/acceptance.md) retain the
selected contract and qualification boundaries.

Each source cache distinguishes a detached Found payload, confirmed Missing,
conditional Unchanged and Failure. A failed read does not delete a previous
record. An Unchanged response requires existing source state. Source tokens are
opaque equality values, never ordered revisions or effective-value checksums.
The coordinator reads both scopes and retains each successful result even when
the other read fails. That attempt cannot commit until both sources are usable.

Source state remains separate from accepted live values. A newly retrieved
invalid payload stays cached with its source token, so later Unchanged responses
still validate that payload and cannot clear the failure. A common change masked
by a host override is retained without a false change event; removing that override
uses the latest common record. Conversion and checksums belong to the value
pipeline, not to separate polling/watch implementations.

## Initialization and activation

The internal `LoadAsync` operation loads entries sequentially before consumer DI
construction. It establishes revision zero without an initial change event.
Repeated calls retain the first operation and its success, safe failure or known
cancellation; later callers cannot replace its token. Failed initial loading
prevents the prepared configuration from being delivered to consumers.

`ActivateAsync` is a separate retained lifetime operation for the existing
sequential Startup integration. It establishes observation before directly
reconciling the bootstrap-to-activation gap. Watch callbacks received while
activation is incomplete mark pending entries. The coordinator covers those
invalidations before publishing activation completion. Polling-only mode also
directly reconciles during activation; it does not await a Heartbeat callback
while Startup intentionally keeps Heartbeat inactive. An empty catalog needs no
provider read or subscription.

Notification guarantees are captured once from the provider. Reliable mode needs
usable reconnect/invalidation behavior. RequiresReconciliation also needs the
explicit host polling interval. A supplied interval in Reliable mode explicitly
requests periodic reconciliation too. W4 hosting binds the actual shared
Heartbeat schedule; the refresh coordinator creates no periodic timer or thread.
The actual provider adapter must document its own consistency and recovery
guarantees. Two scope reads do not imply a cross-record storage transaction.

## Serialized work and notification

Every entry has one tracked runner and one pending bit. A notification only marks
the relevant discovered entry or scope and starts a runner when idle; it does not
perform storage or mapping in the provider callback. Repeated signals coalesce.
Retrieval, detached preparation, commit and ordered notification remain serialized
through actual completion. Pending work is checked with the transition to idle,
so a late trigger cannot disappear between that check and the next attempt.

The internal per-entry and all-entry `ReconcileAsync` operations return a Boolean
for the latest reconciliation's validity, not whether effective values changed.
They join actual work, including pending reconciliation and synchronous handlers.
They do not turn a canceled caller wait into proof of settlement. Routine refresh
failures retain accepted values and are retried only on later legitimate triggers.
A live-setter contract failure disables that entry; it does not disable unrelated
configuration or enter an immediate retry loop.

The value pipeline checks cancellation/stopping at its private value gate before
accepting a commit. Consumers use the synchronized reader with that same gate;
raw multi-property reads have no promised atomicity. Events and diagnostics run
after releasing the gate. Once accepted, a revision still publishes its event if
cancellation begins before publication.

One failing synchronous handler cannot roll back the commit or prevent later
handlers. A fixed safe notification-failure code is recorded separately from
candidate validity and cleared on the next accepted publication. An unchanged
read does not claim that a previously failing subscriber recovered. Handlers may
read safely or signal pending work; they must not wait for a drain containing
their own execution. Indefinite synchronous handlers and async-void continuation
failures have no forced-abort guarantee.

Current internal snapshots report per-type validity/disabled state, accepted
revision, last successful reconciliation/change timestamps and fixed failure
codes, plus overall watch/activation/stopping state. They contain no configuration
values, source tokens, checksums, exception graphs or streaming history. Optional
health observation is outside coordination gates and cannot replace work outcomes.

## Shutdown ownership

The host integration must call Configuration's `BeginStopping` before waiting for
Heartbeat's drain. Heartbeat does not automatically cancel a running callback's
execution token. Configuration therefore owns a separate provider-work token;
its cutoff closes commit/trigger admission and requests cancellation using tracked
`CancelAsync` work outside coordination gates. Blocking or throwing cancellation
callbacks do not execute under those gates, and their actual completion remains
part of shutdown.

Stop tracks watch acquisition independently from activation reconciliation. It
detaches an acquired subscription promptly, or waits for a late acquisition and
then disposes its handle. It still joins actual initial/activation work, every
entry runner and synchronous notification handler, and owned cancellation
callbacks. A subscription cleanup fault cannot skip the remaining drains;
cleanup errors propagate using safe diagnostics only after actual work settles.

Only acquired subscription handles and the coordinator's cancellation resources
are disposed. The store and configuration POCOs remain host-owned, and stopping
does not reset accepted values. The supported Startup hosting integration must supply
combined Configuration/startup/Heartbeat settlement before dependency teardown.
Arbitrary noncooperating provider or handler code can keep that barrier pending;
a timeout alone never authorizes destructive disposal.

The focused [refresh tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Configuration/ConfigurationRefreshCoordinationTests.cs)
and [lifetime tests](../../../implementations/csharp/tests/CIS.Core.UnitTests/Configuration/ConfigurationRefreshLifetimeTests.cs)
use a controlled provider and value pipeline to verify these boundaries. They do
not establish a production backend, real mapper qualification or actual periodic
Heartbeat integration. Those composed paths and exact native results belong to
the existing integration/package work and the
[CIS-0008 verification record](../../changes/CIS-0008/verification.md).

## Automatic Metrics observations

With the optional Metrics integration, real serialized entry attempts publish
`cis.configuration.operation.duration` using only `operation=initial_load|refresh`
and `outcome=success|failed|cancelled`. The boundary includes both source reads,
mapping/validation, commit and any accepted synchronous notification. It does not
start another refresh, change the store, or alter the existing failure policy.
Notification-handler failure remains a notification diagnostic when the accepted
reconciliation itself is valid.

Current snapshot gauges report `cis.configuration.prepared`, aggregate
`cis.configuration.entries` counts by `state=valid|invalid|disabled`, retained
`cis.configuration.initialization.duration`, and the latest already-owned
`cis.configuration.last.success` timestamp as Unix seconds. Unknown durations,
last-success timestamps and pre-preparation entry states are omitted. Snapshot
collection reads no POCO value or store and executes no subscriber.

Preparation normally precedes DI/provider activation. Its terminal duration is
retained once, without recording a historical load into a later histogram or
running it again. The same operation binding is retained through every actual
attempt; attaching while it is held does not create a partial/replayed event.
Mutable refresh state belongs to one host. Applications need no additional
instrumentation calls, and hosting drains collection callbacks before destructive
owner teardown. See the [portable catalog](../../../contracts/metrics/README.md#bootstrap-and-startup-catalog)
for fixed names and dimensions; native Metrics results are separate evidence.
