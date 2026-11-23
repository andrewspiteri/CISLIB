---
title: RM-07 portable contract checkpoint
type: document
status: Draft
change_id: CIS-0024
---

# Portable contract checkpoint

WORK-001 freezes the twelve typed v1 APIs and the shared C#/schema/fixture
boundary for independent server and client implementation. It establishes no
service, SQL, native authentication, TypeScript HTTP or deployment acceptance.

| Selected native check | Actual result |
| --- | --- |
| C# Management wire/model/source-boundary units | 88 passed, 0 failed/skipped |
| Independent schema, literal-value/outcome and OpenAPI checks | 60 passed, 0 failed/skipped |
| Affected Core/CommonTesting/unit/Architecture builds | 0 warnings/errors |
| Ordinary scoped pinned-SDK whitespace format and verification | Passed |

Execution uses SDK 10.0.401 and .NET/ASP.NET 10.0.12. Local commands, logs, TRX,
source estimates and actual elapsed costs remain in
`.artifacts/management/CIS-0024/WORK-001/` and
`.cis/local/management-intake/`. Passing phases are
`corrected-contracts-wire-tests` and `portable-schema-openapi-final-tests`.
The schema selection includes 56 independent fixtures and four structural,
lossless/outcome and required OpenAPI route/example/reference checks. Its
structural result is separate from the full runtime expectation of each fixture.

The first native run passed 85 wire cases and 56 of 57 Architecture cases; the
failure was a brittle lowercase-word assertion against explanatory prose. The
corrected test retains the real structured schema/runtime distinctions. Later
wire changes preserve native empty-versus-whitespace descriptions and exclude
exact `.`/`..` operation keys to prevent URI path normalization; focused
regressions and both first results remain retained. No failed cost was reset.

Independent review corrected and verified initial Jobs configuration revision
zero, category identity as `(executionScope, categoryId)`, complete native owner
fields, resource-specific execution observations, UTF-8 byte versus Unicode
scalar bounds, precise timestamps and nested additive response definitions.
The published schemas distinguish supported absolute ceilings from initial
runtime limits; the defaults remain 32 modules, 64 categories, 1,024 jobs and
4,096 total job observations. No silent truncation or new runtime framework was
introduced.

The review explicitly found no material product-responsibility or verification
machinery expansion. Wire validation reuses the existing strict JSON primitive;
schema tests remove only documented annotations before the existing evaluator.
The final OpenAPI check uses finite BCL JSON traversal, not a new validator or
runner. Native service/client work retains its own eligible allocated owners.

## Integration correction owned by WORK-005 and WORK-007

The first contract checkpoint remains retained. Actual RM-03 integration showed
that native Rejected retains only OperationError, not the full typed metadata
value. The unreleased v1 envelope now explicitly admits one nonnull typed result
or native rejection, retaining native business decision and original completion
without inventing metadata/revision/time or changing the foundation coordinator.
The canonical schema uses the existing evaluator's anyOf subset; eight additional
independent vectors bring the catalog to64. Reported application/service versions
also follow the actual512-scalar environment contract, with native boundary tests.

Current subsequent proof:129 CSharp management units passed with zero failures or
skips; the pinned TypeScript compiler,90 Node cases and the browser DOM/types:[]
compile passed. Native service/Audit/current-package/cross-language acceptance
continues under its active owners and is not inferred from those component passes.
