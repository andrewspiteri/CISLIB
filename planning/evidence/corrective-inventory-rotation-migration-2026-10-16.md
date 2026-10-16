# Native inventory and rotation reader migration

16 October 2026, based on `d4f71eee`. This corrective Batch B changes tests and fixture observations only. Shipping Core, dependencies, public behavior, original catalogs and historical evidence remain unchanged. No Python execution or Claude transfer.

The inventory process class preserves the original physical matrix: JSONL/Human creation/restart/format refusal, all three missing-inventory preimages, eleven malformed metadata cases, closed/damaged/escaped/missing members, three lost-active retries, 1,000/1,001 retained-segment bounds, metadata-directory collision and live two-process contention/distinct identity/restart. Twenty physical cases fill gaps in the existing native selection; all five original report corruptions follow physical positive validation. Success verifies actual metadata hashes/header/segment identities and leaves no private candidates. Pre-existing metadata, active and retained/unrelated bytes remain unchanged; failure/release reports cannot leak owner facts.

Rotation retains all four JSONL/Human exact/over-limit cases and all five original report corruptions. Native checks now include literal bounded records, complete result/ticket/output shapes, exact seed arithmetic including LF, complete inventory identities/order and closed/active bytes. Reports are independently compared with actual captured active/reference/closed files and stored inventory. The existing process helper verifies actual PID/runtime/OS, before/after managed hashes, full parent environment/CWD and original output, with original 15-second inventory and 30-second rotation limits. Fixture changes add PID observations only.

## Verification

Windows integration `59fa5638514144f18d88ec25e1293a38` and Ubuntu integration `1434d702e6304fd782404cfb3d06124b` each pass 41 cases (32 inventory, nine rotation). Windows unit `853f846a3c9d4c1ca1e21387c68865dd` and Ubuntu unit `fb20e5fb064348cba2ed9dd5ba98a9ee` each pass 56. All have zero failures/skips with matching case identities. Builds have zero warnings/errors; formatting completed. Ubuntu uses isolated scoped source copies, not shared exact packages or a full lane checkpoint.

Initial Windows inventory `ab5b7b13ad844b93a29ddecc3c97945e` passed 31/failed one; the first combined selection `1a0f35014b3641f895fb7358e894e1f7` passed 40/failed one. The added preimage reader first omitted write sharing before and then after a live holder probe. Both reads now explicitly allow FileShare.ReadWrite, preserving the original reader-access expectation. No product change or observation relaxation was needed.

## Retirement and limits

After call-site reconciliation, `verify-file-inventory.py` (12,171 bytes; SHA-256 `2a6a747a07164517a919498ef17da860aee802a86ee471a9606a06d4cfee6013`) and `verify-file-rotation.py` (6,965 bytes; SHA-256 `7f9ce03fed647e6eb8f7ebefec948036eca0f39198858faf303025a824750e91`) are retired. Guarded retirement/case audits remain under `.artifacts/corrective/file-inventory-rotation-readers`; Git retains the original scripts.

There are 23 nonhistorical implementation Python files left: 13 DiscoveryHost drivers, one temporary read helper, six engineering utilities and three Type Manager fixture utilities. One maintained documentation selector remains. Frozen historical helpers/evidence remain unchanged. These focused checks do not replace the earlier 5,948-case full Windows Developer checkpoint or complete corrected-candidate coverage/mutation/compatibility/package/both-lane/release qualification. The original roadmap remains paused.

Final graph: schema 2, 10,826 nodes/51,975 edges, zero diagnostics, build `06ddc4b83a8437f1363e8d86f582051d942f80ea5ce0e45d22b7779f54944fd6`. Compiler/semantic extractors remain 6/3. Both exact process classes resolve uniquely; their depth-one related queries are fresh/untruncated (four nodes/four traversals each, limit ten, proposed edges excluded). This is bounded navigation evidence, not semantic completeness.
