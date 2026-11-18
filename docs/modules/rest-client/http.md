---
title: RestClient native HTTP and content
type: document
status: draft
cis:
  stable_id: cislib.document.modules.rest-client.http
---

# RestClient native HTTP and content

The native component uses `HttpClient`, `SocketsHttpHandler`, native content and
stream I/O, and `System.Text.Json` inside `CIS.Core`. Native helpers are in
`CIS.RestClient.Http`; the ordinary [consumer contract](core.md) contains no native
HTTP message types. The public [client and registration](core.md) compose this
component with retry, deadline and host ownership. Consult
[verification](../../changes/CIS-0010/verification.md) for actual executed evidence.

## Request bodies and ownership

`RestBody.Json<T>`, `Text`, `Bytes` and `Form` select explicit representations.
JSON preserves its declared type, including a typed null. Text is UTF-8. Form
encoding preserves repeated fields and uses native form-urlencoded content.
Objects and byte buffers remain borrowed and must remain unchanged until the
logical operation settles.

`RestHttpBody.Stream(stream)` borrows a readable one-shot stream. Passing
`ownsStream: true` transfers disposal only when an admitted operation acquires
valid input. An unreadable supplied stream is rejected without taking ownership.
`RestHttpBody.Reopen` promises a fresh equivalent client-owned stream per attempt:

```csharp
RestBody upload = RestHttpBody.Reopen(_ =>
    ValueTask.FromResult<Stream>(File.OpenRead("upload.bin")));
```

Creating a descriptor opens nothing. A factory result is already acquired and
owned, so cancellation or rejection after acquisition still awaits its cleanup.
Borrowed streams stay open. A native multipart extension is available through
`RestHttpBody.Content`, whose factory returns fresh client-owned `HttpContent`.
Its native content headers, including the generated multipart boundary, are
available before final authentication. Set `replayable: false` when the factory
cannot promise equivalent replacement content.

Authentication may request an explicitly bounded body representation. It is
materialized once and the identical bytes are reused for sending and any declared
replay. Exceeding that bound fails preparation; consumed one-shot remnants cannot
later be sent. No upload bytes are counted while preparing a signing buffer.
There is no automatic buffering solely for retries or Metrics.

## One bounded response reader

The internal reader consumes each owned response once. Default JSON decoding
delegates to `System.Text.Json`; text honors native charset/BOM handling, and
bytes return detached data. Zero consumed bytes mean absence in all three modes.
JSON `0`, `false`, `""`, `[]` and representable `null` remain present values.
`RequireValue` checks presence. HEAD and `RestNoValue` skip success-body decoding.

The success limit applies to actual consumed, decompressed bytes. Content-Length
does not prove consumption or bound decompressed content. Non-success HTTP bodies
are not deserialized into the expected success type: the reader captures at most
the configured detached diagnostic prefix, plus one consumed probe byte when
needed to prove truncation. A zero diagnostic cap disables body capture. The
outer operation owns disposal of the remaining native response.

Known native I/O, default decoding and processing-limit failures have identified
internal signals. Arbitrary custom codec exceptions remain faults. Unexpected
encoding faults retain their original exception through native content wrapping;
the outer owner rethrows it after cleanup before cancellation/result mapping.
If acquisition and cleanup both fail, both causes are preserved.

## Native pool and observation boundaries

`RestHttpTransport.Create` creates a known long-lived native pool without network
I/O. Its captured profile uses infinite native client/connect timeouts so the
logical owner supplies the actual operation token. Redirects, cookies, automatic
credentials and environment proxies are disabled. Native TLS validation remains
in place. Pool rotation defaults to two minutes; decompression defaults to gzip,
deflate and Brotli; response headers have the native 64-KiB default bound.

A composed host must supply its exact existing `IMeterFactory` before first use.
The native transport checks that identity; it cannot import an opaque raw
`HttpClient`. A standalone transport without a factory makes no isolated native
collection claim. Pool disposal closes new sends and awaits retained response
leases without canceling another borrower. Borrowing logical clients must be
stopped before the shared pool is disposed.

Primary dispatch increments at the known innermost handler boundary, after
preparation/authentication. It is not a count of TCP connections or internal
native transmissions. Upload/download observations count only completed writes
and actual reads, including retransferred payload and any diagnostic probe.
Unknown partial failed writes are not guessed. Response-header completion is
separate from body consumption and owned cleanup; native HTTP timing cannot
replace the entry-to-settlement logical duration.
