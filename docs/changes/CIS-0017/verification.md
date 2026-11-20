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

## WORK-002 lifecycle native checkpoint

Twenty-five new cases qualify admission/reentry, same-callback misuse, external
disposal joins, accepted final marks, native commit cancellation cutoffs,
conservative Unknown, original-fault precedence, operation-token lifetime,
cooperative rollback-budget settlement, three ordered cleanup failures and
unavailable clocks. They retain the 29 foundation cases. No production, public
API, instrumentation hook or provider-double changes were needed.

Scoped formatting and verify-no-changes passed. The affected Release build passed
with zero warnings/errors. Windows and actual Ubuntu each passed all **54 cases**,
zero failed/skipped. All evidence starts at
`.artifacts/transactions/CIS-0017/WORK-002/`: `lifecycle-format`,
`lifecycle-format-verify`, `lifecycle-build/native.log`,
`lifecycle-test/results/lifecycle-windows.trx`,
`lifecycle-linux/results/lifecycle-linux.trx` and `lifecycle-receipt.json`.
The current source-built and executed Core bytes match SHA-256
`0f7de3247a67cdfde4ac8fbd50f80068cf341274e20272bcb31656e7584e8823`.
SDK/product-runtime pins remain 10.0.401/10.0.12 with roll-forward disabled.

[Independent lifecycle review](review-002.md) found no required findings and
confirmed both receipt/TRX hashes and the six-class scope: 8 admission,
6 cleanup, 11 completion, 3 contract, 13 foundation and 13 registration cases.
Controlled holds are released and joined in finally paths; cancellation is
observed through the real rollback token rather than sleep timing. A canceled
budget does not publish completion before the held provider operation settles.

Source authors reported forty and twenty active minutes within their one shared
authoring cap, plus the separately retained five-minute preparation. Independent
review used twelve minutes. Root source/coordination estimates and measured native
invocations remain separately charged. The only WORK-002 code changes are its
three new test files; the portable contract explains the verified token lifetime.
These cases execute no SQL. They do not substitute for actual PostgreSQL,
concrete Logging/Metrics or public-package consumption, which remain pending.

CIS then returned WORK-002 Complete with valid:true and the approved five-task
plan retained. Controller progress is at work revision
`8351035b241f4939ace19b3cc8d71d81:2595`; it records these external results with
`closeAttempt=false`. The external lifecycle slice is closed without fabricating
automatic CIS consumption. WORK-003/004/005 can now be admitted under their
existing finite allocations and stable public/instrumentation contracts.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1860; failed=264; possibleTokenSavings=591; ledgerDigest=sha256:60522c6f33be15bb6e0f5944e0b2628b8e802c5ed7b22dbda99ecf684088ddbf |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1864; failed=264; possibleTokenSavings=591; ledgerDigest=sha256:fa63460b344ab7a83d695ea7124e0c16c61a4235c5c67f2c2bc3ff6bfb90321d |
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1883; failed=269; possibleTokenSavings=591; ledgerDigest=sha256:5feb0152ea5148cfdb246cd263620a6fb0cf54c161f553c2b02d22edbef64e9e |

## Integrated RM-02 result — 21 November 2026

The frozen working candidate passed its affected Release build with zero warnings
and errors. Ordinary solution-scoped whitespace formatting and verify-no-changes
passed for the eighteen changed integration-stage C# files. Earlier foundation
and lifecycle formatting/results remain preserved. SDK 10.0.401 and both product
frameworks 10.0.12 with roll-forward disabled are unchanged.

| Actual group | Windows | Ubuntu 24.04 |
| --- | --- | --- |
| Transaction contracts, lifecycle and observations | 73 passed | 73 passed |
| Public portable helper | 12 passed | 12 passed |
| Real PostgreSQL three-table consumer | 11 passed | 11 passed |
| Collector/scrape and affected composition regressions | 33 passed | 33 passed |
| Isolated Core/CommonTesting public-package consumer | 1 passed | Unexecuted |
| Total focused executed cases | 130 passed | 129 passed |

Every executed case passed, with zero failures or skips. The 33 collection cases
contain ten new transaction cases and 23 affected retained module/host cases.
The actual held-cleanup scrape passed on both systems. Linux ran the current
built assemblies using the retained Linux ELF runtime and existing managed VSTest;
it did not execute Windows children. No existing Linux SDK is installed, so the
unchanged SDK-backed package fixture was executed only on Windows. That explicit
platform limit is not filled by the portable helper or runtime tests.

The exact aggregate receipt is
`.artifacts/transactions/CIS-0017/integrated-receipt.json`; it contains every TRX
path/hash/count, component hashes, runtime configuration and candidate limits.
Source, Unit and Integration copies match exactly for Core
`ea1eef108d6efcc07350388e9493d5bb0ca0a81b4309f8bfb7dcb3a7b29eb69b`
and CommonTesting
`9071fa1c4b08f74691c7ce8163c6f98be21104e18cdfc37cab514b8c2f7b3174`.
The optional collector source/Integration copies match
`a1c9e82a999227b25a71da4b318bd40c0748de9396751eb8034f095c98e150c1`.
This identifies the built working candidate at HEAD `66e291f4` plus its frozen
approved source changes; a later metadata commit is not an unexecuted rebuild.

