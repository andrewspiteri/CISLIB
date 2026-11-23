# CISLIB RM 07 Central Management Service and Client

Implementation specification  
Version 1.0  
23 November 2026  
Service and C sharp target .NET 10 with current repository pins  
Repository baseline dcb61dc13c8af138f409db4e13c477cc3a06a0b5

## 1 Purpose and delivery decision

RM-07 provides an independently hosted Central Management application and reusable CISLIB clients. Authenticated applications register their running instances and report bounded management information. Authorized consumers discover applications, inspect their reported state and use explicitly implemented management APIs.

The service remains a C# application built with CISLIB. The language boundary is a versioned HTTP and JSON contract. Deliver a C# management client and the first small TypeScript client foundation and management client against that contract. Future Kotlin Multiplatform clients must be able to implement the same protocol without reproducing .NET classes, hosting or dependency injection.

The client SDK family has three layers: common client facilities, API-specific clients and host integrations. RM-07 implements the facilities needed for management now. It does not port CIS or the complete CISLIB runtime to TypeScript.

MUST and MUST NOT identify requirements. SHOULD identifies a preferred approach whose departure needs a concise technical reason. MAY identifies an optional behavior. These terms do not create additional approval stages.

### 1.1 Included work

- An independently deployable management service with authenticated HTTP APIs and durable application-owned storage.
- Application and environment identity resolution, instance registration, last-seen observations and explicit capability discovery.
- Read-only projection of available module information, including the existing job catalog, categories and reported execution state.
- One real administrative mutation: replace an application's display metadata, with authorization, optimistic concurrency, durable original-result recovery and transactional audit staging.
- A documented extension pattern for additional typed management endpoints.
- A portable API contract, C# client, TypeScript client foundation and management client, host examples, bounded test substitutes and focused conformance checks.
- Operational configuration, deployment instructions and practical failure behavior.

### 1.2 Roadmap boundaries

| Roadmap item | Ownership retained by that item |
| --- | --- |
| RM-01 Identity and security | Credential validation, trusted caller projection, complete capability grants and security provider behavior. |
| RM-02 and RM-03 | Explicit local transactions, durable operation arbitration, original results and uncertain completion. |
| RM-04 Messaging | Durable outgoing delivery, commands, request/reply, inbox/outbox and broker integration. |
| RM-05 Shared coordination | Duty ownership, lease loss, fencing, exact job bindings and existing local execution machinery. |
| RM-06 Audit | Portable audit capture and delivery integration. Destination audit persistence remains separate. |
| RM-08 Configuration publication and distribution | Publishing configuration, selecting recipients, distribution, application adoption and version convergence. |
| RM-09 Management portal | Next.js user interface, production operator sign-in/session flows and framework-specific portal integration. |

RM-07 does not implement rule evaluation, calendar recurrence, a distributed scheduler, configuration publishing, arbitrary remote code execution, a generic command queue, credential issuance, live permission administration or an audit archive. It does not implement the customer presentation database, owner/sequence synchronization, SQLite replication or WebSocket synchronization discussed separately.

A management API is not automatically a remote command to a managed process. The initial metadata mutation executes in the management service. Future remote controls must declare their target-side authorization, delivery, execution and recovery semantics and reuse RM-04/RM-05 where applicable.

## 2 Architecture and existing foundations

The service is a CISLIB consumer with its own executable, deployment, database, migrations and operational configuration. Reusable client contracts belong in CISLIB. Server repositories, migrations and product-specific handlers do not belong in the lightweight client or Core runtime.

The main relationships are:

| Consumer | Connection and responsibility |
| --- | --- |
| Managed C# application | Uses the C# client and optional registration host integration to call the management service over authenticated HTTPS. |
| Authorized automation | Uses either SDK and an explicitly authorized workload identity. |
| Future Next.js server | Uses the TypeScript management client with credentials scoped to the current server request. |
| Future browser portal | Calls the Next.js server; receives selected management data, never workload credentials or private keys. |
| Future Kotlin client | Implements the shared API and behavior using appropriate platform transport, credentials and cancellation. |

Use the current CISApplication host, native ASP.NET authentication/authorization, existing REST transport and the approved dependency policy. Constructors and registration remain passive; there is one real service provider and existing sequential startup. Do not create a parallel lifetime, retry, authorization or telemetry framework. [S1, S2, S3]

The inspected baseline contains RM-05 and its jobs/catalog integration. RM-06 is a specified dependency; a general Audit implementation is not present at that baseline. The audit-dependent mutation is complete only after the required RM-06 producer path is available and integrated. Do not substitute the existing nondurable security audit hook and claim equivalent delivery. [S4, S5]

### 2.1 Suggested source organization

Follow the repository's current naming conventions when implementation begins. The intended separation is:

| Location | Content |
| --- | --- |
| contracts/management | Portable contract, checked-in OpenAPI document and shared JSON fixtures. |
| implementations/csharp/src/CIS.Core/Management | Public contracts, typed HTTP client and passive hosting integration under CIS.Management and CIS.Management.Hosting. |
| implementations/csharp/applications/CIS.Management.Service | Independently hosted management application, persistence and deployment assets. |
| implementations/typescript/packages/client-core | Portable client facilities actually used by management. |
| implementations/typescript/packages/management-client | Typed management methods, DTOs and explicit host entry points. |
| Existing testing and documentation locations | Focused provider tests, public consumer checks and concise guides. |

Do not split the server into several services or packages without a concrete ownership or deployment need. The service is a product application, not another copy of the CountryData tutorial. Core gains no new third-party dependency. Optional provider and application dependencies follow the current approved policy; the historical blanket no-NuGet rule is not the current policy. [S1]

