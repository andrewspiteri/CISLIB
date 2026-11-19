---
title: Workflow metrics and structured lifecycle logging
type: document
status: draft
cis:
  stable_id: cislib.document.modules.metrics.workflows
---

# Workflow observations

Opt in through the existing service collection. Core-only callers require no
collector, provider or SDK types:

```csharp
using CIS.Workflows;
using CIS.Workflows.Hosting;

builder.Services.AddCisWorkflows();
```

When the optional Metrics package is installed, its existing activation attaches
one reusable three-handle bundle before hosted application work. Both
`AddCisMetrics(); AddCisWorkflows();` and the reverse order use the same final
conditional catalog. Without workflow registration, existing catalogs and explicit
small profiles remain unchanged. Consult the [fixed catalog](../../../contracts/metrics/workflows.md)
for names, dimensions, finite capacities and expected exporter families.

Build admits up to sixteen distinct pipeline names and sixty-four pipeline/step
alias pairs transactionally. Repeated aliases reuse capacity, including repeated
registration positions. Each explicit `Add<TStep>("find")` alias is a bounded
metric value. Omitted aliases aggregate under `step`; their type display names
and positions remain in reports/logs. Names must be printable and fit 256 UTF-8
bytes. Validation applies with collection disabled, making workload admission
independent of instrumentation selection.

Whole-run duration and active work last through actual owned cleanup. Step
duration describes only an entered Execute or Error method. Report durations
and emitted seconds are the same measurements from the runner's clock, including
when tests inject a controlled clock. No duration is fabricated for an unentered
method. Late attachment does not replay work; each accepted entry keeps its
original binding until completion. Recording failures do not change execution,
primary exceptions, cancellation attribution or reverse handling.
An unavailable final clock measurement remains unknown: active work still
balances and the terminal summary is emitted, while no overall duration is fabricated.

The fixed `CIS.Workflows.Pipeline` logging category uses events 13000–13005:
accepted, phase started, phase completed, cleanup budget expired, lifecycle failed
and one terminal completed summary. Accepted runs log at Information, starts at
Debug, successful phases at Information, reported failures at Warning, and faults
at Error. Identified forward cancellation is Information; cleanup-budget expiry
and its interrupted handler are Warning. A terminal summary uses Information for
success/cancellation, Warning for an expected failure, and Error when a primary
fault or secondary failure requires it.

Structured fields include pipeline, execution ID, registration position/display
name, finite phase/outcome, measured seconds, failure code and final counts.
Existing provider/Activity correlation stays authoritative. The implementation
does not serialize business contexts or response payloads. Unexpected faults pass
their original exception once to the existing logger at the originating phase or
lifecycle event; the terminal summary carries only counts and the failure code.
Explicit safe business messages are sanitized and bounded to 512 scalars; codes
are bounded to 128 scalars. Exception messages are not inserted into templates.
It borrows the configured logger and does not flush per run or create a new
logging runtime. Full retained failures remain available through the execution report.

`PipelineOptions.LogCalls` defaults to true and is copied by Build. Explicitly
choose false where an existing frequent-callback diagnostic policy already owns
reporting; it suppresses automatic lifecycle logs while reports and metrics
remain intact. Central logger filtering also applies normally. A throwing logger
does not interrupt business work, unwind or cleanup.

Native results are recorded under [CIS-0013](../../changes/CIS-0013/implementation-plan.md).
Authored API/source/tests do not establish exporter, platform, coverage or release
qualification.
