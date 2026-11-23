# Implementing the management protocol in Kotlin

Use `contracts/management/v1/openapi.json`, `contract.schema.json` and the exact
JSON text vectors in `conformance/management/v1/wire-cases.json` as the language
boundary. No C# reflection model or TypeScript runtime is required.

Keep sequences, metadata revisions, generations and configuration revisions as
validated decimal strings or exact signed 64-bit values serialized as strings.
Allow zero only for effective job configuration revision. Preserve canonical
lowercase UUID D text, seven-digit UTC timestamp fractions and original Unicode
without NFC conversion. Disable duplicate-key acceptance in the chosen JSON
reader, reject malformed Unicode and bound UTF-8 bytes, depth and inventory
counts. Commands reject unknown fields/nulls; responses allow additive fields and
retain unknown enum/status text explicitly. Opaque module JSON must not lose
unrecognized numeric precision.

Require explicit HTTPS endpoint selection, preserve its path prefix, reject URI
credentials/ambiguous paths and disable redirects. Supply native TLS/Bearer
credentials per call from the owning application; copied identity headers do not
establish authority. Do not install an accepting TLS verifier.

Start one default 30-second coroutine deadline at API entry. Propagate its
cancellation through credential acquisition, actual HTTP, streaming decode and
bounded read-retry waits. Join owned native work and cleanup in structured
concurrency instead of detaching a timed-out request. A canceled HTTP call does
not prove rollback. Default to no retry; an explicit read policy permits at most
two selected transient GET retries inside that deadline. Never automatically
replay PUT/DELETE or Commit.

Represent HTTP status, typed business decision, original operation completion and
current cleanup as separate facts. A matching replay/found result retains its
original committed decision even if the new read/arbitration attempt rolls back
or cleanup fails. Preserve the original metadata key, exact command and ETag when
the response is lost; query the original-key endpoint. NotFound is not evidence
of rollback and does not authorize inventing a replacement key.
