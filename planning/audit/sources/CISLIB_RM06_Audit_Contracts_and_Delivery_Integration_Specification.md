# CISLIB RM 06 Audit Contracts and Delivery Integration

Implementation specification  
Version 1.0  
23 November 2026  
Target implementation .NET 10  
Repository baseline eee73d581460a2e20fd406d526b7303070f0bc25

## 1 Purpose and agreed scope

RM-06 gives CISLIB applications a common contract for attributable business, administrative and selected security records, together with integration into the existing identity, transaction and messaging facilities.

Implement a usable producer path and a portable receiving contract. Audit destination persistence is a separate application concern. This specification deliberately leaves its technology, schema, indexes, retention and archive design undefined.

The principal invariant for an application action designated as requiring transactional audit is:

**The application change and its outgoing audit record commit together. A rolled-back change leaves no committed record claiming that change succeeded.**

This guarantee applies when the application explicitly stages the audit through the same supported RM-02 transaction. It does not apply retrospectively to an observation written after an effect.

MUST and MUST NOT identify requirements. SHOULD identifies a preferred approach whose departure needs a concise technical reason. MAY identifies an optional behavior. These terms define implementation behavior, not additional approval stages.

### 1.1 Included work

- A portable, versioned audit envelope and documented event-detail schemas.
- Immutable audit record capture, trusted context enrichment and validation.
- Public producer interfaces for transactional staging and standalone submission.
- Integration with existing RM-02 and RM-04 APIs.
- Explicit adaptation of the existing RM-01 security audit hooks.
- A portable receiver contract and a documented RM-04 receiving pattern.
- Bounded resource behavior, safe operational observations and CommonTesting substitutes.
- Focused conformance and integration checks, one consumer guide and concise agent guidance.

### 1.2 Deferred work

Do not implement an audit database, audit tables or migrations, an audit repository or storage-provider interface, an archive service, historical query APIs, retention or compaction workers, reporting, exports or portal screens.

Do not select a destination database or prescribe its physical schema. Do not add a production audit collector, an audit-specific acknowledgement service, a second outbox/inbox, a retry scheduler, a local disk spool or a new idempotency coordinator.

Storage signatures, hash chains, protected archival media, legal retention rules and compliance certification remain separate requirements. Append-only contract behavior alone establishes none of those properties.

The existing Messaging store continues to persist outgoing audit messages. Reusing that already defined storage is within scope; designing another audit persistence system is outside scope.

## 2 Existing foundations and ownership

The inspected baseline is CISLIB main at the revision on the title page. Implement against the actual selected checkout, preserve subsequent compatible changes and record its real revision. The source references identify the integration contracts used here. [S1–S7]

| Foundation | Required reuse |
| --- | --- |
| Identity and security | Existing local identity, trusted caller projection, subject and instance-evidence semantics, capabilities and security audit hooks. |
| Local transactions | Explicit original participant context and separate Committed, NotCommitted and Unknown completion facts. |
| Durable operations | Existing operation identity and original-result recovery. Audit adds no business replay coordinator. |
| Messaging | Existing event routes, DTO codecs, outbox, submission client, transport, inbox, retry, quarantine and host ownership. |
| Shared coordination | Optional job and occurrence references supplied by the owning application. No hard dependency on an unmerged coordination API. |
| CommonTesting | Bounded substitutes for the public audit ports, with clear limits on what scripted results prove. |

New C# module types belong in the existing CIS.Core package under CIS.Audit, with passive registration helpers under CIS.Audit.Hosting. Follow current repository layout, compatibility and dependency policy. Core must acquire no new third-party runtime dependency for this work. The optional existing RabbitMQ adapter retains its own dependency boundary. [S1]

Applications retain ownership of their data sources, transactions, host and configured external resources. Audit must not create another service provider, capture ambient transaction authority or start workers from a constructor.

## 3 Audit facts and capture policy

Applications select the business actions that require audit. CISLIB supplies consistent structure and trusted enrichment; it cannot infer all business outcomes from HTTP responses, database change tracking or authorization results.

Use stable event names such as ledger.freeze.applied, scheduler.definition.updated and rules.version.activated. These are examples of application events, not requirements to implement those domain modules.

A meaningful request, acceptance and completion can produce separate records linked to one operation or occurrence. Each record describes the fact known at its own boundary. Accepting a job-run command does not establish that the job completed.

