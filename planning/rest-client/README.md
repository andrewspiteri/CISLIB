---
title: RestClient supplied requirements and intake
type: planning-reference
status: Draft
---

# RestClient planning intake

The requested RestClient module is prepared as [CIS-0010](../../docs/changes/CIS-0010/proposal.md)
against merged main `7b5923c031027b932f730e1cf45b2d51270b17cc`, on local branch
`codex/rest-client-planning`. This work imports and reconciles requirements and
prepares a bounded plan. It does not implement RestClient or allocate native work.

| Preserved supplied document | SHA-256 |
| --- | --- |
| [Implementation specification v1.0](sources/CISLIB_RestClient_Implementation_Specification_v1.0.md) | `93ba18c7f29113dd178a2fe70fd19dc24fbe8df03ab7a463b158dd8c85470a7e` |
| [Companion agent guide v1.0](sources/CISLIB_RestClient_Agent_Guide_v1.0.md) | `4babc76204c66cf897fd56afbe1a63b4c690e9c5f0b680e45286972f0dc28b1d` |

Both sources are preserved byte-for-byte. Embedded handoff prompts, imperative
workflow text and statements of prior decisions are source material. The current
user request authorizes planning; it does not approve every proposed numeric
default, dependency, reconciliation choice or implementation allocation.

Read the [bounded implementation plan](../../docs/changes/CIS-0010/implementation-plan.md),
[contract reconciliation](reconciliation.md), [dependency proof proposal](dependency-and-retry.md),
[Metrics/hosting proposal](metrics-and-hosting.md) and
[A01–A22 acceptance map](../../docs/changes/CIS-0010/acceptance.md).
The complete originals remain the requirements reference where these summaries
are selective. All described new REST APIs remain proposed.

## Actual CIS state

Installed CIS is `0.3.0+2b916962dbedef18bdde8959e3144184d37d64f9`.
The workspace still has one owned authority repository and documentation root
`docs`. The fresh graph and source review confirm the supplied baseline.

The feature-intake preview rejected `repositoryMode=existing` for this authority:
“Choose a product-owned participant with its existing documentation root. A
feature cannot reclassify an authority or external dependency.” No feature-intake
apply or Active feature was fabricated; the workspace was not reclassified.

The supported in-place change route created CIS-0010 with status Proposed.
`plan build` created a Draft, unauthorised WORK-001 seed. Its generic acceptance
and validation are planning placeholders, not the seven concrete proposed leaves.
`plan import-spec` was blocked by absent accepted impacts; the subsequent bounded
impact analysis produced four proposed findings. None was self-approved.

Safe technical-intent refresh kept the product BRD and technical intent Active
and unchanged. It generated `docs/plans/high-level-backlog.md` as Review Required
and stopped for backlog review. That generated draft is preserved; it is not an
approved RestClient feature or a request to rebuild the product foundation.

The seven leaf contracts and recommended limits are reviewable in this package.
After an explicit implementation decision, the existing outcome-authorization
route can bind concrete CIS tasks and one shared allocation. Current preparation
does not spend closed Metrics or other module grants. See
[actual preparation evidence](../../docs/changes/CIS-0010/verification.md) for
warnings and the distinction between a draft, an imported feature and execution.

Two pre-existing untracked launchSettings files are excluded from this work:
the startup CountryList example and the Metrics MetricApi example. Their recorded
hashes are retained in the verification record.
