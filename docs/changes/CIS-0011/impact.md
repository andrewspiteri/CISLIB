---
title: "CIS-0011 impact analysis"
type: impact-analysis
status: Draft
change_id: CIS-0011
graph_build_id: "sha256:0dbed0e4cc8c4e4b5fe5f2f7470be717e270917d879250939ca02f5b5287a84c"
analysis_truncated: false
authority: human-reviewed
---

# Impact analysis

Findings remain proposals until a recorded human decision or eligible derivation supplies their disposition. Derived rows retain original authority separately from the controller action.

## Findings

| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IMPACT-4D82A0F688 | implementation | cislib::source-file::implementations/csharp/examples/startup/CountryList/CountryCatalog.cs | CountryCatalog.cs | proposed | high | implementations/csharp/examples/startup/CountryList/CountryCatalog.cs:file=implementations/csharp/examples/startup/CountryList/CountryCatalog.cs | Explicit analysis root. |  |
| IMPACT-0599B331F0 | implementation | cislib::source-file::implementations/csharp/src/CIS.Core/Configuration/Hosting/ConfigurationRuntime.cs | ConfigurationRuntime.cs | proposed | high | implementations/csharp/src/CIS.Core/Configuration/Hosting/ConfigurationRuntime.cs:file=implementations/csharp/src/CIS.Core/Configuration/Hosting/ConfigurationRuntime.cs | Explicit analysis root. |  |
| IMPACT-5E7BF2C291 | implementation | cislib::source-file::implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs | StartupHostingExtensions.cs | proposed | high | implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs:file=implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs | Explicit analysis root. |  |
| IMPACT-DD96BCADAD | implementation | cislib::source-file::implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsServiceCollectionExtensions.cs | MetricsServiceCollectionExtensions.cs | proposed | high | implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsServiceCollectionExtensions.cs:file=implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsServiceCollectionExtensions.cs | Explicit analysis root. |  |

## Coverage

- Total findings: 4
- Accepted: 0
- Proposed: 4
- Rejected: 0
- Deferred: 0
- Traversal truncated: false
