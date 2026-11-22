---
title: "RM-04 provider and dependency profile"
type: document
status: Draft
---

# Optional provider only

The current user delegates completion of the attached two-part module without
another authorization request. RabbitMQ is the specification's concrete initial
provider, selected under that instruction. This is not represented as a prior
user broker deployment or a separately reviewed exact dependency payload.

Select **RabbitMQ.Client 7.2.2**, the stable official AMQP 0-9-1 client, for a new
optional **CIS.Messaging.RabbitMQ** project/package. Its declared net8 group names
System.IO.Pipelines and System.Threading.RateLimiting at minimum 8.0.0. Permit
those two exact package IDs in source mapping alongside RabbitMQ.Client; inspect
actual net10 framework pruning and generated lock/assets before recording the
resolved deployment closure. Do not claim a declared/pruned dependency ships.
Any necessary exact locked transitive resolution remains within this selected
optional provider; no wildcard source mapping or new source is authorized.

The supported reference server is **RabbitMQ 4.4.1**. Pin its official Alpine
image registry digest before pull. Reuse **PostgreSQL 18** and the existing
test-only **Npgsql 10.0.4**; pin the available official PostgreSQL image digest
and record actual server versions. One newly owned single-node broker/DB fixture
proves only that deployment's persistence/restart behavior, not quorum failover.

Core's two approved Microsoft package pins/framework requirement, SDK10.0.401,
selected NET/ASP10.0.12 runtime and every existing package pin remain unchanged.
The optional project references Core and its approved framework. Core/CommonTesting
and Core-only consumers acquire neither RabbitMQ.Client nor Npgsql. Root owns
solution/project/central package/source-mapping/lock coordination; builds sharing
outputs are serialized. Required project paths are in the canonical shared scope.

Use four serialized native publisher channels with confirmations/tracking,
mandatory route returns, persistent properties and validated user-id. Consumer
callbacks copy only after owned-byte admission; native buffers are not retained.
One native recovery owner manages reconnect/topology recovery. Explicit topology
provisioning uses separate restricted fixture/application-owned credentials; runtime
registration never installs schema/topology or accesses an operator service.

Authoritative inspected sources:

- [Official client 7.2.2 release](https://github.com/rabbitmq/rabbitmq-dotnet-client/releases/tag/v7.2.2)
- [Official release information](https://www.rabbitmq.com/release-information)
- [Client API and buffer/channel ownership](https://www.rabbitmq.com/client-libraries/dotnet-api-guide)
- [Publisher confirmation and acknowledgement semantics](https://www.rabbitmq.com/docs/confirms)
- [Validated user-id trust boundary](https://www.rabbitmq.com/docs/validated-user-id)
- [Quorum queues and at-least-once dead lettering](https://www.rabbitmq.com/docs/quorum-queues)
- [Resource/topic access control](https://www.rabbitmq.com/docs/access-control)

No external credential provider, delayed-message plugin, alternate broker protocol,
SDK/runtime installation, performance harness or release work is selected.
