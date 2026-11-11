---
title: "CIS-0001 verification evidence"
type: verification-record
status: Draft
change_id: CIS-0001
authority: human-reviewed
---

# Verification evidence

Record exact commands, artifacts, results, blockers, deferrals, coverage, and
independent-assurance findings as agent tasks are completed.

## Current accepted unit evidence — 11 November 2026

The receipts below supersede the earlier holds. They are retained under ignored `.cis/local/workflow-repairs/step-4/`; native state/output remains under `.cis/local/testing/incremental/healthcheck-diagnostic-unit/`. The existing approved owner, finite allocation and unchanged profile/filter were used throughout.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Original disposable execution disposition | `unit-disposal.json`, `disposal-result.json` | Supported `retired-environment` disposition; finalized | Original root `native/incremental/b8fc43c85f0e4e4785397265885ad4ca` and child `native/incremental/stage/d4bee5a0016342fa9041becfeacff8e9` remain interrupted/inconclusive, with logical attempt `WORK-001:1` retained and unknown duration charged at the admitted 300,000 ms bound. No historical child stop or namespace proof is asserted. |
| WORK-001 | Actual selected native build/test | `unit-run.json`, unchanged `cis test incremental run` profile/owner with `--layer unit` | Native command exit 0; **2 passed / 0 failed / 0 skipped** | `ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(False)` and `(True)`; new root `native/incremental/7e9a8982aac647589a17a395282e6b3c`, child `native/incremental/stage/d57ad673eae44afa816b672587a0fceb`, logical attempt `WORK-001:2`. |
| WORK-001 | Retained nonempty TRX | `reports/file-66df71e1403df5691bc4-unit.xml` beneath the profile incremental directory | Accepted, 4,328 bytes | SHA-256 `c5162182701dd8ed3dfb0e694971a049dae2f33872247040f4c7f071af1656be`; saved/current unit fingerprint `9dc940a3526a49f4e6de35e2fec871900d9f875c88f919f90d2b2b4331f4e434`. |
| WORK-001 | Read-only native evidence verification | `cis test incremental verify`, same profile and `--layer unit`; `current-verify.json` | Verified, exit 0; unit scope complete | Verified Ubuntu-built CIS [d36e2aa](https://github.com/andrewspiteri/CIS/commit/d36e2aa39e4a14d8f2a4656c5e4a62bf32c1bf53); assessment contract 2. Source, test, SDK/runtime and profile SHA-256 `1c36079948f05414698317527c72591140f2b46cdffa17cfb0f6e86d4fed02e4` remain unchanged. |
| WORK-001 | Equivalent executable spelling correction | `unit-run.json`, `native-progress-result.json`, CIS `d36e2aa` | Existing passing evidence accepted without replay | Initial invocation returned exit 3 / batch-limit because the shared currentness reader compared equivalent absolute executable slash spellings literally. Native exit 0 and the passing report were preserved; five governed producer checks qualified the correction. No consumer rerun was used for currentness/reuse. |
| WORK-001 | Exact time/recovery settlement | `verified-work.json`, revision `8351035b241f4939ace19b3cc8d71d81:38` | All four executions finalized; zero time reserved; no recovery hold | Change/task consumed 433,787 ms: original 300,000 ms charge plus observed retry root 133,787 ms. Both used attempts remain counted; task limit 2 of 2 used. No allocation or reserve increase. |
| WORK-001 | Logical-attempt closure | `closure-progress-result.json`, revision `8351035b241f4939ace19b3cc8d71d81:39` | Closed, exit 0; recovery known | No open logical attempt; 433,787 ms consumed and zero reserved remain unchanged. The 2-of-2 attempt allowance is exhausted for future native admission; this does not invalidate the settled current report. No further native run is requested. |
| WORK-001 | Unchanged read-only unit status | `current-plan.json`, `current-status.json`, same profile/owner and `--layer unit` | Exit 0; zero pending unit batches; scope complete | Zero native batches executed by these checks. `qualified=false` and `thresholdMet=false` retain their separate mutation/release meaning. |
| WORK-001 | Current associated owned inventory | `current-inventory.json` | Current, exit 0; within allocation | 4,598,827,058 owned / 6,442,450,944 maximum bytes; zero reserved, 67,108,864 restoration bytes and 1,776,515,022 remaining bytes. |
| WORK-001 | Supported lifecycle completion | `task-complete.json` | `task-transitioned`, exit 0; Complete | The approved plan remains approved. Existing evidence and settled accounting supported completion; no new native execution, scope or approval was introduced. |

The bounded unit acceptance criteria are met, and WORK-001 is Complete through supported lifecycle control. Product approvals, plan scope and broader release obligations remain unchanged.

## Historical interruption and recovery evidence

The records below describe earlier states. Their held/unresolved claims are historical and are superseded by the accepted unit evidence above.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Exact human plan approval | `cis plan approve CIS-0001 --reviewer "Andrew Spiteri" --reason <exact approved reason>` | Approved, exit 0 | Exact reviewed commit `bfbb21d834916af5aa8e44c2f466fc55447ac19b`; native event `2026-11-10T15:22:33.9655648+00:00`. |
| WORK-001 | Separate finite allocation | `cis agent work allocate CIS-0001 --adopt-accounting --input <reviewed allocation JSON> --actor "Andrew Spiteri" --reason <exact approved reason>` | Allocated, exit 0 | Input SHA-256 `444975f0d9f91c2cf1d5cda5a12e200ee05e32d8a6fed453dc33100753af3101`; actual allocation/storage references and Codex controller below. |
| WORK-001 | SDK/runtime, membership and profile | Read-only SDK/runtime/config/source inspection and profile SHA-256 | Preserved | SDK 10.0.401; installed/pinned .NET/ASP.NET 10.0.12; one method / two Boolean DataRows; unchanged class filter/profile. No source/assertion edit. |
| WORK-001 | Single bounded unit attempt | `cis test incremental run --repo . --profile .cis/local/testing/profiles/healthcheck-diagnostic-unit.json --layer unit --max-batches 1 --max-seconds 300 --change CIS-0001 --task WORK-001 --authority C:/miscwork/portfolio/cislib --actor Codex --format json` | Settlement required, exit 4 | Native root `native/incremental/b8fc43c85f0e4e4785397265885ad4ca`; no force, second run, accepted TRX or assessed passing cases. |
| WORK-001 | Build / native discovery | Profile work directory `ff67770d152a/stdout.log` | Build outputs observed | Four selected projects built and VSTest found one test assembly; no result is inferred from discovery. |
| WORK-001 | Native ownership | Retained work status, first-result controller progress and exact process observations | Uncertain; reservations preserved | Original child stopped/job closure not proven in its original namespace. No cancellation, accounting reconciliation or output cleanup was performed. |
| WORK-001 | Unchanged read-only unit state | `cis test incremental plan` and `status`, same profile/owner, `--layer unit` | Settlement required, exit 4 | One pending unit batch, zero executed/reused, scope incomplete. |
| WORK-001 | Historical associated storage | `cis artifacts inventory --change CIS-0001 --task WORK-001 --authority C:/miscwork/portfolio/cislib --actor Codex`, before Blocked transition | Historical inventory, exit 0 | 4,598,103,919 owned / 6,442,450,944 maximum bytes; 268,506,743 reserved, 67,108,864 restoration bytes. Current associated inventory is held; this earlier snapshot is not settled ownership. |
| WORK-001 | Producer corrections / genuine plan restoration | CIS `5f923ee75b5f8bd13e143fe51d15dc2629f1033f`; local `native-recovery-corrections/plan-repair-applied.json` | Delivered / repaired | Independently authorized source work; restored only genuine attribution/prose, preserving current Blocked task state and manifest. |
| WORK-001 | Corrected-host inspection | Verified `0.3.0+5f923ee75b5f8bd13e143fe51d15dc2629f1033f`; local `work-status-corrected.json`, `unit-plan-corrected.json`, `unit-status-corrected.json` | Owner readable; unit settlement-required | No TEMP override; revision `:18`, 300,000 ms reserved, one pending unit batch, zero executed/reused. Ledger/native state/profile hashes unchanged. |
| WORK-001 | Current associated inventory | Local `native-recovery-corrections/inventory-corrected.json` | Held, exit 4 | Task is Blocked; current owned inventory is unknown, not zero. Prior numeric inventory is historical; actual retained reservation stays intact. |
| WORK-001 | Producer restart qualification | [CIS `36484f0`](https://github.com/andrewspiteri/CIS/commit/36484f098397aa20c378144e24f4c4e0832d3f4d), Windows/Ubuntu native TRXs | Read-only preflight delivered | 68/0/0 per platform; no positive custody/restart verifier, prepared challenge or real reboot qualification. These are producer tests, not CISLIB case results. |
| WORK-001 | Exact-tree prepare/verify preflight | Verified host `0.3.0+36484f098397aa20c378144e24f4c4e0832d3f4d`, local `native-restart-qualification/` receipts | Both held, exit 4 / applied=false | Missing original-host custody, copy/rollback qualification and positive verifier; no challenge. Same two-execution tree/revision `:18`; ledger/adoption/state/profile hashes unchanged. |

Receipts are retained under ignored `.cis/local/adoption-continuation/approved-native-unit/`; native state/output remains under `.cis/local/testing/incremental/healthcheck-diagnostic-unit/`. Allocation adoption is `8351035b241f4939ace19b3cc8d71d81`; actual decision references are `CISLIB-CIS-0001-ALLOCATION-2026-11-10` and `CISLIB-CIS-0001-STORAGE-2026-11-10`. Andrew Spiteri is the original allocation authority, Codex is the authorized controller, and no cleanup delegation or engineering-quality policy was adopted.

At that historical checkpoint, actual 2/0/0 case results, accepted nonempty TRX, settlement and zero-pending status were all unmet. The first attempt's result remained null; bookkeeping could not manufacture it. Original Windows containment/namespace proof was not established. The later supported retired-environment disposition preserved that uncertainty and its cost; the ordinary second attempt supplied the accepted native result above.

The authorized legacy-proof follow-up delivered read-only qualification only.
Original-host custody cannot be recovered from a new current-machine seal, and no
positive restart verifier was established. Both historical preflights held; they
did not authorize a restart. At that time one unit batch was pending, 300,000 ms
and 268,506,743 bytes remained reserved, and inventory was held. No cancellation,
settlement, profile/allocation change or new consumer test occurred in that
qualification work. The current receipts above record the subsequent supported
disposition, native retry and settlement without a reboot or allowance reset.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=246; failed=41; possibleTokenSavings=123; ledgerDigest=sha256:a0956e4fe6bb71fe3e2e38021eed99193e865a8514be57db800f5188f5b61aab |