The broad v1 categories are business, administration and security. Application-specific job categories and resource classifications belong in event details. No central registry service is required.

Routine heartbeat ticks, lease renewals, polling and publication attempts remain operational observations unless an application explicitly defines a meaningful audit event for them. Required audit records are not sampled or silently suppressed.

### 3.1 Outcomes

The v1 outcome values are accepted, succeeded, rejected, failed, canceled, unknown, allowed and denied. allowed and denied describe authorization decisions. accepted describes admission, not completion. Other values are interpreted with the event type.

A succeeded business-change event staged in the original transaction becomes externally visible only after that transaction commits. A post-effect observation records the owner's reported outcome and does not independently prove a database commit.

Cancellation or timeout alone does not establish rollback or absence of an external effect. Unknown outcomes must remain unknown until evidence resolves them. A later resolution is a new linked record, not an edit of the earlier observation.

## 4 Portable record contract

Publish the language-neutral contract, JSON Schema and a small independent fixture set under contracts/audit/v1 and conformance/audit/v1, following repository conventions. C# types implement this contract; CLR names and assembly identities are not wire discriminators.

An audit record is a UTF-8 JSON object. Field names use camelCase. Required fields are always present; absent optional fields are omitted rather than written as null.

| Field | Requirement |
| --- | --- |
| auditVersion | Required integer 1 for the common envelope. |
| auditId | Required nonempty UUID in hyphenated D form, identifying one immutable audit occurrence. |
| eventType | Required stable name, using the existing Messaging type-name grammar and 128-character maximum. |
| eventVersion | Required positive integer identifying the details schema for this event type. |
| category | Required business, administration or security value. |
| occurredAtUtc | Required UTC time when the source fact occurred or became known. |
| recordedAtUtc | Required UTC time captured when this record was first formed. |
| producer | Required local application and environment; instance information retains its actual evidence classification. |
| actor | Required actor classification and available safe identity facts. Missing authenticated identity is represented explicitly. |
| target | Resource application, environment, type and identifier where known and required by the event definition. |
| outcome | Required value from section 3.1. |
| reason | Optional stable code and safe human-readable explanation or justification. |
| operationReference | Optional projection of the existing RM-03 identity using operationName, scope and key. It grants no authority. |
| correlationId | Optional existing bounded correlation value. It is not an idempotency key. |
| causationAuditId | Optional audit UUID identifying the fact that caused this record. |
| domainEventId | Optional stable reference to the originating domain event. |
| details | Required explicitly defined JSON object for the event type and version. An empty object is valid when its schema permits it. |

Reuse the existing canonical Messaging timestamp profile: UTC with seconds and Z, allowing up to seven fractional digits; the canonical writer emits seven. Neither time is a database commit timestamp or proof of global ordering. A destination may separately retain its receipt time without changing the original record. [S3]

The operation reference uses the actual available operation identity. Do not invent an identity when there is no durable operation, add transport identifiers to business idempotency input or serialize a complete operation result.

Event definitions may require a target, reason, operation reference or specific detail fields. The generic envelope permits a missing target for observations such as failed authentication where none was resolved. Never fabricate a target to satisfy a generic validator.

### 4.1 Identity representation

Keep the local producer separate from the caller who initiated the action. A caller application may differ from the application producing the audit.

The actor kind is user, application, system, anonymous or unknown. user and application identity facts retain the validated subject's issuer or trust relationship and stable identifier. system represents an explicitly identified local application action without inventing a remote credential. anonymous requires an actual anonymous context; otherwise use unknown.

Where established by the existing security projection, retain the initiating human separately from the executing application identity. An application actor may carry a human identity only when the existing security contract established verified delegation.

Instance data preserves NotProvided, Reported and Verified meanings and the distinction between reported and verified identifiers. Do not upgrade process metadata, request headers or an application certificate into proof of a particular human or replica.

The principal nested field names and conditions are:

