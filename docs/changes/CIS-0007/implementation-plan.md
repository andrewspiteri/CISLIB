---
title: Startup and DI implementation scope and shared limits
type: delivery-plan
status: Approved
change_id: CIS-0007
authority: derived-outcome-authorization
---

# Startup and DI implementation scope and shared limits

**Authorized 16 November 2026.** Andrew Spiteri approved the prepared allocation
by replying **Approve proposed allocation**. See [actual authorization](authorization.json)
and [work allocation](work-allocation.json). The user selected
revision 1.0 for the module after the merged Heartbeat implementation. The
[source register, contract reconciliation and acceptance mapping](../../../planning/startup/README.md)
make that scope concrete. The imported technical source remains Draft; existing
accepted product documents and all completed CIS-0001–CIS-0006 owners stay intact.

## Bounded implementation

Deliver one `CIS.Startup` module in `CIS.Core`, ordinary Microsoft DI enrollment,
complete construction before sequential asynchronous startup, one retained
operation, existing readiness/Heartbeat integration and a runnable consumer.
Use the existing host and native MSTest projects. The runtime loop directly
awaits actual tasks in registration order. Parallel agent work does not create
parallel runtime initialization.

The [task inputs](task-contracts.json) record two coherent medium leaves.
Supported CIS task update/add operations resolved the original seed into
[canonical tasks](plan.json), retaining its identity and dependency ledger.
Canonical task state and actual completion evidence remain separate from this plan.

| Canonical leaf | Observable outcome, including tests, documentation and review | Dependency / ownership | Allocated share |
| --- | --- | --- | ---: |
| [WORK-001](agent-tasks/WORK-001.md): Core startup | `IStartable`, final descriptor/lifetime validation, complete real-DI materialization barrier, reference identity, sequential await, retained once-only terminal operation, cancellation/failure monitor, checked HB acknowledgement and useful diagnostics. Cover AC-02–AC-09 and document the usage contract. | Core writer owns `Startup` contract/coordinator, the narrow HB acknowledgement and focused Unit files. | 4,200 active s; 2 logical attempts; includes 100 restoration s |
| [WORK-002](agent-tasks/WORK-002.md): Application integration | Type Manager pre-build example; one awaited host bridge; actual Healthcheck/HB connection, stopping/publication boundary, live HTTP and existing pipe/probe tests, cache/reader/refresh example and guides. Cover AC-01 and AC-10–AC-15 plus actual host lifetime validation. | Compiled acceptance depends on core. Integration writer owns `Startup/Hosting`, narrow HB hosting edits, integration/business tests and example. | 6,600 active s; 2 logical attempts; includes 200 restoration s |

Each leaf has a bounded direct implementation and reuses existing modules. No
test-, documentation-, governance- or review-only delivery tasks are introduced.
Independent fixture/example preparation and read-only review may proceed after
the core seam stabilizes; integration completion waits for the core. One writer
per file. The root controller owns solution/project changes and serializes shared
native builds. Any later smaller decomposition stays within this same outcome
and total, preserving task identity/history through supported commands.

## Contract choices and host boundaries

- Use `CIS.Startup.IStartable` and one actual root. Only ordinary unkeyed singleton
  interface registrations enroll. No scan, custom registration language, keyed
  startup service support or second `ServiceProvider`.
- Retain the startup operation before calling user code. Resolve the entire
  sequence once before calling any Start; reference-deduplicate in first-occurrence
  order. Preserve the first host token and every terminal result without replay.
- Establish one existing critical condition monitor before participant
  materialization when Healthcheck is installed. Expected host cancellation is
  neutral; synchronous/async/contract failure and unexpected cancellation remain
  observable and prevent readiness.
- Use the awaited `StartedAsync` host phase so the real listener can serve health
  while the coordinator awaits blocked initialization. Keep health routing before
  the business 503 gate. Preserve health 500/200 and pipe exit 1/0 semantics.
