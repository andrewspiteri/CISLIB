---
title: "RM-06 audit contracts and existing delivery integration"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0023
outcome: "Deliver the versioned portable Audit envelope, immutable trusted source capture, original-transaction staging and standalone submission over existing Messaging, compatible opt-in Security hooks, bounded host/resource ownership, public testing substitutes and focused producer/receiver conformance without audit destination persistence."
baseline_kind: git
baseline: "dcb61dc13c8af138f409db4e13c477cc3a06a0b5"
graph_build_id: "sha256:ed5e60e7cad52824bc52cd1b05c6c420810153513692fb708e053b549a56950b"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:f1678aa2773f9becbb673909f8a986ea9f1ba9cdb8ff022e67ccac659adce39e"
authority: derived-outcome-authorization
---

# CIS-0023: RM-06 audit contracts and existing delivery integration

## Outcome

Deliver the versioned portable Audit envelope, immutable trusted source capture, original-transaction staging and standalone submission over existing Messaging, compatible opt-in Security hooks, bounded host/resource ownership, public testing substitutes and focused producer/receiver conformance without audit destination persistence.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

The preserved source's AC01–AC12 define the outcome. The proposed profile and
task definitions are a draft interpretation, not an approval record.

| Source acceptance | Bounded owner and required evidence |
| --- | --- |
| AC01–03 | WORK-001: strict portable exact-byte codec/schema/vectors, immutable trusted capture, native identity semantics and source eligibility. |
| AC04 | WORK-004: real original-transaction PostgreSQL business/outgoing commit, rollback and unfinished disposal. |
| AC05–06 | WORK-002 logical writer proof and WORK-004 native poisoning/receipt/completion proof. |
| AC07–08 | WORK-004 actual existing RabbitMQ delivery after source reconstruction, stable retry bytes/IDs, distinct deliberate submissions and portable future-receiver conflict obligations. |
| AC09–10 | WORK-003 complete native-hook mapping and observable bridge failure/post-effect semantics. |
| AC11 | WORK-003 logical recursion/deadline contracts plus WORK-004 real outer Operations/nested Messaging pressure and ingress integration. |
| AC12 | WORK-001 public contracts/substitute, WORK-002 passive actual host ownership and WORK-004 outside-friend/example/dependency proof. |

## Imported requirements and reconciliation

Source: [RM-06 v1.0](../../../planning/audit/sources/CISLIB_RM06_Audit_Contracts_and_Delivery_Integration_Specification.md),
SHA256 `1dba943444b4228d086d765152aff61c2197d9a0cb422dbe4c607394cad2a402`.
Its inspected `eee73d58` baseline is reconciled against actual merged `dcb61dc1`;
see [contract reconciliation](reconciliation.md).