| Nested object | Fields and validity rules |
| --- | --- |
| producer | application and environment are required. instance is optional. |
| actor | kind is required. subject is required for user/application and absent for system/anonymous/unknown. An optional trusted application/environment pair is either fully present or fully absent, including for application actors. |
| subject and humanSubject | Each contains issuer and identifier together, preserving exact case-sensitive values. humanSubject is separately validated; for an application actor it requires verifiedDelegation. Do not equate a user's provider subject and HumanSubject unless the existing projection did so. |
| actor provenance | Optional profile, authenticationMethod, policyVersion, instance, humanSubject and verifiedDelegation retain the meanings supplied by the trusted projection. verifiedDelegation contains safe bounded provenance, never a credential. |
| instance | Contains kind and optional reportedIdentifier and verifiedIdentifier. notProvided has neither identifier; reported has only reportedIdentifier; verified requires verifiedIdentifier and may also retain reportedIdentifier. Omission means notProvided. |
| target | application and environment are required whenever target is present. resourceType and resourceIdentifier are optional as a pair, matching CapabilityTarget. |
| reason | code is required whenever reason is present; text is optional. |

Selected profile, authentication method and policy version may be retained as bounded safe provenance. Do not serialize CallerContext, ClaimsPrincipal, native tickets, credentials, arbitrary claims or HTTP contexts directly into the wire format. The portable audit schema is independent of the structural security schema. [S2]

### 4.2 Details and compatibility

Use explicit DTO codecs or equivalent explicit field mappings. Do not add arbitrary object-graph serialization or discover all public entity properties automatically.

Amounts that require decimal precision use a documented lossless representation, normally a decimal string plus currency. Preserve the application's existing precision rules; never round-trip financial values through binary floating point. Represent large identifiers as strings where necessary for other language implementations.

The v1 reader rejects unsupported envelope versions, unknown top-level envelope fields, duplicate property names at any nesting depth, malformed UTF-8 or Unicode, comments, trailing input and invalid required values. Producer details follow their explicitly selected versioned schema. A generic receiver preserves structurally valid unknown application details. Do not relax RM-04's existing strict envelope reader to make Audit more permissive.

An incompatible envelope change requires a new auditVersion. An incompatible details change requires a new eventVersion. Optional detail additions are compatible only when that event schema explicitly permits them. Preserve the ability to interpret previously supported records; do not rewrite stored history to upgrade it.

Producer capture validates the explicitly selected event type/version and its detail schema. The generic receiving codec validates the common envelope, bounds and JSON structure, and preserves unknown application event types or event versions opaquely. It must not require a global registry or fetch schemas over the network. Domain consumers may additionally interpret the schemas they support.

The capture boundary must detach mutable detail data. Once formed, a record's ID, source facts, timestamps and encoded content are immutable. The codec retains exact audit payload bytes across decoding and forwarding. For v1 duplicate comparison, the same audit ID requires the same original audit payload bytes; differing content is a conflict. Outer messaging IDs, timestamps and transport metadata are excluded from that comparison.

## 5 Bounds and information handling

Use finite bounds without introducing another resource-management subsystem.

The audit payload default is 256 KiB, including the common envelope and details. An application may select a smaller positive limit or a larger limit up to the existing Messaging maximum of 1 MiB. The effective limit must also fit the selected Messaging route and runtime. Default JSON depth is sixteen, with the existing maximum of thirty-two; count the complete audit payload consistently.

A total record limit may reject an oversized otherwise valid record. Individual security identity fields must nevertheless support the existing security limits and must never be truncated into a different identity:

| Existing identity field | Minimum supported existing bound |
| --- | --- |
| Application and environment | 255 Unicode scalars each. |
| Issuer, subject and resource identifiers | 1024 Unicode scalars each. |
| Profile and authentication-method aliases | 64 Unicode scalars each. |
| Capability | Existing lowercase ASCII grammar, up to 128 characters. |
| Capability collection | Up to 256 entries, preserving the complete observation. |
| Correlation and operation outcome code | Existing 128-character bounds. |
| Policy version | 1024 Unicode scalars. |

Follow the exact existing validator semantics where these fields are projected. A Unicode-scalar bound and an encoded-byte bound are different checks. Reason text is bounded to 512 Unicode scalars; a safe reason code uses the existing 128-character outcome-code bound.

Before/after values are explicit per-event selections. Never include passwords, tokens, private keys, connection strings or unfiltered request/response bodies. Include personal or financial details only when the event's defined purpose requires them. A generic redactor is not a substitute for selecting a safe schema.

Corrections are new records referring to the original. Delivery attempt counts, retry errors, quarantine state and destination timestamps belong to delivery or destination metadata.

## 6 Producer API and source capture

