# CISLIB REST Client — Implementation Specification

**Version:** 1.0  
**Date:** 17 November 2026  
**Status:** Implementation handoff; this document defines additions, not existing REST APIs.  
**Reference baseline:** CISLIB main, commit [7b5923c031027b932f730e1cf45b2d51270b17cc](https://github.com/andrewspiteri/CISLIB/commit/7b5923c031027b932f730e1cf45b2d51270b17cc).  
**Companion:** [CISLIB_RestClient_Agent_Guide_v1.0.md](CISLIB_RestClient_Agent_Guide_v1.0.md).

## 1. Purpose and agreed requirements

Add a reusable REST client to CISLIB. C# is the first binding. Consumers use a common CISLIB contract; the C# implementation reuses the .NET HTTP stack and supported resilience components. The module must remain small enough to follow directly in ordinary source code.

The user's decisions are authoritative:

| ID | Requirement |
| --- | --- |
| REST-01 | Reuse the HTTP, serialization, asynchronous I/O and extension facilities available in .NET. |
| REST-02 | Define common consumer behavior for future language implementations; keep platform HTTP/resilience/telemetry types behind adapters. |
| REST-03 | Provide asynchronous verb methods in the form Post<ResultContract>(url, options), with a common execution path. |
| REST-04 | Supply authentication and request-customization callbacks through options. |
| REST-05 | Make the internal request/response workflow extensible. |
| REST-06 | Return RestResult<T> with Success, ErrorCode, ErrorMessage, Value of type T, and additional context. |
| REST-07 | Represent business failures as results. The client must not throw business exceptions for remote rejections. |
| REST-08 | Make retries policy-based and explicitly configured; supply useful configurable defaults for temporary failures such as 503 and delayed 429. |
| REST-09 | A client timeout must cancel the underlying request and all remaining work owned by that operation. Returning while an owned request continues as detached work is unacceptable. |
| REST-10 | Integrate automatically with the existing Metrics module at the logical-operation and actual payload boundaries. |

MUST denotes a required implementation behavior. Numeric limits, exact added type names, and the supplied retry preset below are concrete v1 design defaults, selected to close implementation ambiguity. They are not claims that the user specified each number separately.

This handoff is the input for the established CIS intake, technical-intent and implementation workflow. It does not claim a REST implementation, a completed native test run, or an execution allocation.

## 2. Scope and repository fit

### 2.1 Deliverable

Deliver the portable contract, C# implementation, optional resilience integration, automatic metrics, focused native acceptance coverage, and usable consumer/agent documentation.

The v1 consumer surface covers ordinary HTTP request/response operations: GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS and TRACE, plus additional method tokens through Send. Protocol upgrades and CONNECT tunnels require a separate streaming/channel contract; accepting a method token must not falsely imply support for those capabilities. Return a documented unsupported-operation result before dispatch where the requested capability is unavailable.

V1 includes JSON, text, byte-array and bodyless results; JSON, text, byte-array, form-urlencoded and explicitly owned/reopenable stream request bodies. Multipart content can use the content extension seam; a separate multipart object model is not required for first acceptance. Public streaming response APIs are deferred. Their mandatory future lifetime rules are recorded in section 10, so a later addition cannot accidentally abandon an active transfer.

Do not add a workflow designer, plugin-discovery engine, generated API-client platform, distributed scheduler, background request queue, token store, OAuth server, new HTTP transport, serializer, telemetry collector, or independent test harness. Circuit breaking, hedging, caching and client-side rate limiting are extension opportunities, not implicitly enabled v1 features.

### 2.2 Verified current baseline

The reference commit merges the bounded Metrics module. Read the current checkout again at implementation time; a newer accepted contract takes precedence.

| Concern | Verified location or behavior |
| --- | --- |
| Runtime base | CIS.Core; explicit CIS.* namespaces under implementations/csharp/src/CIS.Core/ |
| Target | net10.0; selected SDK 10.0.401 and .NET/ASP.NET runtime 10.0.12 |
| Core package closure | Logging.Abstractions and DependencyInjection.Abstractions 10.0.12, plus the approved Microsoft.AspNetCore.App framework reference |
| Third-party integration | Separate optional package; a Microsoft package prefix alone does not authorize expanding Core's closure |
| Metrics facade | CIS.Metrics.IMetrics, MetricDefinition, MetricDimensions and MetricUnit |
| Collector | Optional CIS.Metrics.OpenTelemetry; one application-owned provider and bounded selection |
| Existing HTTP selection | Inbound ASP.NET duration and active requests only; unselected instruments are dropped |
| Tests | Existing native C# MSTest projects; focused affected tests and ordinary dotnet workflow |
| Portable material | contracts/ and conformance/ |
| Agent governance | Root AGENTS.md, .github/instructions/cis-import.instructions.md, .cis/repository.yml; documentation root docs |

The outgoing selection gap is implementation work in this module: native System.Net.Http observations are not currently exported just because Metrics is installed. Section 11 defines the addition.

### 2.3 Proposed package and file ownership

| Layer | Owns |
| --- | --- |
| contracts/rest-client/ | Language-neutral behavior, errors, options, policy and lifecycle semantics |
| CIS.Core, namespace CIS.RestClient | Consumer contract, native HTTP binding, preparation/result orchestration, deadline cancellation, single-attempt behavior, metrics recording |
| Optional CIS.RestClient.Resilience | Supported upstream retry strategy and policy adapter; no dependency from Core to this package |
| CIS.Metrics.OpenTelemetry | Outgoing native selection/views/catalog and existing exporter ownership |
| docs/modules/rest-client/ | Implemented API, defaults, examples and operational semantics |
| planning/rest-client/sources/ | This specification and its companion as supplied sources |

The optional resilience package is a proposed new integration. Record and pin its exact compatible package closure through the existing repository decision route as part of the REST change. Do not extend Core's approved dependencies or reuse the Metrics-specific dependency exception. The base client and None policy work without this optional package. Selecting a retry policy without a registered executor must fail during registration/validation, never silently become a single attempt.

Use HttpClient, SocketsHttpHandler, DelegatingHandler and System.Text.Json directly. Use the approved hosting/framework facilities for IHttpClientFactory where available. Reuse managed handlers or appropriately configured long-lived clients; never create a privately owned connection pool per call. Do not upgrade the repository SDK/runtime to implement this module. [R2, S1, S2]

## 3. Public consumer API

The following is the intended C# binding. Method spelling follows the requested Post<T> form; all methods are asynchronous. CancellationToken and Task are the C# expressions of portable cancellation and asynchronous completion, not contracts another language must reproduce literally.

~~~csharp
public interface IRestClient
{
    Task<RestResult<T>> Get<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<RestResult<T>> Post<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<RestResult<T>> Put<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<RestResult<T>> Patch<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<RestResult<T>> Delete<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<RestResult<T>> Options<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
    Task<RestResult<T>> Trace<T>(string url, RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RestResult<RestNoValue>> Head(string url,
        RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RestResult<T>> Send<T>(string method, string url,
        RestRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}
~~~

RestNoValue is a small marker for a metadata-only result. Other verbs can use it where the caller does not require a response value; do not create three parallel families of simple/detailed/raw verb methods.

All wrappers delegate to one execution path and differ only in their method and body semantics. The generic argument denotes the response value, independent of the request body type. HTTP method tokens are validated as tokens; no method-specific business behavior is inferred from a URL.

Example, using proposed additions:

~~~csharp
RestResult<CreateOrderResult> result = await _restClient.Post<CreateOrderResult>(
    "/orders",
    new RestRequestOptions
    {
        Body = RestBody.Json(command),
        OperationName = "create-order",
        ConfigureRequest = (request, ct) =>
        {
            request.Headers.Set("X-Api-Version", "1");
            return Task.CompletedTask;
        },
        Authenticate = async (request, ct) =>
        {
            string token = await tokenProvider.GetAccessToken(ct);
            request.Headers.Set("Authorization", $"Bearer {token}");
        }
    },
    cancellationToken);

if (!result.Success)
{
    // The application decides what the returned failure means to its business flow.
    return HandleFailure(result.ErrorCode, result.ErrorMessage);
}

if (!result.Context.HasValue)
{
    return HandleSuccessfulEmptyResponse();
}

return HandleCreatedOrder(result.Value);
~~~

The examples are contract sketches. Publish runnable examples against the actual implementation; do not claim the proposed registration methods or classes already exist.

## 4. Result contract and errors

### 4.1 RestResult<T>

The immutable public shape is:

~~~csharp
public sealed record RestResult<T>(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    T Value,
    RestResultContext Context);
~~~

Production construction must enforce the invariants, using factories or guarded constructors consistent with repository style. The positional record above describes the shape, not a requirement to permit invalid combinations.

| Rule | Required behavior |
| --- | --- |
| Success | The complete promised operation succeeded, including decoding and any configured business classification |
| Successful errors | ErrorCode and ErrorMessage are null |
| Failure | ErrorCode is nonempty; ErrorMessage provides a bounded explanation |
| Value | Actual T; no HttpResponseMessage, JSON DOM or object wrapper substituted for it |
| Failed value | Context.HasValue=false and Value=default(T); diagnostic error content lives separately |
| Empty success | Success may be true with HasValue=false and Value=default(T) |
| JSON null | A present decoded value with HasValue=true when the native codec can represent null in T; it differs from no body |
| Required value | An operation that requires a value fails with client.invalid_response if the value is absent |
| HTTP error | Do not deserialize an error document into the expected success type merely because T was supplied |
| Property access | Inspecting Success, errors, context or Value must not throw a business exception |

Reference-type bindings must document that default(T) can be null when HasValue=false even when the declared property is T. Use appropriate nullable annotations without changing the observable invariants. Consumers check Success and HasValue before using Value.

RequireValue checks whether a value was supplied, not whether a supplied value is non-null. A response mapper enforces a business requirement for a non-null value. Do not attempt to infer top-level runtime reference nullability from C# T versus T? annotations; an unrepresentable null, such as null decoded as a non-nullable integer, is an invalid response.

By default, an HTTP 2xx status plus successful required processing yields success. A configured business classifier may reject a 2xx response. Non-2xx statuses remain failures in v1; do not silently treat redirects or provider-specific error statuses as success.

For HEAD and RestNoValue, skip success-body decoding. A HEAD Content-Length is not evidence that a response body was consumed. Distinguish an empty body from malformed nonempty JSON. A 202 response indicates successful acceptance of the HTTP operation, not proof that a remote asynchronous job finished.

### 4.2 RestResultContext

Use typed, read-only context rather than an arbitrary persistent object bag.

| Field | Semantics |
| --- | --- |
| HttpStatusCode | Nullable integer; status of the terminal exchange if a response was received |
| Headers | Case-insensitive, multi-value response headers, including content headers; no lossy comma concatenation |
| HasValue | Whether a successful value was produced |
| FailureKind | Finite category described below; None on success |
| RemoteErrorCode | Original provider code when explicitly extracted; optional |
| ErrorBody | Optional bounded diagnostic payload; never an unlimited response copy |
| ErrorBodyTruncated | True when diagnostic capture was truncated |
| ClientName / OperationName | Registered aliases or the documented fallback |
| AttemptCount | Number of owned dispatches; increment after preparation/authentication, immediately before the primary transport handler |
| Duration | Monotonic elapsed time from logical entry through owned cleanup; C# TimeSpan, portable duration semantics |
| RetryStopReason | Nullable finite reason: Disabled, NotEligible, NonRetryable, AttemptLimit, InsufficientTimeRemaining or BodyNotReplayable |
| CorrelationId | Existing CISLIB correlation convention; not a new identifier system |

AttemptCount can be zero for a preparation/authentication rejection. It is not a count of physical packets, TCP connections, native internal retries or redirected transmissions. A failed later attempt with no response must not inherit an earlier attempt's HttpStatusCode. If retaining a previous status is useful, expose it under a separately named optional LastReceivedHttpStatusCode field.

Keep the terminal outcome and necessary summary context only. Do not retain an attempt history or native transport objects in RestResult<T>.

### 4.3 Error mapping

ErrorCode is a stable portable string, independent of native exception type names. Reserve client.* and http.* for the module. A registered mapper may provide documented business-domain codes; preserve the provider's original value separately. ErrorMessage is explanatory, not a branching key.

| Outcome | Default result/error behavior |
| --- | --- |
| Non-2xx HTTP response | FailureKind.Http; http.<status>, e.g. http.503 |
| Mapped business rejection | FailureKind.Business; documented mapper code or business.rejected |
| Expected transport failure | FailureKind.Transport; client.transport_error |
| Overall timeout | FailureKind.Timeout; client.timeout |
| Exhausted per-attempt timeout | FailureKind.Timeout; client.attempt_timeout |
| Invalid required response | FailureKind.InvalidResponse; client.invalid_response |
| Response body above limit | FailureKind.LimitExceeded; client.response_too_large |
| Declared authentication failure | FailureKind.Authentication; client.authentication_failed |
| Declared preparation failure | FailureKind.Preparation; client.preparation_failed |
| Unsupported capability | FailureKind.Preparation; client.unsupported_operation |
| Shutdown cancellation owned by the client | FailureKind.Stopping; client.stopping |
| Caller cancellation | Cancelled asynchronous operation; C# OperationCanceledException associated with the caller token |
| Invalid caller arguments/configuration | Normal validation/configuration error; validate early |
| Unexpected programming defect in client/extension | Surface the defect; do not disguise it as an ordinary service rejection |

Expected transport/timeouts/decoding failures are converted into results by the outer owner. Retry exhaustion preserves the terminal cause rather than replacing it with a generic RetryFailed code.

Do not use a blanket catch(Exception) that converts programming defects into Success=false. Custom callbacks can report expected failures through a small typed completion/failure facility on their CIS context; unexpected thrown exceptions remain defects unless an explicit integration contract classifies that exception. Preserve cancellation separately.

Limit ErrorMessage to 1,024 Unicode scalar values by default, with an explicit truncation indication in context if a mapped message is shortened. Do not include credentials, raw authorization values, exception stack traces or uncontrolled response content in default messages.

## 5. Options and configuration

### 5.1 Two scopes

Client registration supplies immutable defaults: alias, optional base URL, default headers, timeout, body/response limits, codec/mapper, callbacks, retry selection, transport configuration and declared metric operations.

RestRequestOptions supplies per-call overrides:

| Option | Meaning |
| --- | --- |
| Body | RestBody descriptor; absent means no request body |
| Headers | Multi-value per-call headers |
| Query | Query parameters with explicit set/append semantics |
| Timeout | Whole logical-operation timeout; null inherits |
| RetryPolicy | Registered selection; null inherits, None explicitly disables |
| ReplaySafety | Explicit operation replay assurance where required |
| OperationName | Registered metric/diagnostic operation alias |
| ConfigureRequest | Async customization callback |
| Authenticate | Async authentication callback |
| ResponseFormat | Json, Text, Bytes or NoValue; Json is the ordinary typed default |
| RequireValue | False by default; true requires a decoded value |
| ResponseMapper | Registered integration-specific error/business mapping |
| Limits | Optional explicit overrides within validated client policy |

Scalar options and callbacks explicitly supplied on the call replace defaults. Do not run both default and replacement authentication implicitly. An explicit authentication-disable selection is distinct from an absent callback that inherits defaults. Apply the same distinction to customization when disabling a default is needed.

Capture effective options and header/query collections into isolated operation state at entry. Do not mutate reusable options or HttpClient.DefaultRequestHeaders during a call. Body objects are not deep-cloned; callers must not mutate a supplied object while its operation is running.

### 5.2 Defaults

| Setting | V1 default |
| --- | --- |
| Overall timeout | 30 seconds, positive and finite |
| Per-attempt timeout | Disabled unless explicitly included in policy |
| Retry policy | None unless the client/call explicitly selects one |
| Automatic redirects | Disabled; return the redirect response |
| Automatic cookie storage | Disabled for the general REST profile; explicit isolated client profile required to enable |
| Success response size | 16 MiB of actual decoded/application payload bytes |
| Error diagnostic body | At most 16 KiB |
| Response format | JSON for ordinary T; explicit text/bytes/no-value modes |
| Required response value | False; absence is represented through HasValue |
| Metrics operation fallback | request |
| Registration lifetime | Immutable host/client snapshot; changed transport/policy definitions take effect through the established reconfiguration/restart route |

Limits are configurable. They are processing bounds, not guarantees on total object-graph or process memory. Apply the success limit to actual body consumption, including decompressed bytes; Content-Length alone is insufficient. Reaching the error diagnostic cap truncates capture and disposes the remaining response; it does not turn a meaningful HTTP error into a successful result.

Use native response-header limits where available. Avoid creating a second general HTTP parser.

### 5.3 URL, headers and content rules

Resolve a relative URL against the configured base using normal URI resolution. Require absolute http/https when no base exists. Document trailing-slash/base-path behavior with one example. Validate the final destination after customization and before credentials are applied.

Header names are case-insensitive. Set replaces values; Append adds a value. Keep request headers and content-header placement correct in the adapter. Query values use standard URI encoding exactly once and preserve explicitly repeated values. Do not silently rewrite an already encoded query or sign one form and send another.

TRACE has an explicit method guard: it carries no request body or sensitive credential fields. Disable authentication callbacks and omit inherited authorization/cookie/declared credential fields for TRACE; reject an explicit body or explicitly supplied credentials before dispatch with client.preparation_failed. Revalidate after customization, including configured credential-bearing query parameters. Do not reflect credentials simply because other verbs share an authenticated client. [S11]

Default authentication is scoped to declared allowed origins. An absolute URL or customization that moves to a different origin must not receive inherited credentials unless its origin is explicitly allowed. Keep redirect and cookie behavior in client configuration, not mutable per-request handler fields. Preserve normal TLS validation.

JSON uses System.Text.Json with a documented client serialization profile: web defaults for property naming/case-insensitive reading, UTF-8, and no silent coercion beyond that profile. The portable contract defines wire names, nullability and application DTO meaning; platform attributes are implementation details. Text and bytes are explicit modes, so T=string does not ambiguously select raw text instead of a JSON string. [S3]

The default codec uses T=string for Text, T=byte[] for Bytes and T=RestNoValue for NoValue. Reject incompatible format/type combinations at entry unless an explicitly configured codec supports that combination. Do not rely on an unchecked object cast to satisfy the typed result contract.

## 6. Callback and extension contracts

### 6.1 Per-attempt callbacks

ConfigureRequest and Authenticate are awaited and receive the effective cancellation token. They run on every dispatched attempt's preparation path, including the first. Callbacks may therefore execute again after an explicitly permitted retry; they must not independently perform the business operation.

ConfigureRequest receives a mutable CIS request context containing method, resolved URL, headers, query and body descriptor, plus read-only operation/attempt information.

Authenticate receives the finalized ordinary request and controlled credential mutation facilities. It may add authentication headers or protocol-defined credential parameters, but cannot silently replace the destination or request body. All ordinary mutations precede authentication. No later extension may modify signed fields.

Expected preparation/authentication failures are typed context failures and produce RestResult<T>; they do not require throwing business exceptions. Token acquisition remains application-owned. The client does not create a global credential cache or refresh-token store.

A body-signing authenticator must use the exact representation to be sent. If it needs materialized bytes/digest, request that capability explicitly under a body-size bound and reuse the resulting bytes; do not serialize independently and assume two serializations are identical. Streaming content that cannot supply the required signing representation is rejected before dispatch, not silently buffered without a bound.

### 6.2 Reusable handlers

Provide a portable ordered HTTP exchange handler seam, conceptually Handle(request, next, cancellation) -> response. Its CIS-owned types must not expose HttpRequestMessage, HttpResponseMessage, DelegatingHandler, Polly or OpenTelemetry.

A/B/C registration order means A/B/C outward and C/B/A inward. In C#, adapt transport-facing handlers to native DelegatingHandler composition. Ordinary handlers call next at most once; retry orchestration has exactly one separate owner. A short-circuit response is permitted only when it follows the same result/disposal rules; it produces no invented native transport observation.

Handler instances may be shared and must be concurrency-safe. Store caller credentials and mutable state on the operation/attempt context. Do not assume a pooled factory handler's DI scope is the current incoming application request scope. [S2, S4]

The HTTP exchange seam does not alone cover serialization or business classification. Keep those explicit seams below instead of claiming a SendAsync handler surrounds all logical work.

### 6.3 Codec and response mapping

A replaceable content codec owns encoding/decoding. Default JSON delegates to System.Text.Json; other formats use native content/stream APIs.

A response mapper owns integration-specific error extraction and business classification. It can inspect status, bounded headers, decoded value or bounded error content through an owned reader. Define a single consumption path; an extension may not consume a response stream and leave the default decoder unknowingly reading EOF. Buffer only when explicitly required, within the configured limit.

For each attempt, known response classification produces an internal outcome. A mapper can turn HTTP 200 plus a documented business rejection into a failed outcome, and can extract errors from non-2xx bodies. It does not change actual status or attempt count. V1 preset retries never retry a business rejection or decoding error; an explicitly registered custom policy may use a documented transient business classification only when replay safety is also established.

No class per workflow stage is required. Prefer a small orchestration method, native handlers, and focused replaceable responsibilities.

## 7. Execution, ownership and attempt boundaries

The outer RestClient operation owns:

1. Capture of the monotonic entry timestamp and initial total-budget origin.
2. Minimal argument validation, resolution of the configured timeout, operation admission and instrumentation.
3. A linked cancellation lifetime timed from the remaining entry-based budget, then the isolated effective-option snapshot using that cancellation scope; preparation never restarts the budget.
4. Invocation of the selected execution policy.
5. Settlement of all owned work and actual cleanup on every exit path.
6. Final cancellation/timeout/outcome classification, metric completion, and construction of the immutable RestResult<T> with the finished duration, or propagation of caller cancellation/a programming defect.

Invalid arguments can be rejected before operation admission. For every admitted operation, duration and timeout start at the captured public-entry timestamp. Finish asynchronous cleanup before taking the final duration; metric recording and immutable result construction add no further asynchronous operation work.

Each policy attempt owns:

1. Fresh request/context and a fresh/reopened body source.
2. Ordered request handlers, customization, finalized ordinary content and authentication.
3. Cancellation check, then increment of AttemptCount immediately before the primary transport handler, after all preparation and authentication.
4. Native send, response-body handling and appropriate classification.
5. Disposal of owned request/response/content resources before another attempt can start.
6. A bounded internal outcome for the policy predicate.

The effective cancellation token flows through every awaited operation. An extension that starts work must await and settle that work as part of its callback; fire-and-forget work is not part of this service contract.

If authentication runs inside a DelegatingHandler, the outer HttpClient.SendAsync invocation is too early for the dispatch counter. Place its owned dispatch boundary after those callbacks and immediately before the primary transport. Pre-dispatch rejection or an exchange-handler short circuit does not increment AttemptCount.

Create a new HttpRequestMessage for every explicit attempt. Reuse connections/handlers, not an already sent message. Preserve the same logical idempotency key across attempts, while allowing per-attempt credentials/signatures to refresh. [S5]

For response-body read failures, the supplied preset does not replay after a successful response has entered body processing; return a failed result. Custom policies can explicitly cover a replay-safe full operation, with the same ownership and total deadline. A malformed success body is never retried by the preset.

A resource disposed after an exception must not overwrite the primary result/cancellation with an incidental cleanup failure without preserving the original cause. Expected cleanup failures can be diagnosed through existing bounded logging; programming defects still surface.

## 8. Timeout means cancellation

### 8.1 Mandatory semantics

The overall deadline starts at public operation entry and includes option preparation, callbacks, request creation/serialization, transport, response reads, decoding/classification, retries and delays.

When it expires, MUST:

- Signal the actual native operation's cancellation token.
- Cancel owned upload/body reads, callbacks and pending retry delays.
- Prevent any further dispatch.
- Await the owned operation's cancellation and cleanup.
- Release owned request/response resources.
- Return client.timeout after settlement, unless caller cancellation takes precedence.

A Task.WhenAny/WaitAsync timeout that only stops waiting while the underlying operation continues is non-conforming. Cancelling a token without observing/awaiting the underlying completion is also insufficient.

Cancellation is per logical operation. Do not call shared HttpClient.CancelPendingRequests or dispose a shared client to time out one request.

### 8.2 C# mechanism

Use separate identifiable sources for caller cancellation, total timeout and any optional attempt timeout, linked into the token supplied to the actual operation. Use CancellationTokenSource timers/native cancellation or a supported upstream cooperative timeout strategy; no bespoke timer service.

The same effective token reaches HttpClient.SendAsync, native content writes, body reads, decoder and callbacks. Keep the source alive until those owned operations finish. ResponseHeadersRead does not make HttpClient.Timeout cover subsequent body consumption. [S5, S6, S7]

Configure a CIS-owned native client once so a hidden HttpClient timeout cannot supersede the declared total budget; an infinite native timeout with the explicit operation token is suitable. Do not change a borrowed/shared client's properties per call. Validate incompatible externally supplied client configuration at registration and explain the conflict.

A deadline signals cancellation promptly; it is not a hard wall-clock bound on method return. Cleanup may take longer. User callbacks must cooperate. If a callback ignores cancellation, await it rather than report completion while it remains owned running work. This limitation must be documented without pretending arbitrary code can be forcibly terminated. [S8]

### 8.3 Classification and races

At terminal classification:

1. If caller cancellation is observed, preserve caller cancellation.
2. Otherwise, if the total timeout expired, return client.timeout.
3. Otherwise, classify an attempt timeout using its identified source.
4. Otherwise, use the actual terminal result/failure.

Do not identify every OperationCanceledException as timeout. Recheck cancellation before dispatch and before committing a completed result; a completion committed before a later cancellation remains complete. Define these race semantics consistently in tests.

A configured per-attempt timeout cancels and settles that attempt before retry consideration. It never resets the total deadline. The total deadline cannot be retried.

Client cancellation cannot undo a remotely committed operation or prove the server stopped execution. Preserve replay/idempotency requirements for uncertain writes.

## 9. Explicit retry policies

### 9.1 Selection and engine

No selected policy means one attempt. Client defaults are explicit configuration. A call may inherit, replace the complete policy, or select None. Do not layer the client's policy and the call's policy into nested retries.

The optional C# integration reuses maintained Microsoft.Extensions.Http.Resilience/Polly strategy facilities. The adapter may use the underlying strategy around the fresh-attempt callback where required to satisfy cancellation/body/classification boundaries. It must not implement its own general retry scheduler/backoff engine. Native HTTP handlers with independent retry/hedging must not be stacked underneath the owned retry executor.

Build bounded registered policy instances once and reuse them. Do not construct a persistent pipeline per URL, credential, operation instance or response type. Any per-execution state is local and released.

The standard .NET resilience handler's full default stack is not this contract: it includes additional strategies and retries all methods by default. Configure only the selected strategies and eligibility. [S9, S10]

### 9.2 Supplied TransientHttp preset

| Setting | Selected default |
| --- | --- |
| MaxRetries | 2; at most 3 owned dispatches when every iteration reaches send |
| Local backoff | Upstream exponential backoff with jitter |
| Initial nominal delay | 500 ms |
| Maximum local delay | 5 seconds |
| Retry-After | Honored for an eligible response |
| Eligible methods | Replayable GET, HEAD, OPTIONS |
| Retryable statuses | 408, 429, 502, 503, 504 |
| 500 | Excluded unless a configured policy adds it |
| Transport failures | None by default; an explicit policy may select narrowly classified transient transport failures |
| Business/decoding/authentication failures | Not retried by the preset |
| Attempt timeout | Not configured by the preset |
| Total limit | Inherited operation deadline; never reset |

Different language bindings need not generate identical random jitter sequences. They must preserve configured bounds, maximum attempts, server minimum delays, cancellation and eligibility. Conformance tests assert these invariants rather than a vendor library's exact random sequence.

Custom policies can specify finite status/error eligibility, limits, backoff and optional attempt timeout, plus a cancellation-aware decision callback over bounded outcome metadata. User callbacks must not schedule or dispatch the retry themselves.

### 9.3 Eligibility and replay

A retry requires all of:

1. Selected policy permits the outcome.
2. The remote operation is eligible for replay.
3. Request content can be recreated.
4. Attempt count remains within the configured limit.
5. The chosen wait fits within the remaining overall deadline.
6. No caller, host or total-deadline cancellation is active.

PUT and DELETE have idempotent HTTP semantics, but are deliberately outside the conservative preset; include them through a client policy after confirming the endpoint and body contract. POST/PATCH require explicit replay assurance such as a provider-supported idempotency contract. Adding a random header alone does not establish idempotency. The same supported key and equivalent business payload must be used on every attempt. [S11]

A stream body is one-shot unless a declared factory supplies a fresh equivalent stream per attempt or an explicit bounded replay representation exists. If replay is unavailable, perform no additional send, preserve the original failure and report BodyNotReplayable. Do not buffer arbitrary uploads solely to enable retry.

### 9.4 Delay and Retry-After

Parse Retry-After using native HTTP parsing facilities, including delay-seconds and HTTP-date. For a valid server delay, use at least max(server minimum, chosen local delay). Clamp a past HTTP-date to zero before that comparison. For missing/invalid values, use policy backoff and bounded diagnostics. [S10, S11, S12]

The 5-second local cap must not shorten a longer server delay. If that delay is not representable, or does not fit the remaining total deadline, preserve the last response and stop with InsufficientTimeRemaining; do not retry early and do not falsely report an already-expired timeout.

Choose the delay once, use the same value for budget admission and waiting, and use a cancellable asynchronous upstream wait. Release the previous response before waiting. If the deadline expires during the wait, cancel it and return timeout; dispatch nothing further.

Retry exhaustion preserves the final cause: e.g. http.503 with AttemptCount=3 and AttemptLimit. If the final attempt instead failed without a response, report that actual failure and a null terminal HttpStatusCode.

## 10. Content, resource ownership and lifecycle

For typed results, the client owns its created native messages, response content and internal streams, consumes the promised body under limits, and disposes owned resources before return. It never returns a hidden live HttpResponseMessage in Context.

Request body descriptors declare ownership:

- JSON/text/bytes/form content wrappers are client-owned.
- A borrowed input stream remains caller-owned unless explicit ownership transfer was selected.
- A body factory creates a fresh client-owned stream per attempt.
- Caller-supplied byte buffers/body objects remain unchanged during use.

No automatic body buffering solely for metrics or retries. Native serialization and native stream I/O remain the implementation.

If a future explicit streaming response API is added, it must return a disposable transfer owner; transfer cancellation/deadline/resource ownership moves with that object until EOF/disposal. Logical call completion and transfer completion are separate. Early disposal is not a completed download; no retry occurs after bytes are exposed to the caller. That addition requires its own focused acceptance, not a speculative v1 implementation.

Use existing host/application lifetime ownership. Constructors and registration do not perform network calls. During owned shutdown, close new admission, cancel active operations and await actual settlement before disposing owned transport or releasing metric owners. Do not dispose borrowed HttpClient/handler instances. Keep this within the established lifecycle; do not build another startup/shutdown framework.

## 11. Automatic Metrics integration

### 11.1 Existing API and ownership

Use the implemented CIS.Metrics.IMetrics facade. Relevant actual signatures are CreateTimer(MetricDefinition), CreateCounter(MetricDefinition), CreateUpDownCounter(MetricDefinition), ITimer.Record(double, MetricDimensions), ITimer.Start(MetricDimensions), ITimingScope.Stop(MetricDimensions), Counter.Add and UpDownCounter.Add. Units are MetricUnit.Seconds and MetricUnit.Bytes. [R4]

Create reusable handles once when the module owner attaches to the host. Preserve existing disabled/no-op behavior. No REST-specific MeterProvider, exporter, scrape endpoint, event queue or custom aggregation.

When using timing scopes, initial and completion timer keys are disjoint. Initial client/operation plus completion outcome is valid. Alternatively, record the final entry-based elapsed duration once through ITimer.Record. Do not record both ways, set outcome twice, or infer success during Dispose. Active work is decremented in finally on success, returned failure, thrown defect and caller cancellation.

### 11.2 Required logical instruments

| Logical name | Kind/unit | Dimensions | Boundary |
| --- | --- | --- | --- |
| cis.restclient.operation.duration | Timer / seconds | client, operation, outcome | Public operation entry through result/classification and owned cleanup |
| cis.restclient.active.operations | Up/down counter / count | client | +1 at entry, -1 once on actual completion |
| cis.restclient.payload.uploaded | Counter / bytes | client, operation | Actual application payload bytes accepted at owned request-content write boundary |
| cis.restclient.payload.downloaded | Counter / bytes | client, operation | Actual bytes returned from owned response-body reads |

Use duration histogram count for logical completed-call count. Do not add a duplicate completed-call counter.

Finite outcomes: success, http_error, business_error, transport_error, timeout, cancelled, authentication_error, preparation_error, invalid_response, limit_exceeded, stopping, unexpected_error. ErrorCode, RemoteErrorCode and exception names are not dimensions.

Count actual retransferred payload on each attempt. Count partial reads/writes only as observed. Counts exclude headers, TLS framing and network-interface overhead; response counts are at the application/decompressed boundary. They are not wire-byte measurements. A Content-Length without consumed bytes adds nothing.

The deferred streaming API would add cis.restclient.transfer.duration with explicit EOF/disposal/cancellation/failure outcomes. Do not register a placeholder transfer instrument before such a producer exists.

### 11.3 Native outgoing selection — required integration change

At the reference baseline, the optional collector selects only inbound HTTP native instruments and drops foreign/unselected streams. Extend the single owned Metrics selector/catalog to admit exact host-owned System.Net.Http meters for:

| Native instrument | Default retained dimensions | Capacity |
| --- | --- | --- |
| http.client.request.duration | http.request.method | 64 |
| http.client.active_requests | http.request.method | 128 |

Preserve native names, kinds and units. The native duration ends at response headers; it does not replace logical duration. Its count is neither a guaranteed logical-call count nor an exact explicit retry count. [R3, S13]

The small default key set is intentional: logical metrics supply registered dependency/operation/outcome detail without exporting arbitrary hosts or exception types. Native queue/connection metrics may be added through an explicit bounded catalog profile when useful; do not admit every native instrument automatically.

Configure SocketsHttpHandler.MeterFactory from the host's existing factory before first use, or verify the corresponding factory-managed behavior. Capture the exact host-owned meter references through the established Metrics ownership mechanism; do not select process-global meters by name alone. Externally supplied unscoped handlers cannot be claimed to satisfy isolated native collection. [S14]

Preserve application/environment on every exported sample, including overflow and generated histogram children. Do not add a standalone AddMeter call from application code or bypass the existing provider/view ownership guard.

### 11.4 Bounds

Default REST profile: at most 16 configured client aliases and 32 legal client/operation pairs, including each client's request fallback. Use 512 ordinary combinations for the logical duration stream, enough for 32 pairs x 12 finite outcomes = 384; use 128 for each other logical stream. Register these stream overrides and reservations before producer activation.

This adds four Core logical streams and two native streams. The current automatic catalog reserves 19 Core streams; update it intentionally rather than relying on unbounded lazy admission. Keep the existing 96-definition, 128-stream, 16,384 aggregate-capacity and 4-MiB scrape limits. The new profile must fit the finished catalog; adjust documented per-stream reservations, not global limits by default. [R3, R5]

Unknown per-call operation names must not allocate series. Use the registered request fallback with bounded diagnostics. Raw URLs, paths, query strings, request IDs, tenant/user IDs, payloads, error text and arbitrary names are excluded. Metric recording/overflow failure must not change business outcomes.

Verify a real exported scrape and two-host isolation for the changed outbound selection. Reuse the existing provider and test fixtures. No new broad memory/benchmark campaign is implied by this module.

## 12. Logging and diagnostics

Use existing CISLIB logging and correlation. Record a bounded completion summary when the configured logging policy calls for it: registered client/operation, method, result category, actual status, attempts and duration.

Do not log authorization/cookies, credential-bearing query values, full request/response bodies or raw sensitive URLs by default. Detailed diagnostic logging is separately explicit, bounded and redacted.

Avoid a per-byte/per-header/per-dropped-metric log. Retry events may use supported resilience diagnostics, with a single logical completion summary; do not emit contradictory success/failure summaries from every layer.

## 13. Acceptance criteria

These are behavior groups, not a mandate for one test class or a full combinatorial matrix per row. Map each to a small number of meaningful native cases and reuse current fixtures.

| ID | Acceptance |
| --- | --- |
| A01 | Verb wrappers and custom Send use the same execution path; method, URI, headers and request body reach the transport correctly; unsupported channel capabilities fail explicitly. |
| A02 | RestResult invariants hold for a normal value, legitimate default value, empty success, representable JSON null, unrepresentable null, required-but-absent value and failed result. |
| A03 | Non-2xx and mapped HTTP-200 business rejections return failures without business exceptions; expected transport/invalid-response failures return documented codes; an unexpected callback defect remains visible. |
| A04 | Async customization precedes finalized authentication; signed URI/body values are unchanged at transport; callbacks receive the correct token and independent per-call state. |
| A05 | Concurrent calls sharing client/options do not leak credentials or headers; client/per-call precedence, explicit None and disabled inherited auth behave as documented. |
| A06 | Native body encoding and decoding cover JSON/text/bytes/no-value and the declared request body modes; invalid or oversized bodies produce bounded deterministic behavior. |
| A07 | A total timeout cancels the actual blocked send/upload or response read; the owned task/stream observes cancellation and cleanup completes before the failed result is returned. |
| A08 | A timeout during auth/customization or retry delay propagates cancellation, dispatches no further request and releases owned resources. |
| A09 | A deliberately delayed cooperative-cleanup callback prevents premature completion; releasing its cleanup barrier lets the timeout result finish. The test always releases its barrier in finally. |
| A10 | Caller cancellation, total timeout, optional attempt timeout and their documented race precedence are distinct; one request's cancellation does not cancel another shared-client request. |
| A11 | No selected policy dispatches once; inherited/replaced/None policies follow precedence; 503/503/200 yields success and three dispatches; exhausted 503 preserves http.503. |
| A12 | 429 and 503 honor seconds/date Retry-After, invalid values fall back, and a server delay beyond the remaining budget causes no early retry. |
| A13 | The preset does not replay unapproved POST/PATCH, business/decoding failures or post-headers body failures; an explicitly replay-safe operation retains its stable idempotency key and equivalent fresh content. |
| A14 | Every explicit retry builds fresh native request/content state, reruns applicable callbacks and disposes the previous response; one-shot content prevents another send. |
| A15 | Overall deadline is not reset across attempts; an optional attempt timeout settles the first attempt before the next begins; retry adapters cannot leave detached timed-out work. |
| A16 | Redirect/origin/cookie defaults prevent cross-call credential leakage; TRACE rejects bodies/explicit credentials and omits inherited credentials before dispatch; normal TLS validation is retained; result/log diagnostics are bounded and redacted. |
| A17 | One logical duration/count and balanced active work cover success/failure/cancellation/throw; retries and waits remain inside the logical boundary; actual partial/retried payload bytes are counted. |
| A18 | A real exporter scrape contains new logical and selected native outgoing metrics with mandatory identity; nonselected meters/keys are dropped and two hosts remain isolated. |
| A19 | Metrics-disabled or overflow behavior preserves request results; configured alias/pair/stream bounds prevent per-URL state allocation. |
| A20 | Core-only consumer works without optional resilience/collector packages; a packaged optional consumer exercises the configured policy and metrics through the repository's supported runtime. |
| A21 | Shutdown cancels and awaits owned work, keeps required owners valid until completion, and preserves borrowed-resource ownership. |
| A22 | Runnable examples and consumer/agent guidance match the published API; public contracts contain no native HTTP, Polly or OpenTelemetry types. |

For A07-A10, a quick timeout result alone is not evidence. Observe the underlying operation's token, completion and disposal. Use controllable handlers/streams and one focused real local HTTP integration where it resolves transport/body cancellation risk. Server-side rollback or an exact remote disconnect instant is not an acceptance claim.

Use synchronization barriers and bounded test safeguards instead of fragile millisecond ordering. Upstream time-provider support or existing clock fixtures may control retry delays; do not build a clock/testing framework. Tests cannot leave background requests or barriers blocked after failure.

## 14. Implementation tasks and parallel work

Use these as bounded outcomes when deriving the real CIS task plan. Final work identifiers and allocations come from CIS; the labels below are not preallocated execution owners. Keep every task at medium size or smaller, splitting only when the inspected implementation demonstrates a distinct larger outcome.

Each implementation task includes its own necessary tests, documentation changes, independent review and completion record. Do not make three separate approval tasks for those parts.

| Task | Outcome and owned files | Dependencies | Acceptance |
| --- | --- | --- | --- |
| RC-1 | Portable contract, result/options/body descriptors, error vocabulary and normalization; owns contracts/rest-client and shared public contract files | Supplied-source intake and current baseline check | A01-A03, A05, A22 |
| RC-2 | Native transport, content codecs/read limits and resource ownership; owns transport/content implementation files and related tests | RC-1 | A01, A06, A14, A16 |
| RC-3 | Callback/handler composition, final authentication and response mapping; owns extension/pipeline/mapper files and related tests | RC-1 | A03-A05, A13, A16 |
| RC-4 | Optional upstream resilience adapter, policy registration/selection, delay and replay rules; owns optional package and retry tests | RC-1; dependency decision recorded | A11-A15, A20 |
| RC-5 | Logical orchestrator, real deadline/caller cancellation, ownership/drain and result finalization; owns RestClient implementation and lifecycle tests | RC-2, RC-3; integrates RC-4 when enabled | A07-A10, A15, A21 |
| RC-6 | REST automatic metrics and bounded native outgoing collector integration; owns REST metric bindings and Metrics selector/catalog changes | RC-1; agreed RC-5 boundary | A17-A19 |
| RC-7 | Actual host/DI consumer example and package integration, final contract alignment and complete affected acceptance review | RC-2 through RC-6 | A20-A22 and unresolved cross-task cases |

After RC-1, RC-2/RC-3/RC-4 and the independent collector part of RC-6 can proceed in parallel. RC-5 integrates the transport and callback contracts. RC-6 final owner attachment coordinates with RC-5 rather than editing its orchestrator concurrently. RC-7 is a real consumer integration outcome, not a separate approval campaign.

One coordinator owns solution/project files, package/source mappings, dependency policy edits and shared registration entry points. Serialize edits and builds that share output directories. Tests owned by a task may be prepared alongside its implementation, using the fixed contract.

Do not build a new test harness, run mutation/performance campaigns by default, regenerate broad evidence bundles, or repeat already current unaffected results. Run the required current repository gates, preserving the difference between focused acceptance and release qualification.

## 15. CIS intake and execution instructions

1. Read root AGENTS.md, README, current dependency policy, the Metrics skill/contracts, .github/instructions/cis-import.instructions.md and actual APIs. Reconcile material changes from the pinned baseline.
2. Preserve this source under planning/rest-client/sources and the companion beside it. Import the user requirement source through the current CIS supplied-document route; use its requirement/technical sections without inventing a second narrative.
3. Inspect installed CIS help. The reviewed distribution documents definition documents, definition prepare/status, graph build and plan derive. Do not copy a stale command line without checking the installed command's required inputs.
4. Derive the technical intent/backlog from REST-01 through REST-10 and A01 through A22. Reuse resolved decisions; no fresh questionnaire for already answered items. Record this as no-UI library work.
5. Use new legitimate work owners and finite allocations through the existing workflow. Completed Metrics, recovery and prior module records remain closed; never borrow their authority, time or results.
6. Review the concrete dependency closure and task/file ownership once. User authorization in the current session persists; do not invent extra approval gates for routine implementation, documentation, targeted validation or safe metadata refresh.
7. Use real native execution, actual accounting and current-result checks. Do not claim tests from source inspection. Safe technical-intent refresh alone does not require repeated product approval.
8. If a genuine gate blocks execution, preserve completed preparation and describe the exact blocked action and governing requirement. Do not loop on administrative artifacts or fabricate successful evidence.

The specification-preparation task does not itself run CIS or alter repository state. The implementation agent must perform intake on its actual checkout and report real resulting identifiers/status.

## 16. Documentation and handoff requirements

Publish:

- The portable REST contract and error/result/policy vocabulary.
- C# registration and examples for JSON POST, GET with explicit retry policy, async authentication, response business mapping, empty success and cancellation.
- Defaults, response limits, retry eligibility, Retry-After handling and request-body ownership.
- The distinction between timeout cancellation, caller cancellation, remote side effects and optional streamed-response lifetime.
- REST metrics catalog and the changed outgoing selection/profile.
- Concise agent instructions pointing to the actual implementation and existing Metrics guidance.

The companion guide is repository documentation, not an installed personal skill. Add a concise root AGENTS.md routing entry to the implemented REST guide and contract during implementation. A new installable skill is not needed to satisfy this handoff; do not introduce a parallel instruction system.

Completion must report the implemented public API, relevant dependency decisions, focused native results, remaining actual gaps and whether release gates were in scope. Keep examples synchronized with code. Do not characterize specified-but-unimplemented capabilities as available.

## 17. Source references

Repository evidence was read at the pinned baseline on 17 November 2026.

- **R1 — Repository guidance:** [AGENTS.md](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/AGENTS.md)
- **R2 — C# dependency boundary:** [csharp-dependency-policy.md](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/architecture/csharp-dependency-policy.md)
- **R3 — Existing Metrics selection and limits:** [collection.md](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/docs/modules/metrics/collection.md)
- **R4 — Implemented Metrics public API:** [MetricContracts.cs](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/implementations/csharp/src/CIS.Core/Metrics/MetricContracts.cs)
- **R5 — Portable Metrics contract/catalog:** [contracts/metrics/README.md](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/contracts/metrics/README.md)
- **R6 — CIS import route:** [cis-import.instructions.md](https://github.com/andrewspiteri/CISLIB/blob/7b5923c031027b932f730e1cf45b2d51270b17cc/.github/instructions/cis-import.instructions.md)
- **R7 — Supplied-document command reference:** [CIS business-requirements skill](https://github.com/andrewspiteri/CIS/blob/672fd39c569f16c6edc18c25985f78ab72eec8b6/.github/skills/cis-govern-business-requirements/SKILL.md)
- **S1 — Native client lifetime:** [HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- **S2 — Factory and handler lifetime:** [IHttpClientFactory](https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory)
- **S3 — Native JSON:** [System.Text.Json overview](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview)
- **S4 — Outgoing handler composition:** [HTTP request middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-10.0)
- **S5 — Native send/cancellation/request reuse:** [HttpClient.SendAsync](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-10.0)
- **S6 — Headers and body timeout boundary:** [HttpCompletionOption](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)
- **S7 — Cancellation deadline:** [CancellationTokenSource.CancelAfter](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelafter?view=net-10.0)
- **S8 — Cooperative cancellation:** [.NET cancellation](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)
- **S9 — .NET HTTP resilience:** [HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- **S10 — Native Retry-After support:** [HttpRetryStrategyOptions.ShouldRetryAfterHeader](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.http.resilience.httpretrystrategyoptions.shouldretryafterheader?view=net-10.0-pp)
- **S11 — HTTP method/idempotency/Retry-After semantics:** [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html)
- **S12 — Rate limiting response:** [RFC 6585, section 4](https://www.rfc-editor.org/rfc/rfc6585.html#section-4)
- **S13 — Native HTTP measurement boundaries:** [System.Net metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/built-in-metrics-system-net)
- **S14 — Host-scoped native HTTP meters:** [SocketsHttpHandler.MeterFactory](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.socketshttphandler.meterfactory?view=net-10.0)
- **S15 — Upstream retry and timeout strategy:** [Polly retry](https://www.pollydocs.org/strategies/retry.html) and [Polly timeout](https://www.pollydocs.org/strategies/timeout.html)

Reuse these sources for mechanisms. CISLIB's observable semantics remain those defined in this specification, including where its conservative preset differs from upstream defaults.