The source supplies requirements and no independent agent execution authority.
Use the [four-task draft plan](implementation-plan.md),
[proposed scope/profile](proposed-authorization.json),
[proposed task contracts](proposed-tasks.json) and
[shared allocation](proposed-allocation.json). Native product execution awaits one
fresh approved outcome/allocation under existing repository controls. Older
completed work remains closed.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM06-2026-11-23-USER-APPROVED-P01-P16-24h28`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Implement RM06 portable Audit contracts, immutable trusted source capture, transactional and standalone publication through existing Messaging, compatible opt-in Security bridge, bounded ownership/observations and focused native/public consumers with receiver persistence explicitly deferred.

- `capture-contracts`: Portable v1 schema/vectors, strict exact-byte codec, source eligibility/trusted immutable capture and bounded public Audit testing ports. Paths: `.artifacts/audit`, `.cis/local/audit-intake`, `conformance/audit`, `contracts/audit`, `docs/catalog.yml`, `docs/changes/CIS-0023`, `docs/modules/audit`, `docs/modules/common-testing.md`, `implementations/csharp/src/CIS.CommonTesting/Audit`, `implementations/csharp/src/CIS.CommonTesting/README.md`, `implementations/csharp/src/CIS.Core/Audit`, `implementations/csharp/tests/CIS.Core.ArchitectureTests/Contracts/AuditContractTests.cs`, `implementations/csharp/tests/CIS.Core.ArchitectureTests/Contracts/FixtureSchemaValidator.cs`, `implementations/csharp/tests/CIS.Core.UnitTests/Audit`, `implementations/csharp/tests/CIS.Core.UnitTests/CommonTesting/ScriptedAuditWriterTests.cs`, `planning/audit`.
- `native-consumers`: Focused existing PostgreSQL/RabbitMQ atomicity/reconstruction/pressure proof, public package and opt-in CountryData consumers, one guide plus agent skill/routing and final review. Paths: `.artifacts/audit`, `.cis/local/audit-intake`, `.github/skills/cislib-audit`, `AGENTS.md`, `README.md`, `conformance/audit`, `contracts/audit`, `docs/catalog.yml`, `docs/changes/CIS-0023`, `docs/modules/audit`, `docs/modules/common-testing.md`, `docs/modules/messaging/dispatch.md`, `docs/modules/messaging/publication.md`, `docs/modules/messaging/testing.md`, `docs/modules/security/audit.md`, `docs/references/test-suite-profile.md`, `implementations/csharp/examples/CountryData.UnitTests/Audit`, `implementations/csharp/examples/CountryData/Audit`, `implementations/csharp/examples/CountryData/README.md`, `implementations/csharp/src/CIS.CommonTesting/Audit`, `implementations/csharp/src/CIS.CommonTesting/README.md`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Audit`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessagingPackageConsumerTests.cs`, `planning/audit`.
- `publication-hosting`: Original-context producer writer and standalone results using existing Messaging; actual Audit admission/drain, passive native/facade composition and bounded existing observations. Paths: `.artifacts/audit`, `.cis/local/audit-intake`, `contracts/metrics/audit.md`, `docs/catalog.yml`, `docs/changes/CIS-0023`, `docs/modules/audit`, `docs/modules/messaging/hosting.md`, `implementations/csharp/src/CIS.Core/Application`, `implementations/csharp/src/CIS.Core/Audit`, `implementations/csharp/src/CIS.Core/Coordination/Hosting/CoordinationHostLifetime.cs`, `implementations/csharp/src/CIS.Core/Coordination/Hosting/CoordinationHostedService.cs`, `implementations/csharp/src/CIS.Core/Heartbeat/Hosting/HeartbeatHostingExtensions.cs`, `implementations/csharp/src/CIS.Core/Messaging/Hosting/MessagingHostingExtensions.cs`, `implementations/csharp/src/CIS.Core/RestClient/Hosting/RestHostedService.cs`, `implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs`, `implementations/csharp/src/CIS.Core/Workflows/Hosting/PipelineHostedService.cs`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsCollectionContext.cs`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsHostingOrder.cs`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsProviderActivation.cs`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsServiceCollectionExtensions.cs`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsSettings.cs`, `implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsStreamCatalog.cs`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Audit`, `implementations/csharp/tests/CIS.Core.UnitTests/Audit`, `planning/audit`.
- `security-bridge`: Explicit compatible Security audit adapter, versioned detached observations, fixed trusted ingress/retry suppression, cooperative five-second submission boundary and safe failures. Paths: `.artifacts/audit`, `.cis/local/audit-intake`, `conformance/audit`, `contracts/audit`, `docs/catalog.yml`, `docs/changes/CIS-0023`, `docs/modules/audit`, `docs/modules/security/audit.md`, `implementations/csharp/src/CIS.Core/Audit`, `implementations/csharp/tests/CIS.Core.UnitTests/Audit`, `planning/audit`.

- Actual baseline dcb61dc13c8af138f409db4e13c477cc3a06a0b5 includes merged RM05, jobs corrections and VisualStudio fixture solution membership. Preserve them and the user-owned untracked CountryData launchSettings.json.
- Audit v1 closed envelope, cis.audit.record Event/schema1 and one selected bounded route; unknown application details remain structurally valid opaque data with exact original bytes. Decoded AuditRecord is not a source-eligible CapturedAuditRecord.
- Bridge timeout defaults5seconds positive through60seconds, includes capture/admission/connection/SQL submission, respects earlier caller cancellation and awaits actual provider/transaction settlement. Preserve late established commit; no forced completion, retry or global capacity increase.
- Conditional audit.capture.results counter with finite3category x3outcome combinations and existing central app/environment identity; reuse Messaging backlog/age/failure health/metrics, quiet bounded diagnostics, no payload/subject/resource/AuditId labels, histories or duplicate delivery metrics.
- Core single package/assembly CIS.Audit with CIS.Audit.Hosting, .NET10 SDK10.0.401 NET/ASP10.0.12 and existing dependency closure. No new NuGet package/source mapping/pin/runtime/solution project or production dependency is selected.
- Existing counted physical ceiling6442450944bytes includes67108864protected reserve,2147483648Docker allowance, retained150994944native/cache charge and declared roots. At most4newlyownedcontainers inclRyuk; preserve foreign resources/user files, clean only exact new Audit-owned scratch/services after actual drain and retain compact results/hashes.
- Four coherent tasks no larger than medium include their tests/docs/review. Freeze Task001 public contracts before parallel Task002 writer/host and Task003 bridge source work; root serializes SDK/shared outputs and native verification. Reuse existing Testcontainers/public package/schema evaluator fixtures; no broad Messaging/mutation/performance/release campaign.
- Implement P01-P16 in implementation-plan.md and AC01-AC12. Audit destination persistence, tables/migrations/indexes, store/provider APIs, archive/query/retention/export, collectors, acknowledgement services, new inbox/outbox/retry/spool/idempotency coordinators and compliance claims are excluded.
- One approved shared86400active seconds28logical attempts includes2700restoration seconds and all already-performed intake/agents/native checks/docs/review. Controller may organize and redistribute only within approved totals; no previous completed owner is reopened or costs/attempts reset.
- Preserve native Security scalar limits/full256capabilities/issuer and delegation distinctions. ProcessInstanceId is Reported; null caller is Unknown. Full security correlation remains payload data; optional outer correlation is omitted when it cannot fit Messaging byte semantics.
- Producer route registration occurs before Messaging graph freeze, delivery/host selects only existing valid runtime/database/route, constructors perform no I/O. Stop/join Audit actual operations before Messaging disposal, preserve RM05 final draining job audit and sequential runtime startup; amend only necessary known cooperating lifecycle paths.
- Receiving examples bind claimed Audit producer to authenticated original envelope producer; fixed TargetResolver is not an initial-only check. Existing schema evaluator may gain only minItems/maxItems and focused regression to enforce the native256capability bound; no new validator engine/package.
- Security bridge is explicit before AddCisSecurity, does not replace a real sink implicitly, maps detached immutable hook observations through singleton-safe dependencies, preserves post-effect outcomes/capture gaps, and returns normally only for proven OutboxCommitted.
- Standalone IMessagingClient.PublishAsync maps only real NotSubmitted, OutboxCommitted and Unknown with actual receipt/completion/cleanup; reject inconsistent NotConfirmed, never auto-replay unknown or claim receiver/archive storage.
- Suppress only automatic observation for exact trusted configured Audit ingress target plus complete configured ingress/retry capability sets; keep real authentication/authorization/denial. No header/body switch or global disable. Existing failed Messaging native authentication emits no audit hook.
- Use existing native MessagingResourceBudget nonblocking admission for additional library-owned encoding copies before allocation. No public admission API/new budget is selected. Enforce256KiB/depth16 defaults, up to1MiB/depth32 and actual route payload/depth limits; retain outer metadata checks and original four-envelope reservation. Returned captured objects are application-owned, not a global heap guarantee.
- Use original IMessageOutbox.PublishAsync transaction without retaining/completing owner. Audit pre-Messaging staging refusal/fault poisons usable original context while preserving first/original fault; caller must abandon/poison if capture itself fails.
- User explicitly approved CIS0023 P01-P16 and four-task plan, shared24activehours28logicalattempts including2700restorationseconds, existing6GiB/64MiB reserve and2GiBcountedDocker. Task organization/distribution delegated to Codex; attached requirements are not independent authority.
- Windows native PostgreSQL/RabbitMQ Audit proof and actual Ubuntu managed unit lane are selected. New Linux-client service proof is claimed only if actually run on an authorized available engine; unavailable selected services fail rather than skip. No new Windows key-container exception, existing database access, cloud proof, remote push/merge/deployment or release is included.

The total is 86,400 active seconds and 28 logical attempts, including 2,700 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
