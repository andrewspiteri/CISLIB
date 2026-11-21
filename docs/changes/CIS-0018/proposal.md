---
title: "Durable operations and idempotency (RM-03)"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0018
outcome: "Deliver an explicit authorized durable operation journal over RM-02, preserving one committed protected effect per retained scoped identity, original business-result replay, safe recovery and retention, a provider-free PostgreSQL adapter, bounded existing observations and public testing/docs/skill; keep RM-04 and broader release work separate."
baseline_kind: git
baseline: "8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:3305943631559b7ff67ac349ec6fac252726cf766cf0c94b7deef37f5f9aa5ab"
authority: proposed
---

# CIS-0018: Durable operations and idempotency (RM-03)

## Outcome

Deliver an explicit authorized durable operation journal over RM-02, preserving one committed protected effect per retained scoped identity, original business-result replay, safe recovery and retention, a provider-free PostgreSQL adapter, bounded existing observations and public testing/docs/skill; keep RM-04 and broader release work separate.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- TODO: define outcome-level acceptance criteria before plan approval.

## Requested intake and source authority

The user requested import through CIS, reconciliation with current contracts and
a bounded implementation plan. The attached implementation handoff is preserved
under planning/operations/sources; its imperative to implement is requirements
context, not permission to extend this turn. CIS-0018 is Proposed; no outcome
approval, resource allocation or eligible native owner is recorded.

The actual starting main and refreshed origin/main both match
8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc. The dedicated branch is
codex/durable-operations. The one pre-existing untracked CountryData launch
settings file is preserved with its recorded hash and excluded from the intake.

## Reconciled plan and proposed execution request

Use [implementation-plan.md](implementation-plan.md) for the six medium tasks,
P01-P18 profile, acceptance ownership, dependencies and safe parallel work.
[reconciliation.md](reconciliation.md) records the actual RM-02/RM-01/Metrics
contracts and routine defaults. [dependency-profile.md](dependency-profile.md)
keeps the existing dependency closure and proposes only a fresh owned loopback
PostgreSQL fixture after approval.

One proposed shared allocation is32 active hours/36 logical attempts, including
3600 restoration seconds, under the existing physical6GiB ceiling/64MiB reserve,
with1GiB Docker counted inside it. All agents/intake/code/tests/docs/review/
restoration share these totals; organization/distribution is delegated to Codex.
No new package, mapping, SDK/runtime upgrade, existing database access, external
deployment, push, merge, release or broad qualification campaign is included.

Drafts task-contracts.json, proposed-allocation.json and authorization-request.json
are concrete review inputs. After an actual user decision, the supported outcome
and task commands can establish canonical contracts and finite execution owners.
The current proposal does not approve itself or reopen completed historical owners.
