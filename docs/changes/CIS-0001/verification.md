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

The current failed acceptance criteria are actual 2/0/0 native case results, accepted nonempty TRX, proven settlement with no reservations/recovery hold, and current zero-pending unit completion. The case count remains expected only. Use the corrected verified host for legitimate work inspection without a TEMP override; original Windows containment/namespace proof is still required before supported reconciliation of that same execution. The retained unit result is null; bookkeeping reconciliation cannot manufacture its missing report/admission. The task remains open, and this evidence establishes no release qualification.

The authorized legacy-proof follow-up delivered read-only qualification only.
Original-host custody cannot be recovered from a new current-machine seal, and no
positive restart verifier is available. Both supported preflights hold; no restart
approval is justified. Current unit plan/status still show one pending batch, zero
executed/reused and incomplete scope. The original 300,000 ms and 268,506,743 bytes
remain reserved; task status stays Blocked and current inventory remains unknown.
No cancellation, settlement, profile/allocation change or new consumer test occurred.