## 3 Discovery and bootstrap

Use a configured DNS endpoint, supplied by `CIS_MANAGEMENT_URL` or equivalent explicit host configuration. Explicit client options take precedence over the environment variable. The value identifies a stable service/load-balancer address, not a particular management replica.

An enabled integration MUST have a valid absolute HTTPS base URI without user information, query or fragment. Validate its configured base path and append API paths without dropping that prefix. Do not scan networks, enumerate pods or implement a separate discovery registry. Authenticated redirects to another origin are forbidden; pagination returns opaque cursors rather than arbitrary URLs.

Bootstrap endpoint, trust configuration and credentials MUST be available independently of Central Management. They cannot depend solely on configuration fetched through the client being created. This also prevents a future RM-08 bootstrap cycle. [S2]

The default integration is disabled until explicitly selected. Once selected, missing or invalid local configuration is a startup error. A correctly configured but unreachable management service is an operational outage, with the policy described in section 11.

The management service does not require registration with itself to start. Its own database, security configuration and operational listeners are available through ordinary deployment configuration.

## 4 Identity and authorization

### 4.1 Application admission

Reuse the RM-01 reference profiles: Entra workload identity, direct client-certificate authentication and isolated LocalDevelopment credentials. Preserve their actual provider prerequisites and deployment restrictions. LocalDevelopment must not become a production fallback. [S3]

Resolve the managed application's name and environment from validated credentials and the trusted server mapping. A request body, hostname, source address, reported instance ID or claimed capability cannot replace that identity. A correctly authenticated but unmapped or unauthorized caller is rejected.

Registration records an admitted identity; it does not create trust or grant permissions. An admitted application may update only its own registrations and reported data. A caller cannot choose another environment or application by changing a route or payload. If a request repeats identity fields for diagnostics, they must match the authoritative mapping exactly.

The service maintains a stable opaque `applicationId` for the exact application/environment pair. It may be seeded by deployment configuration or created transactionally on the first authorized registration. Display metadata changes never change this identity. Case folding and silent Unicode normalization must not merge distinct existing identities.

### 4.2 Management capability targets

Management operations execute in the management service. Their RM-01 target application and environment are the service's configured identity. An individual managed application is represented as an exact resource of type `management.application` with its stable `applicationId`. That resource's stored mapping identifies the managed application's environment and name.

This convention fits RM-03's service-owned operation scope. It does not treat a managed application's self-reported identity as the management service's identity. Any future command executed in a managed application needs a separate target-side authorization decision.

Use a small explicit capability set:

| Capability | Required boundary |
| --- | --- |
| management.registrations.write | TargetOnly grant at the management service plus a mandatory trusted self-application mapping check on every registration/report request. |
| management.applications.read | Actual management.application resource, including its instances and permitted inventory. |
| management.applications.metadata.write | Actual management.application resource for the metadata command and its authorized replay. |
| management.operations.read | Actual management.application resource plus the initiating caller scope for original-result lookup. |

Additional modules register their own exact capabilities. Reuse whole-grant semantics; do not invent wildcard, same-environment or administrator shortcuts. A deliberately configured `AllOfType` grant has the existing RM-01 meaning within the management service's application/environment. Deployments requiring narrower access use exact resources or a compatible explicit policy. [S3]

Filter list results by actual authorized resources before returning items, totals or continuation information. Every page request, direct lookup, replay and result lookup is authorized afresh. An inaccessible application is returned as not found rather than revealing its existence. Discovery hints never replace authorization at execution.

### 4.3 Human operators and Next.js

Workload credentials establish workload identity. They do not prove that a particular operator authorized an action. Existing RM-01 workload profiles continue to reject human/delegation projections. [S3]

Human or delegated access uses the existing `SecurityProfileKind.Native` extension with an independently validated credential and trusted projection. Preserve the authenticated application and validated human actor, when present, according to RM-01. Plain user headers, cookies copied without validation and serialized CallerContext objects grant no authority.

RM-07 supplies the compatible server integration seam, per-request SDK credential input and focused tests. It does not implement a new identity provider, token exchange service, login interface or session store. If a requested operator mode has no configured compatible integration, reject it explicitly. The example must never retry that operator action under a more powerful workload credential.

Separately authorized automation can act as an application; its audit actor remains the application. Operator sessions and production Next.js login integration belong to RM-09. The Next.js server example keeps credentials server-side and returns only selected DTO fields. [S9]

RM-01 trust, mappings, grants and profile selection remain an immutable startup snapshot. Restart or replace instances to adopt changes; certificate leaf renewal retains its existing exception. RM-07 adds no live grant-editing endpoint. [S3]

## 5 Application registration and presence

### 5.1 Registration identity

A client creates a random UUID `registrationId` before its first registration request. It retains that ID across retries while the registration remains retained. A process restart or replacement of an expired registration uses a new ID, even if the deployment instance label is reused. A client never intentionally reuses a closed or expired registration ID.

`PUT /registrations/{registrationId}` submits immutable registration facts: the reported instance identifier, process start time, application version and supported management protocol major. The server binds the record to the authenticated issuer/subject and mapped application/environment and returns its stable application ID and server registration time.

The same registration ID and same immutable content return the existing result. Conflicting content is rejected. Another subject or mapped application cannot take over that record. Credential rotation that preserves the validated subject remains compatible; a changed subject creates a new registration unless a later explicit migration design says otherwise.

