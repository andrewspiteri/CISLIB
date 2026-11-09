# Healthcheck decisions

The user selected the Healthcheck Technical Intent/contracts as the working baseline on 20 October 2026. The following product decisions remain current; historical pilot dispatch and build-ownership instructions are retired.

| Decision | Accepted direction |
| --- | --- |
| HC-D01 | `Microsoft.AspNetCore.App` is approved in the single `CIS.Core` package. HTTP integration uses `CIS.Healthcheck.AspNetCore`; no separate adapter package. |
| HC-D02 | `HealthcheckOptions.Resolve(setup)` explicitly reads the eight module variables once per call. Constructors validate/copy selected options without rereading. Preserve readonly static `EnvironmentOptions` identity behavior. |
| HC-D05 | Preserve the two approved Microsoft runtime NuGets and Core-only package-pruning exception. Framework/package identities remain separate concerns. |
| HC-D06 | Original definitions remain reference specifications under `sources/`. The current technical intent/catalog and targeted addenda record accepted amendments. Frozen HC00 development fixture hashes are not released compatibility history. |
| HC-D09 | Use existing native test clocks/signals/barriers where adequate; no runtime dependency on TestSupport. |
| HC-D11 | Actual Heartbeat and generic startup-provider integration remain absent and unaccepted. Local binding examples do not close these cases. |
| HC-D12 | Internal state access uses the real service/factory and monitor identities. No global lookup, pre-created binding monitor or registry retention of owner graphs. |

The separately approved 21 October bounded registration diagnostic is preserved in [its amendment](registration-diagnostic-amendment.md). [Collection](collection-contract-amendment.md) and [execution](execution-contract-amendment.md) addenda preserve the necessary settlement/binding semantics. Current commands and open release obligations live in [README](../../README.md) and [current decisions](../decisions.md). These records authorize no publication.
