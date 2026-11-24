# CISLIB TypeScript management clients

This workspace contains the RM-07 `@cislib/client-core` and
`@cislib/management-client` ES modules. The only external dependency is the pinned
TypeScript 5.9.3 development compiler. The packages have no external runtime
dependencies. Use Node 24.15.0 and npm 11.12.1 for the repository commands:

```sh
npm ci
npm run build
npm test
npm run check:browser
```

The build emits JavaScript and declarations in each package's `dist` directory.
The browser check uses DOM libraries and `types: []`. Native `node:test` checks
consume the independent exact-text vectors in
`conformance/management/v1/wire-cases.json`, then exercise deadlines, held cleanup,
read retries, concurrent credentials and selected DTO projection. Native service
acceptance uses `tests/real-service-consumer.mjs` against the actual C# service;
unit substitutes do not establish SQL, native authentication or deployment proof.

```ts
import { ManagementClient } from "@cislib/management-client";

const client = new ManagementClient({ baseUrl: "https://management.example/cis/" });
const result = await client.listApplications({ pageSize: 20 }, {
  signal,
  credentials: async ({ signal }) => ({
    kind: "bearer", token: await authenticatedSession.getManagementToken(signal)
  })
});
if (result.kind === "http" && result.status === 200 && result.value) {
  for (const application of result.value.items) {
    // Display selected fields, not arbitrary module details or transport results.
    console.log(application.metadata.displayName);
  }
}
```

The endpoint must be explicit HTTPS. A configured path prefix is preserved; URI
credentials, dot segments, escaped separators, query/fragment and redirects are
rejected. The portable client never reads environment variables, process state,
cookies, browser storage or global credentials. A per-call credential provider
receives the same abort signal as the actual transport and is invoked for each
attempt. Reusing a client for multiple users does not share their credentials.
An optional constructor provider is an explicit host callback, not an authority
derived from a copied caller context.

Each call has one 30-second default deadline beginning before request encoding and
credential acquisition, including transport, bounded streamed decode, retry waits
and real cleanup. An explicitly configured timeout is 1–300000 milliseconds.
Cancellation requests abort and waits for actual owned work; a provider or custom
transport which ignores its signal delays completion until it drains. There is no
abandoned `Promise.race` task and cancellation does not imply remote rollback.
Unexpected programming faults propagate unchanged; expected transport/protocol
failures return structured facts. A decoded HTTP result remains established if
the deadline elapses while cleanup drains. `cleanupSucceeded` is separate from
HTTP status, a business decision and original persistence.

Native fetch response-body connection loss, deadline and caller cancellation
return those structured facts after actual stream cleanup, including the original
key and uncertainty for a metadata PUT. The no-Docker native HTTPS consumer test
uses freshly generated fixture PEM files and a child-process public-root input;
it changes no machine trust store and does not establish a remote database commit.
Build this TypeScript workspace first and make the pinned Node executable
available before selecting `ManagementFetchTransportTests` in the C# integration
project. `CIS_TEST_NODE_EXECUTABLE` can explicitly select that installed executable.

Calls make no retry by default. Deliberately setting `readRetries` to 1 or 2
permits only GET retries for 408, 429, 502, 503 and 504, with bounded Retry-After or
200/400 ms delays inside the same deadline. Authentication rejection, failed body
decode and transport failures do not trigger replay. POST, PUT and DELETE never retry.
For an uncertain metadata PUT retain `operationKey` and the original command and
ETag. Use `getMetadataOperation` with that key; a 404 does not prove an earlier
transaction rolled back. Do not generate another key or retry Commit.

Ordered 64-bit values remain decimal strings, including values above 2^53 and
configuration revision `"0"`. UTC timestamps retain all seven fraction digits.
Strings retain Unicode code points without normalization. Requests are closed;
response additions and unknown status text remain readable. Use the exported
`known*` helpers or `knownValue` to distinguish known and future values. Unknown
module `details` is an `OpaqueJsonObject` retaining raw JSON numeric lexemes; it is
not a UI payload. `decodeManagement` rejects duplicate decoded property names,
malformed UTF-8/UTF-16, invalid calendar dates, excessive nesting/counts/bytes and
known-field nulls before returning frozen detached values. `encodeManagement`
uses the same fixed DTO rules. This is a management codec, not a JSON Schema or
general serialization framework.

Default wire bounds are 64 KiB ordinary requests, 4 MiB inventory requests, 1 MiB
ordinary responses, 8 MiB inventory responses and depth 16; inventory limits are
32 modules, 64 categories, 1024 jobs and 4096 job observations. A captured
`wireLimits` override must stay within the explicit portable contract ceilings.

`examples/node/management.mjs` uses the explicit server export and host-owned PEM
paths. `examples/next-server` shows a real `server-only` import boundary and a
per-request credential owner, then selects a small response DTO. Its compile-only
declaration does not install a Next runtime or implement login; production UI and
identity/session flows remain RM-09. See [the Kotlin protocol note](KOTLIN.md) for
another language implementation.

The additive configuration client exposes nineteen publication/distribution APIs;
see the [configuration guide](../../docs/modules/management/typescript.md).
Validation and preview POSTs are explicitly non-mutating, while publication,
release, assignment and catalog commands retain original-key uncertainty. Native
WebCrypto preparation/decoding is awaited inside the existing request owner.