Registration IDs are identifiers, not bearer credentials. Every subsequent operation authenticates and checks the bound identity. Where replicas share a workload identity, this establishes an application trust boundary, not cryptographic proof of a particular replica. Retain RM-01 `NotProvided`, `Reported` and `Verified` evidence classifications separately. [S3]

### 5.2 Presence and observations

The client reports a latest observation, normally every 30 seconds, using a non-overlapping completion-based loop. The observation includes a monotonically increasing per-registration sequence, reported lifecycle/health summary and source observation time. The server records receipt time from a shared database time source.

Sequences are positive decimal strings on the wire and are independent for each registration and report stream. They are not a global synchronization cursor. Higher sequences replace the latest value; lower sequences cannot overwrite it. An equal sequence with identical content is a replay; an equal sequence with different content is a conflict. A replay does not extend last-seen time.

Presence is calculated from the latest accepted receipt and the configured stale interval, initially 90 seconds. Represent active, stale and explicitly closed states. Stale means the service has not received a timely observation; it does not prove a process is dead, unhealthy, stopped or unable to access its business database.

Keep presence, reported application health and authoritative job ownership separate. Missing management reports MUST NOT release or transfer an RM-05 lease. A healthy management connection does not grant ownership. [S4]

An explicit close is terminal while that registration is retained. Late reports cannot reopen it. An observation or inventory request for an unknown or purged registration is rejected and never creates a registration implicitly. Graceful host shutdown may send a bounded best-effort close while transport admission permits; otherwise presence expires normally. A close is an application report of registration shutdown, not proof that every business callback physically stopped.

### 5.3 Reconnection and replica changes

After a transient outage, reuse the current retained registration ID and send a new latest observation. If the server reports that registration absent after retention, create a fresh ID and explicitly register the still-running process again. A conflicting or closed registration is not silently recreated. Registration/report duplicate protection is a bounded telemetry guarantee for retained records; RM-07 does not promise indefinite rejection of historical registration-creation requests after their records have been purged. Each newly admitted registration still requires current authentication and trusted self-identity checks.

Server replicas share registration and sequencing state. Any request may reach any healthy replica, without sticky sessions. Duplicate and out-of-order requests must behave identically across replicas. Keep only the latest pending client observation; recovery does not replay a backlog of heartbeat ticks.

## 6 Inventory and capability discovery

An inventory is a bounded, explicitly mapped snapshot, uploaded on registration and when its contents change. It has its own sequence and server receipt time. Normal heartbeat requests do not retransmit an unchanged job catalog. Coalesce changing execution observations and catalog updates through the existing reporting owner, with at most one replacement upload per reporting interval after the initial upload. A busy job must not trigger an immediate full-catalog upload on every state transition. Retain only the latest pending snapshot.

Inventory replacement is atomic. A rejected snapshot leaves the previous complete snapshot intact and the reporting error visible. Do not silently truncate jobs or merge half of a new catalog with half of the previous one. Default limits must comfortably support hundreds of jobs.

### 6.1 Distinct meanings of capability

The API distinguishes three facts:

1. **Implemented management APIs:** typed operations installed in the management service and their contract versions.
2. **Reported application features:** modules and versions advertised by a particular application instance.
3. **Caller operation hints:** operations currently available to the caller for a resolved resource, based on actual server policy.

A reported feature does not install a server handler. A permitted-operation hint is advisory and may be stale when used; the actual operation is reauthorized. Never use a capability descriptor as executable code, a URL to fetch, a CLR type name or a replacement permission grant.

### 6.2 Initial job projection

When the existing Jobs module is installed, expose its public catalog and available snapshot/inspection data through an explicit adapter. Preserve the hierarchy Application, Execution Scope, Category and Job, with per-instance observations where they differ.

The projection includes stable job and category IDs, supported execution modes, display metadata, effective local configuration revision and enablement, available run state, safe last outcome and observation time. Include owner/generation or last/next-run information only when an existing authoritative source actually supplies it. Generation values retain their decimal-string representation.

Unknown next-run time remains absent/unknown. Schedule descriptors alone do not establish that a recurring scheduler exists. Category grouping never selects a handler, grants authority or changes a job's coordination key. [S4]

During rolling deployments, different instances may advertise different versions or catalogs. Preserve those differences; do not present their union as the guaranteed capabilities of every instance. A service restart preserves stored inventory but does not make old observations fresh.

No RM-07 endpoint changes job enablement, publishes schedules, runs a job or activates rules. Future modules add typed controls with their own authority and execution semantics. RM-08 will distinguish published desired configuration from each instance's accepted, rejected and effective revision. RM-07 reports the currently available local facts without inventing a central desired revision.

## 7 Initial HTTP API

The base route is `/management/v1`, relative to the configured service base path. The checked-in OpenAPI document describes every route, schema, status, bound, security requirement and example. OpenAPI 3.1 is the selected interoperable format; pin the exact supported patch/tool versions in the implementation. This is not a claim that 3.1 is the latest OpenAPI version. [S8]

| Method and relative path | Behavior |
| --- | --- |
| GET /service-info | Authenticated protocol majors, service version and installed API features; no credentials or unrestricted policy dump. |
| PUT /registrations/{registrationId} | Create or recover this process's registration under the trusted self identity. |
| PUT /registrations/{registrationId}/observation | Atomically accept a newer bounded presence/health observation. |
| PUT /registrations/{registrationId}/inventory | Atomically replace this registration's complete inventory. |
| DELETE /registrations/{registrationId} | Idempotently mark the caller's registration closed. |
| GET /applications | Authorized, filtered, stable-order application page. |
| GET /applications/{applicationId} | Application identity, display metadata and current metadata ETag. |
| GET /applications/{applicationId}/instances | Bounded per-registration presence and reported-health page. |
| GET /applications/{applicationId}/inventory | Bounded filtered inventory, with instance/version/freshness provenance. |
| GET /applications/{applicationId}/capabilities | Implemented typed operations and current caller hints for this application. |
| PUT /applications/{applicationId}/metadata | The administrative command defined in section 8. |
| GET /applications/{applicationId}/metadata-operations/{operationKey} | Authorized original-result recovery in the caller's own operation scope. |

