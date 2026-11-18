---
title: RestClient Core API and ownership
type: document
status: draft
cis:
  stable_id: cislib.document.modules.rest-client.core
---

# RestClient Core API and ownership

`CIS.RestClient` defines the consumer contract in `CIS.Core`. Native HTTP, ordered
extensions, the public runtime, retry integration and existing host bridges have
passing evidence: 93 joined integration cases and 136 component cases. Follow
[CIS-0010 verification](../../changes/CIS-0010/verification.md) for actual checkpoints.
The [portable contract](../../../contracts/rest-client/README.md) and preserved
[requirements](../../../planning/rest-client/sources/CISLIB_RestClient_Implementation_Specification_v1.0.md)
define the complete outcome.

## Typed results and explicit body modes

`IRestClient` has asynchronous `Get<T>`, `Post<T>`, `Put<T>`, `Patch<T>`,
`Delete<T>`, `Options<T>`, `Trace<T>`, `Head` and `Send<T>`. The generic parameter
is the response value, independent of the request body. `Head` returns
`RestResult<RestNoValue>`; other verbs can also use that marker.

`RestResult<T>` exposes exactly `Success`, `ErrorCode`, `ErrorMessage`, `Value`
and `Context`. Construction is internal and guarded; public properties have no
setters. A failed or absent value is default(T), which may be null despite T's
reference annotation. Check both success and `Context.HasValue`. A present
default value such as zero, false or a representable null differs from absence.
Failed results never expose the expected success DTO as a diagnostic substitute.

Zero consumed bytes mean absence for JSON/Text/Bytes. JSON `""`, `[]` and
representable `null` are present decoded values. `RequireValue` tests presence,
not nullness. HEAD/RestNoValue skip success decoding and ignore RequireValue;
Content-Length does not prove any bytes were read.

Context copies response/content headers using case-insensitive multi-value keys.
It retains terminal status, finite failure category, separate provider code,
bounded detached error prefix/media type, independent body/message truncation
flags, aliases, actual primary dispatches, finished elapsed time, retry stop
reason and optional existing W3C trace ID. There is no native response or attempt
history. Logging's operation ID remains a separate concept; no ID is generated.

## Reusable inputs and per-call snapshots

`RestClientOptions` supplies named registration defaults. `RestRequestOptions`
supplies nullable overrides; null inherits, `RestRetrySelection.None` disables,
and `Named` replaces the complete retry choice. Authentication/customization
have separate disable flags; combining a replacement with its disable flag is
invalid. Configuration and callbacks are copied/borrowed into immutable internal
`RestClientSnapshot` and `RestCallSnapshot` instances. Calls never mutate reusable
options. Concurrent callers must not mutate input objects while they are captured.

Defaults are thirty-second total timeout, None retry, JSON, optional value,
sixteen-MiB success bytes, sixteen-KiB error prefix and 1,024 message scalars.
Per-call processing limits may only tighten the registered limits. A zero error
prefix cap disables its capture. Positive timeouts must fit the native cancellation
timer. These bounds are not a whole object-graph or process-memory guarantee.

`RestHeaders.Set` replaces inherited values, `Append` adds repeated values and
`Remove` removes inherited values. Query changes preserve Set/Append/Remove intent
and raw values; the native adapter must encode once and preserve unaffected
already-encoded URI text. Call snapshots retain those changes independently.
Ordinary query additions are applied/cleared before authentication; only later
credential additions remain for final sending.

`RestBody.Json<TBody>`, `Text`, `Bytes` and `Form` are passive descriptors.
JSON retains its declared type even for null. Objects/buffers remain borrowed and
must stay unchanged until settlement; form field structure is copied. C# stream
adapters live in `CIS.RestClient.Http.RestHttpBody`: `Stream` is one-shot and leaves
the input open unless ownership transfer is explicit; `Reopen` promises a fresh
equivalent client-owned stream per attempt. Constructing a descriptor or snapshot
does not open, consume or dispose a stream. The admitted attempt owns actual acquisition/cleanup.

## Ordered extension and retry seams

