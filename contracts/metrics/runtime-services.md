# Runtime service observations

These definitions observe actual Logging, Heartbeat and Healthcheck owners through
the optional Metrics integration. Applications do not add recording calls. Core
uses the Metrics facade and has no collector SDK dependency. Names in this table
are logical names; the selected exporter applies its verified unit/counter suffixes.

| Logical name | Kind / unit | Dimensions and operation boundary |
| --- | --- | --- |
| `cis.logging.records` | Counter / Count | `outcome=accepted\|partially_accepted\|filtered\|rejected\|invalid\|stopped`; one returned frontend write disposition. A caller factory that throws has no returned disposition. |
| `cis.logging.processing.records` | Counter / Count | `outcome=routed\|suppressed\|failed`; one actually claimed incoming occurrence through backend preparation and dispatch. `routed` means dispatch was attempted, without promising writer admission or delivery. |
| `cis.logging.discarded.records` | Counter / Count | `reason=overwritten\|shutdown`; one frontend occurrence actually displaced by rotating admission or removed by shutdown/fault cleanup. Repeated attempts that remove nothing add nothing. Output rejection/abandonment is not counted again here. |
| `cis.logging.writer.failures` | Counter / Count | No dimensions; an actual output write or buffer flush that throws. Cleanup, rendering and clock faults are not writer failures. |
| `cis.heartbeat.execution.duration` | Timer / Seconds | `outcome=success\|error\|cancelled\|skipped\|suppressed`; first actual condition/callback entry through awaited recovery, cleanup and outcome reporting. Uses the standard fourteen bounds. |
| `cis.heartbeat.active.executions` | Up/down counter / Count | No dimensions; started executions whose same settlement boundary has not ended. Queued eligibility and posted-but-unstarted wrappers add nothing. |
| `cis.healthcheck.evaluation.duration` | Timer / Seconds | `outcome=success\|error\|cancelled`; actual collector invocation through result acceptance, excluding subsequent lease disposal. Uses the standard fourteen bounds. A valid unhealthy result is a successful evaluation. |
| `cis.healthcheck.ready` | Observable gauge / Count | No dimensions; the existing readiness rule as zero/one: startup completed, not stopping and no critical unhealthy monitor. |
| `cis.healthcheck.monitors` | Observable gauge / Count | `state=healthy\|unhealthy\|pending`; current aggregate active monitor counts, including zero counts. |

Heartbeat `error` includes a false callback, user exception and an existing final
failure caused by cleanup. `cancelled` uses the engine's existing expected
cancellation classification. `skipped` requires an actually invoked false
condition. `suppressed` can describe a started condition followed by a closed
callback gate; fully unstarted suppression produces neither duration nor active
work. Reporting infrastructure failures retain their existing diagnostic path;
Metrics does not rewrite the engine's result.

The detached Heartbeat `Settled` notification follows actual work-task completion
and is outside the duration; queued delay and registration retirement are also
outside it.

Each operation captures its binding once at entry and completes against that
same binding. Attachment during an admitted Heartbeat execution, actual collector
evaluation or output write cannot replay it. Logging frontend returns, backend
processing and output I/O are distinct operations, so later stages may begin
after attachment even when the originating call preceded it. A complete handle
bundle is constructed before publication. Each mutable module owner accepts one
host runtime; same-runtime attachment is idempotent and a second host is rejected.

Healthcheck gauge callbacks only read existing state under the state gate. They
do not invoke collectors, sweep weak ownership, dispatch notifications, refresh
configuration or change readiness. Collected weak handles are excluded from the
read, while actual dead-owner retirement remains the existing module lifecycle's
responsibility. Observable callback admission and its actual
materialization drain belong to Metrics; the host must retain observed owners
until that barrier succeeds.

All labels come from the finite vocabularies above. Logger categories, monitor or
work identifiers, messages, exception text and arbitrary type names are not
labels. No unaccounted spool-byte gauge is invented. Recording failures are
isolated from the original operation, health policy and Heartbeat infrastructure
failure channel. Without attachment, existing module behavior remains unchanged.
These boundaries describe the implementation; native result receipts and broader
platform/release qualification are recorded separately in the change evidence.