- Validate `HostOptions.ServicesStartConcurrently = false` and
  `ServicesStopConcurrently = false` for this cooperating profile. Startup is the final lifecycle
  participant after HB; allow the known ordinary web-host tail by a narrow
  composition rule; reject an unclassifiable factory tail without resolving a
  second hosted-service graph. Close health/startup/HB gates before awaiting drains. Preserve
  existing standalone HB ordering checks outside Startup mode.
- Treat checked HB activation and final readiness as one coordinator completion
  boundary. HB's current void activation swallows infrastructure failure into its
  stopping state; readiness must require a positive bookkeeping acknowledgement.
  Do not wait for a first callback or aggregate healthy state.
  Test actual activation/timer failure and a stopped activation no-op after all
  startables succeed: neither may mark StartupCompleted, and cleanup still joins
  actual settlement.
- Cancellation/host wait timeout cannot dispose dependencies while startup or
  accepted HB operations remain active. The example explicitly joins true
  settlement on normal stop and failed startup before disposing its root. No
  rollback, retry, general `IStoppable` or new shutdown framework.

These reconcile existing accepted contracts; they are implementation selections,
not claims of earlier human approval or completed runtime behavior. Standard DI
scope validation is enabled on the actual application builder, not emulated by
a custom graph validator. Existing bounded Logging records stage/index/type,
outcome and elapsed time without retaining service graphs or raw secret values.

## One approved outcome allocation

The historical [authorization input proposal](authorization-proposal.json) requested
**10,800 active seconds (3 hours), 4 logical attempts and the existing shared
6 GiB physical counted-storage ceiling**. All agents, tests, review and settlement
share these totals. The total includes **300 restoration seconds** and storage
includes a **64 MiB restoration reserve**. A logical attempt is an accountable
work attempt, not a fresh allowance for each shell command or subagent.

Reuse storage group `CISLIB-CIS-0001-STORAGE-2026-11-10`; do not create another
6 GiB pool or erase prior claims. Count existing files before allocation and
reservation. Owned/counted roots are `.cis/local`,
`implementations/csharp` and `.artifacts/startup/CIS-0007`, retaining every older
root already in the shared group. The required scratch schema marker on the
source tree grants no source, user-file or historical-output deletion authority.
No cleanup controller is delegated. Preserve actual prior costs, used attempts,
uncertainty, reports and completed owners. Stop at a finite-capacity boundary if
safe completion or restoration no longer fits.

Task organization and resource distribution within the shared total are delegated
to Codex under the recorded actual user decision. The closed Heartbeat authority cannot
be reused for this new module; old unused capacity does not fund it.

## Execution and checkpoints

1. The actual outcome decision, concrete validated task contracts and finite
   allocation are recorded through supported CIS operations. Each task retains
   its own eligibility, dependencies, accounting and completion evidence.
2. Implement and verify core construction, order, identity and retained outcomes.
   Report the first useful checkpoint with actual test results, cost/storage and
   remaining integration work. No additional product approval is requested.
3. Implement the narrow host/HB connection and example, prove real listener and
   pipe transitions plus stop/drain races. Report the host integration checkpoint
   with actual results, cost/storage and any remaining gap.
4. Review responsibility growth explicitly, complete meaningful affected native
   checks, map exact test symbols/results to AC-01–AC-15, update guides and settle
   accounting in the owning tasks. Use canonical CIS completion operations with
   actual evidence; do not claim completion from prose or a passing placeholder.

Use SDK `10.0.401`, the current .NET/ASP.NET shared frameworks and approved
dependency lockfiles. No tooling upgrade or new harness is planned. Run affected
Startup/Heartbeat/Healthcheck/lifetime checks and relevant compatibility and
architecture boundaries using the existing projects; do not repeat broad suites
without changed inputs, failures or an unresolved concern. No visual UI or manual
test catalogue obligation arises from this library module. The small example's
business gate makes no database/repository/cache-miss claim requiring a new
product data architecture.

This allocation covers the Windows environment available here. It does
not authorize package publication, external source transmission, default-branch
merge or a new platform/release qualification campaign. Runtime implementation,
actual native checks, costs and the observed standard-DI limitation are recorded
in [verification](verification.md) and the [acceptance mapping](../../../planning/startup/acceptance.md).
