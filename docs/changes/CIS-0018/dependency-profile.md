---
title: RM-03 dependency and infrastructure profile
type: document
status: Proposed
cis:
  stable_id: cislib:change:cis-0018:dependency-profile
---

# Dependencies and operational boundary

Retain SDK10.0.401 and .NET/ASP.NET10.0.12, the single Core assembly, Core's two
approved abstraction NuGets and CommonTesting's sole Core package dependency.
PostgreSQL SQL code uses DbConnection/DbTransaction/DbCommand and the existing
public RM-02 capability. DbException.SqlState is already a BCL API. No shipping
provider-specific dependency, EF, Redis, broker, Testcontainers, mocking framework,
new source mapping or pin change is selected.

IntegrationTests already has Npgsql10.0.4 with PrivateAssets=all, its central pin
and exact mapping. The two RM-02 retained-package mappings also already exist.
Preserve every baseline locked ID/version/content hash. Use ordinary locked
restore; a new graph/pin/source difference needs its specific actual decision,
not an opportunistic fix. Reuse selected cached archives read-only with hash
validation, or a new counted owned cache; do not mutate accepted historical
evidence or borrow an uncounted cache.

## Fresh database fixture after approval

RM-02's resources were removed and its owners are Complete. This plan does not
grant use of an existing database or inherit that fixture's execution authority.
The requested new owner may provision one newly labelled private Docker fixture
only after approval and finite work allocation.

Use the same selected official PostgreSQL18 amd64 immutable image reference:
postgres@sha256:68e33adb18d9962952f00d5e3b8644305e10e375f98dd652bf6ee6a1836314ba.
Reverify the referenced image/size before provisioning; record the actual server
version. Do not silently substitute a moving tag. No Docker/DB access occurred
during this intake.

- One fresh named owner-labelled container/volume, generated database/user/password,
  private owned credential files and explicit127.0.0.1 ephemeral port.
- Private anonymous Docker client configuration/known local engine endpoint;
  do not read/use operator registry auth, existing databases, passfiles or credentials.
- At most2CPU,512MiB memory/swap,128PIDs,16 server connections and a fixture pool
  maximum6 with minimum0. Serialize provider lanes/cases; at most6 live leases.
- Preserve fsync/synchronous_commit/full_page_writes. No TEMP/UNLOGGED shortcuts.
  Use one GUID schema per case, then drop only that created schema and drain source.
- Four ordinary logged fixture tables are appropriate: schema metadata, journal,
  business and outgoing. RM-02's three tables/two rows/1KiB were fixture choices,
  not RM-03 production requirements.
- Per-case caps:512 tiny journal rows (to prove256-row maintenance),128 business/
  outgoing rows each; ordinary payloads at most4KiB, with isolated configured
  size-boundary cases up to1MiB. No load/performance campaign.
- Native connect timeout15s, command timeout30s and cancellation timeout2000ms
  remain fixture limits; pool6, Enlist=false, No Reset On Close=false, no passfile,
  SSL/GSS disabled only on this private loopback fixture. Production journal
  commands separately use the explicit15s default described in the profile.
- Observe real blocked sessions deterministically before cancellation/deadlock
  release; no sleep alone as evidence. Any fixture-only lock_timeout configuration
  must be distinguished from production adapter behavior.

The1GiB Docker allowance remains INSIDE the existing6GiB physical ceiling:
unpacked image512MiB, compressed cache128MiB, data256MiB, logs8MiB,
metadata/overhead120MiB. This is counted admission, not a hard storage quota.
Measure before image/data expansion and stop new admission if it cannot fit.
Preserve the64MiB restoration reserve and existing8MiB native charge.

After consumers actually settle, verify exact ownership, stop/remove only the
new container/volume and unshared newly owned image, and remove generated secret
files within verified absolute owned paths. Preserve receipts/native evidence.
No engine pruning, historical fixture deletion or trust-store changes are proposed.

## Native evidence scope

Use existing MSTest projects, shared-output serialization, current candidate/
package hashes and actual Windows11/Ubuntu24.04 runtime lanes. Existing Ubuntu
has no SDK; the full unchanged SDK-backed public-package fixture is selected on
Windows. Record the Linux package gap instead of introducing a wrapper/install
campaign or counting Windows children as Linux evidence.

The acknowledgement-loss case may use a narrow C# native-commit decorator that
performs a real commit then reports failure through RM-02's actual commit
boundary. Prove the owner reports Unknown and authoritative recovery finds the
original. This is simulated acknowledgement loss, not a real network fault or
a fabricated completion report. Keep the compiling consumer within existing
test structure; CountryData's default stays database-free.

## Primary technical references

- [PostgreSQL18 isolation and statement snapshots](https://www.postgresql.org/docs/18/transaction-iso.html).
- [INSERT / ON CONFLICT semantics](https://www.postgresql.org/docs/18/sql-insert.html).
- [SELECT locking / SKIP LOCKED](https://www.postgresql.org/docs/18/sql-select.html).
- [PostgreSQL error codes](https://www.postgresql.org/docs/18/errcodes-appendix.html).
- [Lock timeout contract](https://www.postgresql.org/docs/18/runtime-config-client.html#GUC-LOCK-TIMEOUT).
- [BCL DbException.SqlState](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbexception.sqlstate?view=net-10.0).

These inform the design; authored SQL/tests and primary-source reasoning are not
executed database evidence. No native provider, package, scrape or release
qualification is claimed by this intake.


Identity index sizing/equality references:
[PostgreSQL18 B-tree entry limit](https://www.postgresql.org/docs/18/btree.html)
and [deterministic C bytewise collation](https://www.postgresql.org/docs/18/collation.html).
The2488-byte data bound reserves room for index tuple overhead on an8192-byte
page; WORK-003 must exercise maximum-length identity values in the real fixture,
not infer success from character counts or compressible repeated text alone.
