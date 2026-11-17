---
title: "Bounded RestClient execution and outgoing HTTP integration"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0010
outcome: "Prepare REST-01 through REST-10 and A01 through A22 for a portable CIS RestClient, native HTTP/content and effective cancellation, separately optional explicit retry execution, bounded automatic Metrics and real consumers. Preserve current module contracts and source inputs; implementation requires a later decision on the concrete plan, scope and finite shared allocation."
baseline_kind: git
baseline: "7b5923c031027b932f730e1cf45b2d51270b17cc"
graph_build_id: "sha256:887b3ae20a87ae34b9c0a99cd53df2d5d6855f2a5426fa1c27b4e61377e2c2a2"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:24b2f9ea49a3c5ccea6ea4cb53d5d1883fd19474073abc6d02a606c5cac7906c"
authority: proposed
---

# CIS-0010: Bounded RestClient execution and outgoing HTTP integration

## Outcome

Prepare REST-01 through REST-10 and A01 through A22 for a portable CIS RestClient, native HTTP/content and effective cancellation, separately optional explicit retry execution, bounded automatic Metrics and real consumers. Preserve current module contracts and source inputs; implementation requires a later decision on the concrete plan, scope and finite shared allocation.

## Impact roots

Bounded deterministic review covers the existing Metrics collection context and
stream catalog, and the Core Startup/Heartbeat hosting cutoffs. The findings in
[impact.md](impact.md) remain proposed; they do not adopt scope or approve execution.
The detailed new-module/package scope is in [the implementation proposal](implementation-plan.md).

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.
- Current user authority covers supplied-source intake, reconciliation and plan
  preparation. No RestClient implementation grant is recorded.
- Preserve Core's dependency closure, runtime pins, existing module contracts and
  sequential startup. Keep optional retries and the existing one-provider Metrics
  integration separate from ordinary consumer APIs.
- No new HTTP/resilience/DI/lifecycle framework, test harness, Configuration
  module, installable skill, broad qualification campaign or historical cleanup.
- The proposed twelve-hour/sixteen-attempt allocation is shared by all tasks and
  agents, includes restoration, and retains the physical six-GiB ceiling.

## Acceptance criteria

- REST-01–REST-07: native C# HTTP/content facilities behind portable contracts;
  guarded typed results, explicit absence/null semantics, ordered customization
  and authentication, typed expected failures and visible programming defects.
- REST-08–REST-09: explicit inherit/None/replacement policies, one upstream retry
  owner, replay and server-minimum delay rules, and entry-based cancellation that
  awaits actual owned cleanup before result completion or resource teardown.
- REST-10: one logical duration, balanced active work and actual payload counts;
  bounded exact-host outgoing native selection with mandatory identity and no
  duplicate provider or unbounded URL/operation state.
- A01–A22: meaningful focused component, actual host/export and genuine package
  consumer evidence, with matching portable fixtures, runnable examples and
  documentation; preserve actual platform, qualification and accounting limits.

## Review package

The [preserved sources and actual intake state](../../../planning/rest-client/README.md),
[P01–P12 reconciliation](../../../planning/rest-client/reconciliation.md),
[seven proposed task contracts and recommended limits](implementation-plan.md),
[acceptance map](acceptance.md) and [authorization proposal](authorization-proposal.json)
are preparation for a later implementation decision. The generated Draft WORK-001
seed is not the seven-leaf implementation plan or executable authority.