Request customization/handlers mutate `RestRequestContext` before finalization.
`RestAuthenticationContext` exposes finalized method/destination and only declared
credential writes; signed fields are sealed before primary dispatch. Its bounded
body-representation capability must reuse exactly the bytes sent. Typed `Fail`
declares preparation/authentication rejection; unexpected thrown defects remain
faults. Callback token propagation, final URI rendering, TRACE/origin enforcement
and actual ordered execution share the same awaited operation owner.

`IRestExchangeHandler` wraps one next call or supplies an owned short-circuit
`IRestResponse`. That response lease is restricted to the awaited exchange and
decoder lifetime; it is not a public download result. Native HTTP messages remain
behind the adapter. `IRestContentCodec` uses ordinary stream I/O, and
`IRestResponseMapper` sees detached decoded values or bounded error bytes, so it
cannot unknowingly consume the default decoder's stream. Reusable extensions are
concurrency-safe; mutable state belongs to their per-attempt context.

The immutable `RestRetryPolicy.TransientHttp` has two retries, GET/HEAD/OPTIONS,
408/429/502/503/504, 500-ms initial exponential jitter and five-second local cap.
It selects no transport/business/body replay and no attempt timeout. Named custom
policies copy finite status/method/failure/code/transport sets. Validation permits
at most 100 retries, 500 status entries, 32 method/error-code entries, valid method
tokens up to 64 ASCII characters, bounded aliases and local delays up to one day.
These are configuration ceilings, not new defaults or work-execution grants.

Success/stopping never retry. Finite status/failure/code eligibility is established
first; `ShouldRetry` can only veto/narrow it. Transport additionally requires an
explicit matching `TransportFailures` category. A post-headers Transport, Timeout,
InvalidResponse or LimitExceeded processing failure additionally requires
`RetryBodyFailures` and actual replay assurance/content recreation. Ordinary HTTP
diagnostic-prefix capture and an explicit business decision are separate outcomes.
Nothing bypasses remaining budget/cancellation. Policy iteration is zero-based
and differs from primary dispatch count; a short circuit can have zero dispatches.

## Internal ownership boundaries

One root TimeProvider is shared by runtime and optional executor, with System
fallback. Per-call retry context carries the same clock and entry-based remaining
budget. `IRestRetryExecutor` exchanges non-generic detached outcomes; typed values
stay local to one operation. An outcome records its actual finalized method;
missing/invalid method is fail-closed for retry. Attempt timeout is created,
canceled and settled by the actual attempt owner, without another timeout layer.

The passive `RestClientComposition` marker copies bounded aliases/pairs and one
registration identity, never a factory, transport or provider. The later real
`IRestClientRuntime` owns stopping and existing Metrics attachment. Marker/runtime
identity is matched by reference. One captured observation provides uploaded and
downloaded completed bytes and one finite final outcome/entry-based elapsed time.
The four-handle producer attaches once to the actual host runtime.

Unexpected programming faults stay faults despite later cancellation. Expected
classification follows caller, total deadline, owned stopping, attempt timeout,
then actual outcome. Completion committed before later cancellation stays complete.
Existing Startup/HB hosting must close REST admission/cancel it before awaiting a
Heartbeat callback that may itself await REST, then join actual cleanup. Neither
a canceled wait nor disposal certifies drain; no parallel lifecycle framework is
introduced. Actual ownership and host tests verify these boundaries; source and
package delivery retain their separately recorded evidence.

## Standalone and existing-host composition

`CIS.RestClient.Http.HttpRestClient` takes `RestClientOptions`, an optional borrowed
`RestHttpTransport` and an optional `TimeProvider`. Construction performs no network
work. Standalone clients use None retry; a selected named policy requires the
optional host executor. Await standalone disposal to close admission, cancel and
join actual operations, then dispose owned pools. A borrowed transport remains
usable by its owner after client disposal.

`CIS.RestClient.Hosting.AddCisRestClient` registers each alias as an ordinary keyed
`IRestClient` in the application's existing container:

