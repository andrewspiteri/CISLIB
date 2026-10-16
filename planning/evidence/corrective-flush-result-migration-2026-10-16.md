# Native public flush-result reader migration

16 October 2026, based on `05b14923`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior, original vectors and historical receipts remain unchanged. Local review applies; no Claude transfer.

All 17 public-result modes and all 26 original corruptions are migrated to C#. Together with preceding boundary/coordination regressions, the native process selection contains 33 modes and 54 original corruption cases. Every corruption follows positive physical output validation. Native result checks now retain actual PID/runtime binding, complete result/UTC/canonical counter observations, output order, held/eventual registrations, original bounded console/file records and producer PID, eventual diagnostics, timer ownership and physical lease facts. Existing timeout/cancellation/loss/failure/clock/exhaustion assertions remain.

The old reader required zero retained registrations after ticket exhaustion even while its covered write remained active. Current coordination retains that registration until the write settles, matching the active-I/O behavior already reconciled during shutdown migration. The native reader requires one at that observation and still checks the final settled diagnostics. No runtime behavior is changed to satisfy the migration.

The existing process helper supplies before/after managed hashes, full parent environment/CWD preservation, actual PID/OS/runtime and original command/report retention with the unchanged 60-second child limit. The physical case independently compares file bytes and reacquires the exclusive group lease.

## Verification

Windows unit `dfe3196428154a308a893730d3ce84d4` passed 24 and integration `2e4efd0a3f8643d4a6ed5aa1e116bb65` passed 87 (33 modes plus 54 corruptions), zero failures/skips. Ubuntu unit `635b2c0cd74541c390cd1a8a8ca3113d` passed 24 and integration `2d61d82ade874aec860438e44c9a4034` passed 87, zero failures/skips. Native audit confirms all 111 case identities across lanes. Ubuntu builds scoped current copies with isolated outputs, not a shared exact package or full lane checkpoint.

Initial Windows unit `2e34e44ba8a5429f833dd092d972b7d5` passed 23 and exposed the stale exhaustion-registration assumption. It is not credited as a passing selection. The current active-I/O expectation precedes the successful native runs. No other observations were relaxed. Formatting completed cleanly.

The most recent full Windows Developer result remains the earlier 5,948-case value checkpoint. Current full coverage/mutation/compatibility, corrected both-lane qualification and release remain open. The original roadmap remains paused.

## Python command retirement and remaining helper

The original `verify-flush-results.py` command, report checks and corruption runner are removed. Its first **1,359 bytes remain byte-for-byte unchanged** because six remaining stress/file/timer drivers import the read helpers. No new Python implementation is added. Read-only call-site reconciliation found no importer of the retired check/control/main functions. The helper file will be removed after those callers migrate; it is no longer an executable verification gate.

Original complete file SHA-256: `d26e47188fd752810f2b8c090f1b0f6c5c12fc2ea21dd68b07f212074b94a9bc` (10,665 bytes). Retained helper prefix SHA-256: `644c01f722a1d81153444b7facfa5a91c533d1d6c3168c866793fe6a7125d305` (1,359 bytes). Guarded audit is retained under `.artifacts/corrective/flush-result-reader`; Git history preserves the full original command. Historical reports/bundles remain unchanged, and their former verifier hashes are not silently reassigned to current code.

The file inventory remains **27 nonhistorical implementation Python files**: 17 DiscoveryHost drivers plus this temporary read helper, six engineering utilities and three Type Manager fixture utilities. One maintained documentation selector also remains. Five historical implementation helpers and 106 frozen planning/evidence files remain preserved.

Final graph: schema 2, 10,766 nodes/51,569 edges, zero diagnostics, build `07004babc50bddd38ac4c28f20741cf2e38e140b1495ab403a13e9b3862b91c3`. FlushResultExpectations resolves uniquely; its depth-one related query is fresh/untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This is bounded navigation evidence, not semantic completeness.