Provide one substitutable public writer with two explicit operations. The following C# surface is the intended shape; adapt naming to repository conventions without changing the behavioral distinction:

```csharp
public interface IAuditWriter
{
    Task<AuditStageResult> StageAsync(
        ILocalTransactionContext context,
        CapturedAuditRecord record,
        CancellationToken cancellationToken = default);

    Task<AuditSubmissionResult> SubmitAsync(
        CapturedAuditRecord record,
        CancellationToken cancellationToken = default);
}
```

Provide a small capture/factory API that builds an immutable CapturedAuditRecord from an explicit event definition, selected details and available trusted context. It exposes the portable AuditRecord and its original bytes. Capture the ID and timestamps once. A retained captured record can be submitted again without recapturing or rewriting it.

AuditRecord is portable data. CapturedAuditRecord is a source-side construction type created only by the trusted local capture or adapter path, without a public conversion from decoded records. Decoders return AuditRecord and cannot manufacture source eligibility. This distinction prevents an externally supplied record with matching producer fields but a forged actor from being passed straight to the producer API. It is not a credential protocol and adds no persistent token registry. Local application/adapter code remains part of the trusted implementation.

The ordinary capture path obtains local identity from existing EnvironmentOptions and caller information from the owning trusted scope. It does not let request payloads override producer identity or authenticated actor facts. Trusted adapters may map an existing security observation without retaining its scope.

An externally decoded AuditRecord is data, not proof that the current process or caller may produce it. Schema validation and matching producer strings do not qualify it for local source publication. Ordinary source submission binds the producer to the configured local application and environment and refuses mismatches. Authorized forwarding retains original content through the established Messaging provenance path rather than impersonating the original source. After restart, pending source delivery resumes from the existing outgoing record; this module adds no API that converts untrusted decoded data into a captured local record.

Capture performs no persistence or network I/O. It must not be described as durable acceptance. When capture occurs inside a business transaction before StageAsync, its failure must cause the owner to abandon or mark that transaction rollback-only. Document this in the safe consumer example; swallowing a capture exception and committing is invalid usage.

An unconfigured writer must not succeed through a logger, a no-op or an in-memory queue. Contract-only consumers may use the DTOs and explicit testing substitutes without registering production delivery.

### 6.1 Transactional staging

StageAsync uses IMessageOutbox.PublishAsync with the original ILocalTransactionContext. At the inspected baseline, the context must be active, non-poisoned, use the configured Messaging database and use ReadCommitted isolation. Preserve these restrictions; do not silently select another owner, isolation or database. [S3, S4]

The writer must not commit, roll back, dispose or retain the caller's owner. The caller must await staging and inspect its actual transaction completion.

Audit validation or encoding failures inside StageAsync must mark the supplied usable context rollback-only before returning a refusal or propagating a fault. This includes failures occurring before the call into Messaging. An ignored audit refusal must not allow an apparently successful mandatory audited commit.

Preserve the original fault when poisoning a disposed or otherwise unusable context also fails. Expected refusals use bounded audit failure codes; programming faults and cancellation retain the existing exception and actual-settlement behavior.

Staging returns Staged only when the underlying outgoing record was staged. The result includes AuditId and the exact available Messaging receipt. Staged proves no commit, broker confirmation or final storage.

### 6.2 Standalone submission

SubmitAsync uses IMessagingClient.PublishAsync and its existing short transaction ownership. Do not implement another transaction coordinator.

Use it for independent observations such as a denial, a confirmed failure after another transaction rolls back, or an existing post-effect hook. Its completion remains separate from the outcome of any business action.

| Submission outcome | Required meaning |
| --- | --- |
| OutboxCommitted | The existing submission transaction is known committed. |
| NotSubmitted | The submission is known not to have committed, including refusal before opening an owner. |
| Unknown | Commit may have occurred; retain the actual unknown completion and available receipt. |

Preserve AuditId, MessageId, OutboxRecordId and actual TransactionCompletion where available, with safe failure information and cleanup facts. Do not flatten these results into an ambiguous Success flag or return AlreadyRecorded, Archived or StoredByReceiver without evidence.

Do not automatically repeat an unknown submission, retry commit or repeat its associated business action. Reuse the established receipt/operation recovery facilities where applicable. A missing later read does not establish rollback.

## 7 Delivery integration

