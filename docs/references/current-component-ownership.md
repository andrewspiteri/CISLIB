---
title: "Current implementation component ownership"
type: component-sheet
status: Active
scope: Repository
owner: CISLIB maintainers
last_reviewed: "2026-11-24"
review_cadence: on accepted module boundary or implementation location change
cis:
  stable_id: cislib:projection:current-component-ownership
---

# Current implementation component ownership

This is a derived implementation projection of accepted module contracts through
RM-08. The [approved component sheet](component-sheet.md) remains unchanged,
including its four stable `TI-MOD-DOC-*` identities, approved bundle and source
baseline. Those definitions retain their meaning. The locations below add
explicit current paths without changing that approval.

Later module meanings come from the accepted contracts and implementation plans
linked in each row. Existing public module/package names serve as stable projection
identities; these rows do not introduce requirements, a new runtime package or a
new solution-design approval. Verification projects remain development dependencies.
Structural discovery alone does not establish ownership.

## Component catalogue

| Component ID | Name | Classification | Responsibility | Owns | Must not own | BRD authority |
| --- | --- | --- | --- | --- | --- | --- |
| `CIS.Core` | Core package and shared text helpers | Package boundary | Retain one Core assembly and its shared value/text support. | Core package metadata and shared helpers. | Application resources and independent module state. | [CIS-BR-001](../specs/business-requirements.md) |
| `CIS.Heartbeat` | Heartbeat | Existing logical module | Run the accepted local scheduler and drain owned callbacks. | Heartbeat contracts, runtime and adapters. | A general lifecycle or business retry framework. | [Heartbeat](../modules/heartbeat/README.md) |
| `CIS.Startup` | Startup | Existing logical module | Execute the accepted sequential startup coordination. | Startup stages and coordinator. | Heartbeat runtime ownership. | [Startup](../modules/startup/core.md) |
| `CIS.Configuration` | Configuration | Existing logical module | Load and reconcile retained typed roots under existing acceptance rules. | Configuration catalogs, paired snapshots, readers and events. | Central publication authority or a new DI framework. | [Central acceptance](../modules/configuration/central-snapshots.md) |
| `CIS.Metrics` | Metrics | Existing logical module | Provide bounded module observations independent of collectors. | Core metric catalog and measurements. | Collector SDK lifecycle. | [Metrics](../modules/metrics/core.md) |
| `CIS.RestClient` | REST client | Existing logical module | Perform the accepted native HTTP calls with explicit ownership. | Core client, HTTP content, tracing and observations. | Application resources or optional retry policy ownership. | [REST](../modules/rest-client/core.md) |
| `CISApplication` | CISApplication facade | Existing composition facade | Compose existing hosting contracts and join failed-Build cleanup. | CIS.Hosting facade and application builder. | A second provider or module runtime authority. | [Facade](../modules/application/core.md) |
| `CIS.Workflows` | Workflows | Existing logical module | Preserve distinct pipeline and state-machine execution semantics. | Workflow contracts, reports and scope settlement. | Transaction commit authority or a new scheduler. | [Pipeline](../modules/workflows/pipeline.md), [state machine](../modules/workflows/statemachine.md) |
| `CIS.Security` | Security | Existing logical module | Bind application identity, capabilities and native transport policy. | Core identity and authorization contracts. | External identity-provider or certificate issuance ownership. | [Security](../../contracts/security/README.md) |
| `CIS.Transactions` | Local transactions | Existing logical module | Own one explicit local transaction and actual cleanup facts. | Local contexts, guarded native participation and persistence outcomes. | Ambient transactions or automatic replay. | [Transactions](../modules/transactions/README.md) |
| `CIS.Operations` | Durable operations | Existing logical module | Retain original-key durable operation and recovery semantics. | Operation contracts and original journal results. | New-key retries after an unknown result. | [Operations](../../contracts/operations/README.md) |
| `CIS.Messaging` | Messaging | Existing logical module | Preserve reliable delivery through the existing durable participants. | Core outbox/inbox and delivery contracts. | Broker resource ownership or an Audit destination store. | [Messaging](../../contracts/messaging/README.md) |
| `CIS.Coordination` | Shared coordination | Existing logical module | Preserve bounded shared coordination contracts and ownership. | Core coordination APIs and observations. | A general orchestration service. | [Coordination](../../contracts/coordination/README.md) |
| `CIS.Jobs` | Jobs | Existing logical module | Preserve bounded job execution through existing coordination. | Job contracts and execution observations. | Central scheduling or application-owned resources. | [RM-05](../changes/CIS-0022/implementation-plan.md) |
| `CIS.Audit` | Audit | Existing logical module | Capture trusted immutable observations through existing delivery. | Core Audit contracts and submission facts. | Audit destination persistence and conflict enforcement. | [Audit](../modules/audit/README.md) |
| `CIS.Management` | Central Management clients | Existing logical module | Preserve native management authority, reporting and central configuration client boundaries. | Core management and configuration distribution clients. | Service SQL authority, rollout policy or remote CLR construction. | [Management](../modules/management/README.md) |
| `CIS.CommonTesting` | CommonTesting | Existing development package | Provide the existing bounded public substitutes and recorders. | Public development helpers. | Production service or module lifecycle ownership. | [Accepted follow-up](../changes/CIS-0012/implementation-plan.md) |
| `CIS.Metrics.OpenTelemetry` | OpenTelemetry integration | Existing optional package | Adapt the Core metric contract to its selected collector. | Optional collector integration. | Core module behavior. | [Collection](../modules/metrics/collection.md) |
| `CIS.RestClient.Resilience` | REST resilience integration | Existing optional package | Apply the selected optional resilience contract. | Optional REST resilience adapter. | A second Core retry owner. | [Retry](../modules/rest-client/resilience.md) |
| `CIS.Security.Certificates` | Certificate integration | Existing optional package | Provide the accepted native certificate/TLS integration. | Optional certificate provider. | Trust-store installation or external issuance authority. | [Security](../../contracts/security/README.md) |
| `CIS.Security.Entra` | Entra integration | Existing optional package | Provide the selected native identity-provider adapter. | Optional Entra SDK integration. | Application or external federation authority. | [Security](../../contracts/security/README.md) |
| `CIS.Messaging.RabbitMQ` | RabbitMQ integration | Existing optional package | Adapt reliable delivery to the accepted broker contract. | Optional RabbitMQ adapter. | Broker deployment and Core durability authority. | [Messaging](../../contracts/messaging/README.md) |
| `CIS.Management.Service` | Central Management service | Existing product application | Own the accepted central authority and RM-08 publication operations. | Management application and qualified backend. | Consumer typed validation, portal or automatic rollout. | [RM-07](../changes/CIS-0024/implementation-plan.md), [RM-08](../changes/CIS-0027/implementation-plan.md) |
| `@cislib/client-core` | TypeScript client core | Existing portable client | Preserve native portable HTTP transport and cleanup. | Existing TypeScript client-core package. | C# runtimes or new remote authority. | [REST contract](../../contracts/rest-client/README.md) |
| `@cislib/management-client` | TypeScript Management client | Existing portable client | Preserve portable management and configuration distribution operations. | Existing TypeScript Management client. | Server authorization or local typed C# roots. | [Portable Management](../../contracts/management/README.md) |
| `CountryData` | CountryData tutorial | Existing example consumer | Demonstrate current explicit composition and public module use. | The one maintained tutorial application. | Test fixtures or production module state. | [CountryData](../../implementations/csharp/examples/CountryData/README.md) |
| `CIS.Core.UnitTests` | CIS.Core.UnitTests | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |
| `CIS.Core.IntegrationTests` | CIS.Core.IntegrationTests | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |
| `CIS.Core.ArchitectureTests` | CIS.Core.ArchitectureTests | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |
| `CIS.Core.BusinessCases` | CIS.Core.BusinessCases | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |
| `CIS.Core.CompatibilityTests` | CIS.Core.CompatibilityTests | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |
| `CIS.Security.IntegrationTests` | CIS.Security.IntegrationTests | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |
| `CIS.Core.TestSupport` | CIS.Core.TestSupport | Existing verification project | Own the existing native checks or shared fixture support. | This development project's implementation. | A shipping Core module or release qualification claim. | [Native tests](../../implementations/csharp/tests/README.md) |

