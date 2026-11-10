---
title: "CIS-0001 impact analysis"
type: impact-analysis
status: Draft
change_id: CIS-0001
graph_build_id: "sha256:4bfca6477c6d1b545bf66c31574359d1d49583ba15e1841dd15c27986276e163"
analysis_truncated: false
authority: human-reviewed
---

# Impact analysis

Deterministic findings are proposals until explicitly accepted, rejected, or deferred by a human.

## Findings

| ID | Category | Target | Label | State | Confidence | Evidence | Rationale | Review reason |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| IMPACT-492EEAD2A2 | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Fsrc%2FCIS.Core%2FCIS.Core.csproj/csharp/method/CIS.Healthcheck.HealthDiagnostic.FromException%28Exception%2C%20string%3F%2C%20string%3F%29 | CIS.Healthcheck.HealthDiagnostic.FromException(Exception, string?, string?) | proposed | high | implementations/csharp/src/CIS.Core/Healthcheck/Contracts/HealthDiagnostic.cs:line=57 | Related through calls at depth 1. |  |
| IMPACT-B086751FDB | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Ftests%2FCIS.Core.UnitTests%2FCIS.Core.UnitTests.csproj/csharp/method/CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ScalarCount%28string%29 | CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ScalarCount(string) | proposed | high | implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthDiagnosticRuntimeAcceptanceBoundaryTests.cs:line=46 | Related through calls at depth 1. |  |
| IMPACT-C74657D845 | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Ftests%2FCIS.Core.UnitTests%2FCIS.Core.UnitTests.csproj/csharp/method/CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic%28bool%29 | CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool) | proposed | high | implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthDiagnosticRuntimeAcceptanceBoundaryTests.cs:line=13 | Explicit analysis root. |  |

## Coverage

- Total findings: 3
- Accepted: 0
- Proposed: 3
- Rejected: 0
- Deferred: 0
- Traversal truncated: false
