# CISLIB RM 07 Central Management Service and Client

Implementation specification  
Version 1.1  
23 November 2026  
Service and C sharp target .NET 10 with current repository pins  
Repository baseline dcb61dc13c8af138f409db4e13c477cc3a06a0b5

## 1 Purpose and delivery decision

RM-07 provides an independently hosted Central Management application and reusable CISLIB clients. Authenticated applications register their running instances and report bounded management information. Authorized consumers discover applications, inspect their reported state and use explicitly implemented management APIs.

The service remains a C# application built with CISLIB. The language boundary is a versioned HTTP and JSON contract. Deliver a C# management client and the first small TypeScript client foundation and management client against that contract. Future Kotlin Multiplatform clients must be able to implement the same protocol without reproducing .NET classes, hosting or dependency injection.

The client SDK family has three layers: common client facilities, API-specific clients and host integrations. RM-07 implements the facilities needed for management now. It does not port CIS or the complete CISLIB runtime to TypeScript.

A successfully bootstrapped managed application retains its last accepted state when Central Management becomes unavailable. It can settle already admitted work under that state and its remaining valid authority, but must close admission for new work. A new or restarted managed process requires live authoritative bootstrap. Multiple management replicas are supported through an explicitly selected shared backend; single-instance deployment remains available.

MUST and MUST NOT identify requirements. SHOULD identifies a preferred approach whose departure needs a concise technical reason. MAY identifies an optional behavior. These terms do not create additional approval stages.

### 1.1 Included work

- An independently deployable management service with authenticated HTTP APIs and durable application-owned storage.
- Application and environment identity resolution, instance registration, last-seen observations and explicit capability discovery.
- Required managed bootstrap, last-known-state continuity and a small reusable gate for new-work admission during management outages.
- A provider-neutral management backend boundary with standalone and optional shared deployment modes.
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

The service is a CISLIB consumer with its own executable, deployment, selected persistence provider and operational configuration. Reusable client contracts belong in CISLIB. Server repositories, provider migrations and product-specific handlers do not belong in the lightweight client or Core runtime. Shared backend use is optional for one management instance and required for multiple concurrent management replicas.

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

The managed execution integration is disabled until explicitly selected. Once selected, missing or invalid local configuration is a startup error, and live registration plus authoritative bootstrap are mandatory before new business work is admitted. Installing only a read-only API client does not select managed execution. An unreachable management service keeps a new process unready and puts an already bootstrapped process into the continuation behavior in section 11.

The management service does not require registration with itself to start. Its selected backend, security configuration and operational listeners are available through ordinary deployment configuration.

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

The client reports a latest observation, normally every 30 seconds, using a non-overlapping completion-based loop. The observation includes a monotonically increasing per-registration sequence, reported lifecycle/health summary and source observation time. The server stores receipt time using the selected provider's documented cross-replica time policy; SQL database time is one implementation, not a portable API requirement.

Sequences are positive decimal strings on the wire and are independent for each registration and report stream. They are not a global synchronization cursor. Higher sequences replace the latest value; lower sequences cannot overwrite it. An equal sequence with identical content is a replay; an equal sequence with different content is a conflict. A replay does not extend last-seen time.

Presence is calculated from the latest accepted receipt and the configured stale interval, initially 90 seconds. Represent active, stale and explicitly closed states. Stale means the service has not received a timely observation; it does not prove a process is dead, unhealthy, stopped or unable to access its business database. This display-oriented threshold is separate from the shorter admission-freshness bound in section 11.

Keep presence, reported application health and authoritative job ownership separate. Missing management reports MUST NOT release or transfer an RM-05 lease. A healthy management connection does not grant ownership. [S4]

An explicit close is terminal while that registration is retained. Late reports cannot reopen it. An observation or inventory request for an unknown or purged registration is rejected and never creates a registration implicitly. Graceful host shutdown may send a bounded best-effort close while transport admission permits; otherwise presence expires normally. A close is an application report of registration shutdown, not proof that every business callback physically stopped.

### 5.3 Reconnection and replica changes

