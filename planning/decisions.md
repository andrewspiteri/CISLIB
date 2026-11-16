# Current decisions and obligations

This record preserves significant accepted decisions after the November 2026 cleanup. Original specifications under each module's `sources/` and the logging frontend BRD/design remain reference material. Their old handoff/task instructions do not govern the development loop. Current explicit user direction takes precedence.

## Architecture and behavior

- CISLIB is independent of CIS. C# is the implemented language; future language specifications do not authorize speculative implementations.
- One project, package and assembly is named `CIS.Core`, with root namespace `CIS` and module namespaces `CIS.Logging`, `CIS.Logging.MicrosoftLogger`, `CIS.TypeManagement`, `CIS.Healthcheck` and `CIS.Heartbeat` (with separate `Hosting` and `Healthcheck` adapter namespaces). Application code owns runtime lifecycle.
- Core keeps its approved Microsoft NuGet dependency closure. The user expressly approved `Microsoft.AspNetCore.App` in Core for Healthcheck; HTTP integration stays in the same package. Third-party runtime dependencies belong in separate optional packages. See the current [dependency policy](../docs/architecture/csharp-dependency-policy.md).
- Target `net10.0`, pinned SDK `10.0.401`, selected qualification runtime `10.0.12`, nullable analysis and warnings as errors remain. Use the existing native C# MSTest framework and IDE test support.
- Preserve Logging defaults and bounded behavior, custom writers, processing/interpolation, the rotating incoming frontend, output independence, explicit flush/stop observations and ownership. The user-selected maximum entry default is 64 KiB; oversized payloads truncate within the configured whole-record bound. `BeginScope` uses creator-disposes/LIFO with documented detection limits.
- `EnvironmentOptions` is the user-selected readonly static container with first-access constructor defaults followed by known overrides and bounded identity interning. Its readonly `Logging` and `Heartbeat` references contain mutable module setup; Heartbeat copies and validates effective settings once per runtime; `EnvironmentKind`, `CommitId` and `BuildId` remain caller policy. Earlier initializer/passive-store candidates are superseded.
- Type Manager discovers definitions without constructing application objects, retains fixed prepared membership and cached queries, and owns automatic SDK inventory generation. `CIS.TypeManager.Build` and package SDK/targets are necessary product machinery. Supported inventory profiles are framework-dependent, untrimmed .NET 10, Windows/Linux x64, conventional build or folder publish; no plugin, arbitrary load-context, AOT, trimming, single-file or self-contained promise is added.
- Healthcheck preserves monitor policies, detached snapshots, owner-safe collection, explicit execution-fact binding, HTTP/local-pipe/probe behavior and source examples. Module options resolve their own variables explicitly; the readonly static environment mechanism is unchanged. [Technical intent](healthcheck/technical-intent.md), [catalog](healthcheck/contract-catalog.md), [execution facts](healthcheck/execution-facts.md) and the collection/execution addenda define the retained behavior.
- The MIT licence and maintainer-only official repository policy remain.

## CIS adoption checkpoint

At its accepted checkpoint, the CIS-0001/WORK-001 bounded adoption result was independently verified: two existing native cases passed, with zero failures, zero skips and zero pending unit batches. The approved product records, plan, original authority and finite limits remain in force; no engineering-quality policy was adopted. WORK-001 is Complete through the supported transition, with no active reservation or recovery hold. Its canonical plan has one task and no queued successor. Later cache-currentness observations are recorded below and do not rewrite that checkpoint; broader qualification obligations remain unallocated.

The original interrupted disposable invocation is retained as inconclusive, with unknown duration conservatively charged at its original 300,000 ms bound. Controlled disposal preserved its history and used attempt. The genuine retry consumed 133,787 ms: total consumption is 433,787 ms, with zero reserved and two attempts used out of the task's two-attempt limit. No original-host namespace proof is required for this disposable-unit retry.