Register one explicit event route for the common AuditRecord envelope and its codec, using the existing stable MessageContract and application-selected bounded route alias. Application event types belong inside the audit payload; do not create one Messaging route per business action.

Use a stable contract identifier such as cis.audit.record with schema version 1, documenting the chosen exact value in the portable contract and conformance vectors. Validate the complete passive registration graph and required database/route identity.

Audit operation references remain inside the audit payload. Ordinary RM-04 event envelopes prohibit MessageSendOptions.Operation; do not misuse that field to carry audit business context. Correlation may be copied to its supported diagnostic field.

Publish through the existing host-owned RM-04 runtime. Preserve its buffer admission, retries, blocked/quarantined responsibility, native confirmation, fencing and real shutdown settlement. Add no timer, background loop or audit scheduler.

The existing transport retry preserves AuditId, MessageId and exact bytes. A broker outage delays delivery while already committed work remains recoverable. Configured capacity refusal must remain visible and must follow the transaction behavior in section 6. Existing protection and cleanup rules continue to govern outgoing Messaging responsibility; this module adds no retention policy.

### 7.1 Audit identity and message identity

Each public RM-04 publication call currently allocates a new MessageId and OutboxRecordId. The receiving inbox protects the logical subscription and MessageId. It does not index an AuditId inside the payload. [S4]

Consequently:

- A retry of an existing outgoing record preserves both identities.
- Two deliberate SubmitAsync calls with the same immutable audit record create separate messaging envelopes.
- RM-06 does not promise durable source-side uniqueness or conflict detection by AuditId.
- The future audit consumer must enforce AuditId duplicate and conflict behavior across distinct MessageIds.
- Correlation IDs, job IDs and operation IDs must not be substituted for an audit occurrence ID.

A replayed RM-03 business result must not rerun the audited business-effect path and create another succeeded event. Applications may separately define a retrieval or repeated-attempt observation when that action matters.

### 7.2 Acknowledgement boundaries

Keep Staged, OutboxCommitted, broker Confirmed and receiver Processed separate. None proves a later milestone. RabbitMQ publisher confirmations and consumer acknowledgements are independent mechanisms. [S3, S8]

Producer-facing RM-06 completion stops at its proven source submission state. Do not add a final-archive status lookup or application receipt exchange in this version.

## 8 Receiver contract with persistence deferred

Reuse IMessageHandler<AuditRecord> and MessageHandlerContext.Transaction as the receiving integration. A separate audit-specific consumer runtime or generic IAuditStore is unnecessary.

Define the following obligations for a future durable audit consumer, without implementing its storage:

1. Establish current source authority through RM-01/RM-04 trusted ingress. Bind the claimed producer application/environment to the established original producer provenance or the explicitly authorized forwarding path. An authenticated application must not claim another producer merely by changing payload fields. Recorded actors remain provenance and grant no receiving authority.
2. Validate the supported common audit envelope, bounds and details JSON structure. Preserve structurally valid unknown application eventType/eventVersion values and exact details bytes; interpreting every application's schema is not a prerequisite for generic durable collection.
3. Retain the immutable record or secure an explicitly equivalent durable handoff.
4. Treat the same AuditId and same audit payload as a repeated occurrence; detect conflicting content without replacing the original.
5. Make its acceptance and applicable inbox disposition atomic when they share the existing local transaction.
6. Permit acknowledgement only after the required responsibility is known committed.
7. Preserve unresolved or rejected responsibility through the existing failure path; never acknowledge and silently discard a record.

For a receiver using a different durability boundary, document that boundary and its failure semantics in its future provider specification. RM-06 must not imply a distributed transaction across arbitrary storage systems.

Do not register a default production receiver that consumes and discards records. A test-only probe may demonstrate route, codec and transactional delivery behavior; its result establishes no archive durability.

No physical store, table, index, retention duration, query contract or archival receipt is selected by these receiver obligations.

## 9 Security audit bridge

Preserve existing ISecurityAuditSink signatures and record constructors. Register an explicit RM-06 adapter rather than changing the meaning of every existing security sink. Applications that do not select it retain current behavior. [S2]

Map AuthorizationCompletedAsync to a versioned security authorization-decision event. Preserve the final composite decision, full capability set, actual resolved target, correlation and established caller where available. Missing caller or target after failed authentication remains missing. An allowed decision is not a business-completion record.