After a transient outage, reuse the current retained registration ID and send a new latest observation. If the server reports that registration absent after retention, create a fresh ID and explicitly register the still-running process again. A conflicting or closed registration is not silently recreated. Registration/report duplicate protection is a bounded telemetry guarantee for retained records; RM-07 does not promise indefinite rejection of historical registration-creation requests after their records have been purged. Each newly admitted registration still requires current authentication and trusted self-identity checks.

Re-establishing a registration or TCP connection does not reopen new-work admission. The client must complete the authoritative control exchange and reconciliation described in section 11. Existing last-known state remains available while that gate stays closed.

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
| GET /registrations/{registrationId}/control | Confirm current bootstrap/admission prerequisites against the selected authoritative backend; return a bounded freshness confirmation. |
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

All business routes require the appropriate selected native authentication profile. Service-info requires a caller admitted by the selected management policy. Protected data is not exposed through anonymous diagnostic routes. The control endpoint additionally enforces the registration's trusted self-identity binding, returns Cache-Control: no-store and confirms that authoritative backend access succeeded. It cannot issue fresh confirmation using only a replica's cached state.

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

For the initial PostgreSQL provider, use the existing RM-02 transaction factory and RM-03 durable operation coordinator. The command's metadata update, original-result journal and RM-06 outgoing audit record participate in the owner's original transaction. The application owner commits once after the complete operation returns. Other complete backend providers must preserve this same atomic outcome through compatible provider integrations, as required in section 12. A separate Cosmos/Redis write beside a PostgreSQL journal or outbox is not an equivalent transaction. Do not add another generic operation coordinator or use a deduplication cache as durable result authority. [S5, S6]

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

Provide a substitutable `IManagementClient` for the delivered typed API methods and a separate optional managed-execution host integration. A read-only consumer does not automatically register a process or start a heartbeat. Selecting managed execution installs registration, control confirmation, inventory reporting and a small substitutable admission-state port. Its gate starts closed and is checked at existing job, queue and effect-producing API admission boundaries; it adds no scheduler or work queue.

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

### 11.1 Required bootstrap

Selecting managed execution makes a live authenticated bootstrap mandatory for every new process, including a restart with a locally retained snapshot. Until registration, authoritative control confirmation and required module preparation succeed, the process admits no business work and publishes no business readiness. Its restricted operational listener may report AwaitingBootstrap and liveness.

The admission gate is closed from construction onward, including while other startup participants are being built or started. Arrange existing sequential startup so credential preparation and management bootstrap precede work-producing participants. Perform the first exchange directly; never wait for an inactive Heartbeat callback. Use finite attempt deadlines and existing retry/host failure handling without executing work while waiting. The earlier optional RequireInitialRegistration behavior is superseded for managed execution.

Bootstrap endpoint, credentials and trust remain independently available as required by section 3. A retained snapshot may help recovery but cannot authorize an offline new instance. The management service itself is a separate host and does not depend on successfully registering with itself.

### 11.2 Continue the last accepted state

An already bootstrapped application retains the last complete state accepted by each owning module. A failed refresh does not install defaults, erase valid configuration, enable disabled work or expose a partially received update. Cached state keeps its original revision and provenance. An optional durable local cache must be atomically written, validated and protected according to its contents; it remains recovery data and never becomes fresh bootstrap or admission authority.

On management unavailability, the existing process enters ContinuingOnly. It stays alive so admitted work and earlier durable obligations can settle. New-work readiness closes independently of liveness and diagnostic availability. Do not use management loss as a reason to kill the process or cancel every healthy in-flight transaction.

| Activity | Behavior while management is unavailable |
| --- | --- |
| Already admitted finite callback, batch or transaction | Continue or settle with its captured settings while its independent authorization, lease, fence and deadline remain valid. |
| Existing configuration and permitted read-only/status queries | Continue from the last accepted state where normal authorization, existing host routing/readiness policy and required data dependencies allow it. |
| Already committed outbox delivery or original-result recovery read | Continue as settlement or inspection of an existing obligation, subject to the owning module's requirements. |
| Another queue item, scheduled occurrence or effect-producing HTTP operation | Defer or reject before work admission. An HTTP route cannot bypass the rule merely because it is not a background job. |
| Next finite iteration of a continuous job | Treat as new work when it would pick up another item/batch; an existing duty is not unlimited permission to start new iterations. |
| New duty acquisition, partition assignment or ownership takeover | Block until current management confirmation and the separate ownership requirements are satisfied. |
| New or restarted application process | Remain AwaitingBootstrap and not business-ready, even with cached state. |

