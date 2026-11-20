---
title: CIS-0017 intake and verification boundary
type: verification-record
status: Draft
change_id: CIS-0017
---

# Intake — 20 November 2026

The new module starts from merged main `539a05d1`, on branch
`codex/local-transaction-integration`. Completed outcomes retain their own
authority, costs, receipts and qualification limits.

| Observation | Actual result |
| --- | --- |
| Supplied specification | Read and preserved byte-for-byte under `planning/transactions/sources`; SHA-256 `8a9e056a7ad30a2d2d6af475a1d1f0e40a138f10e1f61905a374f6f1d1fcbbb7`. Its handoff is requirements input, not execution approval. |
| Existing contracts | Reviewed Core ownership/hosting, Workflow/REST/Startup, Metrics composition, CommonTesting and package consumers. No new framework or second provider is needed. |
| Dependency metadata | Official Npgsql 10.0.4 nuspec net10 group contains only Logging.Abstractions >=10.0.0, satisfied by existing 10.0.12. Exactly one new mapping, Npgsql, is proposed; no restore was executed. |
| Local prerequisite | Initially no PostgreSQL executable/service or Docker engine. The user started Docker; local Linux/amd64 engine now reports 29.6.1, with no official PostgreSQL image cached. No pull, container, volume or database connection was created. |
| Image metadata | Official PostgreSQL 18 Alpine Linux/amd64 manifest is pinned in the dependency profile; metadata reports 120,245,127 compressed bytes. Actual image/server execution and server minor version remain unverified. |
| Independent intake/review | Three read-only slices used 8 active minutes each, plus 3-minute plan reviews each. No material product/verification-framework expansion found. Two reviews caught the same WORK-005 sequencing mismatch: formal start now depends on WORK-002; final WORK-003/004 evidence remains an explicit completion gate. |
| User file | Untracked CountryData launch settings remain outside changes; original SHA-256 `ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784` is preserved. |

No RM-02 product code, native test, package consumer execution, PostgreSQL
qualification or approved execution allocation exists at this checkpoint.
All source AC-01–28 groups remain pending implementation and actual verification.
The [five-outcome plan](implementation-plan.md) includes each task's tests, docs,
formatting and review. Closed owners, provider profiles and source prompts do not
grant this fresh scope.

The proposed allocation includes intake: conservative root estimate 35 active
minutes plus 33 combined agent minutes, retained separately from future measured
native receipts. These are estimates, not automatic CIS measurements.
The proposed physical-group amendment is 8 GiB including a counted 1 GiB Docker
allowance and the retained 64 MiB reserve. It is not an uncounted new pool.
Only exact newly owned RM-02 material may be restored/removed; no historical
cleanup is proposed. No pull/start/implementation runs before the actual human
outcome, dependency and limit decision is recorded.

## Required future evidence

Record actual provider/server versions, candidate hashes, affected build/style,
native Windows/Ubuntu execution, PostgreSQL independent-connection proof, safe
telemetry/scrape, public packages, skill validation and independent review with
their owning task. Preserve failures and unexecuted scope. Scripted Unknown
proof is not a real network-failure qualification; the EF recipe is not an
executed adapter. No full-suite, mutation, coverage, performance or release
campaign is authorized by intake.

## Approval and allocation hold — 20 November 2026

Andrew Spiteri approved the P01–P14 profile, five-task plan and proposed shared
allocation. The canonical outcome authorization and five approved-plan task
contracts are now recorded. The allocation command returned held, exit code 4,
`applied=false`: the same storage-ceiling reference must retain its original
finite byte limit and reserve. All five tasks remain Draft with no applied RM-02
allocation. This is CIS validation, not a sandbox approval rejection.

Independent source inspection confirmed there is no supported shared-group
storage amendment operation in this installed build. No accounting file was
edited and no overlapping pool or reopened historical owner was used.
The [specific alternative](storage-resolution-proposal.md) retains 6 GiB after
proposed cleanup of two exactly identified generated Type Manager fixture roots,
totaling 4,206,421,532 observed bytes. It awaits a user decision; no cleanup has
been performed. Source identifies these as transient copied/emitted assemblies
and generated input inventories, removable only after their test hosts exit.
Root metadata inspection covered both configurations; independent ownership
inspection covered Debug. Historical originating task IDs are not recorded by
that producer, so cleanup is not inferred from absence of a protected claim.

Additional conservative source estimate: root 45 active minutes for authority
recording, allocation diagnosis and concrete resolution preparation, plus 4 core
preflight minutes and two independent 5-minute allocation/cleanup inspections.
These 59 minutes remain charged to WORK-001 alongside the earlier 68-minute
intake estimate; automatic CIS counters and actual native costs are distinct.
No implementation, product build/test/restore, database connection, Docker pull,
container or volume has run. All AC groups remain pending actual implementation.

## Approved storage resolution and implementation start

Andrew Spiteri approved the concrete 6 GiB alternative and exact fixture cleanup.
CIS retained the original 8 GiB decision in authorization history and recorded
the amendment, digest
`sha256:c6c241eb5252ca6fdca93bdaf39d72d01dcff01c6bbafdfa41f8245693549399`.
The shared ceiling reference remains `CISLIB-CIS-0001-STORAGE-2026-11-10`.
All five tasks now have an applied 5/6/5/4/4-hour distribution within the same
24-hour/28-attempt/2,700-restoration-second total; allocation revision is
`8351035b241f4939ace19b3cc8d71d81:2592`. WORK-001 is InProgress.