```csharp
services.AddCisRestClient(new RestClientOptions("orders")
{
    BaseUrl = "https://orders.example/",
    Timeout = TimeSpan.FromSeconds(30)
});
IRestClient client = root.GetRequiredKeyedService<IRestClient>("orders");
RestResult<Order> result = await client.Get<Order>("orders/42", cancellationToken: token);
```

Register dependencies before REST, followed by the existing Heartbeat/Startup tail.
REST inserts only its own bridge before that existing tail when installed later.
Disable concurrent host starting/stopping and await host stop before root disposal.
The Startup coordinator keeps its existing sequential responsibilities and has no
RestClient dependency. Hosting closes REST before waiting for Heartbeat or
Configuration callbacks and joins actual operation/response cleanup even when the
host's wait token has been canceled.

The root supplies one TimeProvider to both runtime and optional retry executor.
Known final composition contains at most sixteen aliases and thirty-two pairs,
including each request fallback. A separate plain identity matches the passive
marker to the real runtime. Metadata contains no transport factory or runtime.
An optional borrowed-transport factory receives the actual root; return a known
transport created with that root's exact `IMeterFactory`. Catalog and named-policy
validation precede caller transport factories. No child provider is constructed.

Idempotency-Key is ordinary stable request data and cannot be declared a credential
field. Replay assurance represents the real provider contract; the library does
not generate keys or infer idempotency from an arbitrary header.

## Logical completion logging

Hosted clients use the configured `ILogger` pipeline, including an existing CIS
Logging provider, for one final logical completion line:

```text
[REST] METHOD URI RESULT durationms (Attempt n/m)
```

Success uses Information; returned failures, caller cancellation and faults use
Error. Result labels have a finite status/kind form, for example `200 Ok`,
`500 Technical Error` or `200 Business Error`. Duration covers the complete logical
operation through actual cleanup. Here n is actual primary dispatches and m is
the selected policy's maximum retries plus one. The attempt suffix appears only
when retry is configured and at least one dispatch occurred.

URI contains scheme, authority and escaped path; query, user information and
fragment are omitted. The line includes no payload or exception details. Logger
filters and disabled logging are honored; an ordinary logger failure cannot
replace the operation's result, cancellation or original fault.

A standalone client can borrow the application's existing factory explicitly:

```csharp
ILoggerFactory loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
await using var client = new CIS.RestClient.Http.HttpRestClient(
    new RestClientOptions("orders") { BaseUrl = "https://orders.example/" },
    borrowed: null, clock: null, loggerFactory: loggerFactory);
```

This four-argument constructor borrows the factory; the original three-argument
constructor remains available. Neither creates a global Logging runtime. The
completion-logging addition passed the focused Windows integration checks;
its actual scope and remaining release limits are recorded in
[CIS-0010 verification](../../changes/CIS-0010/verification.md).

## Trace forwarding

`ForwardTraceId` is nullable on both `RestClientOptions` and `RestRequestOptions`.
A per-call true/false overrides the client. When the client is unset, forwarding
is automatic only for the final validated DNS hostname ending in `.local`
(case-insensitive, with an optional trailing DNS root dot). `localhost`, IP
addresses, bare `local`, and addresses such as `api.local.example` remain off.
A customization or content factory that changes the destination changes that
selection before authentication runs.

```csharp
await client.Get<Order>(url, new RestRequestOptions { ForwardTraceId = true });
await client.Get<Order>(url, new RestRequestOptions { ForwardTraceId = false });
```

The client forwards an existing entry W3C context through `traceparent`, before
native header normalization, retry identity capture and authentication/signing.
Without an existing W3C context it sends no generated trace header. It creates no
trace/span/ID and automatically forwards neither baggage nor `tracestate`.
Explicit caller-supplied `traceparent` remains an explicit manual header.
Native implicit HTTP propagation is disabled so it cannot bypass this policy;
native outgoing HTTP Metrics remain available. The result's existing correlation
ID capture is unchanged. Actual focused results are in
[CIS-0010 verification](../../changes/CIS-0010/verification.md).
