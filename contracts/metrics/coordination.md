# Coordination and jobs metric catalog

Explicit coordination composition adds four instruments; explicit jobs composition
adds three. `CollectCoreModules=false` or absent composition adds none of those
reservations. The existing optional collector owns the one provider, views,
aggregation, exporter and authoritative application/environment labels.

| Logical name | C# instrument / unit | Dimensions | Capacity | Exported family |
| --- | --- | --- | ---: | --- |
| `cis.coordination.operation.duration` | Timer / seconds | operation, outcome | 32 | `cis_coordination_operation_duration_seconds_{bucket,sum,count}` |
| `cis.coordination.active.leases` | Observable gauge / count | none | 1 | `cis_coordination_active_leases` |
| `cis.coordination.ownership.losses` | Counter / count | reason | 6 | `cis_coordination_ownership_losses_total` |
| `cis.coordination.guard.rejections` | Counter / count | reason | 8 | `cis_coordination_guard_rejections_total` |
| `cis.jobs.invocation.duration` | Timer / seconds | mode, outcome | 10 | `cis_jobs_invocation_duration_seconds_{bucket,sum,count}` |
| `cis.jobs.active.invocations` | Observable gauge / count | mode | 2 | `cis_jobs_active_invocations` |
| `cis.jobs.pending.dispatches` | Observable gauge / count | none | 1 | `cis_jobs_pending_dispatches` |

Timer is the C# seconds histogram, recording the owner's existing known elapsed
time. Both duration streams use the existing fourteen finite bounds. Histogram
count supplies completed-operation count; there is no duplicate count counter.
The exporter also reserves the normal base/created child names under its existing
policy. Every actual sample retains central `application` and `environment`.

The complete finite value sets are:

- Physical operation: `acquire`, `renew`, `release`, `resolve`.
- Physical outcome: `succeeded`, `busy`, `refused`, `unavailable`, `indeterminate`,
  `not_observed`, `cancelled`, `failed`.
- Loss reason: `expired`, `superseded`, `renewal_deadline_exceeded`,
  `authority_unavailable`, `shutdown_deadline_exceeded`, `caller_cancelled`.
- Guard reason: `missing`, `released`, `expired`, `superseded`, `wrong_authority`,
  `wrong_database`, `invalid_transaction`, `authority_unavailable`.
- Job mode: `continuous`, `scheduled`.
- Job outcome: `success`, `failed`, `cancelled`, `ownership_lost`, `indeterminate`.

Physical duration starts immediately before provider invocation and ends at the
actual returned result/fault, including calls that finish after local timeout.
Local timeout alone emits no completed observation. Actual canceled provider tasks
or identified physical cancellation produce `cancelled`; an unsignalled faulted
OperationCanceledException remains `failed`. Cancellation establishes neither
rollback nor current authority availability. Busy is contention; refused is a
structured provider refusal; NotObserved remains a read fact, not absence proof.

Loss counts exactly one terminal local transition, independently of cancellation
callback settlement. Guard rejection counts only a returned rejected admission;
the wrapper forwards the original transaction, grant, token, result and exception.
It creates no transaction, retry or completion owner.

Job duration spans actual entered invocation through actual owned execution-scope
cleanup. An unfinished callback, unfinished cleanup or unknown elapsed time has
no completed-duration observation. MoreWork/Idle and Completed/PreviouslyCompleted
map to success. Original Unknown remains indeterminate; cancellation/loss cannot
rewrite a known committed completion. Pre-admission refusal emits no invocation
duration. Active invocations include callbacks ignoring cancellation and unfinished
scope cleanup. Pending dispatches represent actual bounded local admission work.

Seven definitions/streams reserve **60 combinations** and 19 generated/name
reservations. Full existing module composition becomes **51 definitions**, or
**55 streams** with both native HTTP pairs, within unchanged 96/128 ceilings.
Default security capacity sixteen gives **8,901 configured combinations** within
16,384. Larger explicitly configured combinations retain normal startup capacity
validation. A stream override cannot shrink away its finite accepted profile.

Job, resource, instance, source, occurrence, acquisition, generation and arbitrary
category values are excluded from metric dimensions. They belong in bounded
diagnostics. Scrapes use cached state and perform no database/provider work,
authorization, discovery, renewal or callback admission.

See [observability ownership](../../docs/modules/coordination/observability.md)
and [CIS-0022 verification](../../docs/changes/CIS-0022/verification.md) for actual
native/exporter evidence and remaining qualification limits.
