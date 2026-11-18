---
title: "CIS-0010 impact analysis"
type: impact-analysis
status: Draft
change_id: CIS-0010
graph_build_id: "sha256:887b3ae20a87ae34b9c0a99cd53df2d5d6855f2a5426fa1c27b4e61377e2c2a2"
analysis_truncated: false
authority: human-reviewed
---

# Impact analysis

Findings remain proposals until a recorded human decision or eligible derivation supplies their disposition. Derived rows retain original authority separately from the controller action.

## Findings

| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IMPACT-248CA586EE | implementation | cislib::source-file::implementations/csharp/src/CIS.Core/Heartbeat/Hosting/HeartbeatHostingExtensions.cs | HeartbeatHostingExtensions.cs | proposed | high | implementations/csharp/src/CIS.Core/Heartbeat/Hosting/HeartbeatHostingExtensions.cs:file=implementations/csharp/src/CIS.Core/Heartbeat/Hosting/HeartbeatHostingExtensions.cs | Explicit analysis root. |  |
| IMPACT-5E7BF2C291 | implementation | cislib::source-file::implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs | StartupHostingExtensions.cs | proposed | high | implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs:file=implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs | Explicit analysis root. |  |
| IMPACT-0A6F0B7DC8 | implementation | cislib::source-file::implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsCollectionContext.cs | MetricsCollectionContext.cs | proposed | high | implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsCollectionContext.cs:file=implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsCollectionContext.cs | Explicit analysis root. |  |
| IMPACT-129F3399C5 | implementation | cislib::source-file::implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsStreamCatalog.cs | MetricsStreamCatalog.cs | proposed | high | implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsStreamCatalog.cs:file=implementations/csharp/src/CIS.Metrics.OpenTelemetry/MetricsStreamCatalog.cs | Explicit analysis root. |  |

## Coverage

- Total findings: 4
- Accepted: 0
- Proposed: 4
- Rejected: 0
- Deferred: 0
- Traversal truncated: false
