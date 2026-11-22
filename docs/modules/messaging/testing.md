# Testing messaging consumers

Use the public `CIS.CommonTesting` ports for application unit tests. The
[Core guide](core.md#commontesting-boundaries) describes bounded scripts and
ownership. `ScriptedMessagingClient.EnqueueRequest<TRequest,TResponse>` and
`EnqueueWaitForReply<TResponse>` provide explicit typed exchange facts; they do
not run handlers, complete transactions or recover results. Scripted tickets
keep disposal explicit. Publisher and delivery substitutes never establish
trusted native evidence.
For actual caller composition, use [typed requests and recovery](requests.md).

Use the compiling
[client cases](../../../implementations/csharp/tests/CIS.Core.UnitTests/CommonTesting/ScriptedMessagingClientTests.cs)
and [publisher/delivery cases](../../../implementations/csharp/tests/CIS.Core.UnitTests/CommonTesting/MessagePublisherDeliveryTests.cs)
for rejection versus persistence, cancellation/fault identity, independent script
and recording bounds, and actual held-work disposal. Do not replace SQL or broker
tests with those scripts.

The existing named integration fixtures establish different facts:

| Fixture | Evidence boundary |
| --- | --- |
| [PostgreSQL store](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/PostgresMessageStoreTests.cs) | Original-context SQL atomicity, uniqueness, fences and retained responsibility. |
| [Native RabbitMQ](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/RabbitMqTransportTests.cs) | TLS/principal authority, actual confirms/manual settlement, queue bounds and owned broker restart. |
| [Dispatch](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessageDispatchIntegrationTests.cs) | Concurrent native deliveries, unkeyed SQL effects, independent subscriptions and actual Operations replay. |
| [Typed requests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessageRequestIntegrationTests.cs) | Actual hosted query/command decisions, current-attempt recovery, explicit receipt confirmation, separate live endpoints and bounded caller/responder process death. |
| [Request admission](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessageRequestAdmissionIntegrationTests.cs) | Real committed-but-uncertain submission and finite waiter overload without an untracked request. |
| [Reply storage](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/PostgresMessageReplyTests.cs) | Atomic canonical/rejection reply links, schema compatibility and retained responsibility. |
| [Hosting](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessagingHostingTests.cs) | Console/web composition and actual owner drain before dependencies. |
| [Metrics collection](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Metrics/MessagingCollectionTests.cs) | Actual optional exporter names, central identity and conditional bounded streams; Part A and Part B execution are separate observations. |
| [Public packages](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessagingPackageConsumerTests.cs) | Fresh local-feed restore/build/run outside friend assemblies, exact deployed bytes, public helpers and optional dependency separation. |
| [Footprint](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/MessagingFootprintTests.cs) | One fixed local workload, admitted encoded bytes, committed effects and cleanup; no throughput threshold. |

Keep normal MSTest execution and explicit fixture prerequisites. The tests use
new isolated resources selected by private connection-file environment variables;
they do not silently choose a database, broker or credentials. Applications own
installation, topology and data-source lifetime. See
[verification and limits](../../changes/CIS-0019/verification.md) for actual
versions, platforms, preserved failures and passing checkpoints. Source alone is
not a passing result. Single-node restart is not quorum failover or deployment.
CountryData remains the database-free tutorial; these fixtures are distinct
required process/provider/package boundaries, not another maintained application.
