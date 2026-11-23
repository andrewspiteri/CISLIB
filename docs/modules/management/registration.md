---
title: Optional management registration and inventory
type: document
status: Draft
---

# Optional process reporting

`IManagementClient` remains a passive typed API port. Explicitly select
`AddCisManagementRegistration` to register this process and report its current
local observations. Use a dedicated prepared management/REST alias with
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
    RequireInitialRegistration = false,
    WireLimits = management.WireLimits
});
```

Use the selected actual RM-01 provider's existing factories, as described in the
[typed client guide](csharp-client.md). Register credential/local prerequisites
before this startup participant. CISApplication retains its one provider and
sequential Startup. Generic Host callers use the ordinary REST → Heartbeat →
Startup cooperating tail; the reporter adds no hosted scheduler or provider.
Selecting the Jobs adapter requires the already selected `IJobCatalog`; it does
not install Jobs, schedule work or authorize a handler. Omit that adapter for an
application without Jobs, or supply a complete application inventory source.

The startup participant validates local options and complete initial content,
then directly attempts registration before registering its periodic callback.
The default initial budget is thirty seconds. `RequireInitialRegistration`
requires that direct valid registration receipt. An unavailable service normally
leaves this optional integration disconnected while business startup continues.
A later observation/inventory refusal degrades reporting without rewriting a
confirmed registration receipt. Invalid selected local options or initial
content fail Startup explicitly. There is no dependence on the first periodic
Heartbeat callback to satisfy required startup.

The default reported label is the native process instance ID; it remains a body
claim, separate from native NotProvided/Reported/Verified evidence. Process start
uses the actual native process-start fact, with an explicit option required if
that fact is unavailable. Version uses exact `EnvironmentOptions.ServiceVersion`.
No application/environment identity is inferred from the payload; current
credentials and server mapping remain authoritative.

One owner-bound Heartbeat subscription coalesces work. Normal observations run
every thirty seconds after the previous attempt settles. Reconnection starts at
five seconds, doubles with bounded positive jitter and caps at sixty seconds.
The existing Heartbeat condition checks eligibility; no second timer loop or
event history is retained. Reconnect can report current observations sooner,
but a separate upload gate permits at most one complete inventory attempt per
reporting interval after its initial upload. Changing job state does not trigger
an immediate catalog upload.

The initial budget is configurable from one millisecond through one day;
reporting/reconnect intervals are from ten milliseconds through one day, with
the first reconnect delay no larger than its cap. Initial budget cancellation
still joins the actual native attempt and cleanup. A noncooperative dependency
can delay settlement beyond the requested budget.

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
condition is noncritical by default. `IsHealthCritical = true` explicitly selects
an ongoing business readiness policy. Routine successes produce no normal log
stream. Failure/recovery diagnostics share a monotonic one-minute gate and a
sixteen-record lifetime budget that persists across flaps; suppressed diagnostics
do not suppress requests, health reports, cancellation or original faults.
`ManagementRegistration.CaptureStatus()` exposes bounded local facts and counters.

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

At shutdown the known hosting integration first closes reporting admission and
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

`ScriptedManagementObservationSource` and `ScriptedManagementInventorySource`
join the existing bounded `ScriptedManagementClient`, `ManualHeartbeat`,
`ManualTimeProvider` and recording monitors in CommonTesting. Their finite
content/fault queues and Kind-only capture records support ordinary MSTest or
application unit tests. Pre-cancellation consumes nothing; exhaustion/capacity
fail explicitly, and original exceptions remain original. These substitutions
prove no SQL, native authentication, real scheduler timing or broker durability.
