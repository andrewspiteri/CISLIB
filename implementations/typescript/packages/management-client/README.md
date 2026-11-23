# @cislib/management-client

The portable entry exports `ManagementClient`, the twelve typed RM-07 APIs,
readonly DTOs, exact wire codec/limits and known/raw status helpers. It imports
only the sibling `@cislib/client-core` package and has no Node or UI framework
dependency. Build the workspace before using its emitted ES modules/declarations.

The explicit `@cislib/management-client/server` Node export supplies
`createNodeHttpsTransport({ origin, ca? })` and
`createNodeManagementClient({ baseUrl?, ca?, ...clientOptions })`. Only the latter
may use `CIS_MANAGEMENT_URL` when no explicit endpoint was supplied. Native server
certificate and hostname verification remain enabled, with TLS 1.2 or later;
there is no accepting callback, insecure flag or trust-store installation.

Supply a bearer token through a per-call provider, or return
`{ kind: "transport", value: { kind: "mtls", certificate, privateKey } }` to the
Node transport. The owning host supplies bounded PEM bytes/strings explicitly.
The adapter uses a separate native connection per request, joins request/socket
close and zeroes its owned copied private-key buffer on drain. Host-owned source
material remains the host's responsibility. It does not inspect an existing
credential store, enroll certificates or select another identity on rejection.

Metadata PUT requires an explicit original safe operation key and one strong
ETag. Omitted description clears it; explicit null is rejected. Both PUT and the
original-key GET preserve HTTP/business/persistence/cleanup facts independently.
The durable response contains exactly one nonnull `result` value or original
`rejection: { code, message }`; the opposite field may be omitted or null. A native
rejection does not invent a metadata revision, timestamp or successful value.
Retain the original key after a timeout or unknown completion. Matching replay
does not become a fresh administrative decision.

See the workspace README for bounds, retry/deadline details, browser compilation,
examples and the actual same-service native consumer.
