# Logging review corrections — 15 October 2026

The supplied [static review](source-review.md) is retained unchanged (SHA-256 `98a85077b8e8ae7482a0d632d6c84175721852c7b1365407ec62637f9afb237b`). Baseline commit: `4a75f3560d3eeccd170901ad6882dd86c850a618`. Its archive inspection is source evidence, not executed tests. The [current behavior addendum](../../../../docs/modules/logging/review-contract-2026-10-15.md) records the decisions and supersedes conflicting earlier development descriptions.

All eleven supplied findings have implementation, documentation or measured sizing corrections. After explicit user approval, Claude reviewed the exact 75-file bundle and returned one blocking and five smaller findings; the [disposition](claude-disposition.md) records each subsequent fix. Final native verification passes on both platforms. These corrections form a separate commit before resuming the roadmap at WP07-S02-T02. Claude did not review the subsequent edits or return a final PASS; no such claim is made.

| Finding | Correction and regression evidence |
| --- | --- |
| R01 | Schema-2 intent precedes retirement and records exact retired IDs/names/lengths/hashes. Recover none/part/all of only that prefix; missing survivors and changed bytes still fail. Staged/forced-process restart regressions remain; schema-1 intents are readable. |
| R02 | Full groups retire the oldest owned retained member, preserve the whole partial active as damaged and create a fresh active within the bound. Public-runtime processes cover retained limits 1, 10 and 1,000. Lower pre-existing startup limits still reject unchanged. |
| R03 | Monotonic record/writer settlement and originating fault observations determine timing independently of timer dispatch and caller resumption. C# regressions cover withheld timers, sub-millisecond/exact boundaries, timely work observed later, held-worker terminal failure, original shared stop budgets, cancellation and late I/O. |
| R04 | Invalid borrowed scopes reject one event and increment InvalidEventCount once; valid oversize truncates. Owned scopes validate at creation. Bare strings, unsupported values, invalid Unicode/IDs, cycles and non-finite values are covered; invalid host IDs do not silently fall back. |
| R05 | Explicit event fields precede scope fields under bounds, with ordinal byte selection within groups. Provider numeric event_id is reserved through node/byte projection, including zero/negative IDs and conflicting scope/user fields. |
| R06 | A required human-envelope failure faults only that output with RenderTooLarge. A healthy JSONL destination accepts independently without global invalid accounting. Both assignments and an already-faulted destination are covered. Extreme metadata uses the internal metadata seam. |
| R07 | Getter-only InFlight and Buffered expose existing locked gauges. Tests cover a held write, queued records, deadline abandonment, eventual completion and immutable observations. The nonshipping reference declaration/catalog is updated. |
| R08 | One candidate slot per metadata kind; clean only this attempt's created file. Occupied slots are preserved and block publication. Repeated failures, later success, foreign names and cleanup denial are covered. A trusted internal callback injects pre-move failure after close; actual OS deletion is denied by Windows file sharing or Ubuntu directory permission. Production supplies no callback. |
| R09 | Extract queued acquisition/rollback into QueuedRuntimeFactory; share metadata publication and transition commit; expand compound C# blocks and use named construction. Keep admission/ticket/disposition invariants together and document their transitions. No generic pipeline or class-size guarantee. Stale READMEs/scaffold wording are corrected. |
| R10 | Measure first, then cache immutable resource projection and share equal prepared presentations. Sizing, Unicode and recovery checks remain; no pools/custom writer replacement. |
| R11 | Retain independent ranges with explicit capacity guidance, covering human/canonical bytes, in-flight work and producer headroom. Representative saturation is measured; no arbitrary new byte-budget framework. |

## Executed verification

| Native lane | Unit | Integration | Architecture | Business | Total | Stages |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 3,415 | 1,439 | 529 | 24 | 5,407 | 16 passed |
| Ubuntu 24.04 x64 | 3,415 | 1,439 | 529 | 24 | 5,407 | 16 passed |

Both Developer runs exit 0 with zero failures/skips, SDK 10.0.401 and runtime 10.0.12. [Windows](evidence/windows/summary.json) and [Ubuntu](evidence/ubuntu/summary.json) reports retain native discovery, TRX, reconciliation, built/packed dependency inspection and compile-only contract controls. Complete Developer report trees are compressed alongside them; these are report archives, not offline SDK/package-cache replay bundles.

All 940 maintained C#, contract, conformance and invocation inputs match canonical source and both final executed snapshots, with no later code change. Both profiles build/run the current configuration-only console scaffold; runnable logging examples remain pending. The [input audit](evidence/input-audit.json) also confirms protected Type Manager implementation, original source plans, publication records and the foreign launchSettings.json remain unchanged. The 37-case focused follow-up report is retained separately. Earlier failed runs exposed two superseded provider fixture expectations; those were corrected before repeating both complete profiles.

The [Release negative control](evidence/release-negative-control.json) exits 2 when asked to qualify Developer-only evidence: the expected profile mismatch, missing mutation/compatibility executions, missing required release cases and eight full release gates remain. This is not Release-profile execution or release qualification. Coverage/mutation/full compatibility and exact-candidate release work remain pending.

CIS graph rebuilding completes with partial diagnostics, but status/query still fails with SQLite `attempt to write a readonly database`. Retained graph logs describe a tooling limitation, not completion evidence. Derived .cis/local state was not hand-edited or committed.

## Measurements and capacity

The C# harness warms 64 records per mode and observes 1,000 records per runtime mode, including batch flush and physical file I/O where configured. The [before](evidence/measurements-before.json), [final Windows](evidence/windows/measurements.json) and [final Ubuntu](evidence/ubuntu/measurements.json) records include allocations, elapsed throughput, rotation and saturation.

| Mode | Before allocated bytes/record | Final Windows allocated bytes/record |
| --- | ---: | ---: |
| Disabled | 169 | 107 |
| JSONL | 12,826 | 11,104 |
| Scoped JSONL | 14,062 | 12,347 |
| Human normal | 14,548 | 12,824 |
| Human verbose | 18,688 | 16,974 |
| Two human outputs | 25,149 | 17,539 |
| Large attributes | 291,386 | 289,482 |
| Exception | 14,704 | 13,000 |
| Physical rotation | 69,106 | 67,127 |

The before sample already contained correctness corrections and precedes the cache/presentation-reuse changes; it is not pristine HEAD. These are individual process observations, not statistical benchmarks, an isolated attribution of every delta or a release performance gate. Managed allocations include workers/framework activity; warm-up and host load differ. Raw throughput is retained without a causal speedup claim. No CPU sampling profile was collected.

At 128 buffered records/16 KiB, held-writer managed retention was about 2.07 MB; at the default 1,024/64 KiB it was about 67.10 MB for one representation plus the held record. Forced-GC deltas are approximate and exclude native/OS memory. Maximum 65,536 × 16 MiB is a 1 TiB arithmetic payload bound per representation, not an allocation performed or deployment qualified here. The addendum gives practical sizing/headroom guidance.

The [evidence index](evidence-index.json) records retained bytes and SHA-256 identities. Historical task receipts and published baselines remain unchanged.
