# RabbitMQ provider and ownership

The optional `CIS.Messaging.RabbitMQ` package uses official `RabbitMQ.Client`
7.2.2 over AMQP 0-9-1. The reference broker is RabbitMQ 4.4.1. Single-node
developer evidence establishes that setup's persistence/restart behavior,
not multi-node quorum failover. Core and CommonTesting acquire neither
RabbitMQ.Client nor Npgsql. Applications own credentials, trust and deployment.

Configure portable routes/subscriptions/store, then `RabbitMqOptions` and
`AddCisRabbitMqMessaging`. Registration/construction open no connection, read
no credential file, declare no topology and start no worker. Existing hosting
owns `IMessageTransport.StartAsync` and its ordered stop methods. Identity,
routes, limits and trust changes require instance replacement.

## Trust and caller scopes

Select a Native/Inbound Security profile with an explicit scheme. Outgoing
broker credentials remain adapter-owned; HTTPS-bound Outbound profiles are
not repurposed. `options.Capture().SubjectIssuer` qualifies the trusted broker
domain alias with the exact escaped virtual host. Use that effective issuer
for each admitted `SecuritySubject`. The publishing user must map to captured,
known local application/environment. Subject grants cover actual resources;
the bridge invents no provider roles, human identity or delegation.

TLS requires matching server hostname and a valid chain. Null
`TrustedRootCertificatePem` selects ordinary system trust. An explicit public
root uses custom-root server-authentication chain/validity/hostname checks,
without trust-store changes or private-key imports. Revocation checks for
that root are not selected; deployment owns trust/revocation policy. No
accepting TLS callback or plaintext fallback exists.

Incoming publications require broker-validated `user-id`, without impersonator
principals or unverified federation in this trust domain. Missing/unmapped
evidence establishes no authority. Claimed producer fields cannot replace
the publishing principal. Private current-delivery tickets feed the existing
`SecurityHostContextScope` projector and are removed after callback settlement.
Retained tickets/caller data cannot authenticate later work. Core still checks
current retry-service capabilities and retained provenance/ordinal/digest.
Public native provenance uses that principal's captured application/environment
mapping, including external applications; an unmapped principal produces no native
evidence. It never substitutes the receiver's identity or claimed wire producer.
Startup publishes profile availability before releasing queued delivery dispatch.

## Explicit topology

Invoke `RabbitMqTopology.InstallAsync(setupOptions, snapshot)` explicitly with
separate setup credentials. It declares registered durable exchanges, quorum
subscription/quarantine queues, bindings and the local reply exchange. Existing
mismatches fail without deletion or replacement. Independent event subscribers
own different queues; replicas compete on one logical queue. Commands target
one logical service. Names derive from registered aliases and explicit
`RabbitMqEndpointNamespace` mappings, never caller-selected physical names.

Within one virtual host and namespace, each command route must have exactly one
owning logical subscription across deployments. A local snapshot rejects local
duplicates; explicit deployment provisioning and permissions establish that
global ownership. Isolate environments in separate virtual hosts or namespaces.
The provider does not discover other deployments or repair their bindings.

Runtime startup checks existence, then exact active equivalence when
`ValidateTopologyEquivalence` is selected. Narrow configure rights cover only
registered owned resources. Passive checks alone cannot prove type/arguments.
AMQP cannot atomically check existence/equivalence without possible creation
if an operator concurrently deletes a resource. Freeze topology during
startup/recovery; this is an explicitly selected compatibility stage.

The reference queue profile uses durable nonexclusive quorum queues, finite
record/byte limits, `x-overflow=reject-publish`, `x-delivery-limit=-1` and
`x-delayed-retry-type=disabled`. Only the database outbox schedules handler
retries. Native quarantine uses its own exchange and
`x-dead-letter-strategy=at-least-once`.