Exact cleanup removed 4,206,421,532 bytes from the two approved generated fixture
roots after Windows/Ubuntu/Docker WSL idle checks. The retained 5,580-file hash
manifest has SHA-256
`0ce5eaad473b473ad80010a944d45b681752d171d8a85534914f08b8cc697f9a`.
The independent UTC receipt check resolved a nonterminating PowerShell date
conversion guard error: cleanup completed 75.783 seconds after verification,
within the required 180-second window. Path/ancestry/reparse/inventory guards
passed. No other historical files were removed. Launch settings retain their
original hash.

The resulting physical observation is 1,957,008,108 bytes before the included
1 GiB Docker allowance. After that allowance and the 64 MiB reserve,
3,344,592,148 bytes remain within 6 GiB. Future expansion still needs a recount.
The original policy and all historical costs, evidence and completed owners were
preserved; no new overlapping pool or protected ledger edit was used.

A task-transition snapshot retained WORK-005's earlier scope binding while its
derived Markdown had refreshed. The original projection was preserved locally,
its one derived scope header was aligned with its canonical row, and the normal
authorized task-update operation rebound it to the current decision. The final
operation reported valid:true, with WORK-001 still InProgress. Subsequent
canonical mutations are serialized to avoid stale overlapping snapshots.

The first core writer is implementing a genuinely usable begin/owner, not public
terminal stubs. Its stable public surface received
[early independent feedback](review-001.md) with no required findings; native
factory/lifecycle proof is still pending. An additional five-minute read-only
Metrics integration preflight belongs to WORK-004. All external source estimates
and eventual measured native invocations are retained separately from automatic
CIS counters; direct external commands are outside those automatic counters.
No RM-02 product native build/test/restore or database execution has run at this
source checkpoint.

## WORK-001 foundation native checkpoint

The real portable contracts, passive immutable registration, factory, guarded
native owner/participant and sequential acquired-resource cleanup are authored.
There are no public terminal stubs. Initial concrete instrumentation is the
explicit internal null seam; WORK-004 owns the actual Logging/Metrics binding.

| Check | Actual result | Evidence |
| --- | --- | --- |
| Scoped formatting | Passed, then verify-no-changes passed | `WORK-001/foundation-format-pipe-access` and `foundation-format-verify` native receipts |
| Affected Release build | Passed, zero warnings/errors | `WORK-001/foundation-build/native.log` |
| Windows focused foundation | 29 passed, zero failed/skipped | `WORK-001/foundation-test/results/foundation-windows.trx` |
| Actual Ubuntu focused foundation | 29 passed, zero failed/skipped | `WORK-001/foundation-linux/results/foundation-linux.trx` |
| Exact candidate identity | Test output Core matches current source-built Core | `WORK-001/foundation-receipt.json`; Core SHA-256 `7c41840425b9c005f37ff60199b289b50155261af337b9a928877d6c0a8c5d5d` |
| Independent final foundation review | No required findings; both TRX hashes and three-class scope confirmed | [Review record](review-001.md) |

Relative evidence paths above start at ignored
`.artifacts/transactions/CIS-0017/`. Candidate tests use runtime 10.0.12 with
roll-forward disabled; Ubuntu reuses the existing Linux ELF runtime and the
pinned SDK's managed VSTest runner with ReadyToRun disabled. No new runtime/SDK
was installed or pin changed. The formatter's auxiliary build host reported the
already installed .NET 11 runtime; that is distinct from the pinned product test
host. The first sandbox formatter run failed on its local MSBuild named pipe,
before the successful access-enabled run; its failure and measured cost remain.

The 29 cases prove source/provider-double foundation behavior: passive bounded
registration, alias/isolation/ambient rejection before source resolution, exact
native handles, partial begin cleanup and original exception preservation,
successful and refused/uncertain completion baseline, and independent cleanup
tokens. They execute no SQL and establish no real PostgreSQL atomicity or native
query cancellation. WORK-002 retains the deeper race/cancellation/failure matrix;
WORK-003/004/005 retain database, concrete telemetry and public-package evidence.

Source effort reported for this slice: 65 writer minutes, twelve independent
review minutes including early/final evidence review. Previous intake/preflight
costs remain charged. Actual native invocations and failures are measured in
`native-costs.jsonl`, separately from conservative external source estimates and
CIS's automatic counters. The engineering `completion-context` command reports
inapplicable because engineering defaults were not adopted; this does not adopt
new gates or assert broader release readiness. The existing authorized task
lifecycle and its resolved checklists/evidence govern foundation completion.

CIS subsequently returned WORK-001 Complete with a valid approved plan; WORK-002
through WORK-005 remain Draft at that closeout. Controller progress is retained
at work revision `8351035b241f4939ace19b3cc8d71d81:2594`. One attempted checkpoint
incorrectly requested closing a CIS-managed logical attempt, which these external
runs never opened; CIS rejected it without applying a change. The corrected
checkpoint records external results with `closeAttempt=false`, preserving that
rejection and all measured/source costs. No automatic attempt or consumption was
invented or reset. The manual external foundation slice is complete.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1860; failed=264; possibleTokenSavings=591; ledgerDigest=sha256:60522c6f33be15bb6e0f5944e0b2628b8e802c5ed7b22dbda99ecf684088ddbf |
