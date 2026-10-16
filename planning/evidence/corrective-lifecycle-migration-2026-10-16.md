# Native shutdown crash and restart reader migration

16 October 2026, based on `797a541c`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior, original vectors and historical receipts are unchanged. Local review applies; no Claude transfer.

The lifecycle Python reader is retired. Native C# preserves all 11 modes, including three actual kill/restart scenarios (14 child launches). All 40 original corruptions execute in the corresponding native mode: 32 report/checkpoint cases, three physical console cases and five restart cases. Each follows original physical report validation. Native TRX explicitly records the rejected IDs, and the audit requires exactly the original 40 unique identities across each lane. These are assertions within 11 process cases, not 40 additional discovered tests.

Native readers now retain exact stop-result fields/types and UTC labels, canonical decimal counters, complete output identities, raw-CR/empty-record rejection, original status/payload/PID checks, maximum active deadline count, complete crash-stop dispositions and restart kind/allowed producer identities. Existing ticket/receipt/producer races, cached-result ownership, caller-only cancellation, actual partial bytes, damaged preservation, restarted records and safe cleanup remain. The catalog requires all 11 modes; the required native process binding now requires 11 executions, and the four physical kernel unit cases receive an explicit binding.

The interactive runner adds before/after managed hashes, complete parent environment/CWD checks and original commands, actual PIDs and report/stream output in TRX. It retains actual kills with nonzero exit and physical file/damaged-byte comparisons. First crash checkpoints use 30 seconds, normal child completion 60 seconds and termination joins 15 seconds, preserving the former driver bounds. The fixture still uses controlled barriers over actual file ownership/I/O; it does not claim forced OS blocking, durability or release qualification.

## Verification

Windows unit `351951eb1cc34480b9371bc9d5c80bcf` passed four and integration `7b9a00f0e2ae408eb65df06d035d7aeb` passed 11, zero failures/skips. Ubuntu unit `a658748263444b52b76fbf63f824b949` passed four and integration `8109aada2cd144d1b314dc683ad43da2` passed 11, zero failures/skips. Native audit confirms matching 15 case identities, all 40 distinct corruption IDs and 14 logged actual launches per lane. Linux uses scoped current source copies with isolated outputs, not a shared exact package or full lane checkpoint.

The latest full Windows Developer result remains the earlier 5,948-case value checkpoint. Current full coverage/mutation/compatibility, both-lane corrected qualification and release remain open. The original roadmap remains paused while separately authorized corrective work continues.

## Retirement

`verify-stop-lifecycle.py` original SHA-256: `8fa7a3b5c49361c4997ea4f547276c8959c8e52947beaf1543cafe6be7559f3a`. Guarded audit is retained under `.artifacts/corrective/lifecycle-readers`; original bytes remain at the base revision. No maintained importer remains.

Remaining: 29 nonhistorical implementation Python files (20 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation helpers and 106 frozen planning/evidence files remain preserved. The shared Python flush helper remains until its importers are migrated.

Final graph: schema 2, 10,734 nodes/51,410 edges, fresh with zero diagnostics, build `97ced01815620724ac7837a80844b0d13792ca1bf9edd000e8ea5dbd3351f6ab`. StopLifecycleExpectations resolves uniquely; its depth-one related query is untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This is bounded navigation evidence, not semantic completeness.
