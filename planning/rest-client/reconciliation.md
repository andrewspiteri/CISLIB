---
title: RestClient proposed contract reconciliation
type: planning-reference
status: Draft
---

# RestClient contract reconciliation

The supplied REST-01–REST-10 and A01–A22 requirements fit the merged library's
architecture. The table below proposes a concrete v1 profile; these dispositions
are not implementation, dependency approval or new authority. Material choices
P04, P07 and P08 need to be included explicitly in any implementation decision.

| ID | Proposed disposition | Existing contract and consequence |
| --- | --- | --- |
| P01 | Put ordinary CIS-owned REST contracts and native single-attempt binding in `CIS.Core`; keep `net10.0`, SDK 10.0.401 and runtime 10.0.12. | The existing ASP.NET framework supplies `IHttpClientFactory`; Core acquires no new NuGet. New platform configuration stays outside ordinary consumer signatures. |
| P02 | Use the specified five result properties, guarded immutable construction and typed detached context. Add the prose-required `ErrorMessageTruncated` separately from `ErrorBodyTruncated`. | No RestClient API currently exists. Failure has default(T)/HasValue=false; JSON null is present when representable; RequireValue checks presence, not non-nullness. |
| P03 | Zero consumed response bytes means no value in JSON/Text/Bytes modes. JSON `""`, `[]` and representable `null` remain present values. Keep a detached bounded error-byte prefix and media type, with one reader/decoder/mapper consumption owner. | This closes unspecified Text/Bytes presence and error-payload representation. HEAD/RestNoValue skips success decoding and never counts Content-Length as consumed bytes. |
| P04 | Unexpected programming faults remain faults, even if cancellation also becomes signalled. Apply caller → total deadline → owned stopping → attempt timeout → actual outcome to expected outcomes and identified cancellation only. | This reconciles §§4.3/7 with §8.3 and inserts the omitted shutdown outcome. Never convert an unrelated OperationCanceledException or a programming defect into an ordinary timeout result. Preserve the original cause when cleanup also fails. |
| P05 | Capture valid existing W3C TraceId at entry as nullable CorrelationId; keep Logging's OperationId distinct. Generate no new identifier. | Logging has W3C trace/span and per-runtime operation scopes, not a single public CorrelationId API. A missing existing trace remains absent. Neither correlation nor remote codes become metric dimensions. |
| P06 | Preserve the supplied 30-s total timeout, retry None, redirects/cookies off, 16-MiB consumed success limit, 16-KiB error prefix and 1,024-scalar error message. | All remain proposed v1 defaults from the attachment. No default whole-upload buffering is introduced. Signing materialisation requires an explicit finite cap and one reused exact representation. |
| P07 | Optional `CIS.RestClient.Resilience` uses exact Polly.Core 8.9.1, whose net10 dependency group is empty. Its one additional exact NuGet mapping and lock belong in the initial scope. | This is a new optional dependency decision; the Metrics exception does not apply. Missing selected retry executor fails validation. A bounded early proof must establish the public-API delay bridge in [the dependency proposal](dependency-and-retry.md). |
| P08 | Preserve existing Metrics-only profiles. Reserve four REST logical streams only for configured REST composition; propose outgoing Auto/true/false selection, with Auto enabled for that composition. | Unconditional fixed growth would invalidate a current `MaxDefinitions=19` profile without REST. Effective catalog capture must use final known composition, before producer activation, in either registration order. See [Metrics/hosting](metrics-and-hosting.md). |
| P09 | Derive inherited authentication's default allowed origin from BaseUrl; require explicit allowed origins if inherited authentication has no base. Reject a disallowed final destination before authentication/dispatch. | This makes the origin rule concrete. TRACE also removes inherited credentials, never runs inherited authentication and rejects explicit/customized credentials or body, including declared credential query fields. |
| P10 | Support a verifiable factory-managed/owned native profile. A borrowed client keeps ownership and must have compatible visible settings plus explicit handler provenance/guarantees; reject an unsupported opaque profile. | A bare HttpClient does not reveal hidden redirect/cookie/retry/meter behavior. Do not mutate borrowed timeout/default headers, inspect private fields or claim validation/isolation that cannot be established. |
| P11 | Freeze one attempt/context/body-lease and bounded policy-outcome seam before parallel work. Ordinary handlers/customization finish before final authentication; immutable signed fields and the primary-send counter follow. | A handler calls next at most once; short circuits have zero dispatches and normal consumption/disposal. Each retry recreates request/content, with stable replay assurance and equivalent business payload. No stage graph, history or second retry owner. |
| P12 | Signal optional REST cancellation in existing Startup/HB hosting before waiting for HB/Configuration, then join actual REST drain. Keep StartupCoordinator independent of RestClient. | ApplicationStopping alone occurs too late if HB awaits a REST call. Closed admission, cancelled host wait and disposed provider are not proof that owned callbacks/body work have settled. |

