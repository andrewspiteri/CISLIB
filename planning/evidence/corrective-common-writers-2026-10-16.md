# Common writer composition checkpoint

16 October 2026. Partial corrective Batch D follow-up to incoming checkpoint `450a3ee3`.

Console, file and application registrations now become `OutputWriterBinding` entries before workers start. Queue creation, initialization, optional periodic flush and cleanup iterate those entries. Runtime encoding/preparation/admission uses the same collection and no longer retains concrete console/file writer fields or branches by name for rendering. Built-in adapters preserve configured presentation and file recovery/ownership; the public application writer still receives immutable JSONL strings. Legacy paired queue constructors remain for controlled historical fixture seams.

Four mixed-destination cases cover JSONL/Human console and file together with a healthy or failing owned application writer. They verify equal physical built-in bytes, independent progress after application-writer failure, incoming settlement, owned disposal and borrowed console survival.

Windows complete unit verification passed 3,495 cases with no skips: `.artifacts/csharp/focused/ac9a57b9d2ab4e6b8857ee473041222d`. An initial build identified two fixture constructor adaptations. The first full run found five failures caused by three regressions: nullable prepared memory became empty memory, healthy rendering interest was sampled after a caller callback, and invalid capacity was wrapped as initialization failure. Production code was corrected; the existing assertions were preserved. No shipping dependencies or public API changed.

This development checkpoint does not complete Batch E, the remaining native verification migration, coverage/mutation, IDE acceptance or final release qualification.

The complete Windows integration project passed 1,444 cases, including the 19 exact Debug package consumers, after repacking current Core: `.artifacts/csharp/focused/ba16606e73044c029c60b1eff46620e3`. Ubuntu passed 278 focused incoming/writer/rendering/file/lifecycle cases in the existing isolated checkout: `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/2c43214b02e24de4b8f60d4f87d7c2e7`. Both runs had zero failures/skips. These use SDK 10.0.401 and runtime 10.0.12; the Linux scope remains selected source copies, not complete candidate qualification.

Graph refresh succeeded with 10,477 nodes, 49,088 edges and zero diagnostics, build `1667975523772c165447964de1f05d7c1346e5407495f08efe1fc40f1587252f`. Its first sandboxed attempt failed to persist the SQLite transaction; the authorized retry succeeded. Later convenience edits correctly made that graph stale, so it needs another refresh at the next checkpoint. No full semantic/test coverage is inferred from these graph counts.
