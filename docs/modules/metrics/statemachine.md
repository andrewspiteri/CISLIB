---
title: State Machine metrics and structured logging
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.statemachine
---

# State Machine observations

Explicit registration uses the current host and one provider:

```csharp
using CIS.Workflows.Hosting;

builder.Services.AddCisStateMachines();
```

The optional Metrics integration recognizes this passive registration in either
order relative to `AddCisMetrics`. It attaches one reusable three-handle bundle
before hosted application work. Pipeline-only and unused hosts retain their
existing catalogs. Add Pipeline explicitly when a handler needs its separate
operation boundary. See the [fixed catalog](../../../contracts/metrics/statemachine.md)
for capacities, dimensions and required exported families.

One trigger histogram covers every public operational call, including Busy,
rejection and pre-cancellation. Its count supplies call volume by outcome.
Active attempts count only ownership of the machine gate, including admitted
mapping rejections and actual compensation/disposal. Handler histograms describe
only entered Execute/Error methods. Direct transitions and never-started methods
do not create handler observations. Report and metric seconds come from the same
monotonic measurement. Unknown measurements remain absent while active balances.

The finite Build-time metadata union holds sixteen machine aliases, 512 complete
machine/source/trigger tuples and sixty-four machine/handler pairs. These limits
apply even without collection. Reusing an alias/domain does not consume new
capacity. Different definitions add their distinct label tuples transactionally.
Use stable configured aliases rather than per-entity names. Undefined enum values
become `invalid`; a declared member with that name becomes `enum:invalid`.
Unaliased handlers aggregate under `handler`, retaining their CLR display names
only in reports/logs. No instance IDs, arbitrary numeric trigger strings or
business failure codes become labels.

The `CIS.Workflows.StateMachine` logging category uses events 13100–13105:
call started, handler phase started/completed, cleanup expiry, lifecycle failure
and one completed summary. Routine events, Busy/rejection/cancellation and phase
starts default to Debug. `StateMachineOptions.LogRoutineAsInformation=true`
promotes successful routine completions only. Expected work failure is Warning;
unexpected originating faults log at Error with their original exception once.
Cleanup expiry is Warning and secondary faults remain visible. Terminal events
contain outcome/state, duration, a bounded code and secondary count without
duplicating exception stacks.

Fields preserve machine, attempt/correlation identity, source, trigger, intended
target, final state and actual phases. Existing logger correlation and filters
remain authoritative. Explicit safe business messages are sanitized and bounded
to 512 scalars, codes to 128; context and payloads are not serialized. Logging
exceptions cannot change commitment, outcome, compensation or disposal.

`LogCalls=false` is a captured per-definition opt-out for callers whose existing
frequent-callback policy owns bounded diagnostics; it does not suppress reports
or metrics. Child Pipelines retain their own logging policy. There is no new
logging runtime, automatic readiness policy, flush-per-trigger, scheduler,
current-state-per-entity gauge or metric history.

The [CIS-0014 implementation plan](../../changes/CIS-0014/implementation-plan.md)
tracks delivery and focused native evidence. Source and authored fixtures alone
do not establish exporter, platform, coverage or release qualification.
