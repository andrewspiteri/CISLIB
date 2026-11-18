# RestClient observations — v1

These definitions accompany the [REST contract](../rest-client/README.md).
They are part of configured REST composition, rather than unconditional additions
to existing Metrics-only applications. Native verification is recorded separately
under [CIS-0010](../../docs/changes/CIS-0010/implementation-plan.md); declarations
and authored tests are not exporter or composed-operation passing evidence.

| Logical name | Kind / unit | Dimensions | Default capacity |
| --- | --- | --- | --- |
| `cis.restclient.operation.duration` | Timer / Seconds | client, operation, outcome | 512 |
| `cis.restclient.active.operations` | Up/down counter / Count | client | 128 |
| `cis.restclient.payload.uploaded` | Counter / Bytes | client, operation | 128 |
| `cis.restclient.payload.downloaded` | Counter / Bytes | client, operation | 128 |

The twelve outcomes are `success`, `http_error`, `business_error`,
`transport_error`, `timeout`, `cancelled`, `authentication_error`,
`preparation_error`, `invalid_response`, `limit_exceeded`, `stopping` and
`unexpected_error`. They do not change result or exception classification.

One mutable registry belongs to one host and publishes one complete four-handle
binding. At most sixteen client aliases and thirty-two distinct client/operation
pairs, including every client's literal `request` fallback, are registered.
Aliases compare ordinally and satisfy the ordinary 256-UTF-8-byte Unicode value
bound. Unknown operation names use that fallback, without retaining unknown
names, URLs or a per-call definition. No correlation ID, remote code, exception
type, path, destination or secret becomes a dimension.

An admitted logical operation captures its binding once. Its entry timestamp
starts the total operation budget; terminal duration uses that exact entry
through actual retry waits, decoding, callbacks and owned cleanup. Active work
adds one at admission and subtracts one at settlement, including thrown defects.
First completion is final. Later attachment does not replay earlier operations
or introduce a decrement without its matching increment.

Uploaded bytes are bytes accepted by completed application-content writes;
downloaded bytes are bytes returned by completed application/decompressed reads.
Count partial completed chunks and retransmissions across retries. Exclude
unknown partial bytes from a write that threw, signing-buffer preparation,
Content-Length inference, headers, TLS framing and network-interface traffic.
Error-prefix and limit-probe reads count because they actually consume bytes.
HEAD/NoValue does not fabricate a body transfer. There is no transfer timer or
duplicate logical completion counter.

The optional collector selects only native `http.client.request.duration`
(histogram / seconds) and `http.client.active_requests` (up/down / requests) from
the exact root factory's `System.Net.Http` meter on .NET 10. Both keep only
`http.request.method`, plus mandatory central application/environment identity.
Native duration and active work end at response headers; neither substitutes for
logical completion or establishes an exact owned primary-dispatch count.

`CollectOutgoingHttp=null` is Auto: outgoing collection is enabled for final
configured REST composition. Explicit true/false selects/opts out independently
of logical observations. `CollectCoreModules=false` suppresses automatic logical
REST binding. The final profile is immutable before activation in either
registration order; there is no second provider or live rebuild.

With default Core/incoming selection, no REST and Auto retain nineteen Core
reservations, twenty-one total streams and capacity sum 3,328. Configured REST
with Auto uses twenty-three Core reservations, twenty-seven total streams and
sum 4,416. The difference is 896 logical plus 192 native capacity. Global
96-definition / 128-stream / 16,384-capacity / four-MiB encoded-response limits
remain unchanged. Standard fourteen duration bounds and SDK additional special
points remain in effect. This profile arithmetic is not a new heap/footprint
or platform qualification result.

Expected exported families are `cis_restclient_operation_duration_seconds`
with `_bucket`, `_sum`, `_count` children;
`cis_restclient_active_operations`;
`cis_restclient_payload_uploaded_bytes_total`;
`cis_restclient_payload_downloaded_bytes_total`;
`http_client_request_duration_seconds` with histogram children; and
`http_client_active_requests`. Exact observed exporter evidence remains distinct
from these required spellings.
