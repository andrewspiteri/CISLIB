---
title: "CISLIB product business requirements — registration proposal High-Level Backlog"
type: high-level-backlog
status: Review Required
owner: Product owner and delivery leads
last_reviewed: null
review_cadence: on approved BRD or technical-intent change
cis:
  stable_id: cislib:plan:high-level-backlog
  backlog_schema: 3
  brd_hash: semantic-v1:sha256:6190ff89fbcb18f0e009e516f8cd1bafa6855e5891713c66dbc9eed57e3bc5ce
  technical_intent_hash: semantic-v1:sha256:90692b384b0041bb71ed8ae26100042726f34453dc27ead65f89fad14b52695c
  approved_by: null
  approved_at: null
  approval_reason: null
  approved_content_hash: null
---

# CISLIB product business requirements — registration proposal High-Level Backlog

This reviewed bridge decomposes each functional BRD requirement into one high-level product outcome. It is not an executable implementation plan. Accepted items become feature specifications and then change dossiers with bounded tasks.

## High-level items

<!-- cis:brd-backlog-items:start -->
| ID | BRD requirement | Outcome | Priority | Repositories | Frontend types | Depends on | Feature specification | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| HLT-CIS-BR-001 | CIS-BR-001 | Provide Logging, Type Manager and Healthcheck through the existing single `CIS.Core` package/assembly, independent of CIS orchestration and application-owned infrastructure. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-002 | CIS-BR-002 | Preserve the user-selected readonly static `EnvironmentOptions` entry point, constructor defaults followed by known overrides, bounded identity interning and module-owned explicit option resolution. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-003 | CIS-BR-003 | Preserve bounded Logging capture, rotating incoming storage, processing/interpolation, custom writers, independent output policy, correlation/scopes and explicit truthful lifecycle/delivery observations, including the selected 64 KiB whole-entry default. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-004 | CIS-BR-004 | Prepare the declared managed assembly set before DI construction, automatically generate the supported host inventory through the existing `CIS.TypeManager.Build` assets, expose actual CLR type identities and fixed prepared membership, and cache valid read-only queries without constructing application objects. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-005 | CIS-BR-005 | Preserve application-owned health state and monitor policies, detached immutable snapshots, explicit execution-fact binding and owner-safe optional collection without taking ownership of business retries or application shutdown. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-006 | CIS-BR-006 | Preserve bounded Healthcheck serialization, reserved HTTP reporting, local pipe/probe behaviour and host-owned access/deployment boundaries with the exact existing wire and transport contracts. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-007 | CIS-BR-007 | Retain meaningful native consumer examples, portable specifications and behavioural/wire fixtures, with current consumer compatibility and frozen development baselines honestly distinguished from published release history. | Must | cislib | none | none | not-created |  |
| HLT-CIS-BR-008 | CIS-BR-008 | Execute the applicable unit, integration, architecture, backwards-compatibility, business, coverage, mutation and useful performance verification required for publication; keep narrow checks and historical results scoped to what actually ran. | Must | cislib | none | none | not-created |  |
<!-- cis:brd-backlog-items:end -->

## Global acceptance obligations

These requirements constrain every applicable feature specification and the final delivery sweep; they are not silently converted into implementation tasks here.

<!-- cis:brd-backlog-obligations:start -->
| BRD requirement | Obligation |
| --- | --- |
| BRD-IMPORT-B8F4A406F3146CFE | Keep direct, readable C# with nullable analysis and warnings as errors. Use the retained native MSTest/VSTest workflow and ordinary IDE support. [S01, S07] |
| BRD-IMPORT-7986DC3280BC735C | Preserve the approved Microsoft runtime dependency closure and `Microsoft.AspNetCore.App`; third-party runtime integrations require separate optional packages. Development-only tooling and the Type Manager build helper must not enter the runtime dependency closure. [S01, S03] |
| BRD-IMPORT-AE093724F8E9AC04 | Preserve accepted 95% line / 90% conservative branch coverage and 90% conservative mutation release obligations across shipping Core and the build helper, including actual child activation and no Timeout kill credit. No new policy activation or fresh pass is implied. [S01, S07] |
| BRD-IMPORT-D5D05B5A2E4906CB | Windows 11 x64 and Ubuntu 24.04 x64 are the selected initial qualification lanes. Supported Type Manager deployment is framework-dependent, untrimmed .NET 10 in conventional build or folder-publish layouts. Actual qualification remains to be recorded for the current revision. [S01, S05] |
| BRD-IMPORT-5700F19A918FA499 | The recorded performance targets are 30 seconds warm, 60 seconds unchanged build plus tests and 300 seconds full regression. They remain unverified for the cleaned revision; this baseline does not create a benchmark campaign or increase an old allowance. [S01] |
| BRD-IMPORT-89279FAFA9754BB8 | Retain MIT licensing and maintainer-only official publication controls. The source material establishes no sector-specific regulatory certification or new data-residency guarantee. Host deployment and data policy remain the application's responsibility. [S01, S03, S06] |
<!-- cis:brd-backlog-obligations:end -->

## Review rules

- Every functional BRD requirement must appear exactly once.
- Reviewers may add dependencies, notes, and a feature-specification path without changing stable item identity.
- Frontend types identify public, customer, and backoffice scope; a later feature specification splits mixed UI scope into one requirement row per type.
- Approval authorizes feature-specification preparation, not implementation, deployment, or release.
