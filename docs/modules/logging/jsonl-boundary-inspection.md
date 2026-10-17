# Managed JSONL boundary inspection

> Current status, 17 October 2026: runtime, output and provider implementation and the native C# reader migrations are complete development work. Task-numbered milestones, earlier Python comparisons and task-local passing counts below describe historical execution, including their then-pending work. Use the [feature report](../../../planning/feature-status-2026-10-15.md) for current corrective/release status and [native test instructions](../../../implementations/csharp/tests/README.md) for maintained commands. Historical receipts retain their original source and do not qualify the current tree.

WP05-S07-T01 inspects the owned active file before append. The path lease and managed inventory must first establish JSONL, UTF-8, no BOM and LF identity. Missing or incompatible inventory fails before tail inspection, including when the underlying bytes are malformed.

The internal inspection returns the physical length and one of four statuses:

| Status | Meaning |
| --- | --- |
| Empty | The active file has zero bytes. |
| Complete | The final LF-terminated record is a strict UTF-8 JSON object without a raw CR. |
| Partial | The nonempty file does not end in LF. Incomplete UTF-8 or JSON is left uninterpreted. |
| Invalid | An actual UTF-8 BOM prefix or an invalid terminated final record was found. |

Inspection restores the stream position and changes no file or inventory bytes. It checks the actual first three bytes for a UTF-8 BOM and scans backward for only the final record, using a 4 KiB buffer and the absolute supported 16 MiB entry bound, including LF. A valid historical record remains eligible when new configuration selects a lower entry cap. JSON parsing uses depth 64. It does not inspect every earlier record, enforce the full event schema, or validate the contents of retained segments.

The existing file writer accepts Empty/Complete and appends one prepared record through its queue worker. At the T01 checkpoint, Partial/Invalid failed with the fixed safe code FileRecordBoundaryInvalid, preserve existing evidence and release acquired handles. File size validation still precedes this check; retention startup validation still follows it. Human output keeps its existing final-LF check. No public API, dependency, output queue or retry mechanism changes.

## Verification and scope

Twenty-one unit cases cover classification, unchanged bytes/position, managed-owner eligibility, exact absolute-cap boundaries including a previous record, and actual writer startup with a lower new entry cap. Seventeen fresh-process cases cover actual append or unchanged rejection, Unicode paths/content, the LF at the start of the first backward-scan block, malformed terminated records, partial JSON/UTF-8, BOM and metadata priority. Independent native Python readers compare literal seed bytes, exact inventory identity and filesystem membership, original process streams, current assemblies, actual producer PID and before-exit exclusive handle probes. Seven targeted corruption controls qualify those readers on each platform.

The [task receipt](../../../planning/logging/evidence/wp05-s07-t01/receipt.json) retains the final 4,605 passing Developer tests, 59 compiler controls and 213 current-assembly Windows/Ubuntu native consumers (34 boundary cases plus 179 filesystem regressions), with 92 independently rejected corruption controls. Current source, assemblies, verifiers, original streams and indexed evidence are audited separately from Claude initial/follow-up PASS. Full interrupted-file recovery remains WP05-S07-T02/T03/T04: uniquely preserving damaged segments, recovery diagnostics, damaged retention, interrupted inventory reconciliation and collector selection. This inspection does not complete the four S07 acceptance criteria. Public bounded lifecycle, provider integration, activated runtime examples/skills and release remain later work. No crash durability, exactly-once ingestion or hostile filesystem race guarantee is added.

WP05-S07-T02 now [recovers Partial](jsonl-partial-recovery.md) into a uniquely identified damaged segment before fresh append. Invalid boundaries retain the unchanged failure. The receipt above is historical T01 evidence; current recovery results are recorded separately.

The 16 October [native boundary migration](../../../planning/evidence/corrective-jsonl-boundary-migration-2026-10-16.md) retires its Python driver after preserving all 23 modes and twelve original corruptions. Both lanes pass 35 integration, 38 related unit and one acceptance-map architecture case. Literal complete preimages, records and physical membership remain checked; retention-full recovery follows the current bounded intent-first retirement rule.