Define the finite admission boundary in the existing worker/API adapter. Check the gate before claiming new work and again at the actual admission point where necessary. Prefetched but unadmitted messages remain under the existing broker's defer/requeue ownership; do not acknowledge and discard them. Admission checks add no generic scheduler or message queue. A retry that might create a new effect also needs admission; a recovery read does not.

Last-known settings do not extend credentials, RM-05 leases or transaction deadlines. Renewals needed to retain an already-held lease may follow RM-05 while its authority is reachable. If that lease is lost or expires, existing cancellation and fencing still apply. Do not reacquire or transfer ownership during ContinuingOnly. A management report is never a fencing token. [S3, S4]

### 11.3 Detect loss and bound admission freshness

A silent network partition cannot be detected at the instant it starts. Define the guarantee in terms of observed failure and a finite freshness window, separately from the 90-second presence display threshold.

The authenticated control response includes authorityId, registrationId, an opaque controlRevision, admissionAllowed, validForMs and the echoed per-attempt identifier. It confirms the server consulted the current authoritative management state and that management-level prerequisites permit admission. It does not grant business capabilities, assign a job or replace an RM-05 lease.

The initial control-check interval is five seconds, with a maximum confirmation lifetime of fifteen seconds. These are explicit finite deployment settings. Measure usable freshness conservatively from the request's local monotonic start time, not its response arrival, so a slow response cannot create a new full window. Suspend/resume or an unmeasurable timing gap requires revalidation before admission. Late responses from a previous registration or older attempt cannot reopen a newer closed/stopping gate.

Close new admission on the first failed/refused authoritative exchange, freshness expiry, stopping or loss of a required local prerequisite. Check expiry at each new-work admission, independently of timer scheduling. A ping, HTTP process health response, presence upload or successful TCP connection does not refresh this permission.

This gives a documented maximum silent-failure detection window, not a promise of zero admissions from the exact instant connectivity disappears. A use case needing a fresh decision for every admission must make that authoritative round trip; it still cannot make two separate network operations atomic. No longer offline grace period or unlimited cached permission is silently introduced.

### 11.4 Reconcile before reopening

Use the small lifecycle states AwaitingBootstrap, Ready, ContinuingOnly, Reconciling and Stopping. A successful reconnection enters Reconciling. Reauthenticate, validate the current registration and management authority, read current control revision, reconcile the required state through its owning modules, and only then reopen admission. A rejected or incomplete update keeps the prior usable state and the gate closed.

The authorityId identifies the persisted management namespace and stays the same across its API replicas. An unexpected authority change is not ordinary replica failover and cannot silently reopen the existing registration. Explicit migration/rebootstrap establishes the new binding before admission resumes.

RM-07 defines this lifecycle and the control confirmation. RM-08 still owns configuration publication/distribution and the concrete configuration adoption protocol; its later integration must use this reconciliation boundary. An admitted invocation retains its existing immutable execution snapshot even if a newer revision becomes available.

Use the existing non-overlapping Heartbeat/lifecycle owner for control checks and slower presence/inventory reporting. Reconnect backoff starts at five seconds and is capped at sixty seconds; admission remains closed during backoff. Keep one latest observation and one pending replacement inventory. Routine success creates no normal-level log stream.

Shutdown closes admission, signals cancellation and joins actual owned work before dependency disposal. Best-effort registration close must fit the existing REST/Heartbeat order. Deadline cancellation does not prove cleanup finished, and a late successful control response cannot reopen a stopping application. [S2, S7]

## 12 Management backend and deployment

### 12.1 Standalone and shared modes

The management application selects a persistence provider through a small application-level backend contract. The public HTTP contract and clients do not depend on that product. Redis, Cosmos DB and PostgreSQL are provider targets; support depends on the concrete adapter and its documented guarantees.

| Deployment mode | Requirement |
| --- | --- |
| Standalone | One management process uses one authoritative durable store. No multi-instance/shared deployment is required. The store may be private or dedicated to that process; acknowledged administrative changes, results and audit intent must survive its restart. |
| Shared | Multiple management API instances use the same explicitly configured authoritative backend and management namespace. This mode requires a provider qualified for concurrent access and its declared failover behavior. |

