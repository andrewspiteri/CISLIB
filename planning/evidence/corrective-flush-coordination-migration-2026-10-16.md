# Native flush boundary and caller coordination reader migration

16 October 2026, based on `145e1eb1`. Corrective Batch B changes verification only. Shipping Core, dependencies, public behavior, original vectors and historical receipts remain unchanged. Local review applies; no Claude transfer.

Two Python readers are retired. C# retains all 16 physical modes (seven boundaries and nine caller-coordination modes), with 28 original corruptions (12/16) executed after validated physical child output. The four repeated coordination modes still execute 10,000 detached callers each. This correctness observation is not a performance benchmark or release qualification.

The native readers now preserve complete capture/output shapes, canonical decimal Int64 counts, UTC observations, enabled output presence, initial/rejection/final facts, exact transition phases, truthful race totals, writer attempts/flush counts and original bounded record framing/identity/producer PID. Literal wire timestamps, service/environment/version/resource/component, message and truncation rules remain. Concurrent wire checks retain all 65 distinct producer names without inventing a producer order. Existing immutable snapshots, target coalescing, atomic targets, bounded continuation storage, shared ownership, fixed writer ordering and disabled outputs remain.

Physical readers use the existing helper's actual PID/runtime/OS, before/after managed hashes, parent environment/CWD preservation and original command/report retention. The physical boundary case still compares actual file bytes and independently reacquires its exclusive lease. Child limits remain 30 seconds for boundaries and 60 for caller coordination. Existing shell entry points and shipping dependencies are unchanged.

## Verification

Windows unit `7e3075edcbd240378bb0352b52c40c0f` passed 31 and integration `834e9746c49a44369493aa3d7a047366` passed 44 (16 modes plus all 28 corruptions), zero failures/skips. Ubuntu unit `19367ddcb5b74932a16f5e1ba877cedd` passed 31 and integration `7c62793c3ec0405e9f264f53a03616bd` passed 44, zero failures/skips. The audit confirms matching identities for all 75 native cases. Linux uses scoped current source copies and isolated outputs, not a shared exact package or full lane checkpoint.

The initial unit attempt `09621e7da4a842849f772e260b2e1a4a` stopped at compilation on the new reader's nullable JsonNode string conversion. It receives no test credit. Using GetValue<string> resolved the guard before the successful runs. Formatting then passed. No assertions were weakened and no shipping code changed.

The latest full Windows Developer result remains the earlier 5,948-case value checkpoint. Current full coverage/mutation/compatibility, both-lane corrected qualification and release remain open. The original roadmap stays paused while separately authorized corrective work continues.

## Retirement

Guarded original SHA-256 values:

- `verify-flush-boundary.py`: `cff3ef87ad64226fe0a0ec50c7418950ae809b0579cec522d711018122ad9932`.
- `verify-flush-coordination.py`: `4ffb61792ae5316489560e707202154527229e81b40cb8c78f108fc7ddf770e1`.

No maintained importer remains. Remaining: 27 nonhistorical implementation Python files (18 DiscoveryHost readers, six engineering utilities and three Type Manager fixture utilities), plus one maintained documentation selector. Five historical implementation helpers and 106 frozen planning/evidence files are preserved. The shared Python flush-result helper remains for its file/timer importers.

Final graph: schema 2, 10,760 nodes/51,536 edges, fresh with zero diagnostics, build `14cc943077c5f2772fcacc5611cb809d0d91777d85f5536bf8f0cc760c82da88`. FlushBoundaryExpectations resolves uniquely; the depth-one related query is untruncated (three nodes/three traversals, limit ten, proposed edges excluded). Compiler/semantic extractors remain 6/3. This is bounded navigation evidence, not semantic completeness.
