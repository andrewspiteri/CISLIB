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

The fresh-CI portable consumer correction passed independent three-file and delta
receipt review: exact retained package identities, supported lock RIDs and current
Windows 29+1 consumer results, with earlier immutable proof preserved. Subsequent
identity-profile review accepted the conditional anchored project default, its
identity-only ordinary profile and the unchanged strict service gates. This keeps
production unknown-identity rejection and ordinary zero-match/long-test behavior.

The final source-import condition also passed independent review: the exact
anchored path retains the dual-RID lock only in source checkouts. The actual
offline package consumer passes after the retained 2,556/1 fast result, and the
source solution still restores/builds cleanly with its selected Windows RID.