All business routes require the appropriate selected native authentication profile. Service-info requires a caller admitted by the selected management policy. Protected data is not exposed through anonymous diagnostic routes.

Default page size is 100 and maximum is 500. Cursors are opaque, validated and bound to the endpoint, filter, ordering and caller scope. The caller sends a cursor back only to the same API. Document the stable sort and ordinary live-view semantics: pagination is not a frozen historical snapshot. Every page remains subject to current authorization.

Return suitable HTTP statuses with stable machine-readable failures. Distinguish malformed input, unsupported version, unauthenticated caller, forbidden operation, not found, conflicting identity/content, stale precondition, request too large, rate/capacity refusal and unavailable dependency. Expected API errors use RFC 9457 `application/problem+json`, extended with stable `code`, safe `correlationId` and a genuine operation reference when available. [S10]

401/403 or a TLS failure do not by themselves identify the exact credential defect. Preserve existing RM-01 known versus unknown failure evidence. Do not expose stack traces, arbitrary claims, credentials, connection strings or database exception text.

## 8 Administrative mutation and durable results

### 8.1 Initial command

The initial command replaces application display metadata: `displayName` and optional `description`. These are human-readable management labels; they cannot change application identity, credentials, grants, job configuration or executable behavior.

The request requires a caller-selected `Idempotency-Key` and the current metadata `If-Match` ETag. The key is application-defined protocol data, not an authentication credential. The request body is a complete replacement: displayName is required; omitted description clears it; explicit null is rejected. Limits are 256 Unicode scalars for displayName and 2,048 for description.

Require one strong metadata ETag previously returned for this application. Missing, weak, wildcard and multiple-value preconditions are rejected. A valid but stale ETag produces the documented precondition failure. An initial metadata read supplies an ETag even when display metadata is still at its initial value.

The metadata ETag covers only administrative metadata. Heartbeats, inventory uploads and instance churn must not cause unrelated metadata edit conflicts. Concurrent edits with distinct operation keys and the same old ETag permit at most one successful change.

### 8.2 Transaction and identity

Use the existing RM-02 transaction factory and RM-03 durable operation coordinator. The command's metadata update, original-result journal and RM-06 outgoing audit record participate in the owner's original transaction. The application owner commits once after the complete operation returns. Do not add another operation store, deduplication cache or transaction coordinator. [S5, S6]

Derive the operation scope from the actual managed application resource, stable operation name and validated initiating actor. Include authoritative human/delegation identity when present. A shared portal workload cannot make two operators share a result-recovery namespace. Request-supplied actor fields do not participate as trusted identity.

Use one stable versioned bounded encoding, such as a SHA-256 digest of an unambiguously encoded tuple of the full stable resource and validated actor/delegation identities. Submission and lookup use the identical derivation. This must fit RM-03's 256-byte UTF-8 scope bound without truncation. Tokens, mutable roles, policy revisions and display names do not define this scope; full authoritative actor attribution remains available in audit data. [S6]

The canonical fingerprint includes the target, complete semantic metadata, command version and expected metadata revision. It excludes bearer tokens, correlation IDs and transport attempt identifiers. The same scoped key with different canonical input is a conflict.

After current authorization, replay a matching completed operation's recorded result before reevaluating the original ETag. Otherwise a successful first attempt would make its own retry fail the concurrency check. A new operation must evaluate its ETag against the current row before applying its effect.

An inaccessible or changed actor scope cannot disclose a retained result. Expired original-result payloads retain RM-03 duplicate protection and return the existing expired/unavailable semantics; expiry does not authorize the same operation key to execute again.

### 8.3 Outcome and recovery

Keep these separate: the HTTP exchange, the original business decision, the original database completion and any later application execution.

| Observation | Meaning |
| --- | --- |
| Successful metadata response | The service knows the metadata change and its result/audit staging transaction committed. |
| Recorded rejected result | The command made its recorded business decision; any journal completion is reported separately from an applied metadata change. |
| Known NotCommitted | This attempt is known not to have committed. Preserve the existing result and reason. |
| Unknown completion or lost response | The mutation may have committed. Retain the original operation key and recover its result. |
| NotFound during later lookup | That authorized authoritative read found no committed result. It does not prove rollback of the original attempt. |

`Prepared` requires the current owner's known commit before reporting an applied mutation. `Replayed` and `Found` already establish the original committed result. A fresh arbitration/read owner's expected NotCommitted completion or cleanup diagnostics must not downgrade that original fact or cause the effect to run again. [S6]

The recovery route derives the same scope from the current validated actor and target; the caller need not know a server-generated receipt to recover after a lost response. A successful lookup returns the original result and original metadata version, which may differ from the resource's current metadata.

A client may explicitly resubmit the same scoped key and unchanged content through the existing coordinator after uncertainty. It must not retry commit, issue a fresh key or infer permission to rerun from NotFound. No SDK silently resubmits an uncertain mutation by default.

RM-07's initial command is synchronous and needs no background operation queue. Future `202 Accepted` APIs must identify genuinely durable admission and a documented recovery path. Admission, target application adoption and actual execution completion must remain different states; a process-memory queue is not durable acceptance.

