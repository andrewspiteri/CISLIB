# Interrupted file transitions

WP05-S07-T03 keeps damaged and closed segments in the same retained-file count, ordered by persistent segment ID. Required oldest-member deletion still finishes before any further segment growth. A deletion failure faults the file worker through RetentionDeleteFailed, preserving other outputs. Startup does not silently prune pre-existing evidence.

Rotation now publishes one bounded pending record at <active-path>.cis.transition.json after required retirement and before rename. Partial recovery publishes it before rename, with no startup retirement. The record contains the previous and next inventories, the original selected retention limit, the operation kind, and the old active file's length and SHA-256. It is published from a flushed same-directory private candidate without overwriting an existing pending name. Computing the digest reads the old active once per transition; restart checks that complete byte preimage before trusting the intent.

Under the exclusive cooperative path-group lease, startup validates the bounded strict UTF-8 record, inventories, exactly permitted identity progression, selected size/retention limits, surviving named members, and the original bytes. It resumes these known states:

| Persisted state | Startup action |
| --- | --- |
| Old inventory, old active present, target archive absent | Rename the verified old active, publish next inventory, create fresh active |
| Old inventory, old active absent, verified target archive present | Publish next inventory and create fresh active |
| Next inventory, verified target archive present, active absent | Create fresh active |
| Next inventory, verified target archive present, active empty | Remove the completed pending record |

The pending record is removed before runtime publication or the next entry write. No fresh entry is appended while it exists. Recovery diagnostics remain true for a successful interrupted partial-recovery operation, including completion after inventory publication; a later intact runtime reports false.

Both old active and target present under old metadata, neither present, changed original bytes, nonempty fresh active, missing surviving members, reappeared retired members, mismatched inventories, malformed records and unsupported identity progression fail clearly before changing existing bytes. Invalid transition state uses FileTransitionIncompatible; access/publication/create/cleanup errors use FileTransitionInitializationFailed. These fixed internal categories retain no raw paths, payloads or inner exceptions. Lower newly selected size or retention limits fail unchanged using the existing limit categories.

A stop or failure after retirement but before pending publication has no reliable transition proof. Startup fails unchanged on the old inventory's missing member. Older interrupted states without a pending record also retain the existing fail-closed behavior; startup never guesses from unlisted .tmp candidates, archive filenames or timestamps. Private candidates and unrelated/unlisted neighbors are preserved. Failures during a verified resumption can leave another documented stage for a later attempt; there is no retry in the failed runtime.

The protocol coordinates cooperating writers and makes deterministic process restart possible. It does not guarantee power-loss durability, hostile filesystem-race resistance, network-filesystem semantics or exactly-once ingestion. Partial-tail validation and Human exclusions remain unchanged.

## Qualification

Thirty-two direct cases cover the four resumable stages for normal/partial JSONL, malformed and ambiguous states, owned handle release, changed selected bounds, the unprovable retirement gap, and oldest damaged/closed ordering. Sixteen actual producer modes stage physical interruption states and then create the configured runtime: twelve successful JSONL/Human states and four unchanged failures. These are staged process-state fixtures, not observations of forced process termination or power loss. The independent native reader validates literal preimages, exact inventories/filesystem transitions, complete fresh records and actual producer identity, immutable recovery flags, current delivery accounting, exclusive release before exit and twelve specific corruption controls. Full S07 restart/collector qualification remains WP05-S07-T04.


[Task evidence](../../../planning/logging/evidence/wp05-s07-t03/receipt.json) retains 4,681 passing Developer tests, 59 compiler controls and 257 Windows/Ubuntu native process runs with 128 specifically rejected corruption controls. Frozen native DLL copies exactly match the final Developer assemblies; all 599 current critical inputs match execution. Claude initial R1 is corrected together with O1-O5 and the follow-up is PASS with no required findings. Static review is separate from execution and indexed byte audits. The Release gate remains incomplete with all 41 Planned rows preserved.
