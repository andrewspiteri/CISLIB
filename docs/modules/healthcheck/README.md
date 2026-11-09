# Healthcheck

`CIS.Healthcheck` records application-owned health facts in the single `CIS.Core` assembly. Services create monitor handles, capture immutable snapshots and record explicit startup/stopping facts. Snapshot and transport reads do not invoke collectors or change health.

| Guide | Content |
| --- | --- |
| [Usage](usage.md) | Monitor policies, snapshots, configuration precedence, defaults and errors |
| [Collection and lifetime](lifetime.md) | Weak retention, owner leases, activation, binding and shutdown |
| [Transports](transports.md) | HTTP, local pipe, matching probe, permissions and deployment boundaries |
| [CountryList](country-list.md) | Application-owned startup, cache/refresh separation and owner drain |

The working [technical intent](../../../planning/healthcheck/technical-intent.md), [catalog](../../../planning/healthcheck/contract-catalog.md), [collection addendum](../../../planning/healthcheck/collection-contract-amendment.md), [execution addendum](../../../planning/healthcheck/execution-contract-amendment.md) and [registration diagnostics](../../../planning/healthcheck/registration-diagnostic-amendment.md) preserve accepted behavior. The [source definition](../../../planning/healthcheck/sources/CIS_Healthcheck_Library_Definition_v0_1.md) specifies the full acceptance scope.

HTTP uses the user-approved `Microsoft.AspNetCore.App` framework inside Core. The CountryList example uses explicit source references. Actual external Heartbeat/startup integration and current complete platform/package/coverage/mutation release qualification remain pending. Historical campaign receipts are retired and establish no current acceptance claim. Use the native commands in [README](../../../README.md) and the [current gap record](../../../planning/decisions.md).
