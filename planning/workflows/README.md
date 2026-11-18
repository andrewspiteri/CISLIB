# Workflows / Pipeline intake

This page preserves requirements intake and the accepted reconciliation. The
module is now implemented under P01–P12; actual native observations and limits are
in [CIS-0013 verification](../../docs/changes/CIS-0013/verification.md).
The initial request authorized import, reconciliation and planning. Andrew Spiteri
subsequently approved the concrete profile and shared allocation. The attachment's
sections 15–16 did not themselves grant authority or reopen earlier allocations.

The original [specification](sources/CISLIB_Workflows_Pipeline_Implementation_Specification_v1.0.md)
was copied byte-for-byte. SHA-256:
`92778b882a2eaeefc5693a18b85f0294258c20a73790b58f1b4f87f8b191b968`.
The source-directory attributes preserve its bytes. CIS registered
[CIS-0013](../../docs/changes/CIS-0013/proposal.md) against merged baseline
`22902b1f1176a59086fc0bfba2df3d789136dd64` on local branch
`codex/workflows-pipeline`. The untracked CountryData launch settings remain
untouched; ignored workflows-intake/baseline.json records their existing hash.
CIS-0011/0012 remain Closed, with completed owners and original costs preserved.

## Reconciliation with implemented contracts

The following accepted mappings resolve concrete integration choices in the
source. The [profile and plan](../../docs/changes/CIS-0013/implementation-plan.md)
record the actual approval and containing limits.

| Finding | Existing contract / recommended mapping |
| --- | --- |
| Packaging | Add handwritten public types under `CIS.Workflows` in the existing CIS.Core assembly, with C# DI/hosting adapters under `CIS.Workflows.Hosting`. Existing approved Microsoft dependencies and ASP.NET framework suffice. No new runtime package, NuGet mapping or collector upgrade. |
| Generic context | The source promises one shared mutable context but leaves TContext unconstrained. Recommend a non-null reference-type context; no cloning, snapshots of business state or context disposal. |
| Activation | Use the framework ActivatorUtilities.CreateInstance for every registration, including duplicate types; never resolve the step's existing singleton registration. Steps are pipeline-owned; resolved dependencies remain DI-owned. Default async scope and explicit caller-scope borrowing are different composition paths. |
| Cancellation | StartupCoordinator's broad stop catch is not the workflow rule. Freeze exact requested-token attribution at the observed phase. Checkpoint cancellation can leave all previously completed step reports Succeeded. Construction/disposal exceptions remain phase faults; a Failed primary OCE is wrapped by the new narrow PipelineExecutionException. |
| Cleanup budget | Source-selected default30seconds is a proposal to approve, not a historical user value. Keep it independent of execution cancellation and cooperative; expire does not abandon work. Add a bounded CleanupBudgetExpired report fact so successful handlers remain Succeeded even when they finished after a cancellation request. |
| Host ownership | A drain before Kestrel Stop can wait on request work whose abort token has not fired; Stopped alone can be too late for other hosted-service teardown. Close admission early, validate the workflow bridge among the existing cooperating bridges after ordinary business services and before the exact native web-host tail, then reverse Stop drains workflows after listener Stop and before ordinary business Stop teardown. Stopped and the facade's explicit/failure-cleanup drain remain final safeguards before provider disposal. A module-local count/zero-drain task suffices; no execution/task/history registry or scheduler. |
| Existing shutdown order | Preserve REST cutoff before Heartbeat/Configuration waits, native listener management, sequential Startup and the coordinator's independence. Existing Metrics Stop controls callback-owner access; already-attached counter/timer handles remain valid until provider disposal. Pipeline drain must finish before the owning provider/logging resources are disposed. |
| Factory selection | Offer normal idempotent AddCisWorkflows registration inside the existing CISApplication service callback. Opt in explicitly; unused hosts retain their current Metrics catalog and budgets. No new generic DI/configuration framework or second application provider. |
| Logging | Source defaults include per-phase Information events. They cannot be used unchanged inside a frequent Heartbeat callback. Recommend captured per-definition LogCalls=true with an explicit false opt-out for such callers; actual reports, errors and metrics stay intact. Use existing ILogger/CoreLoggerProvider, Activity/correlation, filters and bounded writers. |
| Metric clocks | ITimer.Start uses the Metrics runtime's own clock. Measure each actual duration once using the workflow's injected clock, place it in the report and pass those same seconds to ITimer.Record. Capture one reusable metrics binding at accepted entry; no replay or mid-run attachment. |
| Finite aliases | Dynamic builders need bounded definition metadata. Recommend transactional Build admission of at most16 pipeline names and64 pipeline/metric-step alias pairs, independent of whether collection is enabled. Explicit Add aliases are metric labels; an omitted alias keeps its type display name in reports/logs and uses the stable metric alias `step`. Never emit arbitrary CLR names as labels. |
| Capacity | Reserve3 streams with48/16/384 normal combinations (3P/P/6R), total448. Conditional full REST profile becomes30 streams/4864 combinations, or24/3776 without REST, within unchanged global96-definition/128-stream/16384-combination policy. Validate smaller explicit overrides before execution; do not silently overflow ordinary workload or increase global limits. |
| Example | Use cached country lookup: normalize identifier, find cached country, prepare response. Preserve current 200/400/404 bodies, trimming/case aliases, readiness and zero lookup I/O. A missing country supplies the middle-step expected-failure demonstration. Keep pipeline work out of hourly/frequent refresh and retain one runnable application. |
| Skill | Only cislib-metrics currently exists. Generate one repository cislib-workflows skill and supported agents metadata after its API/example compile. Use existing validation/generator tooling; no personal installation, copied specification or new evaluation harness. |

