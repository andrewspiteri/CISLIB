# Messaging observations

The module uses one registration-bound instrumentation owner. Collection is
conditional on explicit Messaging composition and `CollectCoreModules`; it adds
no Messaging definitions or stream reservations when that composition is absent.
The current global 96-definition, 128-stream and 16,384-combination limits remain
unchanged. Each registry admits at most sixteen aliases.

| Logical stream | Kind/unit | Dimensions | Reserved combinations |
| --- | --- | --- | ---: |
| `cis.messaging.publish.duration` | Timer/seconds | route, outcome | 80 |
| `cis.messaging.delivery.duration` | Timer/seconds | subscription, outcome | 112 |
| `cis.messaging.rpc.duration` | Timer/seconds | request, outcome | 160 |
| `cis.messaging.active` | Up/down counter/count | kind | 3 |
| `cis.messaging.dispositions` | Counter/count | subscription, disposition | 48 |
| `cis.messaging.outbox.pending` | Observable gauge/count | store | 16 |
| `cis.messaging.outbox.oldest.age` | Observable gauge/seconds | store | 16 |

The total reservation is 435 combinations. An explicit stream override must cover
its finite profile; it cannot shrink away supported alias/outcome combinations.
Timers use the existing fourteen finite duration bounds plus positive infinity.
Histogram counts already supply operation volumes.

Publication measures one admitted native invocation through actual native
settlement, before the separate SQL publication-state settlement. Its outcomes
are confirmed, rejected, unknown, cancelled and failed. Confirmation is not
application processing. Delivery measures one admitted consumer attempt through
transaction/disposition/scope cleanup and actual native source settlement. Its
outcomes are handled, duplicate, retry, terminal, unknown, cancelled and failed.
The provider's reserved reply callback alias is excluded from delivery labels.

RPC measures one admitted typed exchange through its owned local settlement.
Its vocabulary is response_received, timed_out, cancelled, not_submitted,
submission_unknown, submission_not_confirmed, unavailable, overloaded,
invalid_response and failed. Its actual typed exchange binding is supplied by
Part B; reserved catalog capacity is not evidence of an executed RPC.

Active work balances publish/consume/rpc at entry and every exit. Each operation
captures one handle bundle; attaching Metrics later never replays an active call.
The committed disposition counter uses only retry, terminal and duplicate;
preparation, native confirmation and an unknown SQL completion do not increment it.

Backlog gauges read only the last bounded store observation in memory. Unknown,
incomplete counts and observations older than the greater of five seconds or
three selected idle poll intervals are omitted. A known empty backlog is zero;
unavailable data is not zero. SQL, authentication and application work never run
from a scrape callback.

The exporter supplies authoritative application/environment labels. Message IDs,
business keys/scopes, endpoints, broker addresses, payloads and exception text are
never dimensions. Expected translated families include
`cis_messaging_publish_duration_seconds_count`,
`cis_messaging_delivery_duration_seconds_count`,
`cis_messaging_rpc_duration_seconds_count`, `cis_messaging_active`,
`cis_messaging_dispositions_total`, `cis_messaging_outbox_pending` and
`cis_messaging_outbox_oldest_age_seconds`. Actual scraped evidence is recorded in
the owning change's verification file.
