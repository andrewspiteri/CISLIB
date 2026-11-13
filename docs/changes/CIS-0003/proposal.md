---
title: "Complete the Logging example and real package consumption"
type: change-proposal
status: Proposed
change_id: CIS-0003
outcome: "An application developer runs the existing ConsoleHuman example from source or the exact CIS.Core development package, performs an asynchronous contextual operation with real human or JSONL/file records, observes truthful write/flush/stop and actual completion outcomes, and verifies ordinary build/folder-publish behavior on pinned Windows and Ubuntu without reopening CIS-0001 or CIS-0002."
baseline_kind: git
baseline: "385b88556e53cc24f337817cab53180cf26930c6"
graph_build_id: "sha256:55ddaa8300bc2ef02d0e335fe9ff002e5045727d73315193a7b28f06f3f8b676"
impact_roots: "[{\u0022Id\u0022:\u0022cislib::source-file::implementations/csharp/examples/logging/ConsoleHuman/Program.cs\u0022,\u0022Kind\u0022:\u0022source-file\u0022}]"
repository_baselines: "[{\u0022RepositoryId\u0022:\u0022cislib\u0022,\u0022BaselineKind\u0022:\u0022git\u0022,\u0022Baseline\u0022:\u0022385b88556e53cc24f337817cab53180cf26930c6\u0022,\u0022WorkingTree\u0022:[],\u0022CreationInputs\u0022:null}]"
authority: human-reviewed
---

# CIS-0003: Complete the Logging example and real package consumption

## Outcome

An application developer runs the existing ConsoleHuman example from source or the exact CIS.Core development package, performs an asynchronous contextual operation with real human or JSONL/file records, observes truthful write/flush/stop and actual completion outcomes, and verifies ordinary build/folder-publish behavior on pinned Windows and Ubuntu without reopening CIS-0001 or CIS-0002.

## Impact roots

- `cislib::source-file::implementations/csharp/examples/logging/ConsoleHuman/Program.cs` (source-file)

## Constraints

- Human authority is required for impact disposition and plan approval; `cis plan derive` may carry forward an exact current feature approval without requesting it again.
- Scope expansion must be recorded as a new finding or decision.

## Acceptance criteria

- The same readable ConsoleHuman source creates an application-owned runtime and logger, performs an awaited operation in its creator-owned scope, and emits useful named records with typed context. Existing defaults, environment overrides and the 64 KiB entry limit remain intact.
- Write observations identify incoming admission, not delivery. The application observes flush and stop completion/success/loss, then actual runtime completion and cleanup. Failed startup, application execution or shutdown produces useful diagnostics and a nonzero process exit; a timed-out stop remains unsuccessful even if cleanup later finishes.
- Clean isolated consumers use the exact development package through an ordinary PackageReference, without a Core source reference, Type Manager opt-in, inherited repository imports, warm Core cache or old assets. Normal build and ordinary framework-dependent folder-published output execute the actual example successfully.
- Focused native assertions cover human output, JSONL/file records and operation/structured context, normal flush/shutdown and actual cleanup completion. One cheap startup-failure check covers the new example exit path. Existing Logging semantics tests remain reusable and unchanged.
- Actual Windows 11 x64 and Ubuntu 24.04 x64 checks use SDK 10.0.401, NET/ASP.NET Core 10.0.12 and the same candidate. An unavailable platform remains an outstanding item, never an inferred pass.
- Existing README instructions describe source/package setup, normal build/run/publish, format/file configuration, expected output and ownership. Focused independent review and settled work precede task completion. Compact native/per-platform results remain; disposable consumers/caches/source build copies are cleaned.

## Remaining gap and proposed single task

ConsoleHuman currently prints configuration metadata without starting logging or
writing records. The existing runtime already supplies the required APIs.
CandidateLoggingProcessTests and LoggingBusinessStories cover output formats,
structured context, lifecycle outcomes and ownership, but run different probe
applications. Replaying those suites would not verify this example.

One medium implementation task will own the example, focused package checks,
README, review and completion. The explicit Program.cs impact anchors that task;
no feature pack or new foundational approval is needed. The scope is medium
because it joins application lifecycle, isolated package setup and two actual OS
lanes while preserving working public APIs and existing test infrastructure.

The direct application flow uses TryCreate, CreateLogger, BeginScope, LogEventData,
FlushAsync, StopAsync, DisposeAsync and Completion. A small async application
method owns and disposes its scope around its awaited work. Outcome summaries
remain ordinary application output, separate from log records. Completion has no
timeout: awaiting it observes actual workers/resource cleanup, rather than
claiming that a bounded stop or disposal establishes that fact.

Reuse or minimally extend CandidatePackageFixture's existing feed/cache isolation,
package checks and process/directory helpers. Its probe protocol must not become
the example interface. Keep its existing tests unchanged; clear the fixture's
16 KiB test override when exercising this example's 64 KiB default. Add no logger
abstraction, runner, replay/evidence framework or generalized package platform.
Change production code only for a demonstrated in-scope defect.

## Retained package and prior work

Candidate `CIS.Core.0.1.0-dev.1.nupkg` remains available under
`.cis/local/testing/type-manager-package-integration/candidate/`: 237,842 bytes,
SHA-256 `de794bafcee21b8a667bb3558e45242fddbab3f8eb7ca3b04974c5037628d93e`.
Its Core DLL is `ffcaf5b367ae901e312dfcc019ca6846cb2f38f31db5cb615d14964f0ba2266a`.
Bounded Git comparison from `599b5c5e` to current baseline `385b8855`, and against
the working tree, found no changed shipped Core/helper/package/configuration or
root README inputs. Reuse these bytes while those inputs remain unchanged;
example/test-only changes do not require repacking production binaries.