### 8.4 Audit boundary

Emit an administrative audit event using the actual trusted actor, target, operation reference and selected safe before/after metadata. Stage it through RM-06 in the same transaction. On a matching operation replay, return the existing result without repeating the effect or its success audit.

If required staging fails or marks the original context rollback-only, do not commit the metadata mutation. A later broker outage can delay already-staged delivery under RM-04's existing responsibility and capacity rules; it does not retrospectively undo the commit. Central Management is not the destination audit archive. [S5]

Ordinary heartbeat ticks and unchanged reports are operational observations. They do not produce a normal-level log or an audit record on every tick.

## 9 Portable wire contract

Use UTF-8 JSON with camelCase names. The portable contract is maintained independently of CLR serialization metadata and TypeScript inferred types. The service, both SDKs and examples must conform to the same checked-in schema and behavior fixtures.

| Concern | Required rule |
| --- | --- |
| Identifiers | Opaque bounded strings; UUIDs use one documented canonical form. Never derive authority from an identifier. |
| Application and environment | Preserve RM-01 exact comparison and bounds. Separate trusted values from reported metadata. |
| Revisions, report sequences and generations | Decimal strings for ordered integer values; ETags and opaque cursors remain uninterpreted tokens. Never round-trip them through JavaScript number. |
| Enums | Stable string values. Unknown response values remain explicit unknown values, never successful defaults. |
| Counts and durations | Bounded safe JSON integers; duration field names declare units such as timeoutMs. |
| Timestamps | UTC RFC 3339 strings with Z and the existing CISLIB precision profile. Preserve the original string where submillisecond precision matters. |
| Optional values | Omission and null rules are explicit in each schema. No implicit patch behavior. |
| Errors | HTTP status plus stable code and safe details, separate from durable operation results. |
| Versioning | URL major for incompatible API changes; per-module contract versions where required. SDK package versions do not redefine the protocol. |

Response readers tolerate added optional fields. Request readers reject unknown fields in closed command objects, duplicate keys, malformed Unicode and invalid required values. Unsupported command versions fail explicitly. Unknown inventory module kinds can be preserved as bounded opaque observations without interpreting them or loading code.

These HTTP evolution rules do not relax the existing strict RM-04 messaging envelope or RM-06 audit contract. A future broker adapter must obey its own established codec. [S5, S7]

The initial compatibility fixtures cover an integer string above 2^53, a precise timestamp, non-ASCII text, absent optional data, rejected null, an added response field, an unknown enum, a stale ETag, a matching replay and changed-content conflict. JSON integer interoperability is the reason to retain large values as strings. [S11]

Maintain supported v1 behavior across compatible releases and mixed service/client versions. An incompatible v2 is additive alongside v1; do not silently remove the existing contract. Feature discovery describes actual support, not permission to downgrade authentication.

## 10 Client SDKs

### 10.1 C sharp client

Provide a substitutable `IManagementClient` for the delivered typed API methods and a separate optional registration/inventory host integration. A read-only consumer does not automatically register a process or start a heartbeat.

Compose the existing named/keyed `IRestClient`, RM-01 outbound authentication and current result/cancellation contracts. Preserve actual HTTP status, safe remote problem details, security evidence, original operation identity and known/unknown completion. Do not redesign RestResult or flatten durable facts into a single Success flag. Normal API rejections are structured outcomes; unexpected programming defects retain their existing fault behavior. [S3, S6, S7]

Expose public ports for registration observations and inventory contributions so application code can be tested and adapted. Use DI and the existing startup/heartbeat owners. Do not resolve handlers while merely inspecting a catalog, construct another provider or perform network work in constructors.

Provide bounded scripted management client/observation substitutes through the existing CommonTesting conventions. They exercise client behavior without pretending to prove SQL atomicity, native authentication or broker durability.

### 10.2 TypeScript foundation and management client

Deliver two small reusable packages or equivalent clearly separated modules:

- `client-core`: transport and per-request credential ports, request context, structured result/error mapping, deadline/cancellation coordination, explicit retry selection and safe observation hooks.
- `management-client`: runtime-validated management DTOs, typed methods for section 7 and management-specific receipt/error interpretation.

Keep common TypeScript imports independent of React, Next.js, Node built-ins, process environment, file systems and browser credential storage. Do not install global mutable credentials or retain a user's request context in a singleton client. Reusable clients must support simultaneous calls for different users safely.

Host adapters provide a suitable transport and credential callback. Native fetch is a suitable initial HTTP adapter. Node-specific mTLS, environment reading and provider integration belong behind an explicit server-only entry point; browser consumers must not acquire those imports transitively. Reuse the host's token provider rather than creating an Entra authentication framework in the portable SDK.

TypeScript configuration means explicit client options, not central configuration distribution. Telemetry means small optional request observation hooks, not a port of Logging, Metrics, Heartbeat or Healthcheck. Call status is not remote application health.

Provide a server-side Node consumer and a minimal Next.js integration example showing per-request credentials and safe result projection. The Next.js example may use its native server-only boundary, but production packages do not depend on Next.js. A framework adapter package, login flow and UI are RM-09 work. [S9]

### 10.3 Shared request behavior

Both clients enforce finite request/body bounds and one total deadline budget covering authentication, HTTP, retries and decoding. The default total request timeout is 30 seconds. Deadline or caller cancellation signals the actual transport and credential work, prevents further attempts and cancels retry waits. It does not establish remote rollback or undo accepted work. Owned response and transport cleanup is still joined; a noncooperative provider can delay settlement beyond the requested deadline. Do not abandon work or claim that a canceled wait forcibly completed cleanup.

