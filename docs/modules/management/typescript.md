# TypeScript configuration client

`@cislib/management-client` exports the separate `ManagementConfigurationClient`
and structural `ManagementConfigurationClientPort`. It preserves the existing
`ManagementClient`, metadata result/problem types, per-call credentials and
native transport ownership. Neither client starts a timer, registration owner,
configuration mapper or UI framework. Constructors capture endpoint/options and
perform no network I/O.

The [canonical protocol](../../../contracts/management/v1/README.md),
[configuration contract](configuration-contract.md), OpenAPI/schema and shared
configuration vectors define all nineteen routes independently of TypeScript.
The module exports readonly `Configuration*` DTOs, exact decimal strings and
explicit known-value helpers. A future Kotlin client should implement those same
wire and ownership obligations; Kotlin and a TypeScript POCO acceptance runtime
remain deferred.

```ts
import { ManagementConfigurationClient } from "@cislib/management-client";

const client = new ManagementConfigurationClient({
  baseUrl: "https://management.example.test/tenant/",
  timeoutMs: 30000
});
const definitions = await client.getDefinitions({ pageSize: 20 }, {
  signal,
  credentials: credentialForThisCall
});
```

The deployment supplies `signal` and the explicit per-call credential provider.
The configured prefix is retained. Browser imports use native fetch/WebCrypto
and DOM types, with no Node, React or Next import. Server applications can select
`createNodeManagementConfigurationClient` from the explicit `/server` export or
provide the existing `createNodeHttpsTransport`. That server path retains actual
TLS validation and explicit public CA/per-call mTLS inputs; it does not expose an
accepting certificate callback. An explicit endpoint wins over the server-only
`CIS_MANAGEMENT_URL` fallback.

The configuration API methods are `getDefinitions`, `validate`, `preview`,
`getSource`, `publishSource`, `getPublication`, `createRelease`, `getRelease`,
`getAssignments`, `replaceAssignments`, `getRegistrationConfiguration`,
`reportStatus`, `getStatus`, `getCatalog`, `applyCatalog` and four typed
`get*Operation` recoveries. Publication, release creation, map replacement and
catalog application require the unchanged original operation key and strong
resource precondition. Creating a release uses the assignment-map precondition
and does not activate or mutate that map. Recovery repeats no effect and never
authorizes a new-key rerun after uncertainty or NotFound.

Definition pages identify authorized common/application source resources.
Application rows contain their binding and host scope; common-only publishers
need no application row. Omitted raw defaults are redacted and are not interpreted
as empty defaults. `preview` accepts either an exact source pair or a complete
assignment proposal, plus ordinary recipient-page query controls. Its result
includes the complete current map/ETag, selected proposal and bounded recipient
compatibility observations, without promising deployed typed acceptance.

Configuration encoding/decoding has a separate maximum one-MiB complete body,
256-KiB decoded source, depth sixteen, sixteen cohort overrides and 256 complete
status entries. Global discovery pages remain at most 500 rows. Lower selected
limits remain effective. Source JSON is exact text, so numbers inside it are
never routed through JavaScript number. Native SHA-256 covers the exact decoded
UTF-8; Unicode normalization, BOM, duplicate decoded keys, invalid Unicode,
trailing input and oversized complete values are refused. Ordinary Management's
64-KiB request default is unchanged. Commands are closed recursively; response
additions and bounded future statuses remain explicit unknowns.

The existing request owner now accepts compatible synchronous or Promise-returning
preparation and decoding. Its one entry deadline includes encoding, credential
acquisition, transport/body, native WebCrypto, explicitly selected GET retries
and actual response disposal. It joins a digest/native operation even when that
operation ignores cancellation. Expected late content rejection cannot replace
an owned cancellation/deadline; unexpected programming faults remain original,
including faults after cancellation. A decoded result retains its established
facts while later cleanup settles.
Check `cleanupSucceeded` separately before treating a retrieved snapshot as usable
for a local runtime. Decoded HTTP facts, actual cleanup and local acceptance are
distinct; this package supplies no TypeScript acceptance runtime.

`POST /configuration/validate` and `/configuration/preview` are explicitly
non-mutating. Their failures do not claim mutation uncertainty. Actual mutation
POSTs retain original keys and uncertainty. No POST retries automatically, even
when a generic read retry count is selected. GET-only retry remains explicit and
bounded; legacy PUT/DELETE mutation classification is unchanged. The added
`POST` transport method should be included in exhaustive custom transport
switches. Requests use the compact `configuration` observation alias while
protocol and durable operation names remain distinct.

Runtime reads take an independently trusted `ConfigurationSnapshotTarget` plus
an optional complete retained snapshot. Preparation copies the target, validates
and hashes retained content within the owned deadline before deriving an
`If-None-Match` header. A 304 is accepted only for that conditional GET, with an
empty body, exact returned ETag and the same authority/application/environment/
registration/cohort/definition/schema/host context. Every other redirect remains
blocked. Reuse supplies no new Management authority, control freshness or local
acceptance observation.

The decoder establishes target identity and strong envelope ETag before content
validation. A failed HTTP 200 can therefore retain a payload-free
`contentRejection` containing only the identified desired release/generation and
safe code. The result preserves actual status, failure kind, attempt/timing and
cleanup facts; it carries neither a successful value nor server Problem Details.
Malformed or foreign outer data has no identified rejection. The public result
union also supports honest substituted ports. No integrity check remains after
the call returns.

Configuration status preserves separate sequence, desired/applied/historical
versions, candidate versus notification failure and observation versus receipt
time. Current adoption, registration freshness, adoption freshness and reported
instance metadata remain separate. Use the `knownConfiguration*` helpers before
interpreting a future value as success. Source payloads, local effective checksums
and equality tokens are absent from status. This client retains no snapshot
history, persistent cache or polling owner.

Focused Node cases consume the independent wire vectors, all nineteen methods,
original-key/result facts, conditional reuse, readonly POST versus mutation,
delayed crypto/deadline/cleanup and unchanged native failure behavior. The
existing native HTTPS fetch fixture additionally exercises GET, legacy PUT,
readonly POST and mutating POST under disconnect, timeout and cancellation,
joining actual sockets. The browser compile checks the public portable types.
Consult the active change's verification record for executed results; authored
tests do not establish a native, provider or deployment pass.
