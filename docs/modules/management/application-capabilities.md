# Application capability descriptions

`ManagementCapabilityDefinition` describes a trusted registered application
feature: a job, trigger provider or an already exposed rule-engine instance. It
does not implement a rule engine. `ManagementCapabilities.ImplementedApis`
continues to describe Management HTTP support; those endpoint descriptors are
separate from the application's feature catalog.

The exact logical key is owner application, kind, execution scope, definition ID
and positive Int64 definition-version string. Equality is ordinal and
case-sensitive. Registration ID, environment binding, application release and
build are separate facts. Changing a label, schema or build does not change a
job/resource coordination key.

The passive `ManagementCapabilityCatalog` captures immutable trusted versions.
Repeating the same version and description coalesces; conflicting content for
that version is refused, and a different version remains separately addressable.
No assembly scan, service resolution, handler construction, network I/O, schedule
evaluation or activation occurs. Current `JobCatalog` consumers remain unchanged.
Provisioning and actual catalog authorization belong to the Management service's
trusted deployment manifest, not presence reporting or a browser form.

Definitions distinguish persistent Configuration contract/shape, invocation
input, emitted event, optional result summary and explicit job execution support.
Each input/event/result contract must have its own declared shape. Configuration
references contain schema metadata without values or defaults. Actual settings
publication, immutable releases, assignment and owner adoption retain RM08's
existing owners. Description availability alone is never execution permission.

`ManagementJobExecutionSupport` supplies the shared exact execution/recovery
contracts and optional original Operations alias. It is permitted only for a job.
The immutable description freezes its supported-contract collections. The
scheduler validates actual active compatibility and workload identity later;
a newer release label or matching JSON shape does not establish support.

## Finite inert forms

`ManagementFormDescription` has copied read-only field, group and named-lookup
lists. Hints address exact declared object-member paths with JSON pointers
(`~1` escapes `/`, `~0` escapes `~`). Unknown members, invalid escapes, duplicate
paths, missing groups/lookups and known editor/type mismatches are refused.

Fixed editor names are `text`, `number`, `boolean`, `enum`, `duration`, `group`
and `calendar`. The enum uses the declared string choices. Duration declares one
of milliseconds, seconds, minutes, hours or days; number hints may also describe
bytes, count or percent. Group/calendar hints address objects. A calendar hint
does not provide a running evaluator or a next occurrence.

Names, labels, descriptions, ordering and read-only hints are data, not HTML,
scripts, React components, CLR types, regular expressions or URLs. A lookup names
a trusted owner/scope/name and portable contract. Its actual implementation,
resource resolution and native authorization remain with the admission service;
the form cannot choose an arbitrary endpoint.

Bounded unknown kind/editor/operation names can remain inspectable as unsupported
metadata (`IsSupported == false`). They cannot enter the trusted passive catalog
or make `DeclaresOperation` return true. A known declared action is still only
metadata: installed provider support and current resource authorization must be
checked independently.

The initial description profile is 64 KiB complete encoded metadata, 1,024
definitions, 128 form fields, 32 groups, 16 lookups and 16 declared operations.
Each structural declaration has at most 1,024 nodes, 256 object members and 256
enum choices; enum values have a 1,024-byte UTF8 ceiling. Structural and complete
encoded depth are at most16, so the enclosing description can impose a stricter
effective shape depth. Ordering is bounded to -10,000 through10,000. Existing
job scope/ID/category/display/description UTF8 byte limits remain
256/128/128/256/2,048. Application names retain the existing255-scalar limit.
Malformed Unicode, controls, overlong text and complete-envelope overflow refuse
the whole description rather than truncating it. Existing enclosing wire limits
may be stricter.

## Participation and evidence

`ManagementCapabilityParticipation` records an exact definition reference,
registration, inventory sequence, original observation and received timestamps,
reported availability/draining and optional actual build reference. Two instances
participating in one definition do not create two trusted definitions.
Unknown availability remains unknown; missing historical build remains absent.
Reported build metadata is attributable information, not binary attestation,
credential evidence, current lease authority or an authorization grant.

The capability unit cases cover detached collections/shapes, conflicting exact
versions, unsupported metadata, declared form references, paired payload schemas,
Unicode/depth/whole-description bounds and passive inspection of an actual Jobs
binding whose handler must never be constructed. Authoring or passing these
logical cases does not establish deployment, schema provisioning, real calendar
production, native identity, business execution or durable history. Those are
separate scheduler/service/runtime integration acceptance boundaries.