Map OperationCompletedAsync to a versioned reported-operation observation. Retain Completed, Failed or Canceled meaning and the safe outcome code. Do not relabel Completed as independently verified database commit or receiver delivery.

The adapter allocates a new AuditId and captures time for each hook invocation. Because the old hook has no occurrence timestamp, the bridge timestamp describes when it observed the supplied fact. Correlation does not deduplicate separately invoked hooks.

The adapter uses standalone submission and returns normally only when that submission is OutboxCommitted. Known refusal and Unknown remain observable as explicit bridge failures with safe audit identity and result context. Preserve original cancellation and fault behavior. Do not silently turn an audit-hook fault into an authorization denial or a successful audit receipt.

### 9.1 Existing post-effect observations

The existing CountryData operation hook and Messaging administration hooks execute after their effects or transaction settlement. Their adapter can durably submit that observation, but cannot make it atomic with the earlier change. A capture gap remains if the process stops between those boundaries. [S2, S5]

Preserve an already committed administrative effect even if its subsequent hook fails. Do not make a caller blindly repeat redrive or maintenance to repair audit submission.

This work does not retrofit every existing administrative owner with pre-commit hooks. New application-owned mandatory audited changes use StageAsync before commit. Any future retrofit of a particular owner must be a separately identified integration change.

### 9.2 Registration and recursion

Provide explicit opt-in registration, documented before AddCisSecurity. If another real ISecurityAuditSink is already registered, reject ambiguous automatic replacement; the application may explicitly choose a composite or replacement. Do not silently discard its sink.

Avoid scoped-service capture by a singleton adapter. Resolve capture in the actual owning scope or use a valid lifetime that consumes only the supplied immutable observation and explicitly owned dependencies.

Automatic audit observation must not reproduce itself through internal submission or through authorization of the configured audit-ingress subscription. Otherwise each consumed audit could create another audit with a fresh ID. A local reentrancy guard alone does not prevent that cross-delivery loop.

Recognize this narrow infrastructure path using trusted configured subscription metadata or the actual configured target and capabilities. Suppress only the recursive automatic observation. Continue authentication, capability checks and resource authorization; a denied internal request remains denied. Never accept a suppression flag from a message body/header, suppress ordinary business auditing or introduce a global disable switch.

A security hook can run while a Messaging operation is already admitted or while an RM-03 caller holds an active database transaction. Standalone submission must not wait indefinitely for a buffer, connection or database lock that its outer operation retains. Prefer nonblocking bounded admission for shared runtime resources and use a finite cooperative submission deadline covering connection acquisition, SQL waits and actual submission.

The bridge option submissionTimeout defaults to five seconds and accepts a positive configured value up to one minute. An earlier caller deadline/cancellation still wins. Request cancellation at expiry and await actual provider and transaction settlement; do not detach work or claim rollback merely because the deadline elapsed. Preserve an actual committed outcome if it was established, otherwise expose known refusal or Unknown accurately. This is a bound on requested execution time, not a promise that every provider can be forcibly stopped.

Test both direct Operations use with an outer transaction retaining the required connection/resource and nested Messaging use. Capacity refusal or deadline failure remains observable to the hook owner. Do not increase global limits to conceal the composition problem.

## 10 Business outcomes and domain integration

For required transactional audit, local capture/staging failure prevents commit. Failure of the remote broker or audit destination after source commit does not rewrite the business result.

A business rejection may commit its operation result and rejection audit together while making no requested business change.

After a confirmed rollback, an independent failure observation uses a fresh standalone submission. Complete persistence failure cannot guarantee capture of every attempted request. An application needing durable evidence of admission before execution must explicitly secure an intent/admission record first; do not silently add that policy to all operations.

External HTTP/provider effects remain outside a local database transaction. Where required, secure intent first, record the actual observed result and preserve uncertainty. Audit does not add compensation or remote cancellation guarantees.

Ledger change events retain their domain meaning and required financial precision. Audit attribution may accompany or reference them. Historical facts required after ledger compaction must be present in the exported immutable financial event; a later consumer must not depend on rereading a mutable or deleted source row. This is an application integration obligation, not an audit archive implementation.

Shared coordination references may include job category, job definition, occurrence, worker and ownership version where meaningful. Delivery of already committed audit records continues under Messaging ownership after a job lease is lost. No global or per-ledger ordering guarantee is added; use business versions and causal references when sequence matters.

