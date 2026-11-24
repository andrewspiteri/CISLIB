---
title: Managed bootstrap, admission and process reporting
type: document
status: Draft
---

# Managed bootstrap and process reporting

`IManagementClient` remains a passive typed API port. Explicitly select
`AddCisManagementRegistration` to select managed execution, register this process
and report its current local observations. Selecting this integration requires
live bootstrap before new managed work. Use a dedicated prepared management/REST alias with
`LogCalls = false`; other clients keep their own logging, authentication and
trace choices. Existing native REST metrics still observe actual requests.

```csharp
var management = new ManagementClientOptions("management-reporting")
{
    BaseUrl = "https://management.example/platform"
};
var rest = new RestClientOptions(management.RestClientName) { LogCalls = false };
rest.ConfigureCisManagementRestClient(management);
services.AddCisSecurityBoundRestClient(rest, selectedBindingFactory,
    selectedAuthenticationFactory);
services.AddCisManagementClient(management);

// Optional: select the existing passive Jobs projection before registration.
services.AddCisManagementJobsInventory(management.WireLimits);
services.AddCisManagementRegistration(new(management.RestClientName)
{
    WireLimits = management.WireLimits
});
```

Use the selected actual RM-01 provider's existing factories, as described in the
[typed client guide](csharp-client.md). Register credential/local prerequisites
before this startup participant. The managed startup participant precedes the
selected Messaging and Coordination workers. CISApplication retains its one
provider and sequential Startup. Generic Host callers use the ordinary REST → Heartbeat →
Startup cooperating tail; the reporter adds no hosted scheduler or provider.
Selecting the Jobs adapter requires the already selected `IJobCatalog`; it does
not install Jobs, schedule work or authorize a handler. Omit that adapter for an
application without Jobs, or supply a complete application inventory source.

The startup participant validates local options and complete initial content,
then directly obtains a valid registration receipt and authoritative control
confirmation. It awaits each selected `IManagedStateReconciler` sequentially,
then rechecks confirmation freshness and local prerequisites before opening
admission. At most thirty-two reconcilers may be selected. Missing, refused,
unsupported or stale bootstrap fails Startup; cached state alone cannot admit
work. The default direct registration budget is thirty seconds. The retained
`RequireInitialRegistration` property remains source and binary compatible, but
does not make selected managed bootstrap optional. There is no dependence on the
first periodic Heartbeat callback to satisfy required startup.

Canceling direct `ManagementRegistration.StartAsync` also closes and invalidates
admission during the initial observation or inventory reports, even after live
control has succeeded. Startup still joins the accepted report and its actual
cleanup before completing as canceled, and installs no recurring subscription
on that path. Repeated startup joins the same outcome. Unexpected admission
notification or accepted-work faults remain visible; cancellation does not hide
them. The application must still await `StopAsync` or disposal for the captured
owner and monitor drain in the established dependency order.

`IManagedWorkAdmission.TryAdmit()` is a point-in-time check immediately before a
new managed claim, acquisition or finite invocation. Its snapshot and Changed
event are observations, not reusable permits. The gate starts
`AwaitingBootstrap`, enters `Reconciling` during a check, and becomes `Ready`
only after a fresh accepted confirmation and reconciliation. An observed outage,
expired confirmation or unavailable local prerequisite changes it to
`ContinuingOnly` after bootstrap. Existing accepted module/configuration state
remains available. Closing admission never cancels an already admitted finite
invocation, revokes a lease, changes business enablement, or blocks settlement
of already committed outbox/recovery work. `Stopping` is terminal.

The selected Jobs and Messaging integrations use this same captured gate for
new work. Effect HTTP routes explicitly select `RequireCisManagedWork()` after
their native authorization policy. Ordinary status/recovery routes keep their
existing access rules. Managed admission contributes to readiness, without
turning management loss into failed process liveness. Both legacy
`IsHealthCritical` values retain this policy. Selecting the reporter rejects a
competing admission-port registration; a later replacement is detected at
activation before work starts.

Control responses echo the exact registration and per-attempt UUID. An
unexpected authority namespace cannot automatically rebind this process. Old
attempts, replaced registrations and late successes cannot reopen a closed or
stopping generation. Each check conservatively closes to `Reconciling` before
dispatch, so a healthy periodic check can briefly defer new work while admitted
work continues.

The default reported label is the native process instance ID; it remains a body
claim, separate from native NotProvided/Reported/Verified evidence. Process start
uses the actual native process-start fact, with an explicit option required if
that fact is unavailable. Version uses exact `EnvironmentOptions.ServiceVersion`.
No application/environment identity is inferred from the payload; current
credentials and server mapping remain authoritative.

One owner-bound Heartbeat subscription coalesces work. Authoritative control
checks normally run every five seconds; presence observations run every thirty
seconds after the previous attempt settles. Reconnection starts at
five seconds, doubles with bounded positive jitter and caps at sixty seconds.
The existing Heartbeat condition checks eligibility; no second timer loop or
event history is retained. Control checks select no automatic read retries;
the first failed exchange closes admission. Reconnect can report current observations sooner,
but a separate upload gate permits at most one complete inventory attempt per
reporting interval after its initial upload. Changing job state does not trigger
an immediate catalog upload.

The selected report and control intervals remain independent. An explicitly
shorter report interval does not wait for the next control check and does not
accelerate control requests. Their next eligible times are measured separately
after actual settlement, using the same coalesced owner and its earliest due
phase. A failed report still closes admission and selects bounded reconnection.

