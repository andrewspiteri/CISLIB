---
title: RestClient handlers authentication and mapping
type: document
status: draft
cis:
  stable_id: cislib.document.modules.rest-client.extensions
---

# Handlers, authentication and mapping

The interfaces in `CIS.RestClient` express fixed extension responsibilities. The
current implementation is under [CIS-0010 verification](../../changes/CIS-0010/verification.md);
component source and examples alone do not establish public runtime/host proof.
Use [the portable contract](../../../contracts/rest-client/README.md) for the
complete outcome and [native content](http.md) for explicit source ownership.

## Exchange handlers

Handlers wrap one exchange in registration order: A/B/C outward, then C/B/A
inward. Forward the same request context and supplied cancellation token to
`next` at most once, within the awaited handler invocation. A retained `next`
cannot dispatch later. Retrying belongs to the separately selected policy.

Each returned response lease belongs to the operation. Do not consume its body
behind the codec or take ownership of another downstream lease. A short-circuit
response follows ordinary decoding, mapping and disposal, with zero primary
dispatches. Starting `next` and then throwing or returning a different response
still joins the started exchange and releases both owned responses. The outer
owner must await the exchange before disposing its scope and preserve original
faults when cleanup also fails.

Per-call state belongs on the request context or local variables. Reusable
handlers, codecs, mappers and callbacks must be concurrency-safe. Registration
and call snapshots copy options, headers and query changes; they do not deep-copy
body objects or buffers, which remain unchanged until actual settlement.

## Final authentication

Ordinary handlers and the selected customization callback run before native
content preparation. After acquisition, the implementation checks the actual
method, destination, Host, upgrade/TRACE rules and body descriptor again. A
retained context cannot use content acquisition to bypass those guards.

The native request canonicalizes ordinary header values before authentication.
The restricted authentication view can change only declared credential fields;
framing/authority fields cannot be declared credentials. Request and content
credential headers use their correct native collections, with transactional
validation. Authentication sees the rendered final URI including credential
query additions. Sealing fixes the signed request before primary dispatch.

For body signing, call `GetBodyBytesAsync` with an explicit finite cap and use
those exact bytes. The resulting representation is reused for sending; no
unbounded upload buffering is implicit. Token acquisition is application-owned.
Use `Fail` for an expected authentication rejection; thrown programming faults
remain faults after owned work settles. Do not put secrets in explanations or
log credential-bearing URIs, headers or payloads.

Inherited authentication and credential-header defaults require configured
origins. BaseUrl supplies its origin by default; without a base, configure them
explicitly. TRACE omits inherited credentials and inherited authentication, and
rejects bodies and explicit/customized credentials, including declared query
fields. Unsupported CONNECT/upgrade capabilities return
`client.unsupported_operation`; this binding does not open another channel.

## Response mapping

One bounded reader owns body consumption. The default codec decodes successful
HTTP responses; HTTP error documents become a bounded diagnostic prefix rather
than `T`. A mapper receives detached status, headers, decoded presence and that
prefix. `RejectBusiness` turns an HTTP-200 envelope into an absent failed value;
it cannot turn non-2xx into success. `RemoteError` retains provider detail
separately from portable error codes. A present JSON null remains present;
`RequireValue` checks presence, and a mapper enforces domain non-nullness.

Unexpected mapper/codec/handler exceptions are not inferred from their CLR type
as service failures. Identified cancellation is supplied by the operation owner;
the public runtime's caller/deadline/stopping classification and real host drain
require their separate operation integration evidence.
