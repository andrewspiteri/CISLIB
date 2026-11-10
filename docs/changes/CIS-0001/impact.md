---
title: "CIS-0001 impact analysis"
type: impact-analysis
status: Reviewed
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
| IMPACT-492EEAD2A2 | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Fsrc%2FCIS.Core%2FCIS.Core.csproj/csharp/method/CIS.Healthcheck.HealthDiagnostic.FromException%28Exception%2C%20string%3F%2C%20string%3F%29 | CIS.Healthcheck.HealthDiagnostic.FromException(Exception, string?, string?) | rejected | high | implementations/csharp/src/CIS.Core/Healthcheck/Contracts/HealthDiagnostic.cs:line=57 | Related through calls at depth 1. | Context only: this shipping method is exercised by the selected existing test but is not changed by CIS adoption. The profile still hashes and exercises the current implementation; no separate implementation task is required or authorized. |
| IMPACT-B086751FDB | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Ftests%2FCIS.Core.UnitTests%2FCIS.Core.UnitTests.csproj/csharp/method/CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ScalarCount%28string%29 | CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ScalarCount(string) | rejected | high | implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthDiagnosticRuntimeAcceptanceBoundaryTests.cs:line=46 | Related through calls at depth 1. | Context only: the selected test already exercises its unchanged ScalarCount helper within the same two native cases. This change requires no helper implementation change or separate task. |
| IMPACT-C74657D845 | implementation | cislib::symbol::unowned/cislib/project/implementations%2Fcsharp%2Ftests%2FCIS.Core.UnitTests%2FCIS.Core.UnitTests.csproj/csharp/method/CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic%28bool%29 | CIS.Tests.UnitTests.Healthcheck.HealthDiagnosticRuntimeAcceptanceBoundaryTests.ValidCallerIdentifiersAndRuntimeExceptionProjectionProduceADetachedDiagnostic(bool) | accepted | high | implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Foundation/HealthDiagnosticRuntimeAcceptanceBoundaryTests.cs:line=13 | Explicit analysis root. | Accept the existing parameterized test method solely as the bounded CIS configuration, accounting-association and native verification scope. Preserve implementation and test source, assertions, native toolchain, profile/filter and release scope; execution still requires an approved plan and finite allocation. |

## Coverage

- Total findings: 3
- Accepted: 1
- Proposed: 0
- Rejected: 2
- Deferred: 0
- Traversal truncated: false
