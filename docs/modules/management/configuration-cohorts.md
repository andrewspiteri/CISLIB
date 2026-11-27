---
title: "Trusted Configuration cohort choices"
type: guide
status: Active
owner: Repository maintainers
last_reviewed: 2026-11-26
review_cadence: on Configuration discovery or assignment contract change
cis:
  stable_id: cislib:guide:management-configuration-cohorts
---

# Trusted Configuration cohort choices

The optional cohort read supplies existing rollout choices for one exact
application/environment, Configuration ID and schema binding:

```http
GET /management/v1/applications/{applicationId}/configuration/{configurationId}/cohorts?schemaVersion=1&pageSize=100
```

The selected qualified backend must implement
[`IManagementConfigurationCohortBackend`](../../../implementations/csharp/applications/CIS.Management.Service/Backends/IManagementConfigurationCohortBackend.cs).
It is an optional companion; existing `IManagementConfigurationBackend`
implementations acquire no compulsory member. Service discovery advertises the
operation only when the selected initialized backend supplies it. A visible
binding on an unsupported backend returns 501
`management.configuration.cohorts.unsupported`, not an empty successful list.

The existing host configuration callback may explicitly register this scoped
Configuration port; the default registration preserves that selection. The
ordinary qualified PostgreSQL backend, main Management port, provider ownership
and sequential startup remain the same. This does not qualify another adapter.

The API does not create cohorts, enroll subjects, publish settings, replace
assignments or start scheduler work. It reads the existing RM08 authority.

## Which choices are returned

The PostgreSQL reader follows the same admissibility predicate used by complete
assignment-map admission. It unions distinct nonnull cohort IDs from:

- deployment-controlled `configuration_cohort` mappings for the actual
  application UUID;
- `configuration_session` cohorts captured when native registrations for that
  application were first admitted;
- current `configuration_override` entries retained by this exact binding.

The application UUID fixes its environment. The store re-resolves the enabled
binding and checks its application, environment, Configuration ID and schema
before reading choices. It does not accept a browser-supplied application name,
issuer, subject or membership mapping.

A changed trusted manifest can replace today's mappings without rewriting an
already admitted registration's frozen cohort. That retained cohort therefore
remains discoverable. A current override can also retain a choice after its
mapping disappears. An override from a different binding is not an origin for
this binding. Once every legitimate origin disappears, discovery no longer
offers that cohort; assignment admission independently rechecks the current
origins on every write.

Explicit release-retention pins do not themselves identify a cohort. Historical
operation payloads, release names and arbitrary typed text do not create choices.
A missing cohort is not synthesized as `default`: the default release remains
the existing separate field in the complete assignment map.

## Disclosure and labels

Each item contains only `cohortId` and `displayName`. The current trusted
manifest has no separate cohort label, so the qualified reader uses the exact
cohort ID as its truthful display-name fallback. It does not infer a business name
from credentials or invent a new label registry.

No issuer, subject, registration ID, member list, member count or hidden total
leaves this query. A retained cohort is not proof of a live instance, current
membership count or successful adoption. Inspect the independently authorized
[adoption observations](configuration-adoption.md) for their actual meaning.

Cohort IDs retain the portable operation-key profile: 1–128 URI-unreserved ASCII
characters, excluding the complete dot segments. Display names are bounded to
256 Unicode scalars. Consumers render these values as text.

## Authorization and paging

Every request resolves the actual binding and requires current
`management.configuration.definitions.read` permission on that native resource.
Absent or unauthorized bindings retain the existing not-found behavior.
Provider, uninitialized-catalog and unclean-read failures remain explicit failures.
Reading choices grants neither settings/default-value disclosure nor permission
to publish, create releases or replace assignments.

`schemaVersion` is a required positive Int64 decimal string. The only optional
query fields are `pageSize` and `cursor`; duplicate/unknown fields, malformed
percent-encoded UTF-8, invalid scalar values and request bodies refuse the read.
Page size defaults to 100 and is bounded to 500.

Choices use exact ASCII ordinal order and keyset continuation after the last
disclosed cohort ID. The backend admits at most 501 output rows for lookahead;
each of its three ordered distinct sources is also limited to that finite
selection. This bounds returned/materialized choices, not a claim that the
database examines a fixed number of physical rows. The qualified native read
uses the existing readiness, cancellation and clean-completion checks.

The opaque cursor binds the actual binding/schema endpoint, requested page size,
authority/namespace/recovery epoch and current query-only actor/profile/policy/
validated-principal scope. A token for another binding, schema, actor, page size
or ordering is invalid. The position is a disclosed cohort ID, not an internal
UUID or member locator. Query scope remains separate from durable original-result
actor scope.

Each page is a reauthorized live view, not a frozen fleet snapshot. A manifest,
session or assignment can change between pages. Restart the list when the
current binding, schema or policy context changes. No page exposes a total or gives a later
mutation permission.

## Typed clients and later writes

Both existing optional administration clients use this route and the existing
Configuration-family result/problem semantics:

- C#: [`IManagementConfigurationAdministrationClient.GetCohortsAsync`](../../../implementations/csharp/src/CIS.Core/Management/Scheduler/IManagementApplicationCatalogClient.cs).
- TypeScript: [`ManagementConfigurationAdministrationClient.getCohorts`](../../../implementations/typescript/packages/management-client/src/configuration-administration-client.ts).

They return a bounded `Page<ConfigurationCohortChoice>` through their actual
language-specific HTTP wrapper. The row has no independent binding echo;
native resource authorization and exact query scope remain server duties. The
clients preserve their existing per-call credentials, deadline and joined
cleanup behavior. No polling or new runtime is selected.

The [source/binding action reads](configuration-service.md) remain advisory.
Publication, release creation and complete assignment replacement still require
their selected write capability plus original-result read capability on the same
actual native resource. Result lookup authorizes independently under current
lookup permission. Cohort selection changes none of the original-key, strong
ETag, complete-map, transaction/Audit or recovery rules. An obsolete or invented
choice can still be refused at actual map admission.

See the [portable Scheduler/catalogue companion contract](../../../contracts/management/v1/scheduler-contract.md)
for wire shapes and [Configuration distribution](configuration-distribution.md)
for release and assignment ownership. The attached RM10 BRD is contract
identification material; this cohort query implements no Scheduler store,
producer, worker adapter or recurrence.

Use the [WORK154 owner](../../changes/CIS-0030/agent-tasks/WORK-154.md) for actual
native execution and passing counts.
This guide does not claim that source review or authored tests establish a
passing run, external deployment or release qualification.
