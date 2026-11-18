---
title: "WORK-001 Compose Core application setup and pre-DI configuration defaults"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-001
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:b52b1820700f4719b8fa9a35b40a6907"
depends_on: []
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0011:outcome:ad93dd647e1f41ad9dac004ce6061dda"
authorization_scope_ids: ["application-facade"]
authorization_scope_basis: "sha256:2ba6ff96550e6f8403209e849521c15a7c0224d9e53d4a659df08abb25579550"
---

# WORK-001: Compose Core application setup and pre-DI configuration defaults

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Compose Core application setup and pre-DI configuration defaults

## Acceptance criteria

- [x] AP1 / APP-01,APP-03–APP-06: freeze a thin CIS.Hosting.CISApplication builder and required constructor/web/lifecycle signatures; collect normal services/host and existing module options/named REST choices without starting work or temporary providers. Compose explicit Core defaults and supported replacements, prepare exact Type Manager and retained Configuration roots before one final Build, put Configuration activation before user participants preserving order. Add an explicit defaults-only Configuration mode using existing catalog/reader/validation with no external read/watch/polling and a Core no-op IMetrics fallback after optional contributions. Preserve all real-store modes/failures and Core closure. Include focused unit/architecture tests, guide and independent review.

## Targeted validation

- [x] A01–A03/A05: constructor forms compile, setup causes no async/module/listener work; defaults-only constructor data and exact prepared/root identities; external store failures never fallback, existing None/RequiresReconciliation requirements remain; supported Replace/RemoveAll/disable semantics and all root enrollment order. Core-only closure has no SDK/Polly dependency. Freeze shared seams before dependent parallel work.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Component correctness | 17 passed, zero failed/skipped/aborted; current source, zero pending batches | `.cis/local/application-intake/core-escalated-observed.trx`; `native/incremental/aff98128b819495295f4666da67d020b` |
| Source and dependency review | Thin native-host API/composition, explicit defaults-only and prepend seams; Core closure unchanged | Core/Application, Configuration and Metrics source; independent review in [verification](../verification.md) |
| Scope of evidence | Component/setup proof complete; actual web/lifecycle/exporter/CountryData proof remains successor work | [Bounded plan](../implementation-plan.md) and [Configuration guide](../../../modules/configuration/hosting.md) |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1434; failed=215; possibleTokenSavings=591; ledgerDigest=sha256:5370b1567a50e3157780b8a9ef48dd590b41db5e0e549b979fe154c4f0d7c0a8 |