Keep the packed root README unchanged for this task. Update the existing Logging
example README and current task/completion references instead. Do not modify or
replay CIS-0001 or CIS-0002 profiles, reports, source/assertions, approvals,
completed lifecycle or accounting. At ledger revision `:106`, both owners have
no open attempt, unsettled execution or hold. CIS-0001 retains 433,787 ms and two
used attempts; CIS-0002 retains 602,214 ms and one used attempt.

## Proposed finite allocation and controller

This is a review proposal, not applied authority. Apply only after the actual
generated plan/task has been approved. Original authority: Andrew Spiteri;
controller: Codex. Proposed new owner: CIS-0003/WORK-001.

| Limit | Containing change | Sole task |
| --- | --- | --- |
| Active time ceiling | 3,600 seconds | 3,600 seconds, fixed/no redistribution |
| Logical attempts | 3 | 3 |
| Protected restoration time | 180 seconds included | 180 seconds included |

Both platforms share this one allowance. Reuse the existing shared storage
ceiling `CISLIB-CIS-0001-STORAGE-2026-11-10`: 6,442,450,944 total bytes including
existing files, with 67,108,864 restoration bytes. No new ceiling, historical
cleanup delegation or engineering-quality policy is proposed. Native private
scratch is permitted. The exact fifteen roots are the existing CIS-0002 roots:

```text
.cis/local
implementations/csharp/src/CIS.Core/bin
implementations/csharp/src/CIS.Core/obj
implementations/csharp/eng/CIS.TypeManager.Build/bin
implementations/csharp/eng/CIS.TypeManager.Build/obj
implementations/csharp/tests/CIS.Core.TestSupport/bin
implementations/csharp/tests/CIS.Core.TestSupport/obj
implementations/csharp/tests/CIS.Core.IntegrationTests/bin
implementations/csharp/tests/CIS.Core.IntegrationTests/obj
implementations/csharp/tests/fixtures/DiscoveryHost/bin
implementations/csharp/tests/fixtures/DiscoveryHost/obj
implementations/csharp/tests/fixtures/HealthcheckHttpHost/bin
implementations/csharp/tests/fixtures/HealthcheckHttpHost/obj
implementations/csharp/tools/CisHealthProbe/bin
implementations/csharp/tools/CisHealthProbe/obj
```

Only `.cis/local` is ordinary storage; bin/obj roots are count-only scratch. The
actual example is compiled in disposable isolated consumers. File-only inspection
at revision `:106` measured 4,739,832,091 owned bytes and zero pending storage,
leaving 1,635,509,989 ordinary bytes after reserve. Inspect capacity again before
execution. The scoped native profile will bound run/retained output to 512 MiB.

Windows CIS accounts admitted local commands. Ubuntu uses externally supervised,
bounded SSH with the existing pinned toolchain, the same package and a fresh
task directory under `/data/user`; its regular-file limit is 2 GiB, manually
inspected before expansion and after commands. Bound the remote lane to 1,200
seconds within the same 3,600-second task allowance. CIS does not enforce remote
containment/storage or transport its ledger; do not copy/reset accounting. Record
actual remote elapsed time, native outcomes and cleanup separately. Unmeasured
historical preparation is explicitly acknowledged as unknown. New commands,
profiles, platforms or source revisions never reset consumption or attempts.

Decision references: `CISLIB-CIS-0003-ALLOCATION-2026-11-13` and
`CISLIB-CIS-0003-STORAGE-2026-11-13` (joining the existing ceiling). Exact input is
retained at `.cis/local/testing/logging-example-package-integration/allocation-proposed.json`;
SHA-256 `74a415e48ae66aac05c407e48b8ae99acd1dd01f210bfccf9e127eb9686bfafc`.
Refresh only the revision token if legitimate
intervening work changes it; preserve all proposed limits, actors, roots and reasons.

Proposed plan-approval reason: "Approve the CIS-0003 plan and WORK-001 contract
for the bounded ConsoleHuman Logging example and real package-consumer integration,
preserving accepted product decisions, completed CIS-0001/CIS-0002 results and
existing release obligations."

Proposed allocation reason: "Authorize CIS-0003/WORK-001 with Andrew Spiteri as
original authority and Codex as controller, the exact proposed fixed time/attempt/
restoration limits and fifteen roots under the existing shared storage ceiling,
acknowledging historical unknown effort and the stated external SSH accounting
and storage limitation."

## Accepted impact decision

Andrew Spiteri explicitly accepted `IMPACT-C3273A4304` with reason: "Accept the ConsoleHuman Program.cs impact
as the anchor for one bounded Logging example and real package-consumer integration
task, including focused Windows/Ubuntu verification, documentation, review and
completion; preserve completed CIS-0001/CIS-0002 work and exclude broader release
qualification or unrelated product changes."

CIS recorded this exact decision and generated one medium WORK-001 task. The
decision does not approve the [generated plan](plan.md) or allocate execution.
No existing governed feature authorizes this new instance through plan derivation.
The one accepted impact is covered, with no truncated traversal or other findings.

## Exclusions

No main merge, package publication, new module, Healthcheck package-example
integration, full regression, coverage, mutation, performance or release campaign.
The existing accepted no-UI direction applies; there is no visual-design outcome.