The actual public-package test packed current bytes, restored from a fresh
local-only feed/cache, built and executed an outside-friend consumer, and checked
package/deployed/current byte equality and package closure. Its TestContext
records archive SHA-256 Core
`13fe6ab348e3bd2126af9211de6feeef2447d32a0772624d80e1a0949401db43`
and CommonTesting
`804dafc95e59fbfc948222ee19c5c7e7994072e04133c0d1ca35ba5e0b20df06`.
Core/CommonTesting introduce no Npgsql, EF or collector dependency. Successful
existing fixture cleanup removed its temporary feed after recording evidence.

### Dependency correction and native database restoration

The [specific two-mapping approval](restore-source-mapping-proposal.md) is
implemented without changing any existing dependency declaration or pin. CIS
preserved started WORK-003 and organized supporting WORK-006 within the same
24-hour/28-attempt totals and original protected restoration reserves. Its
[independent review](review-006.md) confirmed all 54 prior package resolved
versions/content hashes unchanged and Npgsql 10.0.4 as the only addition.
Ordinary locked restore passed. A fresh resolver output that selected four older
10.0.0 transitive versions was rejected before build and retained; baseline lock
entries plus the actual new Npgsql entry were independently validated in locked
mode. The precise comparison is `WORK-006/retained-lock-comparison.json`.

Real provider acceptance used Npgsql 10.0.4 against the one fresh owned PostgreSQL
18.7 container, official pinned amd64 digest
`sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba`.
Its loopback endpoint worked from both actual Windows and Ubuntu. Server fsync,
synchronous_commit and full_page_writes remained on, with max_connections 16.
The cases independently observed three-table visibility, all rollback paths,
declined business-result commit, all three isolation modes, source survival and
cancellation only after an independent blocking-session observation confirmed
the actual query. Schemas/tables were ordinary logged data, not fake SQL.

After both test processes and all package children exited, a fresh query against
only that owned database confirmed zero remaining fixture schemas and zero other
database sessions. Owned data was 66,252,800 bytes, below 256 MiB. The exact
owner/change labels were reverified before normal stop/removal of its container
and named volume. The newly pulled image was removed only after all its tag/digest
references were the exact owned digest and no container referenced it. The two
private generated credential files were removed after absolute-path and reparse
checks; no secret contents enter evidence. The full Docker allowance remains
conservatively counted; no engine-cache reclamation is claimed and no prune ran.
Nonsecret receipts: `.cis/local/transactions-intake/postgres-drained.json` and
`postgres-restoration.json`. All other resources and the user launch settings are
preserved.

Independent final [provider](review-003.md), [observation](review-004.md) and
[public testing/package](review-005.md) reviews inspected actual TRXs and current
hashes. The only required observation finding was corrected and its two original
fault/cancellation regressions passed. No required source finding remains.
The checked-in Transactions skill passed the bundled validator, prerequisite
links were verified, and a fresh-context read-only usage exercise found no
consequential ambiguity. No extra runtime, tutorial, test framework or harness
was introduced.

EF participation remains an unexecuted documentation recipe. Real lost commit
acknowledgements/network failures, complete Linux package consumption, broader
release, coverage, mutation and performance qualification remain unverified.
None is inferred from the native doubles, focused passes or local deployment.
No push, merge, release or external deployment is included.
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1888; failed=269; possibleTokenSavings=591; ledgerDigest=sha256:6e7ad77fb617d8ce4e62e2575cbdca1179ed7a65f1b0ec4be84d71d6e6b1d9b1 |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1889; failed=269; possibleTokenSavings=591; ledgerDigest=sha256:d372dae2263fdaaa87cf3c386463db80a87c018f8a27ce3f94db7ec18d9b8dfd |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1890; failed=269; possibleTokenSavings=591; ledgerDigest=sha256:80cbeaa023c626a0bf8484862feef9f5dc07e60efb26a6c707c894435e261849 |

### Canonical closure and retained limits

CIS returned all six tasks **Complete**, the plan valid and Approved, with known
accounting state and zero active reservation. No new native work is queued by
this closeout. Dossier closure remains a separate human decision; no publication,
push or merge occurred. Final external bookkeeping retains 13.9417 estimated
source/coordination hours plus 0.1822 measured native hours, six manual logical
work attempts, all original failed invocations and the 45-minute restoration
reserve within the approved 24-hour/28-attempt totals. These external observations
are not represented as automatic CIS consumption: its recorded counters remain
zero because these processes were not CIS-managed executions.

The final counted filesystem/native observation is 2,960,955,743 bytes. Keeping
the full 1,073,741,824-byte Docker charge and 67,108,864-byte restoration reserve
leaves 2,340,644,513 bytes inside the same 6,442,450,944-byte ceiling.
`.cis/local/transactions-intake/storage-final.json` retains the paths and hashes.
The preserved original specification and untracked CountryData launch settings
still match their original hashes; the latter remains outside the commit.
Aggregate native receipt SHA-256:
`b87f67decb588c854cbaa606788f125ebde3a7092c4a1195e1439aaff223155e`.
Later small documentation/accounting metadata remains counted; this observation
is neither a hard filesystem quota nor a future free-space guarantee.