Optional shared backend does not mean optional persistence or production in-memory durability. PostgreSQL remains the first complete reference adapter because it fits the current transaction/operation/outbox integrations; it can be used in a dedicated standalone deployment or shared by multiple API replicas. This revision does not require an extra embedded-store implementation or three production adapters before that first delivery. Report the actual delivered provider set; contracts and examples alone do not qualify an adapter.

All replicas in a shared deployment use compatible schema/protocol and the same authoritative namespace. No sticky sessions are required. Do not deploy independently writable private stores behind one shared management endpoint. Startup must reject shared mode with a provider that lacks the required capability profile. Database replication/election belongs to the backend deployment; RM-07 adds no consensus service.

### 12.2 Required backend operations

The backend boundary expresses the management operations actually required: authoritative control lookup, unique registration binding, monotonic observation updates, atomic complete-snapshot visibility, conditional metadata mutation, original-result arbitration/recovery and durable outgoing audit responsibility.

Preserve known Committed, NotCommitted and Unknown outcomes. Conditional comparisons and operation-key arbitration must work across replicas. Each complete provider must make the metadata effect, original result and outgoing audit intent atomic in one supported boundary. Arbitrary CRUD, a distributed lock or a successful cache write is insufficient.

Reuse the existing operations and delivery contracts and compatible provider integrations. The PostgreSQL implementation uses the original RM-02 transaction. A non-SQL implementation must supply the equivalent provider integration before claiming complete mutation support; it cannot combine an independent remote state write with a PostgreSQL journal/outbox and call that atomic. Do not add a second generic idempotency coordinator or management-specific command queue.

A limited adapter may expose transient presence/cache features, but it must advertise that limited role and cannot be selected as the complete authoritative provider. Required capability combinations are validated before serving business APIs.

### 12.3 Provider profiles

| Provider | Required qualification |
| --- | --- |
| PostgreSQL | Use the current transaction integration. Authoritative decisions use the primary or a qualified consistent read path. The deployment profile defines replication, durable commit and safe failover; asynchronous replicas do not automatically preserve every acknowledged write. [S13] |
| Cosmos DB for NoSQL | Define a partitioning and consistency profile that supports authoritative decisions. Atomically related metadata/result/audit-intent records must fit one supported transaction boundary, normally the same logical partition in one container. Supply compatible Operations/Messaging adapters; do not pretend this is an ADO.NET transaction. [S14] |
| Redis | Useful as shared transient state/cache. A complete authority needs an explicitly qualified persistence and failover profile. Ordinary replication plus WAIT can still lose acknowledged writes during failover and does not establish strong consistency. Such a deployment cannot advertise the full durable-authority guarantee; use a qualified durable authority or stop/reconcile rather than promote uncertain state. [S15] |

The declared supported failure model must preserve acknowledged control decisions and durable operation protection before the service resumes admission. If recovery may have restored older state, withhold fresh control confirmations, bootstrap and mutation success until authoritative reconciliation establishes a safe state. Do not invent a weaker profile silently to keep the API green.

Provider transaction limits are separate from HTTP limits. For example, Cosmos transactional batches currently have a 2 MB payload and 100-operation limit, so the API's 4 MiB inventory limit cannot be assumed to fit one batch. Either advertise/reject against a smaller effective provider bound or stage immutable chunks and atomically publish the completed version pointer. Never expose a partial inventory; chunking is an adapter choice, not a new synchronization protocol. [S14]

### 12.4 Backend outages and replica loss

Loss of one management API replica need not stop the service when another replica can reach the same authority. Clients reconnect through the configured DNS endpoint and reconcile before reopening their gate. Operation keys, versions and outcomes survive the replica change.

A replica that cannot reach or trust its authoritative backend must reject new bootstrap, fresh admission confirmations and authoritative writes. It must not fall back to its cache, process memory, another independently writable store or a different provider. Clearly marked stale diagnostic reads may remain available where explicitly permitted; they cannot be mistaken for current control state or operation-result authority.

The shared backend is itself a dependency whose availability must be designed. Adding API replicas alone does not remove that dependency. Distinguish process liveness, authoritative API readiness and client admission readiness in health reporting.

