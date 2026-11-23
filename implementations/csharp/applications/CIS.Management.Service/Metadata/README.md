# Administrative metadata and recovery

The two installed endpoints replace display metadata and retrieve the original
decision for the caller's operation key. They use the existing RM-02 transaction
owner, RM-03 journal and RM-06 required Audit publication. They do not add another
transaction coordinator, credential store or Audit destination.

`PUT /management/v1/applications/{applicationId}/metadata` requires current
`management.applications.metadata.write` permission on the actual stored
resource, `Idempotency-Key`, one strong `If-Match` and command version 1. The
display name and optional description replace the complete administrative
metadata. Omission clears the description. Application/environment identity,
grants, configuration, registrations and jobs cannot be changed here. Missing
preconditions return 428; weak, wildcard or multiple values return 400.

The operation is `management.metadata.replace`. Its scope binds the actual
application and the current validated actor/delegation tuple. Its versioned
fingerprint includes the target, original precondition and exact UTF-8 semantic
strings; composed and decomposed text remain different. It carries neither
credentials nor role claims. The endpoint first resolves and authorizes the
actual application, then binds this detached server value before opening the
original owner. The native resolver opens no nested database lease.

The original participant claims the operation, locks the actual application
row, checks its metadata-only ETag, applies the compare-and-swap, stages one
required administrative Audit and records the native decision. Only its sole
owner commits. The Audit event `cis.management.application.metadata.decision/1`
captures the trusted actor, exact operation reference, actual target and bounded
before/after metadata and revisions. Rejected preconditions have identical
before/after facts and a real RM-03 Rejected error. They have no fabricated typed
metadata result. Capture/staging failure prevents all three effects from
committing. Presence and inventory never advance this ETag.

A matching replay precedes the handler, old-precondition check and Audit. An
applied replay returns the unchanged original metadata/revision/time even after
later edits. A rejected replay returns the unchanged original error. A changed
input or precondition under the same key returns 409. Original result payload
and duplicate protection retain the existing indefinite RM-03 defaults.

`GET /management/v1/applications/{applicationId}/metadata-operations/{operationKey}`
requires current `management.operations.read` on that actual resource and
derives the same actor/resource scope. It returns a successful lookup containing
the actual applied or rejected original decision. Other actors and delegation
identities cannot use a copied key as authority. Hidden resources appear absent.

The response separates `prepared`, `replayed` or `found`, original persistence
and current owner completion/cleanup. A newly prepared decision needs known
commit before HTTP success. Unknown returns 503 `management.operation_unknown`
with the original logical operation and available tentative decision facts;
retain the original key. Same-key explicit resubmission or lookup can establish
the committed original later. A NotFound observation does not prove rollback.
No HTTP handler or SDK retries commit, invents another key or automatically
resubmits this mutation. A replayed/found original remains committed even if the
new arbitration/read owner rolls back or reports cleanup errors.

Delivery requires explicit `ManagementDeliveryOptions`: the real existing
RabbitMQ adapter, selected trust and credential inputs, and the PostgreSQL
Messaging store in the same `management` database. The bounded retained
envelope ceiling defaults to 64 MiB; `management-audit` uses the existing 256 KiB
Audit limit. The broker profile maps only the actual broker identity to the
local service; it receives no management administrative grant. Its HTTP scheme
always returns NoResult. Startup validates real delivery setup. Once started,
broker outage preserves committed outbox responsibility; it is not evidence of
archive delivery. Optional live-broker readiness uses the existing Messaging
selection. Deployment explicitly installs supported schemas before startup.

`ManagementMetadataTests` exercises real authenticated HTTPS product roots over
the owned PostgreSQL/Rabbit services: replay after reconstruction, concurrent
replicas, current resource authorization, actor/delegation isolation, exact
fingerprints, retained rejection, one required Audit and actual storage refusal.
Distinct concurrent keys sharing one precondition establish one applied and one
native rejected decision. A fixture-owned expired-payload state proves that
lookup and same-key resubmission preserve duplicate protection without another
effect or Audit; it does not change the product's indefinite retention default.
`ManagementMetadataCompletionTests` separately uses the existing narrow native
commit/disposal fault seam and real production journal/Audit/outbox, with no
background publisher to consume the intended fault. It proves persistence and
cleanup facts rather than claiming authentication or arbitrary network-loss
qualification. Test execution evidence is recorded in CIS-0024 by the owner.
The after-native-commit fault reports Unknown and retains Npgsql's failed
cleanup rollback of its already completed transaction as a separate cleanup
code; that failure neither establishes rollback nor rewrites the original
persistence fact. A later authorized Found/replay recovers the actual commit.
