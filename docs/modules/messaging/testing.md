# Testing messaging consumers

For [Audit](../audit/README.md), `ScriptedAuditWriter` supplies bounded public
consumer facts, while the existing native fixtures establish original-context
atomicity, reconstruction/retry bytes and real pressure settlement. The receiving
probe is test-only and supplies no destination archive. Generic portable vectors
describe future AuditId duplicate/conflict obligations across distinct MessageIds.

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

Keep normal MSTest execution. Selected `RequiresDocker` cases lazily acquire
fresh owned PostgreSQL/RabbitMQ services through the integration assembly's
Testcontainers fixtures; developers do not provision services or connection
files. Missing Docker, image, readiness or cleanup requirements fail the selected
case. `PostgreSql` and `RabbitMq` categories describe actual dependencies; the
two passive package methods and synthetic Metrics collection remain Docker-free.
Use the [canonical native test commands](../../../implementations/csharp/tests/README.md)
to select the fast or service lane and check nonempty, zero-skipped service results.

The parent RPC fixture creates private per-child connection descriptors from its
typed owned services and passes their paths explicitly. Children receive only the
selected broker secret, required usernames and public CA, and never own Docker.
The server key, setup/rogue passwords and process-global descriptor configuration
are excluded. Data sources, providers, actual child exit/output and topology drain
precede descriptor and container disposal. Interrupted services use dedicated
owners, retain their storage and require authenticated readiness again.

Applications still own production installation, topology and data-source lifetime.
Historical [RM-04 verification](../../changes/CIS-0019/verification.md) and the
[Testcontainers correction](../../changes/CIS-0021/verification.md) retain their
different evidence and limits. Source alone is not a passing result. Single-node
restart is not quorum failover or deployment.
CountryData remains the database-free tutorial; these fixtures are distinct
required process/provider/package boundaries, not another maintained application.
