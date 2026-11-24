# Management persistence

The management application owns these PostgreSQL tables and its deployment
migration. Runtime library/client packages contain no management repository or
data-source dependency. Production composition lends one host-lived Npgsql source
to the existing RM-02 `management` alias; the store uses only original
`ReadCommitted` participant callbacks and awaits actual owner completion/cleanup.

`ManagementSchema.InstallAsync` is an explicit deployment operation. Version two
adds a persisted namespace authority, opaque control revision, recovery epoch and
hold. The explicit version-one upgrade retains every application, registration,
metadata revision, operation journal entry and outgoing audit record. Existing
supported Operations/Messaging schemas are checked rather than reinstalled. The
owner commits once. Unsupported versions, a missing version-two authority or a
mismatched expected namespace/epoch are rejected, not silently repaired.
Ordinary Startup validates schema and the real primary authority without DDL.
No constructor or passive DI registration accesses the database.

The complete product boundary is `IManagementBackend`; PostgreSQL is its only
delivered adapter. Standalone and Shared use the same qualified primary and
original RM-02 participants. The public low-level store also remains usable in
focused participant tests; constructing it without an authority participant is
not selecting or qualifying a complete backend. See [backend profiles and
recovery](../Backends/README.md).

The server creates `ManagementRegistrationOwner` only after selected native
authentication, complete capability authorization and the trusted self-mapping
check. Its exact application/environment/issuer/subject bind each registration.
Constructing that detached value alone grants no trust. Native instance evidence
is persisted separately from the registration body's reported label.

Application names are exact, with PostgreSQL `COLLATE "C"` uniqueness. Administrative
metadata and its positive revision belong to the stable application row. Presence,
inventory and instance churn never update that revision. The metadata coordinator
uses `FindApplicationAsync` and `TryReplaceMetadataAsync` on its original context;
those participants never complete its owner. RM-03 result arbitration and required
RM-06 Audit staging stay in that same transaction.

Registration and independent observation/inventory streams retain canonical
request digests. Newer sequences atomically replace a stream; equal identical
content retains the original receipt; equal changed content conflicts and lower
sequences are obsolete. Shared database time is taken after write admission/locks.
Reported precise timestamp strings remain original text. Close is terminal and
repetition retains the original close time. Unknown reports never create a row.

The default startup limits are 1,024 applications, 128 retained registrations per
application, 8,192 total registrations and 256 MiB of retained encoded inventory.
Deploy replicas with the same captured finite options. Database occupancy is the
authority: capacity-changing work locks its one counter row before application
and registration rows, so concurrent replicas cannot independently spend the same
capacity. Observation and close do not need that global counter. There is no
process-memory admission authority or management idempotency table.

`ManagementStoreResult<T>` separates the actual attempt completion from an
already committed original receipt. New writes expose success only after known
commit. An unknown write exposes no invented success and preserves its actual
completion. A matching committed replay remains known even when the new read
owner rolls back or reports a cleanup diagnostic. Native database failures return
safe unavailable codes rather than cached write acknowledgements.

Reads are ordered by canonical UUID and return detached bounded batches. The HTTP
layer authorizes actual resources before including them or constructing a cursor.
Inventory reads lock candidate registration rows `FOR SHARE` while examining
encoded lengths and fetching only admitted whole snapshots. They never wait for
the capacity counter, preserving the writer lock order. Namespace/recovery state
is locked first in the original transaction. A finite wrapper/cursor
and provenance allowance precedes materialization; `HasMore` records a byte/count
boundary even when fewer complete items than requested fit. An item which cannot
fit produces an explicit refusal, not an empty nonadvancing page. Final Wire
encoding checks the exact response byte limit as well.

Presence uses the accepted observation receipt (or initial registration receipt),
with the configured 90-second stale interval. Inventory has separate sequence and
freshness. Neither stale presence nor a successful connection changes job leases.
Telemetry retention is seven days from the latest accepted registration,
observation/inventory or later terminal close. Existing Heartbeat maintenance
deletes batches of at most 100 using database locks and `SKIP LOCKED`. Multiple API
replicas may safely perform this idempotent cleanup. One existing Heartbeat
subscription checks actual authority every five seconds and rate-limits cleanup
to once per minute. It removes registration
telemetry and releases its capacity; stable application metadata and Operations
duplicate/result protection remain. No leader, permanent presence tombstone or
Audit destination store is introduced.

The version-three configuration catalog shares one logical content/metadata
quota. The same bounded cleanup allowance now also removes eligible releases
and orphan publications after expired registrations. Current default/cohort
references and explicit pins protect releases; all retained releases protect
their publications. The applied deployment's N retains the newest N eligible
unreferenced releases per binding and N eligible orphan publications per source.
Exact deleted session/adoption/content charges are subtracted under the original
capacity → catalog → row locks in the same native commit. Stable schema/source
metadata, required outgoing Audit and operation duplicate/result protection are
independent. See the [retention guide](../../../../../docs/modules/management/configuration-retention.md).

Provider cases in the existing IntegrationTests Management directory reuse owned
PostgreSQL Testcontainers, real durability, two independent RM-02 roots and the
existing native commit-boundary seam. Their authored source alone is not passing
evidence. Actual counts and commands belong in the current CIS verification
record; reconstruction of store objects does not by itself prove a new OS process
or external deployment.