### 12.5 Retention and deployment assets

Persist stable application identities, display metadata, retained registration bindings, sequence/digest state and latest observations/inventory through the selected provider. Required results and outgoing audit responsibilities remain with the compatible operation/outbox integrations. This does not select an audit destination database or create an audit archive.

Retain stale/closed registration diagnostics for an initial seven days, measured from the latest accepted observation or later close time, then clean them in bounded batches. This telemetry cleanup grants no authority and cannot remove durable operation duplicate protection. Unknown reports remain rejected and replacement registrations use fresh IDs. Do not add permanent tombstones for disposable presence telemetry.

Supply standalone and shared deployment examples for the delivered provider, plus its concurrency, consistency, failover and capacity settings. Provider schema migration is an explicit deployment action; parallel API startup checks compatibility rather than racing destructive migrations. Show secret references and trust inputs without real credentials.

Use existing /_health and /_metrics. Certificate-authenticated business listeners retain the restricted operational listener/probe pattern and no business-route bypass. Durable audit submission requires the configured RM-06/RM-04 storage and capacity. Live broker availability follows the selected delivery policy; a committed outgoing record is not destination audit persistence. [S3, S5]

## 13 Bounds and operational observations

Use finite validated limits and preserve inherited identity/module bounds. Defaults may be changed deliberately through startup configuration, within documented finite ceilings. Refusal is visible; truncation must not change identity or silently omit catalog entries.

| Setting | Initial value or rule |
| --- | --- |
| Regular request body | 64 KiB maximum. |
| Inventory replacement body | 4 MiB maximum; 32 modules, 64 categories and 1,024 job definitions by default. |
| JSON depth | 16 by default. |
| Page size | 100 default, 500 maximum. |
| Presence interval and stale threshold | 30 seconds and 90 seconds for management observations, separate from admission freshness. |
| Control confirmation interval and lifetime | Five-second checks and at most fifteen seconds of freshness; observed failure closes admission immediately. |
| Per-call deadline | 30 seconds by default. |
| Read retries | None by default; selected policy at most two retries. |
| Client pending report state | One latest observation and one replacement inventory; no history queue. |
| Server admission and retained state | Finite configured request concurrency, per-application registrations and total retained registrations; documented defaults and capacity failures are required. |

Total payload limits are separate from per-field Unicode or byte limits and selected provider transaction limits. Advertise the effective accepted inventory bound in service-info. Inventory adapters must preserve the existing Jobs identifier/bound semantics. Larger declared catalogs either use a deliberately larger supported finite bound or fail explicitly; do not silently narrow the underlying catalog.

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
| AC14 | A new or restarted managed process, including one with a local snapshot, starts with admission closed and cannot become business-ready without live authoritative bootstrap. Initial bootstrap does not wait for Heartbeat activation. Shutdown joins real work and reporting remains bounded. |
| AC15 | Reconstruction through the selected durable provider retains metadata, retained registration sequence protection and original operation results. No process-memory write is reported durable. Public package consumers and CommonTesting substitutes compile. |
| AC16 | During management loss, an admitted finite operation can settle and accepted configuration remains intact, while the next queue claim, job iteration, scheduled occurrence and effect-producing API operation are deferred. Existing outbox obligations may settle; expired/lost RM-05 authority still fences work. |
| AC17 | Observed failure closes admission immediately and a silent partition closes it within the configured freshness bound. An expired, delayed, out-of-order or previous-registration response cannot reopen the gate; reconnect alone is insufficient. |
| AC18 | After one management replica is lost, another using the same healthy backend provides the same current revision and original operation result. Clients reconcile before reopening. A replica isolated from its backend cannot bootstrap, renew admission or write using a local fallback. |
| AC19 | Each delivered complete provider enforces the same concurrent-update, atomic effect/result/audit-intent and duplicate-recovery contract under its declared failure profile. Unsupported shared/atomicity combinations fail startup. An uncertain restored authority cannot silently resume admission from older state. |
| AC20 | Standalone deployment does not require shared-mode infrastructure and retains acknowledged durable state across restart. Shared deployment uses one authority. Provider size limits reject visibly or preserve atomic snapshot publication; no partial inventory becomes visible. |