Preserve C# CancellationToken behavior and use AbortSignal in TypeScript. Platform-native syntax may differ; observable distinctions between cancellation, timeout, expected rejection, unknown mutation outcome and programming fault must remain consistent.

Retries are explicit and disabled by default, preserving current C# REST behavior. A selected read policy permits at most two retries for the documented transient set and bounded Retry-After/backoff, all inside the original deadline. Authentication rejection and authorization denial are not ordinary transient retries.

Registration/report retries reuse their original ID, sequence and content. A subsequent new observation gets a new sequence. Administrative mutations have no automatic replay unless an explicitly selected endpoint-specific policy uses the established operation identity and replay contract. An auth callback or token refresh alone must not trigger replay.

Do not forward credentials to unapproved origins, automatically follow external pagination links or log payloads and tokens. Correlation and existing trace forwarding retain current CISLIB meanings and do not become operation keys. Test substitutes retain bounded records and support deterministic failure, cancellation and unknown-outcome scenarios.

### 10.4 Kotlin readiness

No Kotlin implementation is required in RM-07. Deliver portable fixtures and a short implementation note mapping cancellation, credentials, results, timestamps and large values to a future Kotlin client. A Kotlin implementation must be possible from the HTTP contract and fixtures alone, without linking C#, running Node, sharing a database or copying browser/session assumptions.

## 11 Lifecycle and outage behavior

The C# registration component starts after its configured credential provider and required local services through the current sequential lifecycle. It can report Starting before full application readiness and report the later observed state. Configuration construction must not depend on an already-started management component.

Default behavior is optional operational connectivity: an unavailable management service marks the integration disconnected/degraded and schedules bounded reconnect attempts, while unrelated business work retains its normal readiness policy. A host may explicitly select `RequireInitialRegistration`; then its initial management registration is a startup prerequisite, with a finite deadline and a clear startup failure.

The startup participant performs the initial registration attempt directly. It never waits for its first periodic Heartbeat callback to satisfy required startup. Deadline cancellation still joins the actual attempt and owned cleanup; it is not forced process termination.

Do not automatically make every later management outage a business readiness failure. Applications that require ongoing connectivity choose that health policy explicitly. Existing local authorization, configuration and RM-05 ownership behavior remain authoritative during the outage.

Use one non-overlapping heartbeat/reconnect owner with jitter and finite backoff, initially 5 seconds and capped at 60 seconds. Keep one latest pending observation and at most one pending replacement inventory snapshot. Do not accumulate an in-memory event history or create a local disk spool for presence reports.

Shutdown stops new registration/report admission, signals cancellation and joins actual owned work before dependencies are disposed. Best-effort close must fit the existing REST/Heartbeat teardown order; do not wait indefinitely on an already-closed REST client or reorder the host's accepted drain barriers. A canceled wait is not proof of completed cleanup. [S2, S7]

## 12 Persistence and deployment

Use PostgreSQL as the initial management application's store, reusing the repository's application-owned provider patterns and RM-02/RM-03 integration. This is management state storage; it does not select the destination audit database. Clients do not need database or broker packages.

Persist stable admitted application identities and display metadata, instance registration facts, latest observation/inventory values and sequence/digest state. Use the existing operation journal and Messaging outbox for their respective responsibilities. No separate management idempotency table or audit archive is added.

Registration identity uniqueness, sequence comparisons and metadata ETag updates are enforced transactionally in the shared database. Process-local locks and caches cannot be the authority. Database outages return explicit unavailable/unknown outcomes; do not acknowledge a write retained only in process memory.

Multiple stateless API replicas share that store. No elected management leader is required for ordinary API processing. Cleanup uses bounded, idempotent database work under an existing lifecycle owner; use RM-05 only if an actual exclusive maintenance duty requires it.

Preserve stale/closed registrations for an initial seven-day diagnostic retention period, then clean them in bounded batches. Seven days is measured from the latest accepted observation, or the terminal close time where later. Cleanup removes telemetry; it creates no registration and grants no authority. Unknown reports remain rejected, and clients use a fresh ID for replacement registration. Do not add a permanent tombstone system for disposable presence telemetry. Durable metadata-operation duplicate/result protection is separate and follows RM-03's existing retention behavior; no cleanup job may make an old metadata operation execute again.

Supply one containerized deployment example, a local PostgreSQL setup and documented external dependencies. Production schema migration is an explicit deployment action with a schema-version readiness check; ordinary parallel API startup does not race destructive migrations. Show secret references and trust inputs without embedding real credentials.

Configure `/_health` and `/_metrics` using existing modules. For a certificate-authenticated business listener, use the established restricted operational listener/probe pattern; HTTP middleware cannot bypass a TLS handshake's client-certificate requirement. The operational listener must not expose a business-route bypass. [S3]

The service requires its database and selected identity integrations for relevant APIs. Durable audit submission requires configured RM-06/RM-04 storage and capacity. Live broker availability follows the selected Messaging readiness policy; a committed outbox record is not claimed as destination persistence.

## 13 Bounds and operational observations

Use finite validated limits and preserve inherited identity/module bounds. Defaults may be changed deliberately through startup configuration, within documented finite ceilings. Refusal is visible; truncation must not change identity or silently omit catalog entries.

