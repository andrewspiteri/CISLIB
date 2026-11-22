# Testcontainers correction review

Independent source review found and corrected retained-schema ownership during
failed PostgreSQL reconstruction. The replacement now borrows the retained schema
until reconstruction succeeds; the new real-provider failure/continuation case
passes. Recheck accepted the correction without a test hook or extra framework.

Cross-author reviews accepted the owned PostgreSQL/common API, RabbitMQ TLS,
minimal per-child handoffs, categories, native TRX guard and Linux workflow.
Subsequent independent review accepted the narrow module binding correction and
the two truthful restart boundaries after actual full-container probes disproved
original random-port preservation. Existing live-client assertions, real persistent
unacknowledged work, actual provider behavior, permissions and owner lifetimes
remain covered. There are no required or optional source findings outstanding.

Final independent local receipt review passed. All 51 source inputs, six context
receipts, ten reports and both current/snapshot paths for ten binary/configuration
snapshots match their recorded hashes. Actual result counts match native counters;
every result is Passed and every run completed. The four current Core copies agree,
shipping dependency direction/closure is preserved, all five guard controls reject,
and exact restoration and the unchanged user file were independently checked.

Actual passing Linux CI is separately required before the exact head is merged.
See [verification](verification.md) for current observed results and retained
limitations; no source review substitutes for native execution.
