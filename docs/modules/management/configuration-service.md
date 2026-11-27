# Configuration catalog and immutable publication

The independent Management application selects configuration publication with
`ManagementServiceOptions.Configuration`. Core clients remain passive. Existing
`IManagementBackend` implementations acquire no required member: the application
uses the separate `IManagementConfigurationBackend` capability, and the PostgreSQL
host resolves both interfaces to the same scoped adapter and original participant
graph.

The catalog layer installs catalog status/apply/recovery, authorized source discovery,
validation, source history, immutable publication/content and publication-result
recovery. It advertises the three catalog APIs before a manifest is applied and
the six source APIs after successful provisioning. The
[distribution layer](configuration-distribution.md) adds coherent release,
assignment, preview and authoritative snapshot handlers. The separately authorized
[adoption projection](configuration-adoption.md) reports current target relationships
and independently fresh registration observations.

## Explicit deployment input

Configure an absolute `Configuration:ManifestFile`, an explicit positive
`Configuration:QuotaBytes`, and the selected `RetainedUnreferencedReleases`
(the approved initial profile retains ten eligible unreferenced releases per
binding and ten eligible orphan publications per source).
`PinnedReleaseIds` is an optional finite exact UUID list. The application loads
one strict UTF-8 manifest before constructing its host. There is no file watcher,
constructor I/O, remote manifest upload or automatic application on restart.
The complete file is bounded to 16 MiB. Definitions, application bindings and
common scopes each have a 4,096-entry deployment bound; native cohort mappings
have a 16,384-entry bound. One application/environment has at most 256 bindings.

The following flat declaration illustrates the manifest layout. Use the
[CountryData recipe](countrydata-configuration.md) for that consumer's actual
four-member schema and defaults.

```json
{
  "manifestVersion": 1,
  "definitions": [{
    "configurationId": "example.flags", "schemaVersion": "1",
    "shape": {"kind":"object","nullable":false,"members":[
      {"name":"Enabled","required":true,"shape":{"kind":"boolean","nullable":false}}
    ]},
    "defaultsJson": "{\"Enabled\":true}"
  }],
  "bindings": [{
    "application":"ExampleApp", "environment":"Production",
    "hostApplicationName":"ExampleHost", "configurationId":"example.flags", "schemaVersion":"1"
  }],
  "commonSources": [{"configurationId":"example.flags","schemaVersion":"1","environment":"Production"}],
  "cohorts": []
}
```

Definitions are global immutable shape/defaults contracts. Host scope belongs to
an application/environment binding, allowing the same schema to serve different
hosts. Description is guarded metadata; changing shape or defaults under the
same identity is refused. The finite structural validator executes no consumer
constructor, CLR reflection selection, validator expression or JSON Schema engine.
It validates each supplied source before later composition, allows unknown
configuration members under the existing mapper policy, and bounds all traversed
values. Final typed and cross-property acceptance remains with Configuration.

### Declared defaults for nested objects

Flat definitions keep their existing constructor and serialized declaration.
When a shape contains nested objects, its trusted definition also supplies
`objectDefaults`: one complete declared-default object for each nested object
shape, including nullable children and array/dictionary element objects. These
templates are local deployment data, retained in the existing immutable
definition and logical quota. They do not become public shape metadata or an
HTTP upload. The service never loads a consumer type or runs its constructor.

For a nullable `Child` and an array `Items`, both with integer members `Left`
and `Right`, the additional definition property is:

```json
"objectDefaults": [
  {"path":"/members/Child","defaultsJson":"{\"Left\":1,\"Right\":2}"},
  {"path":"/members/Items/element","defaultsJson":"{\"Left\":1,\"Right\":2}"}
]
```

Paths follow the declared shape: `/members/{name}` enters an object member,
and `/element` enters an array or dictionary element. Escape `~` as `~0` and `/`
as `~1` inside member names. There is no root template: `defaultsJson` already
captures the root. Every nested object path must appear exactly once; unknown,
duplicate or missing paths fail before host construction with a manifest error.
Templates contain every declared member, including optional members, and valid
complete values recursively. Each is strict object JSON within the existing
256-KiB source bound. There are at most 4,096 templates per definition and each
structural path is at most 16 KiB of UTF-8, covering the existing depth/member
profile including Unicode names; the complete manifest remains
bounded to 16 MiB.

