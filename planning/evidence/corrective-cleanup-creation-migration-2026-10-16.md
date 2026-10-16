# Native cleanup and public creation reader migration

16 October 2026, based on `fabcdd3a`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior, original vectors and historical receipts are unchanged. Local review applies; no Claude transfer.

Two Python readers are retired. All 18 physical modes remain: 13 cleanup and five public creation. All 42 original corruptions (29 cleanup, 13 creation) join the existing start/drain/join controls, yielding 116 native corruption cases across those five families. Original mode catalogs are reconciled; the shared reader checks positive physical output before every mutation.

Cleanup now retains exact result shape/UTC/canonical counters, complete console/file partitions and flush outcomes, eventual file ownership/dispositions, original bounded records/producer PID, safe fault/primary-error retention and exact timer/cleanup attempts. Existing owned/borrowed lifetime, held callbacks, repeated joins and post-stop flush assertions remain. Public creation adds complete record framing/status/byte caps and output delivery facts to the existing configuration/identity/frozen-settings/ownership assertions.

The physical helper binds actual PID/runtime/OS, before/after managed hashes, original command/report and complete parent environment/CWD. It independently reads the public-created file and reacquires its exclusive group lease; disabled/invalid cases must create neither the active file nor its lock. Cleanup's controlled stream fixture creates no physical files. Child bounds remain 60 seconds. These are test-only migrations, separate from full release/package qualification.

## Verification

Windows unit `526fc83b71eb41d5884fcd34b8cc72d4` passed 15 cases. Final integration `3f290b1d6e7141c9b4e769737d4a195d` passed 166: 50 physical modes across the five shutdown/creation families plus all 116 corruption cases. Ubuntu unit `72634bc6a4f24d73b465679c44c2b603` passed 15 and integration `ae8459d115f243bb831eefdd6290b2e5` passed 166, zero failures/skips. The audit confirms matching identities for all 181 cases. Ubuntu uses scoped current source copies with isolated outputs, not a shared exact package or full lane checkpoint.

Initial Windows integration `7781de232f5f49df9d6ec1040285ac51` passed 164 and exposed two reader-guard gaps: a missing validation error caused a NullReferenceException, and an emptied record caused an IndexOutOfRangeException. Explicit nullable/empty guards now reject those damages through the intended validation exception. No production assertions were weakened and no shipping code changed. The subsequent native run and formatting pass completed cleanly.

The latest full Windows Developer result remains the earlier 5,948-case value checkpoint. Current full coverage/mutation/compatibility and both-lane/release qualification remain open. The original roadmap remains paused.

## Retirement

Guarded original SHA-256 values:

- `verify-stop-cleanup.py`: `ad68cafec657ccf463656463d143d902411ca15dd727d53f1c8d7c18b1ddea9e`.
- `verify-runtime-create.py`: `ece4ab4f481f842c91f9c444789705d865aac13810a223a36554bc7c51d06bfe`.

No maintained importer remains. Remaining: 30 nonhistorical implementation Python files (21 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation helpers and 106 frozen planning/evidence files are preserved. The shared Python flush helper remains until its importers are migrated.

Final graph: schema 2, 10,717 nodes/51,315 edges, fresh with zero diagnostics, build `e4fdb67fab490a11beccd0226f20373f84dba7464dec52e19187b1d1dd277067`. StopCleanupExpectations resolves uniquely; the depth-one related query is untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This is bounded navigation evidence, not semantic completeness.