The retry initially returned invocation exit 3 because the CIS reader compared equivalent absolute executable path spellings literally. Its native report still records 2 passed / 0 failed / 0 skipped. The corrected reader in [CIS d36e2aa](https://github.com/andrewspiteri/CIS/commit/d36e2aa39e4a14d8f2a4656c5e4a62bf32c1bf53) verifies that retained result without rerunning native tests. Read-only unit plan/status report zero pending work. Full qualification remains false and release thresholds remain unsatisfied; this bounded result does not close the release obligations below. The [continuation checkpoint](cis/cleanup-disposition.md#practical-workflow-repair-continuation) records the execution and current closure state.

## Compatibility and release

The publication ledger has **zero publications**. Development version `0.1.0-dev.1`, historical checkpoints, API self-comparison and frozen development consumers are not released historical baselines. Keep real current consumer compatibility and meaningful frozen API/configuration/wire tests. Preserve every actually published contract indefinitely from its first publication, including prereleases; version changes do not waive this commitment. Assembly identity remains `CIS.Core`, neutral culture, unsigned, `AssemblyVersion` `1.0.0.0`; package/file/informational versions identify individual releases.

The accepted release scope includes unit, integration, mutation, architecture, backwards compatibility and business behavior, plus useful performance observations. Coverage obligations remain **95% lines / 90% conservative branches** across shipping Core and the build helper. Mutation retains its **90% conservative gate**, complete declared source scope, no Timeout kill credit, and actual product activation where child processes execute mutated code. Native tooling may carry out these obligations without the retired receipt/campaign framework. A build or narrow test pass does not close them.

Windows 11 x64 and Ubuntu 24.04 x64 are the selected initial lanes. Actual platform/package consumer execution is required before advertising qualification; other OS/runtime/deployment profiles are not implied. Recorded performance targets remain 30 seconds warm, 60 seconds unchanged build plus tests, and 300 seconds full regression, unverified for this revision. No governed budget or limit is increased. This cleanup does not run a campaign or reintroduce its harness; applicability must be evaluated for an explicitly authorized future measurement. The former campaigns' 1.5 GiB owned ceiling, 2 GiB free reserve and exact attempt/approval ceilings retain their original scope; retiring a campaign does not authorize rerunning or enlarging it.

## Current gaps

- No all-scope release qualification is claimed. The last pre-cleanup Source47 snapshot had scoped passing native/API/package results, an incomplete mutation pilot/control sequence and stopped/partial shipping coverage. No Full47 ran. Earlier scores/results do not transfer to the cleaned source.
- Heartbeat now has a local scheduler, explicit owner/DI scope barriers, a narrow Generic Host adapter and real Healthcheck binding. [CIS-0006 verification](../docs/changes/CIS-0006/verification.md) records actual scoped Windows observations and unresolved qualification. A generic startup-provider framework remains absent; no arbitrary external lifecycle integration is implied.
- Complete current platform, exact-package/example, coverage, mutation and release acceptance remain unexecuted unless individually recorded by subsequent work. Windows remote/alternate-user/elevated-identity rejection, privileged Unix denial and deployment/container-specific behavior require their actual environment when claimed.
- Type Manager clean-package Console/Web consumption passed on actual Windows 11 x64 and Ubuntu 24.04 x64 with SDK 10.0.401 and NET/ASP.NET Core 10.0.12, framework-dependent and untrimmed, covering ordinary build, folder publish and matching no-build publish. See [CIS-0002 verification](../docs/changes/CIS-0002/verification.md). Visual Studio/MSBuild.exe task hosting, Visual Studio `PublishUrl` copying and nonconventional helper output layouts remain outside the qualified profile; full release qualification remains separate.
- Publication destination, signing/distribution policy and the final supported API/behavior baseline must be settled before first publication. Licence is already MIT.

Historical campaigns, duplicate source snapshots, generated receipts and obsolete active instructions were retired. Their removal does not establish a pass or require replay. Exact historical bytes are available only if retained in Git history or separately identified user data; this record makes no continuing custody/availability claim. No unresolved product defect is inferred from a retired harness timeout or receipt failure.

CIS adoption retains the bounded unit result above; its CIS-0001 plan queues no
successor. Separate [CIS-0002/WORK-001](../docs/changes/CIS-0002/agent-tasks/WORK-001.md)
is Complete after the separate approved Type Manager package-consumer integration,
focused native Windows/Ubuntu checks, instructions and review under its own finite allocation.
Its [verification record](../docs/changes/CIS-0002/verification.md) records the
actual passing package outcomes and completion checks. Broader native qualification
retains its actual authorization requirements. The retired mechanisms stay retired,
and this task starts no new module or CIS correction pass.

Separate [CIS-0003/WORK-001](../docs/changes/CIS-0003/agent-tasks/WORK-001.md) is
Complete after the approved ConsoleHuman Logging example and focused real-package
integration. Two native cases passed on each actual Windows/Ubuntu lane using
identical source and package bytes. Its
[verification record](../docs/changes/CIS-0003/verification.md) records current reuse,
review, cleanup and settled work under the separately authorized finite allocation.
Both earlier owners stay Complete with their accepted results and accounting.
The new shared-outcome [CIS-0004/WORK-001](../docs/changes/CIS-0004/agent-tasks/WORK-001.md)
closes implementation/developer-package integration for all three features.
[Its verification record](../docs/changes/CIS-0004/verification.md) reuses the
accepted Logging/Type Manager results and records six Healthcheck package cases
plus four affected source stories passing on each actual Windows/Ubuntu lane.
The exact retained candidate is unchanged. The same CountryList source supports
ordinary package restore/build/folder publish and matching HTTP/pipe/probe use;
Core contracts, defaults, dependencies and application ownership are unchanged.

The bounded 15 November diagnosis could not identify the earlier effective-input
fingerprint difference because the original individual input/environment basis
was not retained. The new Windows package cache also reports noncurrent despite
its genuine settled six-case pass. These currentness limitations remain explicit;
no report/fingerprint rewrite or demonstration replay was used. New Linux receipts
and the Windows source plan initially reported current. After closeout metadata
and commit `601b3d21`, read-only checks also held Windows source and Linux package
reuse; Linux source was not rechecked past the fail-fast package hold. Retained
native passes are not unconditional cache-currentness claims.
All earlier owners retain their exact consumed work, attempts, completion and
accepted reports. Full release obligations above remain open; this milestone
adds no Heartbeat scheduler, generic startup coordination or publication.

## Heartbeat implementation — 16 November 2026

Andrew Spiteri authorized the Heartbeat v0.2 implementation under [CIS-0006](../docs/changes/CIS-0006/authorization.json), preserving the actual EnvironmentOptions architecture, single Core assembly and approved dependencies. The engine uses weak idle ownership, counted real lifetime holds, monotonic fixed delay, bounded FIFO dispatch and true unregister/shutdown settlement. A separate adapter reports detached facts through existing Healthcheck policy logic. Applications retain responsibility for their actual scope/root lifecycle and business deadlines.

Use the [Heartbeat guide](../docs/modules/heartbeat/README.md), [Polling example](../implementations/csharp/examples/heartbeat/Polling/README.md) and [verification record](../docs/changes/CIS-0006/verification.md) for implemented usage and actual evidence. This checkpoint does not alter earlier owners or historical results, establish Ubuntu/full-release qualification, or allocate coverage, mutation, performance, publication or a generic startup framework.