| Setting | Initial value or rule |
| --- | --- |
| Regular request body | 64 KiB maximum. |
| Inventory replacement body | 4 MiB maximum; 32 modules, 64 categories and 1,024 job definitions by default. |
| JSON depth | 16 by default. |
| Page size | 100 default, 500 maximum. |
| Presence interval and stale threshold | 30 seconds and 90 seconds; validated with room for ordinary report timing. |
| Per-call deadline | 30 seconds by default. |
| Read retries | None by default; selected policy at most two retries. |
| Client pending report state | One latest observation and one replacement inventory; no history queue. |
| Server admission and retained state | Finite configured request concurrency, per-application registrations and total retained registrations; documented defaults and capacity failures are required. |

Total payload limits are separate from per-field Unicode or byte limits. Inventory adapters must preserve the existing Jobs identifier/bound semantics. Larger declared catalogs either use a deliberately larger supported finite bound or fail explicitly; do not silently narrow the underlying catalog.

Reuse native HTTP metrics and existing central application/environment labels. Add only management-specific bounded observations that help operators understand registration, stale instances, rejected inventory and command outcomes. Do not use operation IDs, user IDs, registration IDs or arbitrary payload values as metric labels. The service's own environment label is not replaced by a managed application's reported environment.

Report state transitions and bounded repeated failures. Disable or bound routine per-call reporting for the dedicated frequent-reporting REST client using its existing scoped LogCalls policy. Preserve metrics, actual outcomes and fault handling. Never emit normal-level success logs for every heartbeat tick. [S1, S7]

## 14 Module extension contract

A management module adds explicitly registered typed routes and DTO schemas, capability requirements, authoritative resource resolution, request limits and declared outcome/retry semantics. Registration is passive and frozen with normal host composition.

The service's feature catalog derives from actually registered handlers. A module cannot advertise an action whose handler or required integration is missing. Discovery is bounded metadata, not runtime code loading. Typed endpoint methods may share common infrastructure, but do not expose unrestricted `Execute(string, object)` or caller-selected handler types.

Future configuration APIs can reuse this hosting and authorization pattern in RM-08. Scheduler and rule APIs can add typed commands when their admission and execution contracts exist. If a command crosses processes, reuse existing RM-04 and appropriate RM-03/RM-05 authority rather than inventing another queue, transport envelope or ownership service.

Every future remote command states what acceptance guarantees, which application can execute it, how current authorization is established there, how duplicate delivery is handled and how callers recover its result. Inventory, presence and retained initiator metadata alone satisfy none of those conditions.

## 15 Acceptance criteria

Use the existing native tests and isolated Testcontainers infrastructure. Tests establish the behavior they exercise; scripts and recorded JSON do not prove native TLS/JWT validation, SQL durability, AKS federation or cert-manager issuance. Do not repeat the full qualification of the underlying modules. [S1, S12]

| ID | Observable acceptance |
| --- | --- |
| AC01 | The management service starts and runs independently of CIS and any portal, using the current CISApplication lifecycle and declared application-owned dependencies. |
| AC02 | Valid configured workload profiles can register. Invalid, unmapped or wrong-environment credentials fail. Changing body/header identities cannot register as another application. Native authentication checks and external deployment claims are reported separately. |
| AC03 | Two management replicas handle duplicate registration consistently. New process incarnations remain separate; changed-content reuse and foreign ownership fail. Reported replica IDs never become verified evidence. |
| AC04 | Out-of-order/equal-sequence reports cannot overwrite newer observations, extend presence on replay or reopen a retained closed registration. Reports for purged registrations are rejected without creating a registration. Missing reports become stale without changing RM-05 ownership. |
| AC05 | A representative catalog with hundreds of jobs is displayed with correct categories and per-instance provenance. Invalid/oversized replacement preserves the previous complete catalog. Frequent job transitions coalesce within the reporting rate. Unknown next-run/owner facts remain unknown. |
| AC06 | Listings, direct reads, pages, hints, mutations and result lookups enforce actual resource authorization. Unauthorized resources do not leak through counts, cursors or replay. |
| AC07 | Two real database-backed metadata edits with the same old ETag and different keys cannot both apply. Observation/heartbeat updates do not invalidate the metadata ETag. |
| AC08 | A committed metadata command atomically retains its original result and outgoing RM-06 audit. Required audit staging failure prevents commit. A matching replay creates neither a second change nor a second success audit. |
| AC09 | A controlled lost-response case recovers the original result with the original key. Matching replay with the original ETag succeeds as replay; changed input conflicts. NotFound after an unknown outcome never causes a new key or automatic rerun. |
| AC10 | A different validated operator cannot retrieve another operator's result through a shared portal identity. Forged actor headers confer no authority. A genuinely validated native human/delegated projection preserves the authoritative attribution. |
| AC11 | C# and TypeScript consume shared wire fixtures, retain a value above 2^53 exactly, preserve precise timestamps and handle optional additions/unknown values according to section 9. |
| AC12 | C# and TypeScript make real HTTP calls to the same service for list/detail and metadata mutation/recovery. Normal failures preserve typed codes and uncertainty; cancellation reaches the active operation, prevents further attempts and joins actual cleanup without claiming remote rollback. |
| AC13 | Portable TypeScript imports build for a browser target without Node/React/Next dependencies. Concurrent per-user calls cannot exchange credentials. The server example returns no secret or unrestricted internal DTO. |
| AC14 | Optional management outage/reconnect and RequireInitialRegistration behaviors are distinct. Shutdown joins real work, retained client state remains bounded, and normal heartbeats do not flood logs. |
| AC15 | Database reconstruction/service restart retains metadata, registration sequence protection and original operation results. No process-memory write is reported durable. Public package consumers and CommonTesting substitutes compile. |