## 11 Hosting and operational behavior

Registration is passive. It validates/captures configuration without opening databases, installing schemas, creating broker topology, resolving scoped handlers or starting work. Selected production delivery requires a valid existing Messaging runtime and route; fail configuration clearly rather than register a throwing placeholder or successful no-op.

Use the existing Startup and host ownership model. Stop new capture/submission admission, join actual admitted operations and allow Messaging to perform its established drain before disposing dependencies. Cancellation requests do not permit abandoning borrowed bytes or native operations.

Reuse Messaging's owned-buffer and concurrency limits. Account any additional library-owned transient encoding copies before allocation. Public application-owned record objects are not evidence of a global application-heap bound; document that distinction.

Use existing Logging, Metrics and Healthcheck integration for safe capture failures and delivery status. Reuse Messaging backlog/age/failure observations rather than duplicate them. Any new audit-specific counter uses finite outcome/category labels and the existing central application/environment labels.

Never use AuditId, subject, resource, correlation, operation key or arbitrary event detail as metric labels. Audit payloads do not appear in normal diagnostic logs. Routine success, polling and retry activity produces no repeated normal-level log stream.

Operational telemetry failure must not change a known commit, authorization decision or audit receipt. Keep audit capture itself distinct from optional telemetry about that capture.

## 12 Verification and acceptance

Acceptance is based on the behavior below. Group related assertions in focused cases; this table does not require one new fixture or test project per row.

| ID | Required observable result |
| --- | --- |
| AC01 | Portable valid records round-trip with preserved IDs, timestamps, safe identity facts and exact decimal representation. Unknown application event details remain opaque and intact. Unsupported common-envelope versions, duplicate JSON keys and malformed input are rejected. |
| AC02 | Mutating caller-owned details after capture cannot change the retained record or outgoing bytes. Boundaries reject oversized data without truncating identities. |
| AC03 | Verified/reported instances and issuer-qualified subjects remain distinct. A user header cannot create a verified human; decoded data cannot become a captured source record. Forged producer binding fails receiving validation. Existing valid security identifier bounds and optional caller-application pairs remain representable. |
| AC04 | Through the real PostgreSQL fixture, a business probe and audit outgoing record commit together; rollback or unfinished-owner disposal leaves neither committed. |
| AC05 | An audit-specific staging refusal or fault poisons the original usable context even when it occurs before Messaging and the caller ignores the returned refusal. Staging never completes or substitutes the owner's transaction. |
| AC06 | Standalone results preserve known commit, known noncommit and Unknown, their receipts and actual completion. No result claims broker confirmation or archival storage. |
| AC07 | Through existing PostgreSQL/RabbitMQ fixtures, a committed audit record is delivered using the real route/codec after source runtime reconstruction. Retry preserves the original message, audit identity and bytes. |
| AC08 | Two deliberate submissions with one AuditId remain separate RM-04 envelopes; the producer does not claim source-side audit deduplication. Portable fixtures express the future receiver's identical/conflicting-content obligations without claiming or shipping persistence enforcement. |
| AC09 | A denied authorization produces the correct decision observation, permits missing caller/target and creates no fictional operation completion. All capabilities and relevant safe provenance survive mapping. |
| AC10 | The explicit security adapter reports unsuccessful or uncertain standalone submission. A post-effect hook fault does not turn the earlier committed effect into rollback. Existing custom/unselected sinks remain compatible. |
| AC11 | Internal submission and successive audit-ingress deliveries do not generate recursive audits; authorization still runs and denial remains denial. Nested Messaging admission and an outer Operations transaction retaining a needed database resource settle or fail observably under the cooperative deadline. No refusal claims memory-only durable acceptance. |
| AC12 | CommonTesting substitutes and the public-package consumer compile against public contracts. Registration is passive, shutdown joins admitted work and Core's dependency boundary remains intact. |

Use ordinary MSTest and the existing owned Testcontainers infrastructure. Service tests carry RequiresDocker and the actual PostgreSql/RabbitMq categories, preserve production durability/security settings and fail clearly when selected infrastructure is unavailable. Follow the canonical testing guide and result guard. [S6]

Use scripts for outcome orchestration, real PostgreSQL for atomicity and actual RabbitMQ for the new delivery integration. Scripted receipts do not prove persistence. A test-only receiving probe does not prove archive storage. Label simulated lost-completion seams accurately.

