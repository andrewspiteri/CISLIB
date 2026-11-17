---
title: Type Manager Metrics snapshots
type: document
status: draft
cis:
  stable_id: cislib.document.modules.typemanagement.metrics
---

# Type Manager Metrics snapshots

The optional Metrics integration observes the actual prepared manager's retained
`PreparationReport`. Application callers keep using `TypeManager.CreateAsync`
before building the container; they make no additional recording calls.

| Logical name | Observable value |
| --- | --- |
| `cis.typemanager.assemblies` | `SelectedAssemblyCount` |
| `cis.typemanager.types` | `TypeCount` |
| `cis.typemanager.initialization.duration` | `Elapsed.TotalSeconds` |

These are aggregate observable gauges with no ordinary dimensions. Assembly
identities, CLR type names, query contracts and cache keys are never labels.
Counts are known facts of the completed catalog; elapsed time comes from its
existing monotonic preparation measurement. Scraping does not discover assemblies,
repeat loading, populate query caches or activate application objects.

An immutable report may supply independent hosts. It retains no Metrics runtime,
provider or host claim; each host owns its callback registration and drain. One
host stopping does not disable another host's report observation. No preparation
counter or histogram is replayed after provider activation. A failed preparation
returns no manager/report and therefore creates no synthetic failed-instance
measurement. Its original failure still propagates to the bootstrap caller.

The [portable catalog](../../../contracts/metrics/README.md#bootstrap-and-startup-catalog)
defines the shared meanings. Actual module/exporter checks and qualification are
recorded under [CIS-0009](../../changes/CIS-0009/verification.md).
