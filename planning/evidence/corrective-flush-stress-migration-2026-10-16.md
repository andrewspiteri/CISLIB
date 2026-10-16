# Native flush-stress reader migration

16 October 2026, based on `049ed1a3`. Corrective Batch B changes verification only. Shipping Core, public behavior, dependencies, original vectors and historical receipts remain unchanged. Local review applies; no Claude transfer.

All eight stress scenarios and all 34 original corruption controls are preserved in native C#. The reader now checks complete prefix dispositions, canonical counters, UTC values, bounded copied result/caller storage, monotonic deadlines, ticket and record ordering, timer ownership, callback lifetime and final diagnostics. Each negative control follows physical positive output validation. The original eight-scenario selection includes 15,000 repeated-caller iterations and 32 callback races; these establish correctness, not performance results.

The progress scenario retains one registration behind its active write until settlement. This reconciles the old Python zero-registration assumption with the current fixture and coordination contract, without changing runtime code.

## Verification

Windows unit `5be0ea5452344cfcbd47c7e8ec21a566` and Ubuntu unit `874c04a87d4748e5b2fb2fc012ac8da2` each pass 63 cases. Windows integration `5f8132b193954677a3dbcd33d8645718` and Ubuntu integration `453e8d4444af43cebdae6d170e52caf0` each pass 129 cases: 41 physical modes and 88 original corruption cases across boundary, coordination, results and stress. All runs have zero failures/skips; all 192 case identities match across lanes. The initial Windows stress-only unit selection `596f7449db10402a80a7b72ced375a7f` also passes eight. Builds report zero warnings/errors. Formatting completed cleanly.

Process checks retain actual child PID/runtime/OS, before/after managed input hashes, complete parent environment/CWD preservation and original command/report evidence. Ubuntu uses scoped current source copies and isolated outputs. These are focused native checks, not a shared exact package or complete lane checkpoint. The latest full Windows Developer result remains the earlier 5,948-case value checkpoint. Full corrected-candidate coverage, mutation, compatibility and release qualification remain open. The original roadmap remains paused.

## Retirement

`verify-flush-stress.py` is retired after read-only call-site reconciliation. Original SHA-256: `25657e5379e9775a2fc2b0a3496c048a1aa3524eee70afd61998418cadf591c5`. Guarded retirement and case audits are retained under `.artifacts/corrective/flush-stress-reader`; Git preserves the original source. The temporary `verify-flush-results.py` helper remains byte-for-byte unchanged (1,359 bytes, SHA-256 `644c01f722a1d81153444b7facfa5a91c533d1d6c3168c866793fe6a7125d305`) for its five remaining file/timer importers. No Python was executed or new Python implementation added.

There are 26 nonhistorical implementation Python files remaining: 16 DiscoveryHost drivers, one temporary helper, six engineering utilities and three Type Manager fixture utilities. One maintained documentation selector also remains. Five historical implementation helpers and 106 frozen planning/evidence Python files remain preserved.

Final graph: schema 2, 10,812 nodes/51,803 edges, zero diagnostics, build `d177d58fefb5a013fcaf9110206b24fe99f88de526d18b8485cccddd07fea35a`. Compiler/semantic extractors remain 6/3. The exact stress reader resolves uniquely; its fresh depth-one related query is untruncated, with three nodes/three traversals (limit ten, proposed edges excluded). This is bounded navigation evidence, not semantic completeness.