Use real PostgreSQL for uniqueness, concurrency, atomicity and recovery checks. Use the existing RabbitMQ fixture for the added audit-delivery integration when exercised; reuse existing transport coverage for unchanged behavior. Selected required infrastructure failures must not turn into passing/skipped integration acceptance. Keep production durability and security settings in provider tests.

Run focused affected checks and relevant package consumers. Record the actual revision, commands, platforms, counts, duration and remaining unexecuted deployment claims. Do not introduce another test runner, broad mutation campaign, repeated unchanged test launches or large evidence archives.

## 16 Implementation order and completion

Implement through the repository's current legitimate CIS workflow when implementation is requested. Preserve existing work, current instructions and completed historical owners. This specification defines the implementation scope; creating it does not claim that code has been implemented or tested.

Use work items no larger than medium, each with its relevant code, focused tests and concise documentation. The practical order is:

1. Freeze the initial portable API and behavior fixtures, including target/actor scope and result semantics.
2. Implement the service host, authentication integration, application store and registration/inventory reads.
3. Implement the metadata command using RM-02/RM-03 and the required RM-06 producer integration.
4. Complete the C# API client and optional registration host integration.
5. Complete the TypeScript foundation/client and server examples against the same contract.
6. Run the focused end-to-end and compatibility checks and finish deployment/consumer guidance.

After the contract is agreed, C# client work, TypeScript client work and independent server areas may proceed in parallel with clear file/build ownership. Do not create separate approval stages for plan, tests and closeout where the current authorized work item already covers them.

The handoff consists of one portable contract and fixture set, one management service, the two usable client implementations, a concise deployment/consumer guide and focused test evidence. Add or update concise module/agent guidance through the repository's established skill workflow and link it from AGENTS.md without duplicating whole manuals.

RM-07 is complete when an authenticated application can register and report to the independent service; authorized C# and TypeScript consumers can inspect the same resources and perform/recover the metadata mutation; and the specified security, durability, lifecycle and compatibility boundaries pass their focused checks. A portal screenshot, unexecuted generated client or in-memory stub is not that completion evidence.

### 16.1 Implementation agent instruction

Implement CISLIB RM-07 from this specification using the current repository and accepted foundations. Keep Central Management as an independently deployed C# CISLIB consumer. Deliver one language-neutral management contract, a C# client and a small portable TypeScript client foundation and management client. Preserve future Kotlin compatibility through protocol and behavior, not copied runtime APIs.

Implement authenticated registration, bounded observations and inventory, explicit typed capability discovery and the metadata command with concurrency, original-result recovery and transactional audit staging. Reuse existing identity, REST, transaction, operations, messaging, coordination, startup and testing contracts. Keep workload identity separate from independently validated human authority.

Respect the RM-08/RM-09 and domain-module boundaries. Do not implement central configuration distribution, portal UI, a full TypeScript runtime, a generic remote command engine or customer synchronization. Use readable code, current dependency pins, existing CommonTesting/Testcontainers patterns and proportionate evidence. Report real completion and remaining dependency/deployment limits without reopening completed historical work.

## 17 Source references

Repository links are pinned to the inspected baseline. The earlier roadmap and TypeScript/Kotlin discussion establish the scope decisions; the external standards below support protocol choices. These inputs do not establish implementation test results.

[S1] [Repository agent guidance](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/AGENTS.md) and [approved C sharp dependency policy](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/architecture/csharp-dependency-policy.md).

[S2] [CISApplication lifecycle and readiness](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/application/core.md) and [Configuration bootstrap](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/configuration/hosting.md).

[S3] [Security integration and native extension](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/security/README.md), [portable security contract](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/contracts/security/README.md) and [security audit hook boundaries](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/security/audit.md).

[S4] [Coordination ownership](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/coordination/README.md), [job definitions and scheduled admission](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/coordination/jobs.md) and [local configuration adoption](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/coordination/configuration.md).

[S5] [RM-06 Audit Contracts and Delivery Integration Specification](https://chatgpt.com/library?file_id=libfile_fde3_f-d3020ff9163c8191bb8841bbdf6118c4_a96453786d9081918df59aa18e3ca617&account_id=personal), version 1.0, 23 November 2026. Producer/outbox integration is required; destination audit persistence is deferred.

[S6] [Explicit local transactions](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/transactions/README.md), [durable operations and recovery](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/operations/core.md) and [bounded operation identity](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/implementations/csharp/src/CIS.Core/Operations/Contracts/OperationIdentity.cs).

[S7] [REST client and ownership](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/rest-client/core.md), [Messaging core](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/messaging/core.md) and [typed request/reply](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/docs/modules/messaging/requests.md).

[S8] [OpenAPI 3.1 specification](https://spec.openapis.org/oas/v3.1.1.html). Language-independent HTTP API descriptions; select a supported pinned toolchain.

[S9] [Next.js data security](https://nextjs.org/docs/app/guides/data-security). Server-side access, safe DTOs and authorization of callable server functions.

[S10] [RFC 9457 Problem Details](https://www.rfc-editor.org/rfc/rfc9457.html) and [RFC 9110 HTTP semantics](https://www.rfc-editor.org/rfc/rfc9110.html). Structured errors, idempotent methods and preconditions.

[S11] [RFC 8259 JSON number interoperability](https://www.rfc-editor.org/rfc/rfc8259.html#section-6).

[S12] [Canonical native and Testcontainers testing guide](https://github.com/andrewspiteri/CISLIB/blob/dcb61dc13c8af138f409db4e13c477cc3a06a0b5/implementations/csharp/tests/README.md).
