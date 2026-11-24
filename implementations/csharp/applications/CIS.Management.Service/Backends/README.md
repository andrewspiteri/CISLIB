# Management backend and authority

`IManagementBackend` is the application's typed boundary for registration,
authoritative control, complete observations/inventory, data pages, metadata
execution/original-result recovery and bounded cleanup. It is not a generic CRUD
repository, transaction framework or management command queue. HTTP contracts
and both SDKs remain independent of PostgreSQL types and deployment cardinality.

The sole complete adapter is PostgreSQL. Its frozen capability profile requires
durable namespace authority, concurrent arbitration and atomic metadata/result/
outgoing-audit responsibility. `Standalone` means one API process with a dedicated
durable primary; `Shared` means concurrent API processes using that same primary,
schema/namespace, expected recovery epoch and cursor signing key. Standalone is
an explicit single-process deployment contract, not a new advisory-lock service.
Shared mode does not install database replication, election or consensus.

Redis and Cosmos DB are provider targets, not implemented adapters. Selecting
either fails before network I/O. A cache-only adapter cannot advertise the
complete capabilities. A future complete non-SQL adapter must integrate the
existing Operations and Messaging contracts in its actual atomic provider
boundary. An independent Redis/Cosmos state write beside a PostgreSQL journal or
outbox would not satisfy this contract. That work is not delivered here.

RM-08 adds `IManagementConfigurationBackend` beside the existing interface. The
PostgreSQL product resolves both to the same scoped adapter and original
authority/journal/outbox graph. Select `Configuration.ManifestFile` and an explicit
`Configuration.QuotaBytes`; the [catalog/apply recipe](../../../../../docs/modules/management/configuration-service.md)
describes the guarded deployment step and actual installed source handlers.
An unloaded/uninitialized configuration capability does not advertise release,
assignment or adoption behavior merely because portable DTOs exist.

## Frozen startup selection

The executable reads this public `Backend` section along with the existing
database/cursor secret references and Security/Delivery bootstrap:

```json
{
  "Backend": {
    "Provider": "PostgreSql",
    "Mode": "Standalone",
    "Namespace": "management",
    "ExpectedRecoveryEpoch": 1,
    "RecoveryHold": false,
    "ControlValidForMs": 15000
  }
}
```

The namespace is exact, nonblank, control-free text of at most 128 Unicode
scalars. The expected epoch is a positive signed 64-bit value. The control
lifetime is 1–15000 milliseconds. `RecoveryHold=true` is an additional local
fail-closed selection; it cannot override a persisted hold to false. A shared
recovery incident requires the persisted hold or a coordinated trusted hold on
all replicas, not a local flag on only one node.

One configured schema corresponds to one persisted namespace. Explicit
`--migrate` initializes its stable canonical authority UUID and opaque control
revision, or upgrades the old management schema to version three, including an
empty stable configuration catalog. Normal startup
does not create or replace an authority. Repeating migration checks a current
authority instead of minting another UUID. A corrupt/missing current authority
or incompatible dependency schema requires explicit operator recovery.

## Qualified PostgreSQL profile

Every actual backend transaction checks its original native session: PostgreSQL
18 or later, primary (`pg_is_in_recovery=false`), read/write transaction, and
`fsync=on`, `full_page_writes=on`, `synchronous_commit=on`. The application rejects
multi-host fallback and standby/read-only/prefer-standby session selections.
Use one qualified primary endpoint. A DNS/load-balancer change must retain this
declared authority and durability profile; an asynchronous standby does not
become qualified by receiving the same hostname.

The profile covers durable-primary operation, primary restart and loss/replacement
of API replicas while the same acknowledged state remains authoritative. It
does not qualify an asynchronous database failover, storage that lies about fsync,
or a restore which loses acknowledged operation/outbox protection. API replica
redundancy does not remove the database dependency.

The namespace row is locked `FOR SHARE` inside each original operation context.
Recovery maintenance updates acquire its exclusive row lock. An admitted metadata
owner therefore finishes its original metadata/RM-03 journal/RM-06 staging
transaction before a recovery-hold transition can finish. There is one actual
factory/journal/outbox graph and one application commit. No operation scope,
fingerprint contract or result codec is reset by the new authority fields.

