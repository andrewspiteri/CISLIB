# Implementing the management protocol in Kotlin

Use `contracts/management/v1/openapi.json`, `contract.schema.json` and the exact
JSON text vectors in `conformance/management/v1/wire-cases.json` as the language
boundary. No C# reflection model or TypeScript runtime is required.

Keep sequences, metadata revisions, generations and configuration revisions as
validated decimal strings or exact signed 64-bit values serialized as strings.
Allow zero only for effective job configuration revision and local accepted
Configuration revision. Preserve canonical
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
replay POST/PUT/DELETE or Commit. Explicit validation/preview POSTs are read-only
for uncertainty classification; they still do not retry automatically.

The typed registration control GET always disables retries, even under a selected
read policy. Require the exact echoed registration/attempt UUIDs, a canonical
authority UUID, an opaque bounded control revision, a boolean admission decision
and an integer lifetime from one through 15,000 milliseconds. Measure usable
freshness from local monotonic request entry through actual decoding and cleanup;
response arrival cannot start a new window. Follow the shared integer-token
fixtures rather than rounding fractional or exponent representations.

Keep the API client passive. A separately selected managed host starts closed and
requires live registration/control and module-owned reconciliation before new
work. Failure, expiry, clock gaps and stopping close new admission while admitted
finite work retains its existing owners. A report or connection alone cannot
reopen admission, and a new process cannot bootstrap from a retained snapshot.

Represent HTTP status, typed business decision, original operation completion and
current cleanup as separate facts. A matching replay/found result retains its
original committed decision even if the new read/arbitration attempt rolls back
or cleanup fails. Preserve the original metadata key, exact command and ETag when
the response is lost; query the original-key endpoint. NotFound is not evidence
of rollback and does not authorize inventing a replacement key.

The nineteen RM-08 configuration operations and their independent
`conformance/management/v1/configuration-cases.json` vectors extend this same
boundary. Use portable configuration IDs/schema strings and discover authorized
source IDs through definition pages. Respect redacted defaults, exact source
references or explicit absence, immutable releases and whole-map preconditions.
Source text is exact: compute SHA-256 over its decoded UTF-8 without trimming,
normalizing or reserializing it, before exposing usable content. Include digest
work in the existing coroutine deadline and join its actual settlement.

A conditional304 requires the complete retained snapshot for the independently
expected authority/application/environment/registration/cohort/host/schema,
exact ETag and empty response. It grants no control freshness. Content rejection
may identify a desired release only after that outer identity fence; foreign or
malformed outer data has no identified desired fact. Preserve payload-free
desired/applied/historical status and separate freshness, and use the four typed
original-key recovery families without another mutation. A Kotlin acceptance
runtime remains deferred; these requirements introduce no Kotlin framework.