## Terminal and ownership details to freeze in RC1

Invalid arguments/configuration may fail before admission; they acquire no owned
caller resources. An admitted operation captures entry time and one observation
binding. Stream ownership transfer has an explicit admission/acquisition point;
borrowed streams remain open, and acquired factory-owned/transferred streams are
disposed even after authentication rejection or a handler short circuit.

Expected declared preparation/authentication failures use a small typed context
completion facility. Missing infrastructure is a configuration error. Unexpected
callback/codec/handler faults escape after owned work settles. A handler that
starts next and then throws must not leave the one owned exchange detached.
Cancellation committed after an already completed result does not rewrite it.

Retries retain the supplied conservative preset: at most three dispatches,
GET/HEAD/OPTIONS only by default, statuses 408/429/502/503/504, no default transport,
business, decoding or post-headers body retries, and no attempt timeout unless
explicitly configured. Per-call inherit/None/whole replacement stays distinct.
Known policies are immutable registered instances, never a pipeline per URL,
credential, response type or operation instance.

Authentication sees the final URI and exact content representation. After the
first dispatch, retry preparation must preserve the asserted replay contract,
including method/destination/business payload and a provider-supported stable
idempotency key; credentials/signatures may refresh per attempt. Revalidate before
any new dispatch. An arbitrary added header does not establish replay safety.

## Proposed technical implementation boundary

Use native HttpClient/SocketsHttpHandler/DelegatingHandler and System.Text.Json.
One direct outer method owns entry budget, policy invocation, result commit and
actual cleanup. A small content owner and one policy adapter are independent
responsibilities, not a class for every workflow step. No public response-stream
API, multipart model, circuit breaker, hedging, cache, token store, OAuth service,
generated client framework, queue, scheduler or new Configuration subsystem is
included. No new installable skill is planned; update existing routing/guidance
only when real implemented APIs and examples can be referenced.

The proposed registry has at most 16 client aliases and 32 legal client/operation
pairs including request fallbacks, as supplied. It is finite composition metadata
and lifetime ownership for this module, not general DI/lifecycle infrastructure.
Client/policy/transport configuration is an immutable snapshot. Live Configuration
changes do not silently replace handlers, reset the provider or rebuild policies.

The current Metrics absence/disabled behavior, mandatory EnvironmentOptions
identity, one-provider ownership and actual drain remain in force. Native HTTP
duration and active requests end at response headers on pinned .NET 10; logical
REST duration includes retries, body, mapping and cleanup. Actual application byte
counts include observed partial/retried transfers and exclude guessed write
partials, Content-Length, headers, TLS and network-interface overhead.

## Readiness

This is a source-backed planning proposal with explicit recommendations. The
numeric profile and new dependency require the eventual implementation decision;
the retry-delay composition is not yet a native pass. If the bounded proof cannot
meet the supplied semantics through supported APIs, retain the exact gap and stop
retry-dependent expansion. Do not copy a backoff engine, add an unapproved helper
package, hide a deviation or expand the global limits to make the plan appear ready.