## Implementation and verification locations

One row declares one exact file or directory prefix. A longer explicit path owns
its contained implementation; equal-specificity conflicts require resolution.
A `verified-by` declaration identifies applicable checks, never an execution result.

| Component ID | Relationship | Path |
| --- | --- | --- |
| `TI-MOD-DOC-2ED774F339B1` | owns | implementations/csharp/src/CIS.Core/Logging |
| `TI-MOD-DOC-2ED774F339B1` | owns | implementations/csharp/src/CIS.Core/Environment |
| `TI-MOD-DOC-D7BF0EECC9BE` | owns | implementations/csharp/src/CIS.Core/TypeManagement |
| `TI-MOD-DOC-2083BB5AF8F0` | owns | implementations/csharp/src/CIS.Core/Healthcheck |
| `TI-MOD-DOC-C62609274AA1` | owns | implementations/csharp/eng/CIS.TypeManager.Build |
| `TI-MOD-DOC-C62609274AA1` | owns | implementations/csharp/build/type-manager |
| `CIS.Core` | owns | implementations/csharp/src/CIS.Core |
| `CIS.Heartbeat` | owns | implementations/csharp/src/CIS.Core/Heartbeat |
| `CIS.Startup` | owns | implementations/csharp/src/CIS.Core/Startup |
| `CIS.Configuration` | owns | implementations/csharp/src/CIS.Core/Configuration |
| `CIS.Metrics` | owns | implementations/csharp/src/CIS.Core/Metrics |
| `CIS.RestClient` | owns | implementations/csharp/src/CIS.Core/RestClient |
| `CISApplication` | owns | implementations/csharp/src/CIS.Core/Application |
| `CIS.Workflows` | owns | implementations/csharp/src/CIS.Core/Workflows |
| `CIS.Security` | owns | implementations/csharp/src/CIS.Core/Security |
| `CIS.Transactions` | owns | implementations/csharp/src/CIS.Core/Transactions |
| `CIS.Operations` | owns | implementations/csharp/src/CIS.Core/Operations |
| `CIS.Messaging` | owns | implementations/csharp/src/CIS.Core/Messaging |
| `CIS.Coordination` | owns | implementations/csharp/src/CIS.Core/Coordination |
| `CIS.Jobs` | owns | implementations/csharp/src/CIS.Core/Jobs |
| `CIS.Audit` | owns | implementations/csharp/src/CIS.Core/Audit |
| `CIS.Management` | owns | implementations/csharp/src/CIS.Core/Management |
| `CIS.CommonTesting` | owns | implementations/csharp/src/CIS.CommonTesting |
| `CIS.Metrics.OpenTelemetry` | owns | implementations/csharp/src/CIS.Metrics.OpenTelemetry |
| `CIS.RestClient.Resilience` | owns | implementations/csharp/src/CIS.RestClient.Resilience |
| `CIS.Security.Certificates` | owns | implementations/csharp/src/CIS.Security.Certificates |
| `CIS.Security.Entra` | owns | implementations/csharp/src/CIS.Security.Entra |
| `CIS.Messaging.RabbitMQ` | owns | implementations/csharp/src/CIS.Messaging.RabbitMQ |
| `CIS.Management.Service` | owns | implementations/csharp/applications/CIS.Management.Service |
| `@cislib/client-core` | owns | implementations/typescript/packages/client-core |
| `@cislib/management-client` | owns | implementations/typescript/packages/management-client |
| `CountryData` | owns | implementations/csharp/examples/CountryData |
| `CIS.Core.UnitTests` | owns | implementations/csharp/tests/CIS.Core.UnitTests |
| `CIS.Core.IntegrationTests` | owns | implementations/csharp/tests/CIS.Core.IntegrationTests |
| `CIS.Core.ArchitectureTests` | owns | implementations/csharp/tests/CIS.Core.ArchitectureTests |
| `CIS.Core.BusinessCases` | owns | implementations/csharp/tests/CIS.Core.BusinessCases |
| `CIS.Core.CompatibilityTests` | owns | implementations/csharp/tests/CIS.Core.CompatibilityTests |
| `CIS.Security.IntegrationTests` | owns | implementations/csharp/tests/CIS.Security.IntegrationTests |
| `CIS.Core.TestSupport` | owns | implementations/csharp/tests/CIS.Core.TestSupport |
| `TI-MOD-DOC-2ED774F339B1` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/CommonWriterPipelineTests.cs |
| `TI-MOD-DOC-2ED774F339B1` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/LoggingReviewRegressionTests.cs |
| `TI-MOD-DOC-D7BF0EECC9BE` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/TypeManagerCatalogTests.cs |
| `TI-MOD-DOC-D7BF0EECC9BE` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/TypeManagerFailureContractTests.cs |
| `TI-MOD-DOC-2083BB5AF8F0` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck |
| `CIS.Heartbeat` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Heartbeat |
| `CIS.Startup` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Startup |
| `CIS.Configuration` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Configuration |
| `CIS.Metrics` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Metrics |
| `CIS.RestClient` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/RestClient |
| `CIS.Workflows` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Workflows |
| `CIS.Security` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Security |
| `CIS.Transactions` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Transactions |
| `CIS.Operations` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Operations |
| `CIS.Messaging` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Messaging |
| `CIS.Coordination` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Coordination |
| `CIS.Jobs` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Jobs |
| `CIS.Audit` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Audit |
| `CIS.Management` | verified-by | implementations/csharp/tests/CIS.Core.UnitTests/Management |

## Interpretation limits

Directory declarations identify locations and an intended verification relationship;
they do not establish assertion coverage, a passing result or release readiness.
The [RM-08 acceptance observations](../changes/CIS-0027/acceptance.md) retain the
actual native observations and their limits. The current correction demonstrates
one exact AC-10 check through its native receipt; remaining criterion-to-check
mappings stay explicit gaps rather than inferred coverage.

This projection preserves the accepted single-Core assembly, optional integration
packages, application resource ownership, native MSTest execution and the separate
original solution-design baseline. It grants no execution or publication authority.