Host drain is a concrete module integration obligation, not a promise that calling
Dispose on an arbitrary provider makes unawaited work safe. Standalone consumers
must await their accepted executions and keep a borrowed scope alive. In the
facade, capture the actual instantiated workflow owner before late host-Build
failure can lose it, following the existing provider/logging ownership pattern.
Tests must prove held work and DI-owned singleton/scoped dependencies remain valid
through unwind and owned cleanup, including failed-start and canceled host waits.
The ordering check must cover web and nonweb hosts and reject incompatible
registration order rather than promise a drain that occurs too late. Resources
an application owner destroys on ApplicationStopping or StoppingAsync are outside
DI's disposal guarantee: that owner must first await the executions it started.
Do not claim that the pipeline can prevent arbitrary caller-owned teardown.

## Platform basis

Framework activation is available in the pinned platform; this plan relies on
[the .NET10 implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ActivatorUtilities.cs),
not the source link's currently displayed preview package version. External
activation and container-owned dependencies have separate disposal responsibility;
see [Microsoft DI guidance](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/guidelines).
Cancellation requests require actual cooperative settlement, as described by
[Microsoft's cancellation model](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads).
Histogram counts already provide completed-operation counts, per
[Prometheus guidance](https://prometheus.io/docs/practices/histograms/).
These references support the mapping; they do not authorize upgrades.

## Bounded evidence

WF-01–WF-10 and A-01–A-16 remain the source requirements. Use the existing MSTest
projects, CommonTesting's clock/logger, actual DI scopes, existing Metrics
fixtures and local-feed package consumers. One outside-friend three-step
middle-failure consumer can also be the fresh skill-use check. One targeted real
exposition check verifies all 3 families, identity, capacities and true phase/run
counts. Existing CountryData component/package fixtures remain component evidence;
do not relabel a replaced Program as unchanged Main.

The intake itself changed no runtime source or native result. Implementation and
verification followed the separate actual approval. Native source/package checks,
scoped formatting and skill validation are recorded in the verification record;
historical intake observations do not stand in for those results.
