# Pipes source-partition diagnostics

These four new configs support bounded iteration on the existing Pipes gate.
They are diagnostic test closures, not automatic qualification receipts. The
[full Pipes config](../pipes.json) remains unchanged by this authoring task and
remains the final gate unless the coordinator validates a complete equivalent
partition matrix against its full-scope obligations. No native execution is
recorded by adding these files.

| Config | Exclusive production source, relative to CIS.Core | Selected test closure |
| --- | --- | --- |
| `budget.json` | `Healthcheck/Pipes/HealthPipeBudget.cs` | Complete pipe integration class, internal unit seams, real selected-view probe method, scheduling assertions |
| `endpoint.json` | `Healthcheck/Pipes/HealthPipeEndpoint.cs` | Same closure as budget |
| `server.json` | `Healthcheck/Pipes/HealthPipeServer.cs` | Same closure as budget |
| `probe-client.json` | `Healthcheck/Pipes/HealthProbeClient.cs` | Complete real probe process class, internal unit seams, scheduling assertions |

The mutate arrays are disjoint and their union is exactly all four production
Pipes source files in the full config. Each keeps Complete mutation level, all
mutator kinds, 90% low/break thresholds, coverage analysis off, two native sessions,
disabled bail and the existing 10,000 ms additional native budget. There are no
line ranges, ignored mutations, ignored methods, platform source exclusions or
child-deadline changes. Existing integration classes need no new filter mapping.

The precise common class/method identities are:

- `CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckPipeTransportTests`
- `CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests`
- `CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckProbeProcessTests.ActualProbeProcessExitsExactlyForSelectedCompleteView`
- `CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode`

The selected-view method currently has nine real-process rows: healthy/unhealthy
health, ready/not-ready readiness, healthy/unhealthy details and disabled-details
failure. It exercises the actual local server plus matching probe and provides
actual child origins for the three server-side partitions. The full probe class
also covers raw fragmented/malformed replies, defaults, argument limits, complete
stdout and byte/deadline boundaries. Its selected-view rows already start the
real `HealthPipeServer`, so an additional full pipe class is not needed for the
Client-only diagnostic closure. Internal seams cover caller resource ownership,
cancellation, failed output/error destinations and exact budget/timer behavior.
Tests irrelevant to a selected source can still run; this table is not a claim
that every generated mutant is reachable or killed.

Run native producers only from a fresh frozen source copy using the pinned tool,
SDK and runtime, with independent outputs and short Windows roots such as
`.artifacts/hcc/<run>`. Prepare the current child-origin session with the actual
config before Stryker, retain its source/config/tool pins and raw native report,
then run the existing child audit with `--require-kind probe`. A passing audit is
mandatory for child propagation, separately from the mutation result. Keep actual
parent/child Core hashes, owner, selector, argv, terminal and timeout evidence.
No process/compiler/infrastructure failure, missing receipt or timeout becomes a
kill because the native tool labels a test session Killed.

Final matrix reconciliation must verify identical frozen source/tool/runtime and
test closures, passing native baselines, exact disjoint source union, all native
mutant/status inventories and absent denominator drift. Reconcile by source
hash/path/span, mutation kind and replacement rather than numeric IDs alone.
Preserve compiler errors, surviving/no-coverage mutants, timeouts and ignored
statuses; never average percentages, sum duplicates, infer unexecuted platforms or
credit only a selected passing partition. The final full-scope gate remains open
until that work and the complete required detector closure actually pass.
