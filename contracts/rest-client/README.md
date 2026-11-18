# RestClient contract — v1

This portable contract records the supplied [specification](../../planning/rest-client/sources/CISLIB_RestClient_Implementation_Specification_v1.0.md)
and approved P01–P12 [reconciliations](../../planning/rest-client/reconciliation.md).
The C# binding is implemented in `CIS.RestClient` in `CIS.Core`, with optional
retry and Metrics integrations. The [literal fixtures](../../conformance/rest-client/v1/contract-cases.json)
describe requirements; execution evidence belongs to
[CIS-0010 verification](../../docs/changes/CIS-0010/verification.md).

## Ordinary calls and results

The asynchronous typed verbs GET, POST, PUT, PATCH, DELETE, OPTIONS and TRACE share
the same execution path as an explicit-method Send. HEAD returns `RestNoValue`.
Request and response types are independent. A custom valid method token does not
enable CONNECT, protocol upgrades or an unimplemented channel capability.
Ordinary signatures expose CIS-owned contracts and normal tasks/cancellation,
without native HTTP, resilience or collector SDK types. Explicit platform
configuration and stream adapters remain separate integration surfaces.

`RestResult<T>` has exactly five readable properties:

| Property | Meaning |
| --- | --- |
| `Success` | All promised HTTP, decoding, validation and mapping succeeded. |
| `ErrorCode` | Stable portable client/HTTP/domain code; absent on success. |
| `ErrorMessage` | Bounded explanation; absent on success. |
| `Value` | The typed value, or the type's default when absent or failed. |
| `Context` | Detached typed terminal facts, including explicit `HasValue`. |

Construction is guarded and immutable. A value success requires `HasValue=true`
and no failure; an empty success requires `HasValue=false` and no failure. A
failure requires a nonempty code/message, a failure category, `HasValue=false`
and default(T). Public construction, setters or record-copy operations must not
bypass those invariants. No live response, stream, exception graph, arbitrary
property bag or attempt history escapes through context.

Presence is independent of default values and nullness. JSON `0` and `false`
are present; JSON `""` and `[]` are present empty values. JSON `null` is present
when T can represent null, and invalid when it cannot. Zero consumed bytes mean
absence in JSON, Text and Bytes modes. Text requires a compatible string result;
Bytes requires a compatible byte-array result. `RequireValue` rejects absence,
including an empty successful status, but accepts a representable present null.
HEAD/NoValue skip successful body decoding. Neither status nor Content-Length
fabricates a value or consumed-byte count.

Non-2xx responses never decode into T. A mapper may extract a separate remote
code or reject an HTTP-200 envelope as a business failure, but cannot make an
actual non-2xx response successful. A later terminal exchange without a response
has no status; it cannot inherit an earlier attempt's status.

Context retains copied case-insensitive multi-value headers, actual terminal
status when known, finite failure/retry-stop classifications, client/operation
aliases, actual primary dispatch count, and elapsed time through owned cleanup.
An optional bounded error-byte prefix retains media type. `ErrorBodyTruncated`
and `ErrorMessageTruncated` describe different truncations. Provider error codes
remain separate from portable codes and never become metric dimensions.

## Captured defaults and bounds

| Setting | v1 profile |
| --- | --- |
| Total timeout | 30 seconds from public entry, including preparation, retries, body, mapping and cleanup. |
| Retry | None; no implicit executor or default retry stack. |
| Successful response limit | 16,777,216 consumed/decompressed bytes. |
| Error prefix | At most 16,384 retained bytes. |
| Error message | At most 1,024 Unicode scalars, with a separate truncation flag. |
| Native redirects/cookies | Disabled in the supported default transport profile. |
| Client/operation registry | At most 16 clients and 32 distinct pairs, including each client's literal `request` fallback. |
| Alias values | Nonblank valid control-free Unicode, at most 256 UTF-8 bytes, compared ordinally. |

Processing overrides can narrow registered response/error/message bounds, never
enlarge them. Positive timeouts must be representable by the native cancellation
timer. Limits do not promise a bound on a decoded object graph or process heap.
There is no default whole-upload buffer. Signing obtains one explicitly bounded
exact representation that the sender reuses.

Registration snapshots lists, headers, limits and extension references. Admission
captures per-call overrides under the already-running total budget. Headers are
case-insensitive and multi-valued: Set replaces, Append preserves inherited
values and Remove removes inherited values. Raw query additions preserve explicit
order/repetition and are encoded once. Calls never mutate reusable input options
or shared native default headers. Borrowed body objects/buffers remain unchanged
until settlement; extensions must support concurrent calls.

