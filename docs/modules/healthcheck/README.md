# Healthcheck

`CIS.Healthcheck` records application-owned health facts in the single `CIS.Core` assembly. A service creates monitor handles, captures immutable snapshots, and records explicit startup/stopping facts. Reading a snapshot or transport endpoint never invokes a collector or changes health.

The current C# candidate targets `net10.0` and includes the approved `Microsoft.AspNetCore.App` framework reference. HTTP integration is in `CIS.Healthcheck.AspNetCore`; it is not a separate adapter package. The development example uses a source project reference. Exact-package example qualification, both claimed platform lanes, release coverage/mutation/compatibility evidence, and publication remain separate work.

| Guide | Content |
| --- | --- |
| [Usage and options](usage.md) | Monitor policies, snapshots, configuration precedence, defaults, and errors. |
| [Collection and lifetime](lifetime.md) | Weak handle retention, actual owner leases, explicit collection activation, binding, and shutdown. |
| [HTTP, local pipe, and deployment](transports.md) | Reserved routes, details authorization, matching probe, permissions, and Kubernetes fragments. |
| [CountryList example](country-list.md) | Explicit application startup, retained failed startup, cached data and refresh separation, and development execution. |

The working contract is [the Healthcheck contract catalog](../../../planning/healthcheck/contract-catalog.md), with task-specific internal revisions recorded in [HC02](../../../planning/healthcheck/evidence/HC02/contract-amendment.md) and [HC06](../../../planning/healthcheck/evidence/HC06/contract-amendment.md). The [preserved definition](../../../planning/healthcheck/sources/CIS_Healthcheck_Library_Definition_v0_1.md) supplies the complete acceptance scope; it does not prove implemented or executed behavior. Check each task's receipts in [Healthcheck evidence](../../../planning/healthcheck/evidence) for exact executed candidates and boundaries.