The PostgreSQL journal adapter qualifies the namespace inside the coordinator's
actual original context, including original-result reads. A separate preliminary
read does not consume a cleanup fault and hide a genuine Found result. Original
committed results remain committed through the new read/arbitration owner's
NotCommitted cleanup diagnostics. New bootstrap/control/mutation calls fail
closed on unavailable, unqualified, mismatched or held authority. Permitted
inspection of original obligations can still use its actual authoritative read;
it never falls back to a replica cache.

## Control, readiness and cursors

`GET /registrations/{registrationId}/control?attemptId=<canonical-uuid>` enforces
the native registration capability and exact stored self binding on every call.
It reads the actual primary authority and open registration, echoes the nonce,
returns `Cache-Control: no-store` and never renews observation last-seen time.
It reports no fresh confirmation when its actual read cleanup is uncertain.
The client measures the usable window from its own monotonic request start.
The control value is neither a capability grant nor a job lease/fencing token.

One existing Heartbeat owner probes the backend every five seconds and performs
bounded telemetry cleanup at most once per minute. `IHealthReadinessSource.IsReady`
uses only a retained native probe result and a maximum fifteen-second monotonic
age measured from probe entry; publication follows actual rollback and owner
disposal. Ordinary unfinished transaction reads cannot refresh it, and a late
probe cannot overwrite a newer invalidation or stopping state. It performs no I/O
from health rendering. Actual control/bootstrap/mutation
still consults the backend on every exchange. A hold/failure closes authoritative
readiness without changing process liveness or canceling already committed
outgoing delivery. Operational health and read-only/result inspection retain
their existing host boundaries.

Cursor format two adds namespace, stable authority and recovery epoch to the
existing authenticated endpoint, actor/resource, page-size, ordering and last
authorized UUID. A prior cursor from another namespace/authority/epoch is
rejected. Pagination remains a live view. This change does not add the epoch to
metadata operation scopes: retained original keys must remain recoverable across
an upgrade.

The service always advertises the effective complete-inventory request maximum
in service-info: the minimum of the provider and captured wire limits. PostgreSQL
supports the current portable ceiling of 16 MiB. The default remains 4 MiB; no
provider truncates a complete snapshot to make it fit.

## Recovery is an explicit operator action

An ordinary restored database row cannot prove that the restore retained every
acknowledged write. Keep an independent deployment record of the expected
namespace/epoch and backup/recovery provenance. For an uncertain restore, hold
all affected replicas or advance the externally expected epoch before making
the restored backend eligible. An old restored epoch then fails validation.
Do not restart it as normal and assume its UUID proves freshness.

Use the separately authorized deployment role to establish a persisted hold,
with an actual new opaque revision, before planned maintenance:

```sql
BEGIN;
UPDATE cis_management.management_authority
SET recovery_hold=true, control_revision='operator-selected-hold-revision'
WHERE singleton;
COMMIT;
```

Before releasing the hold, reconcile acknowledged metadata, original-result
duplicate protection and outgoing Audit intent using the authoritative recovery
inputs. Merely generating a fresh authority ID does not restore that protection.
If acknowledgement preservation cannot be established, keep the authority held;
do not advertise a weaker profile or resubmit original commands under fresh keys.

After that reconciliation, an explicit deployment transaction may advance the
positive epoch and set a new control revision while clearing the hold. Set the
same independently recorded `ExpectedRecoveryEpoch` on every API replica and
restart them. An existing process does not silently adopt an unexpected epoch or
authority. This is a documented provider recovery procedure, not a public grant
editing endpoint, automatic restore detector or new synchronization protocol.

Native tests use the existing owned PostgreSQL/RabbitMQ and actual product host.
They cover both modes, same-authority replica control, exact registration binding,
recovery hold, no cached-control fallback, native read cleanup refusal, epoch/
namespace mismatch, actual synchronous-commit refusal and version-one upgrade
with original metadata/result/Audit recovery. Source describes intended checks;
only the recorded native execution establishes its precise passing evidence.
