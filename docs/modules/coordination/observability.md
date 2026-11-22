# Coordination and jobs observations

The [seven metric profiles](../../../contracts/metrics/coordination.md) use the
existing Core `IMetrics` facade and optional collector. Composition reserves the
fixed names and capacities before producers activate. Exact passive composition
identity binds each instrumentation owner to one metrics runtime, independently
of whether Metrics is registered before or after the module. Core acquires no
collector dependency or second provider.

Physical provider timing comes from the managed engine's existing invocation and
return timestamps. A timed-out local wait cannot complete that observation; the
actual late task remains counted and observed. The operation observer records
through the sealed library metrics runtime and never invokes a logger or health
callback inline. Missing job durations are omitted. Metric failures preserve
original business, authorization, transaction and cancellation facts.
Each physical call, guard admission and entered callback captures its existing
metric bundle at entry. Attaching Metrics while work is already running observes
current gauges but cannot replay that earlier work into completion measurements.

Gauges read bounded cached facts only. Active leases count every local Owned or
Draining handle independently of snapshot paging. Job active counts distinguish
continuous and scheduled callbacks and include actual execution-scope cleanup.
An uncooperative callback retains its slot and key after cancellation. Pending
dispatches remain counted through actual source validation/authorization until
callback admission or actual refusal settlement. Collection performs no provider
calls, renewal scheduling, authorization or business work.

## Meaningful diagnostics

Ownership transition context includes scope/duty/resource, instance, acquisition,
generation and stable loss reason. Job diagnostics include exact target/mode,
configuration revision and bounded final outcome. Those values do not become
metric labels. Successful renewal, Busy polling, standby waiting and empty
continuous iterations produce no routine log stream. Ordinary acquired/draining/
released transitions use Debug; loss, fault and configuration rejection warnings
have persistent finite budgets and monotonic spacing.

Each infrastructure failure/recovery budget and terminal-loss warning budget
allows at most eight reports with at least thirty seconds between reports. Job failure budgets are allocated
once per registered definition, excluding resource/occurrence keys; the config
rejection budget is separate and fixed. A successful interval does not replenish
those budgets. Suppression leaves every operation, metric, health update, original
fault and cancellation intact. Telemetry exceptions cannot replace a result.

## Cached health and lifetime

Constructors attach known library observers without creating monitors, touching
providers or starting a polling loop. Sequential startup creates existing health
monitor types after successful runtime startup. Coordination uses availability:
known successful contact publishes availability, established failure invalidates
it, and unknown cancellation does not fabricate success. Jobs use a condition
over cached enabled faults, rejected-versus-accepted configuration revision and
unfinished callbacks whose lease ended. Standby, Idle and Waiting remain healthy.
Configured criticality determines the existing health policy's aggregate effect.

One coalesced refresh task per instrumentation owner updates those cached policies
and bounded infrastructure reporting. It retains no event queue or history and
performs no I/O. Configuration rejection and successful adoption notify outside
runtime gates. One watcher per attached duty observes the lease's actual terminal
task, caches loss while a callback remains unfinished, and is joined before local
key occupancy ends. Health/scrape reads never call `lease.State` or query authority.

Terminal coordination telemetry proceeds independently of arbitrary cancellation
registrations. A blocked cancellation callback cannot suppress the loss counter
or diagnostic. Both actual notification paths remain in the runtime's settlement
barrier. Instrumentation disposal joins its coalesced work and existing monitor
disposal. Hosting keeps callback owners and dependencies alive until those actual
barriers settle; the separate host-stopping condition closes readiness immediately
while graceful work drains.

The real HTTP collection test uses the actual SDK/exporter with controlled local
provider/source/handler boundaries, verifying literal families, seconds/count
units, central identity, finite dimensions, and absence of duration while a
callback or scope is unfinished. These controlled facts do not prove SQL
atomicity, production authentication, broker durability or full release
qualification. Actual native results belong to
[CIS-0022 verification](../../changes/CIS-0022/verification.md).