Null per-call callbacks inherit the registered callback. A replacement replaces
it; an explicit disable flag suppresses inheritance. Replacement plus disable
is invalid. Per-call retry null inherits, `None` disables and a named selection
replaces the whole policy. An unknown selected policy or missing selected
executor fails configuration rather than silently running once.

## One retry owner

The optional upstream executor owns retry iteration and delay. Core/None remains
usable without that package. The `transient-http` preset allows two retries
(at most three actual primary dispatches), GET/HEAD/OPTIONS and statuses
408/429/502/503/504. It uses upstream exponential jitter, a 500-ms nominal initial
delay and a five-second local cap. It has no default transport, business,
decoding or post-headers body retries, and no attempt timeout.

Named policies are immutable finite registered instances. Explicit method,
status, failure, error-code and transport selectors define eligibility; an
optional decision callback may only narrow that eligibility. It cannot bypass
replay safety, content replayability, cancellation, remaining budget or iteration
limits. Custom write retries require real endpoint replay assurance and an
equivalent destination/method/business payload with a stable provider-supported
idempotency key; an arbitrary header is insufficient. Credentials/signatures may
refresh. A one-shot body cannot become replayable through that assurance.

Parse Retry-After with native HTTP support. Choose one delay
`D = max(local jittered backoff, server minimum)` and test that exact choice
against the remaining total budget before waiting. Never shorten the server
minimum to a local cap, recompute jitter or dispatch early when D cannot fit.
Prior request/response/content cleanup must settle before a later attempt.
Policy iteration and actual primary dispatch count are distinct; a handler
short circuit has zero dispatches. The optional adapter's supported-API delay
composition has its bounded native proof recorded in verification.

## Cancellation, authentication and ownership

Unexpected programming faults remain faults even if cancellation is also
signalled. For expected outcomes and identified cancellation, precedence is
caller → total deadline → owned stopping → attempt timeout → actual outcome.
Unrelated cancellation exceptions are not converted into timeout results.
Caller cancellation remains caller-associated cancellation; expected failures
otherwise use documented result categories. Preserve the original cause when
cleanup also fails. Cancellation after terminal result commit does not rewrite
that result.

Callbacks and handlers run sequentially; each handler invokes next at most once.
Ordinary mutation finishes before final destination/origin validation and
authentication. Signed method/URI/content are finalized before primary dispatch.
TRACE omits inherited authentication/credentials and rejects explicit or
customized credential fields and body, including declared credential query keys.
Inherited authentication derives an allowed origin from BaseUrl or requires an
explicit origin list when no base exists. Native TLS validation is retained.

Argument/configuration rejection before admission acquires no caller resource.
Admission/acquisition establishes stream ownership explicitly. Borrowed native
clients/handlers/streams remain open; acquired factory-owned/transferred content
is disposed even after authentication rejection or a short circuit. A handler
that starts next and then throws still joins the owned exchange. The public task
settles only after actual asynchronous body/callback/disposal work, not merely
after signaling cancellation or abandoning a wait.

Shutdown closes REST admission and signals its owned cancellation before existing
Heartbeat/Configuration drains can wait on REST. Existing hosting joins all real
barriers before dependency teardown; Startup's coordinator stays independent.
No generic lifecycle registry, scheduler, callback framework or detached work is
introduced. Borrowed opaque native handler configuration is not inferred or
certified; supported transport provenance is explicit.

At entry, CorrelationId captures an existing valid W3C TraceId or remains absent.
It is not Logging's OperationId, generates no identifier and is not a metric key.
One captured observation binding covers the logical operation through retries
and cleanup. Count only completed accepted upload writes and actual consumed
application/decompressed reads, including retries; never infer failed-write
partials, Content-Length, headers, TLS bytes or network-interface traffic.

## Literal conformance inputs

The JSON fixture contains independent presence/default/retry expectations and
references the existing [valid result-construction vectors](../../conformance/rest-client/v1/result-cases.json),
with complementary invalid-construction cases.
`bodyUtf8` is exact UTF-8 text; `bodyHex` is exact bytes. A JSON
`null` expected value does not imply absence: always inspect `hasValue`.
`resultType` is a portable test descriptor, not a CLR type name. Construction
vectors describe invariant admission, not new public constructors; `argument`
means invalid input is rejected, without prescribing a language exception type. Eligibility
vectors assume replayable content, available budget and an executor unless
otherwise stated. Ordinary native tests may consume appropriate sections without
a new runner. Loading the file does not establish that any vector, transport,
platform, exporter or package consumer passed.

Fixture verification metadata retains its original RC1 authoring checkpoint.
The result-construction vectors and the response-wire sections have actual native
consumers; current passing counts and later public/package evidence are recorded
in verification. Other fixture sections are not claimed as executed solely
because the file was loaded. Preserve the original fixture bytes and distinguish
these facts when adding another language binding.
