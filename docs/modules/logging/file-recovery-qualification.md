Current behavior is amended by the [15 October review corrections](review-contract-2026-10-15.md), including intent-first retirement, full-group recovery, bounded candidate cleanup and monotonic completion timing. The task-specific qualification narrative below is historical evidence at its recorded checkpoint.

# Restart and collector-input qualification

WP05-S07-T04 qualifies the existing C# file recovery implementation through separate real producers, restarts and the [source-preserving four-criterion map](../../../conformance/logging/file-recovery-qualification-map.json). No shipping implementation or dependency is added.

The fixture first writes two actual Core events. It then reacquires the Core group lease while staging one precise physical checkpoint. The pre-state inventory is fixture JSON; retirement, rename and fresh-active creation reproduce the implementation order using fixture filesystem operations. Pending-intent publication, inventory retirement selection and replacement use actual Core operations. Native Windows and Ubuntu verifiers observe that checkpoint, forcibly terminate only their own producer, and start an independent configured Core runtime. Two competing-owner probes fail while the producer still holds the lease. This verifies release on forced process exit and restart from recorded physical states; it does not interrupt an uninstrumented production Rotate call or simulate power loss.

The fifteen scenarios cover intact append; unterminated JSON and UTF-8; rotation and partial recovery after intent publication, rename, inventory publication and fresh-active creation; file/directory damaged-name collisions; an unprovable retirement-before-intent gap; and contradictory old-active/target presence. Restart either resumes the documented consistent state or fails with a fixed safe category and unchanged pre-existing bytes/membership. Literal original records, complete archived bytes, persistent IDs, recovery diagnostics, current counters and four unrelated/unlisted/private neighbors are independently checked.

Damaged and closed members share the retained bound. The rotation checkpoints use the same configured retention limit of two for staging and restart. Core selects the oldest damaged retirement; the fixture performs that deletion before the checkpoint, and restart verifies its result. Direct owner tests execute Core rotation to prove successful damaged retirement, while recovery checkpoints retain three members within their configured bound. The existing native retention-full boundary case proves unchanged failure when partial recovery would exceed the bound. Additional actual runtime cases deny deletion of the oldest damaged member using a directory and an OS-denied operation. They verify no segment growth, retained preimages, console progress, observable terminal fault and released ownership. The pre-existing lower/full-bound startup cases remain unchanged.

## Read-only collector example

[select-jsonl-inputs.py](examples/select-jsonl-inputs.py) is a development helper for a **stopped local managed JSONL group**. Stop the application and wait for its writer to finish before using it. It reads the current private inventory format and prints a JSON array of absolute paths in persistent segment order. Only inventoried closed and active members are selected. Damaged members, another group's files, unlisted same-group archives and private publication candidates are excluded.

```powershell
python -X utf8 docs/modules/logging/examples/select-jsonl-inputs.py C:\logs\application.jsonl
```

The example validates the inventory and every selected record before printing any paths. It refuses a pending transition, missing/nonregular or symbolic-link members, unsupported format/identity/framing, duplicate JSON keys, nonfinite numbers, incomplete records and invalid UTF-8. Inventory reads are bounded to 2 MiB and each JSONL record to the absolute supported 16 MiB including LF. Damaged contents are not parsed. Success returns 0; inconsistent groups return 2 with empty stdout and a fixed diagnostic. These checks validate complete JSON objects and group framing, not the full event schema. The negative mutations test rejection of inconsistent inputs; several violate more than one inventory condition and are not isolated mutation tests for individual guards.

Each successful native restart invokes the actual helper as a separate Python process and verifies exact path selection plus real producer identities in selected records. Failed groups must expose no input paths. Twenty-seven additional filesystem/record/inventory mutations execute the actual helper function and must fail (the symlink mutation may be skipped only when Windows lacks the required privilege), after a valid baseline that excludes malformed damage and unrelated valid JSONL.

Valid exact 2 MiB inventory and 16 MiB record boundaries are also executed. The fixed diagnostic does not expose internal rejection details.

This offline example has no live ownership lock, tailing, transport, acknowledgement or collector SDK. Stop/prevent concurrent writers for the whole selection and read operation; a path list is not a durable snapshot. The private inventory may change before publication. This helper does not implement Core in Python or activate the public runtime/package examples and skills.

## Limits

Execution evidence is development qualification at the internal physical-runtime seam. The staged checkpoints and cooperative same-path lease do not promise hostile-race, network-filesystem, crash-durable or exactly-once ingestion behavior. A pre-intent retirement gap fails clearly without guessing from private candidates. Public creation, registered flush boundaries, bounded shutdown, Microsoft provider integration, activated examples/skills and release qualification remain later roadmap work. Original source plans, historical receipts and the 41 Planned release cases are preserved.
