---
title: "CIS-0008 impact analysis"
type: impact-analysis
status: Draft
change_id: CIS-0008
graph_build_id: "sha256:fd9b45ba6b94f0a95a15a6ee7a39b6f556af21f591bccdf6e084d61c51000d80"
analysis_truncated: false
authority: human-reviewed
---

# Impact analysis

Findings remain proposals until a recorded human decision or eligible derivation supplies their disposition. Derived rows retain original authority separately from the controller action.

## Findings

| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IMPACT-BA302F90DA | documentation | cislib::document::cislib:spec:configuration-supplied-intent | Configuration supplied technical intent | proposed | high | docs/imports/technical/cb1e5fd4a71b4bfe92a5994af4050414/CIS_Configuration_Module_Specification_v1.0.md:document=cislib:spec:configuration-supplied-intent | Explicit analysis root. |  |
| IMPACT-7E21858304 | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Fsrc%2FCIS.Core%2FCIS.Core.csproj/csharp/class/CIS.Startup.Hosting.StartupHostedService | CIS.Startup.Hosting.StartupHostedService | proposed | high | implementations/csharp/src/CIS.Core/Startup/Hosting/StartupHostingExtensions.cs:line=32 | Explicit analysis root. |  |

## Coverage

- Total findings: 2
- Accepted: 0
- Proposed: 2
- Rejected: 0
- Deferred: 0
- Traversal truncated: false
