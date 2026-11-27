# Bounded portal discovery

Application list callers may add `query` and `environment` to
`GET /management/v1/applications`. `query` is a literal **case-sensitive** substring
of trusted application identity or current display name, with one through128
control-free Unicode scalars. Spaces, composed/decomposed Unicode, `%` and `_`
remain exact; no trimming, normalization, case folding or wildcard matching
occurs. `environment` is an exact trusted identity of one through255 scalars.
Omit either field to leave it unrestricted. Empty, duplicate, unknown or malformed
UTF-8/escaped query fields are invalid. Filtering happens in PostgreSQL before
stable UUID paging, then every candidate passes actual native authorization.

`GET /management/v1/environments` pages unique environments in UTF-8 byte ordinal
order. An environment is disclosed only after an application in that environment
passes current native resource authorization. SQL does not expose an unfiltered
`DISTINCT` list. Continuation contains the last disclosed environment; hidden
application identifiers and hidden environment positions never become cursor
evidence. The backend can skip the remaining applications of a disclosed group.

`GET /management/v1/applications/{applicationId}/configuration-definitions` first
resolves the visible application. Its exact native application/environment pair
selects application sources before source-ID paging and independent
`DefinitionsRead` checks. Common sources are excluded. The response uses existing
definition DTOs and always omits `defaultsJson`, including for an operator with
`ContentRead`. It transports metadata for this application, not current values,
effective central assignment or inferred membership of common definitions.

These APIs use existing page sizes1–500, default100, and response byte limits.
Each request scans at most4,096 candidate records and honors request cancellation.
If native authorization cannot produce a complete truthful page within that
budget, `503 management.discovery.scan_limit` replaces the entire response; it
does not turn exhaustion into empty success or an invented complete list.
Clients should present an unavailable state and permit explicit retry or narrower
application filters. There is no unbounded fleet scan in the portal.

New application-list, environment and scoped-definition cursors bind the actual
actor/resource, authentication profile, fixed mapping-policy version, exact
filters, and a bounded digest of authenticated native ticket claims. Only the
digest is retained; no bearer token or claim values are logged. Native claim
inputs are bounded to256 claims and64KiB encoded fields. A changed/renewed ticket,
policy, target, filters or page size can invalidate continuation: restart the
list. Every row is reauthorized even when the cursor remains valid. Cursors are
live views, not snapshots; concurrent metadata changes can change matches.

Original-operation recovery keeps its separate `ManagementActorScope` semantics
and is unaffected. Existing unfiltered methods and backend contracts remain
source/wire compatible; newly issued application cursors use this current read
scope even when filters are omitted. Cursors issued before this change may need
a list restart.

`IManagementDiscoveryBackend` and
`IManagementApplicationConfigurationBackend` are optional companions. The complete
PostgreSQL adapter implements both. Existing custom backends still serve original
unfiltered lists; selected unsupported queries return501 and are absent from
actual feature discovery. The C# clients add `QueryApplicationsAsync`,
`ListEnvironmentsAsync` and `GetApplicationDefinitionsAsync`; default interface
methods keep existing custom implementations compatible and explicitly unsupported.
The TypeScript clients add `queryApplications`, `listEnvironments` and
`getApplicationDefinitions`; the last method is optional on the existing
structural substitution port. Portable packages gain no portal/framework imports.

Native REST observations deliberately group application queries and environment
enumeration under the existing `applications` operation alias. The public
`queryApplications`/`listEnvironments` APIs and exact HTTP routes stay distinct;
this related read family adds no metric dimension or registered operation pair.
Existing operation aliases and the shared32-pair REST budget remain unchanged.
