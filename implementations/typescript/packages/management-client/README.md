# @cislib/management-client

The portable entry exports `ManagementClient`, the thirteen typed RM-07 APIs,
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

`getControl(registrationId, attemptId, context?)` reads the v1.1 authoritative
confirmation. It preserves the HTTPS prefix and current per-call credential;
both IDs are canonical UUIDs and exact echoed IDs are checked during decoding.
It forces no automatic retries even when the client or call selects read retries.
The attempt ID is not an operation key. A typed `admissionAllowed: false` is a
current refusal, not an absent result. Hosts must measure `validForMs` from local
monotonic request start, cap it at fifteen seconds and reconcile current authority
before admitting new work. A late successful HTTP response cannot refresh that
window. The SDK adds no browser or Node hosting loop or admission framework.

Service-info's optional `maximumInventoryRequestBytes` preserves older v1
responses while exposing a new server's effective provider/wire byte limit.

See the workspace README for bounds, retry/deadline details, browser compilation,
examples and the actual same-service native consumer.
## Configuration publication and reads

The additive `ManagementConfigurationClient` and structural
`ManagementConfigurationClientPort` cover the nineteen RM-08 configuration APIs.
They use strict async native WebCrypto decoding, explicit readonly POST semantics
and narrow same-session conditional snapshot reuse. See the
[TypeScript configuration guide](../../../../docs/modules/management/typescript.md)
and canonical Management contract. Existing `ManagementClient` behavior remains
available.

## Optional scheduler and application features

`ManagementSchedulerClient` supplies typed operator, Event-producer and Worker
methods through separate structural ports. It starts no worker or recurrence.
`ManagementApplicationCatalogClient` reads trusted logical Job/Trigger-provider/
rule-engine descriptions and separate instance participation;
`ManagementConfigurationAdministrationClient` reads source/binding action hints
and trusted cohort choices. Existing Management and Configuration client
substitutes gain no compulsory methods. HTTP API capabilities retain their
existing protocol meaning.

All three companions reuse the portable request owner, per-call credentials and
one entry deadline, including asynchronous integrity checks and actual cleanup.
Writes, Events, admission and reports never retry automatically. Administrative
creation retains the chosen UUID/key with `If-None-Match: *`; updates and manual
admission use one strong ETag. Keep the original key after uncertainty and use
its resource-specific lookup. A committed original result can coexist with
failed current HTTP cleanup; neither implies rollback. Source-scoped Event
references, Int64 strings, pinned settings and actual worker build facts remain
exact. See the [portable Scheduler contract](../../../../contracts/management/v1/scheduler-contract.md).
The clients and declarations establish no installed backend or deployment proof.
