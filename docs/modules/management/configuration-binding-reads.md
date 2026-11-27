---
title: "Exact Configuration release history and adoption reads"
type: guide
status: Active
owner: Repository maintainers
last_reviewed: 2026-11-26
review_cadence: on Configuration rollout read contract change
cis:
  stable_id: cislib:guide:management-configuration-binding-reads
---

# Exact Configuration release history and adoption reads

Two optional bodyless reads supply the existing RM08 facts needed to review one
Configuration binding. The application UUID fixes its actual environment;
`configurationId` and required canonical positive `schemaVersion` select the
enabled binding before history or recipients are paged.

```http
GET /management/v1/applications/{applicationId}/configuration/{configurationId}/releases?schemaVersion=1&pageSize=100
GET /management/v1/applications/{applicationId}/configuration/{configurationId}/status?schemaVersion=1&pageSize=100
```

The ordinary qualified PostgreSQL backend implements the independent optional
[`IManagementConfigurationReleaseHistoryBackend`](../../../implementations/csharp/applications/CIS.Management.Service/Backends/IManagementConfigurationReleaseHistoryBackend.cs)
and
[`IManagementConfigurationBindingAdoptionBackend`](../../../implementations/csharp/applications/CIS.Management.Service/Backends/IManagementConfigurationBindingAdoptionBackend.cs)
ports. Existing compulsory backend/client members are unchanged. Service discovery
advertises each operation only when the selected backend supplies that port.
A visible authorized target on a legacy backend returns 501 with
`management.configuration.releases.unsupported` or
`management.configuration.binding_status.unsupported`; this is not an empty list.

The C# `ManagementConfigurationClient` implements the optional
[`IManagementConfigurationBindingReadClient`](../../../implementations/csharp/src/CIS.Core/Management/Contracts/IManagementConfigurationBindingReadClient.cs)
methods `GetReleasesAsync` and `GetBindingStatusAsync`. TypeScript's existing
administration client supplies `getReleases` and `getBindingStatus` through the
separate optional `ManagementConfigurationRolloutClientPort`. Both use the current
Configuration transport, per-call native credentials, deadline and bounded wire
codec. No new profile, transport retry policy or startup activation is selected.

## Retained immutable releases

`ConfigurationReleasePage` contains the existing release records: exact binding
selectors, Common/Application source references, original actor/time/reason and
immutable release ETag. It has no source payload, fabricated display name,
current-release pointer or implicit assignment. Retention remains authoritative:
pruned unreferenced releases are absent from this retained list; original durable
operation results remain governed by their existing independent retention.

The reader uses the existing binding/creation-time/release-ID index, in creation
time descending then UUID descending order. UUID breaks equal-time ties; neither
UUID order nor a returned page establishes semantic version, latest publication
or the assigned release. Current assignment and pinned overrides remain the
separate complete assignment-map read. Each returned release ID is unique.

DefinitionsRead authorizes the resolved actual binding and every disclosed
release. Hidden releases are skipped before filling the visible page. Each call
scans at most 4096 retained rows, in batches of at most 16. Exhaustion returns 503
`management.discovery.scan_limit` and discards any partial visible prefix; it
does not claim the remainder is empty. A page contains at most 500 whole records
within the selected wire byte budget. There is no fleet/history scan in the UI.

## Adoption for this binding

The scoped status read retains the existing application-resource AdoptionRead
authority. It does not silently add AppRead or DefinitionsRead requirements.
The native query restricts the exact binding, Configuration ID and schema in its
target selection before cohort/session expansion and continuation. The original
`GET /applications/{applicationId}/configuration-status` remains the compatible
application-wide read.

Rows keep the existing desired assignment, reported accepted generation/local
revision, last successful version, pending restart/rejection/fault and separate
registration/adoption freshness. Missing observations remain unknown. The
explicit noInstances projection remains distinct from an unsupported backend.
An applied row is an observation for that recipient, not proof of fleet-wide
convergence, current permission or uninterrupted local state. No payload,
checksum, principal mapping or membership identity is added.

## Continuation and failure

Both reads accept only `schemaVersion`, `pageSize` 1..500 and an optional opaque
cursor. Unknown, repeated, differently cased, malformed UTF8/percent-escaped
query names/values, forged cursors and request bodies refuse with 400. Continuation
binds the actual binding/operation/page size and current actor, profile, policy
and bounded validated-principal claims. A changed or renewed ticket/policy or a
different selector requires restarting the list. Cursors do not grant authority;
the server reauthorizes every request and disclosed release.

The qualified store re-resolves the enabled binding under the existing catalog
read lock and completes its owned read transaction/cleanup before returning.
Malformed optional-backend tuples, rows, ordering, duplicate release IDs,
overlarge pages or uncertain rollback/cleanup return 503 without partial rows or
cursor. Only a known clean native absence can become 404. Native cancellation
drains the actual owned read; it does not authorize a write or infer a durable
business outcome. Publication, release creation, whole-map replacement and
original-key recovery retain their existing independent authority and ETags.

The focused native test source is
`ManagementConfigurationBindingReadTests`; client and independent schema/wire
cases live beside existing Management tests and conformance vectors. Execution
results and their exact candidate belong to CIS-0030 verification; this guide
does not declare a passing count or external deployment qualification.