The initial budget is configurable from one millisecond through one day;
reporting/reconnect intervals are from ten milliseconds through one day, with
the first reconnect delay no larger than its cap. The selected control interval
is from ten milliseconds through fifteen seconds; its complete control and
reconciliation budget is from one millisecond through fifteen seconds, initially
five seconds. Cancellation closes admission promptly and still joins the actual
native operation, response cleanup and reconciliation. A noncooperative
dependency can delay physical settlement beyond the requested budget.

Confirmation validity is at most fifteen seconds and starts at the local
monotonic request-start sample. Actual response cleanup and all reconciliation
consume that same window. Every `TryAdmit()` independently checks expiry, so a
delayed Heartbeat cannot extend it. A negative monotonic interval, changed clock
frequency, backwards UTC or more than one second of UTC/monotonic divergence
requires another live confirmation. A host that observes resume or another
unmeasurable gap can call `ManagedWorkAdmission.Invalidate()`. Restoring a local
prerequisite alone does not reopen admission; a fresh check and reconciliation
are required. Custom clock/reconciler faults remain original failures.

Observation and inventory streams have independent positive Int64 sequences.
Repeated registration uses the original UUID and immutable facts. An unchanged
pending inventory retry retains its original sequence, timestamp and content;
a newer complete capture replaces it with a new sequence. Successful unchanged
inventory is not uploaded again. Only one pending observation and one complete
pending inventory are retained, with the selected finite wire/catalog limits.
There is no local disk spool or missed-tick backlog.

An attempted replacement remains unconfirmed until its exact valid receipt is
received, including a lost response or original callback fault. If current content
then returns to the last confirmed value, the owner publishes that current value
with a higher sequence; the remote side may already have accepted the intervening
replacement. A replacement that has never been attempted can still coalesce away.
This requires one bounded uncertainty flag, without retaining an upload history.

A documented missing-registration response to a report selects a fresh random
UUID for the next explicit registration. The protocol's 404 also hides foreign
ownership, so it does not establish that a row was purged or that credentials
still represent the same subject. Every replacement is authenticated again;
the client gains no ownership or migration claim. Closed/conflicting IDs and
sequence conflicts suspend automatic reporting visibly; they are never repaired
by silently selecting another registration. A process restart gets a new UUID.

`IManagementObservationSource` and `IManagementInventorySource` are public
content-only snapshot ports. Their synchronous captures copy current data,
observe cooperative cancellation, perform no I/O, and allocate no sequence or
timestamp. The owner validates, detaches and timestamps accepted complete
content. A later explicit inventory refusal preserves the previous complete
pending value and retains a visible local error. Custom unexpected faults remain
the original Heartbeat fault. Applications can replace either port normally in
DI without replacing native scheduling or shutdown.

`HealthManagementObservationSource` maps the actual detached health snapshot;
it does not run collectors. Without a selected health source the host adapter
reports native startup/stopping lifecycle and unknown health. The connectivity
condition is noncritical; managed admission separately supplies readiness.
Routine successes produce no normal log
stream. Failure/recovery diagnostics share a monotonic one-minute gate and a
sixteen-record lifetime budget that persists across flaps; suppressed diagnostics
do not suppress requests, health reports, cancellation or original faults.
`ManagementRegistration.CaptureStatus()` exposes bounded local facts, counters
and the current admission observation. A later report refusal can close new
admission without rewriting the already confirmed registration receipt.

The explicit `JobsManagementInventorySource` traverses every immutable catalog
page and category tuple, preserving scope, ID, exact UTF-8 bounds, display and
resource grouping. It never resolves a handler. Actual local configuration
revision zero and effective host/category/job enablement are retained. Available
runtime resource observations preserve mode, state, outcome, native owner and
provided occurrence times. Runtime pages are available local observations rather
than an atomic cross-page or fleet snapshot; a changing configuration or detected
page overlap refuses the whole capture. Unknown next-run time stays absent.
Counts beyond selected finite bounds refuse the whole catalog; the reporting
boundary also validates complete encoded bytes. Deliberately larger catalogs
require matching supported finite client/reporter/adapter bounds.

At shutdown the known hosting integration first enters terminal `Stopping`, closes reporting admission and
signals its token, including while admitted Jobs/Audit work retains dependencies.
Those existing barriers remain intact. Native REST admission then closes and its
actual operations drain before the host awaits reporting/Heartbeat settlement.
No Startup coordinator dependency is added. A canceled stop wait affects only
that caller; actual accepted work remains owned. The passive lifetime also
captures admitted factory construction for failed-Build cleanup.

Shutdown makes no mandatory final network call. Applications may explicitly call
`CloseRegistrationAsync` while normal REST admission remains available; otherwise
presence expires normally. A remote close is never a prerequisite for local
teardown or evidence that all business callbacks physically stopped. Always stop
the host/application before disposing its provider and borrowed dependencies.

Selected central Configuration adds a [complete adoption stream](configuration-adoption.md)
on this same reporting owner and interval. It has independent sequence and receipt
freshness, one uncertain capture, and the same actual cancellation/drain barrier.
Its per-root acceptance facts contain no values or checksums; reporting does not
renew authoritative control or registration presence.

`ScriptedManagementObservationSource` and `ScriptedManagementInventorySource`
join the existing bounded `ScriptedManagementClient`, `ManualHeartbeat`,
`ManualTimeProvider` and recording monitors in CommonTesting. The client's
`EnqueueControl` factory receives the actual bounded registration/attempt
metadata so a test can echo that specific attempt. `ManualManagedWorkAdmission`
holds one selected snapshot without real clock, bootstrap or authority policy;
use it in isolated consumer tests, without selecting the real reporter. Their finite
content/fault queues and Kind-only capture records support ordinary MSTest or
application unit tests. Pre-cancellation consumes nothing; exhaustion/capacity
fail explicitly, and original exceptions remain original. These substitutions
prove no SQL, native authentication, real scheduler timing or broker durability.