Use real PostgreSQL for the reference provider's uniqueness, concurrency, atomicity and recovery checks. Each additional delivered provider runs the same semantic cases against appropriate real infrastructure; an emulator or a PostgreSQL pass does not prove another provider's production failover guarantees. Use the existing RabbitMQ fixture for added audit-delivery integration when exercised and reuse unchanged transport coverage. Selected required infrastructure failures must not become passing/skipped acceptance. Keep production durability and security settings in provider tests, with small controlled outage cases rather than a broad failure campaign.

Run focused affected checks and relevant package consumers. Record the actual revision, commands, platforms, counts, duration and remaining unexecuted deployment claims. Do not introduce another test runner, broad mutation campaign, repeated unchanged test launches or large evidence archives.

## 16 Implementation order and completion

Implement through the repository's current legitimate CIS workflow when implementation is requested. Preserve existing work, current instructions and completed historical owners. This specification defines the implementation scope; creating it does not claim that code has been implemented or tested.

Use work items no larger than medium, each with its relevant code, focused tests and concise documentation. The practical order is:

1. Freeze the portable API, backend capability boundary and behavior fixtures, including outage admission, target/actor scope and result semantics.
2. Implement the service host, authentication integration, reference provider and registration/control/inventory APIs in standalone and shared modes.
3. Implement the metadata command using RM-02/RM-03 and the required RM-06 producer integration within the selected compatible atomic boundary.
4. Complete the C# API client and selected managed-execution integration with mandatory bootstrap, last-known-state continuation and admission checks at existing work boundaries.
5. Complete the TypeScript foundation/client and server examples against the same contract.
6. Run the focused end-to-end and compatibility checks and finish deployment/consumer guidance.

After the contract is agreed, C# client work, TypeScript client work and independent server areas may proceed in parallel with clear file/build ownership. Do not create separate approval stages for plan, tests and closeout where the current authorized work item already covers them.

The handoff consists of one portable contract and fixture set, one management service, the two usable client implementations, a concise deployment/consumer guide and focused test evidence. Add or update concise module/agent guidance through the repository's established skill workflow and link it from AGENTS.md without duplicating whole manuals.

RM-07 is complete when an authenticated application can bootstrap and report to the independent service; retain accepted state and settle admitted work through a management outage while refusing new admission; and reconcile safely through a healthy management replica. Authorized C# and TypeScript consumers must inspect the same resources and perform/recover the metadata mutation. The delivered provider must pass its standalone/shared and declared durability checks. Report undelivered adapters separately. A portal screenshot, unexecuted generated client or in-memory stub is not that completion evidence.

### 16.1 Implementation agent instruction

Implement CISLIB RM-07 from this specification using the current repository and accepted foundations. Keep Central Management as an independently deployed C# CISLIB consumer. Deliver one language-neutral management contract, a C# client and a small portable TypeScript client foundation and management client. Preserve future Kotlin compatibility through protocol and behavior, not copied runtime APIs.

Implement authenticated registration and required bootstrap, bounded observations/inventory, control freshness and a minimal new-work admission gate. Preserve the last accepted state and independent fencing/credential rules during outages; block new work and new-instance startup until authoritative reconciliation succeeds. Deliver a provider-neutral backend boundary with standalone and optional shared modes, the first complete reference provider and honest capability profiles for Redis, Cosmos DB and PostgreSQL. Implement typed discovery and the metadata command with concurrency, original-result recovery and atomic audit staging. Reuse existing identity, REST, transaction, operations, messaging, coordination, startup and testing contracts. Keep workload identity separate from independently validated human authority.

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

[S13] [PostgreSQL standby and synchronous replication](https://www.postgresql.org/docs/current/warm-standby.html). Replication and failover durability depend on the selected deployment profile.

[S14] [Cosmos DB transactional batches and limits](https://learn.microsoft.com/en-us/azure/cosmos-db/transactional-batch) and [consistency levels](https://learn.microsoft.com/en-us/azure/cosmos-db/consistency-levels). Atomic partition boundaries, limits and authoritative read behavior require explicit provider design.

[S15] [Redis WAIT consistency limits](https://redis.io/docs/latest/commands/wait/) and [Redis persistence](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/). Replication acknowledgements and persistence settings do not by themselves establish the full management authority contract.
