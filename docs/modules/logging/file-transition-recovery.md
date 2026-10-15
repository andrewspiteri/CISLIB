# Interrupted file transitions

Rotation and partial recovery publish schema-2 intent before any retirement. It names the exact old/new inventories, original active bytes and planned retirement IDs/names/lengths/hashes. Restart validates all surviving members before resuming only that exact retirement/rename/publication/fresh-active sequence. The oldest planned prefix may be partly or wholly absent; a missing survivor or changed retirement still fails. Legacy schema-1 intent retains its already-retired-prefix rule. A full retention group can recover by retiring its oldest owned member and preserving the complete partial active as damaged.

Each metadata kind uses one private candidate slot; a failed creating attempt removes only its own candidate. Failed cleanup leaves a blocking slot, rather than generating unbounded files. Occupied/unrelated candidates are preserved. Committed recovery may complete exact recorded retirement; pre-commit rollback still preserves existing members. Unproven pre-intent gaps and ambiguous states fail without guessing.

The [current review addendum](review-contract-2026-10-15.md) defines resumable states, safe categories and capacity/lifecycle rules. This cooperative protocol does not promise power-loss durability, hostile-race resistance or exactly-once delivery.

## Qualification

Thirty-two direct cases cover the four resumable stages for normal/partial JSONL, malformed and ambiguous states, owned handle release, changed selected bounds, the unprovable retirement gap, and oldest damaged/closed ordering. Sixteen actual producer modes stage physical interruption states and then create the configured runtime: twelve successful JSONL/Human states and four unchanged failures. These are staged process-state fixtures, not observations of forced process termination or power loss. The independent native reader validates literal preimages, exact inventories/filesystem transitions, complete fresh records and actual producer identity, immutable recovery flags, current delivery accounting, exclusive release before exit and twelve specific corruption controls. Full S07 restart/collector qualification remains WP05-S07-T04.


[Task evidence](../../../planning/logging/evidence/wp05-s07-t03/receipt.json) retains 4,681 passing Developer tests, 59 compiler controls and 257 Windows/Ubuntu native process runs with 128 specifically rejected corruption controls. Frozen native DLL copies exactly match the final Developer assemblies; all 599 current critical inputs match execution. Claude initial R1 is corrected together with O1-O5 and the follow-up is PASS with no required findings. Static review is separate from execution and indexed byte audits. The Release gate remains incomplete with all 41 Planned rows preserved.