Quorum length and byte limits are backpressure thresholds, with bounded native
in-flight overshoot rather than an exact one-message cutoff. Keep publisher
concurrency and native buffers bounded and reserve deployment headroom. The
[quorum limit contract](https://www.rabbitmq.com/docs/quorum-queues#length-limit)
allows overshoot while publishing channels receive the reject notification.
Overflow acceptance tests fill until an actual negative confirmation, then
verify that every confirmed original remains; blocked-DLX tests likewise establish
actual retained source responsibility before freeing target capacity.
The owned RabbitMQ 4.4.1 fixture reported
`rabbit.dead_letter_worker_publisher_confirm_timeout = 180000` milliseconds.
Its blocked-DLX restoration test therefore waits at most 210 seconds: one observed
native retry period plus 30 seconds for settlement and bounded observations. The
test keeps its exact confirmed-byte set and source/target-empty requirements;
the adapter does not change the broker's retry policy or add a second scheduler.

`SafeDeadLetteringVerified` is an explicit verified deployment prerequisite,
including effective/operator policies and bindings. Active declarations cannot
establish future overriding-policy state. Actual acceptance must exercise
transfer and a blocked quarantine target. Without that prerequisite malformed
input remains unacknowledged and its consumer stops visibly. A native negative
acknowledgement selects a configured responsibility-preserving path; it never
claims application processing or confirmed target publication.

Broker resource permissions do not authorize arbitrary routing keys. Use
separate owner exchanges or topic permissions for finer route restrictions.
Runtime needs no unrestricted administration or impersonator tag. Narrow broker
configure permission also permits native deletion; this library never issues
delete operations. Use a separately selected validation principal when deployment
must deny configure/delete capability to the long-lived runtime user.
Provision intended event subscriptions before publishing; mandatory routing
cannot prove every intended binding was configured.

## Native operations and bounds

Initially four fixed publisher lanes each serialize one channel, use persistent
messages, confirmation tracking and mandatory returns. Socket completion is
insufficient. Returns classify Unroutable; nacks are Transient rejection;
uncertain settlement is Unknown/Transient. Permanent protocol refusals remain
distinct. Fan-out nacks can follow partial acceptance by other queues, so
inbox protection remains essential. Explicit event AllowNoSubscribers uses the
named `messaging.publish.no-subscriber-policy` acceptance, never Delivered
or Processed.

Core retains encoded-body admission through actual native settlement. This
adapter borrows immutable encoded bytes without a second module working copy.
Consumer buffers remain native-borrowed until the awaited callback returns;
Core admits owned bytes before copying/decoding. The native body maximum is
one MiB plus 16 KiB metadata, matching broker acceptance. Thus bodies larger
than a smaller selected Core limit can reach bounded quarantine. The broker
rejects bodies above that maximum before accepting them.

Native overhead is separate from Core's 32 MiB owned encoded-byte budget.
Prefetch sixteen at sixteen subscriptions can retain 256 supported-size native
bodies, plus bounded frames/confirmation metadata. Validate deployment memory
accordingly. The adapter requests at most 64 channels and has no unbounded
publisher waiting queue. Consumer concurrency/prefetch use the validated snapshot.

Native automatic recovery alone owns reconnect/topology recovery. Initial
connection failure fails startup; permanent channel/configuration faults remain
visible. There is no second consumer-registration recovery loop. Manual
settlement uses `multiple=false`; requeue preserves identity and ordinal.
Core owns transaction, authorization and committed-responsibility decisions.
Each delivery captures its connection generation. An attempt to settle after that
connection is lost sends no old acknowledgement tag and leaves recovery/redelivery
to the native owner. That specific lost-connection path does not permanently
unregister the consumer; unrelated handler and channel protocol faults remain
visible. The restart case also checks a fresh publication after redelivery.
Native registration readiness follows actual consume confirmation. Client 7.2.2
raises `UnregisteredAsync` during both cancellation and channel shutdown, so the
adapter distinguishes the native `basic.cancel` methods from shutdown instead of
treating that shared event as permanent cancellation. An actual broker cancellation
still stops the affected consumer visibly; only native recovery restores a lost
connection. A fresh empty owned-queue deletion supplies that cancellation test.

## Replies and shutdown

One provider-generated endpoint UUID identifies one reusable acknowledged quorum
reply queue; there is no per-request queue or Direct Reply-To. Structured
`MessageReplyRoute` resolves through permitted application/environment namespaces.
Unused endpoint expiry and message age initially ten minutes must cover maximum
wait plus margin; a live consumer prevents unused expiry. Obsolete outgoing
replies classify ReplyDestinationExpired without changing Operations results.

Shutdown closes consumer admission and joins admitted callbacks. A callback can
request affected-consumer stop without joining itself. Final stop joins native
publications and channel/connection cleanup. Delivery disposal never acknowledges
work. Unfinished responsibility remains recoverable; stopping is no remote
rollback claim.

The [provider tests](../../../implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging/RabbitMqTransportTests.cs)
distinguish real TLS/principal/permission/confirmation/restart observations from
scripted behavior. Transactional effects, request correlation and durable-result
recovery still require integrated Core acceptance.

The first native provider cohort exposed RabbitMQ 4.4.1's unsupported quorum
`queue.delete` conditional flags during fixture restoration. The corrected fixture
joins every known owner and native connection, verifies zero consumers with a
passive declaration, then deletes only exact newly generated per-case names with
both flags false. A failed owner drain prevents that deletion. Names are never
shared with external deployments; this is fixture restoration, not production
topology repair. The [server implementation](https://github.com/rabbitmq/rabbitmq-server/blob/v4.4.1/deps/rabbit/src/rabbit_quorum_queue.erl)
rejects both `if-unused` and `if-empty` for quorum queues.