Root-captured values and declared child defaults are different facts. A root may
initialize `Child` to null, or customize it to `Left=30, Right=40`, while that
child type declares `Left=1, Right=2`. Given common `{"Child":null}` followed by
application `{"Child":{"Right":9}}`, composition restores the declared template
and yields `Left=1, Right=9`. With no lower null, an ordinary partial overlay
preserves the current lower `Left=30`. Replacement arrays and dictionaries
discard prior elements and complete each partial object from its declared type's
defaults, matching local Configuration. Supplied invalid known values still
fail publication even when a higher source could mask them. Source bytes and
their SHA-256 remain unchanged.

Template expansion is admitted during streaming composition, before cloning or
materializing an effective object: at most 65,536 value nodes, depth sixteen and
16 MiB of encoded effective JSON. Exceeding that finite bound rejects the
original release/map intent without an effect. This is a request-local structural
bound, not a new process-wide memory framework or a promise of typed acceptance.

Templates are detached and sorted by structural path for the immutable
declaration fingerprint; their values cannot be changed under an existing
definition/schema identity. The exact loaded deployment digest also covers the
manifest containing them. A prior nested manifest without templates now fails
clearly instead of guessing constructor defaults. Supply explicit templates and,
if that identity was already provisioned, declare a new schema version using the
ordinary catalog workflow. Existing flat declarations omit the new property and
retain their original serialized bytes and fingerprint. No new SQL migration or
automatic metadata repair is performed.

`LoadedManifestDigest` covers the exact manifest UTF-8 plus the explicitly selected
quota, retention and sorted unique pin policy. Its domain-separated SHA-256 frame
contains a four-byte big-endian length before each part. Operators use the digest
returned by authenticated catalog status; changing local policy cannot reuse an
old digest as different intent. The manifest remains trusted deployment input,
while an HTTP command only selects that loaded digest, a reason and the original
key/precondition.

## Native resource authority and HTTP workflow

The existing native Security provider establishes the actor. Register capabilities
and complete grants for the Management service's actual application/environment:

| Capability | Actual resource |
| --- | --- |
| `management.configuration.catalog.read` / `.catalog.apply` | `management.configuration.catalog/<catalogId>` |
| `management.configuration.definitions.read` | `management.configuration.source/<sourceId>` |
| `management.configuration.content.read` | The actual source of discovered defaults or an immutable publication |
| `management.configuration.validate` / `.sources.publish` | The actual provisioned source |
| `management.configuration.operations.read` | The original catalog or source resource |

Read `/management/v1/configuration/catalog` to obtain its stable ID, loaded digest
and strong ETag. POST `/configuration/catalog/apply` with one `Idempotency-Key`,
one strong `If-Match` and the typed catalog command. Initial migration creates
only an empty root and readable precondition. Authorized apply preallocates or
reuses the ordinary Management application/environment rows, updates their
existing capacity counter, and provisions stable binding/source IDs and empty
assignment preconditions.

GET `/configuration/definitions` pages source-qualified descriptors. Native
resource authorization filters before pagination. A common descriptor exposes
no application, binding or host facts; an application descriptor supplies all
three. Raw defaults require separate content-read authority; omission redacts
them and does not change the internal complete defaults contract. Cursors bind
the actor, authority/recovery epoch, endpoint and page size. They confer no access.

Use a discovered source ID with GET `/configuration/sources/{sourceId}` to obtain
its current ETag and immutable history metadata. POST its `/publications` child
with the original key/precondition and exact `payloadJson`. GET
`/configuration/publications/{publicationId}` resolves the real source and
authorizes content before loading its bytes. History/metadata authority does not
grant value disclosure. Validation is a side-effect-free POST, not publication.
The actual immutable publication must also fit the server's one-MiB response
encoder. Literal inbound JSON can be smaller than its escaped response. That
representability check occurs after original-key arbitration and before content,
source revision or occupancy changes; refusal records one payload-free original
rejection and required Audit. It never stores a publication whose normal GET
would fail encoding. Release/assignment separately checks the complete resolved
pair with maximum supported target/session overhead.

