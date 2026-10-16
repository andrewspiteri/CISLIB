# Accumulated native file-reader Developer checkpoint

16 October 2026, based on `28e602eb` plus the single case-inventory correction included here. Corrective Batch B changes verification only. The original roadmap remains paused; shipping Core, public behavior, dependencies, original plans and frozen receipts remain unchanged. No Python execution or Claude transfer.

## Corrected binding

The first full Windows attempt `273111785aae4737aac46f8a3892cfda` (11:21:26–11:25:57 UTC) was rejected by native reconciliation because INT-FILE-LIFECYCLE contained the retention method twice. The migration had added a four-execution binding beside its existing one-execution binding. The duplicate is removed and the existing binding now requires all four cases. Every group's bindings were checked for uniqueness and positive minima before the successful full rerun. The duplicate-method rejection guard remains active; no test gate is weakened. The failed attempt executed 3,706 unit cases successfully but did not complete the other families; it is not credited as a full pass.

## Full Windows Developer result

Run `7290005527de4be7a5956c022f3c1cf5`, 11:27:54–11:39:18 UTC, exits zero with zero validation issues. All **6,260** native cases pass: **3,706 unit, 1,911 integration, 619 architecture and 24 business**, zero failures/skips. Restore, formatting verification, complete solution build, compiler/consumer checks, dependency/compiled/package inspection, discovery/execution reconciliation, ledger validation and scaffold host complete. The Developer profile builds Release configuration; it does not execute full Release qualification, coverage/mutation or cumulative compatibility.

Summary SHA-256: `df4672e00eb87cf1285526c7306c71b913c44a386e3f8b5ce81487c0b2165547`. Source-input inventory SHA-256: `880cf59108454a689d0e718995b5cffe2ef74e9ad61f9fc4892cf7b3a6f41049`. The complete failed and passing trees are copied to unique ignored audit directories under `.artifacts/corrective/file-native-checkpoint`, preserving their identities before future runs overwrite shared outputs.

The separately tracked Type Manager chat is completed/idle at `75b4d34c`; no messages were sent. Existing graph/profile edits, untracked graph inputs/evidence and Web launch settings remain outside this change. The launch-settings SHA-256 remains `1509170e5a2d67d067d16e8a3c69db9e67755f5761bc0b5413fe11558054b6be`.

## Remaining scope

Twenty nonhistorical implementation Python files still require individual migration/retirement: ten DiscoveryHost file/recovery/timer drivers, one unchanged temporary read helper, six engineering utilities and three Type Manager fixture utilities. One maintained documentation selector remains. Five historical implementation helpers and 106 frozen planning/evidence files remain preserved. Current Ubuntu focused evidence is retained in each migration receipt; this checkpoint does not claim a fresh complete Ubuntu lane or a shared exact candidate across platforms. Supported IDE acceptance, full corrected-candidate coverage/mutation/compatibility, package/examples/skills/bundle and release/publication remain open.

Final graph: schema 2, 10,827 nodes/52,135 edges, zero diagnostics, build `406982a46b88901a1ef157d1464a587bca062e2a1652e1fd99e7279d63f49feb`. Compiler/semantic extractors remain 6/3. The exact retention class has a fresh/untruncated depth-one related query (four nodes/four traversals, limit ten, proposed edges excluded). This is bounded navigation evidence, not semantic completeness.