Do not rerun the entire Messaging qualification, introduce another test runner, add a mutation/performance campaign or accumulate large evidence collections for this change. Reuse existing foundation coverage and add checks for the new audit boundaries. Report the actual target revision, commands, platforms, counts, duration and any unexecuted claims.

## 13 Documentation and implementation handoff

Keep documentation proportional:

- Maintain one portable contract and schema with a small fixture set.
- Write one consumer guide showing explicit StageAsync ownership, standalone submission, security-hook limitations and the deferred receiver responsibility.
- Include one compiling producer example and a focused receiver integration example in the established examples/fixtures structure. Preserve CountryData's existing database-free default; a durable sample belongs in the existing provider/package fixture.
- Add a bounded ScriptedAuditWriter in CIS.CommonTesting. It can return configured results and record safe immutable metadata; it must not claim SQL or broker behavior.
- Add concise repository agent guidance for CISLIB Audit following the current skill workflow, and link it from AGENTS.md. Explain source versus transport identity, transaction ownership, security provenance, payload selection and the persistence exclusion. Update affected security and messaging links without duplicating their full guides.

Import this specification through the repository's current CIS process when implementation begins. Create one coherent change with work items no larger than medium and acceptance tied to this specification. Keep implementation, focused tests, documentation and review together where they serve the same outcome. Parallelize only independent contract/codec, integration and testing work that will not contend for the same files or build outputs.

Use current session authorization and the repository's legitimate work ownership. Do not reopen historical completed tasks or add redundant approval stages. Preserve current user changes and accepted compatibility contracts.

The implementation report must distinguish authored contracts, passing source/integration checks and deferred destination persistence. RM-06 is complete when the producer and existing delivery integration satisfy this specification; the report must not claim a production audit archive, archival durability, historical search or retention capability.

### 13.1 Implementation agent instruction

Implement CISLIB RM-06 using this specification as the scope boundary. Read the current repository and relevant Security, Transactions, Operations and Messaging guidance before editing. Reuse their public contracts and ownership rules.

Deliver portable audit contracts, immutable capture, a public writer with transactional and standalone paths, an explicit compatible security bridge, and integration through the existing Messaging event/outbox path. Define the receiver's obligations and provide focused conformance/examples without implementing audit persistence.

Preserve staging versus commit versus broker/receiver completion, Unknown outcomes, authoritative caller attribution and the distinction between AuditId and MessageId. Preserve existing post-effect hook semantics and document their capture gap. Use existing CommonTesting and Testcontainers fixtures for the accepted behaviors.

Keep code readable, retain .NET 10 and current approved dependencies, use impact-focused verification, update the concise consumer/agent guidance and report actual results and remaining limits. Resolve routine implementation choices within the agreed scope; do not expand into the deferred audit storage application.

## 14 Source references

Repository references are pinned to the inspected baseline. These are design inputs, not a claim that RM-06 has already been implemented or tested.

[S1] Repository guidance and dependency policy  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/AGENTS.md  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/architecture/csharp-dependency-policy.md

[S2] Security contracts and audit hook boundaries  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/modules/security/README.md  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/modules/security/audit.md  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/implementations/csharp/src/CIS.Core/Security/Contracts/SecurityAudit.cs
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/implementations/csharp/src/CIS.Core/Security/Contracts/SecurityIdentity.cs
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/implementations/csharp/src/CIS.Core/Security/Contracts/SecurityAuthorization.cs

[S3] Messaging portable contract and publication ownership  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/contracts/messaging/README.md  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/modules/messaging/publication.md

[S4] Actual message creation and client results  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/implementations/csharp/src/CIS.Core/Messaging/Runtime/MessageOutbox.cs  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/implementations/csharp/src/CIS.Core/Messaging/Contracts/MessageClientContracts.cs

[S5] Receiving and post-settlement administrative hooks  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/modules/messaging/dispatch.md  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/modules/messaging/failure-handling.md

[S6] Canonical testing and Testcontainers guidance  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/implementations/csharp/tests/README.md

[S7] Local transaction ownership and outcomes  
https://github.com/andrewspiteri/CISLIB/blob/eee73d581460a2e20fd406d526b7303070f0bc25/docs/modules/transactions/README.md

[S8] RabbitMQ acknowledgement boundaries  
https://www.rabbitmq.com/docs/confirms