## Original durability and session binding

Catalog apply and publication use two fixed RM-03 families, native actor/resource
scope and the original RM-02 `management` context. The effect, original result and
required RM-06 outgoing Audit commit together. Audit contains bounded IDs,
digests, preconditions/reasons and native capture provenance, never source/default
values. A newly prepared result requires known commit. Unknown completion retains
the original key and an explicit uncertainty code; it is not permission to rerun.

Matching replay happens before the effect examines the old ETag or today's loaded
manifest. A changed local manifest can therefore recover a committed earlier
catalog decision. Replays repeat neither effect nor Audit. The two recovery GET
routes reauthorize the actual retained resource and native actor; original results
contain no payload and remain independent of later eligible content deletion.

Catalog changes preserve logical resource IDs and immutable history. Reapplying
the already applied deployment leaves catalog/source ETags and assignments intact.
Ordinary replicas serve the explicitly applied state; loading another file does
not silently change it. Unsupported or uninitialized capability is never presented
as complete publication support.

A new registration captures its native issuer/subject/application/environment
cohort mapping and the current binding metadata in the same original admission
transaction. Registration replay does not consult or acquire today's cohort.
Older registrations without this capture need fresh bootstrap for central use.
Application version and binding metadata are observations, not physical-process or
consumer-compatibility proof. Bootstrap's expected cohort comes from independent
trusted deployment input (null selects the expected default), and cannot overwrite
the server's frozen mapping or be learned solely from a configuration snapshot.

## Migration, storage and evidence boundary

Explicit migration upgrades versions one through three to version four, preserving
existing applications, metadata, authority, original result scopes and outgoing
Audit. Repeated migration validates the retained authority/root rather than minting
new ones. Ordinary startup performs no repair or DDL. Version three installs the
release/map/override/session/adoption layout upfront; subsequent stories add their
behavior over this layout.

The persisted catalog owns the shared logical byte quota. Definition charges are
encoded definition bytes plus 64; bindings are UTF-8 host bytes plus 128; source
streams are UTF-8 environment bytes plus 128; an initialized map is 128; cohort
mappings are UTF-8 issuer/subject/cohort bytes plus 64. Catalog metadata is 160 plus
encoded pin bytes. Publications charge exact UTF-8 payload, actor and reason bytes
plus 128. Captured sessions charge encoded bindings and cohort bytes plus 128.
These count logical content/metadata, not PostgreSQL pages or arbitrary host heap.
Required operation-result and Audit stores retain their independent limits.
The service's resource bounds are its finite manifest, per-request/source bounds
and configured concurrent-request admission. The separate 16-MiB aggregate
source/mapping allowance belongs to the consumer Configuration runtime; it is
not a claim that the product server has one process-wide heap quota.
Quota admission is serialized on the same catalog row before effects. Existing
bounded cleanup releases charged session/adoption rows with their registration
and prunes eligible release/publication history using one shared row allowance.
See [reference-safe retention](configuration-retention.md) for protected content,
the explicit per-binding/per-source history policy and original-result recovery.
It adds no timer or journal.

Focused native test source covers initial/stable IDs, schema migration, two-replica
original-key arbitration, exact Unicode bytes, required-Audit rollback, source
preconditions, metadata/content denial, manifest-reload recovery and frozen
cohorts. The existing real-provider commit boundary also injects acknowledgement
loss after a real native catalog/publication commit and a fault after actual
transaction disposal. Original-key recovery preserves that commit even when the
new read finishes NotCommitted with cleanup diagnostics. Required-Audit refusal
proves the distinct no-effect/no-journal path; a later NotFound lookup performs no
new mutation. These controlled provider faults are not network or credential
proof. Source is not passing evidence; the current change records the actual
serialized PostgreSQL/RabbitMQ/HTTPS executions separately.
