---
title: "Application identity and security (RM-01)"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0016
outcome: "Import RM-01, reconcile existing identity, native security, REST and lifecycle contracts, and prepare bounded implementation work for three usable profiles, scoped authorization, credential renewal and consumer guidance. Implementation and new dependency decisions await explicit profile and finite allocation approval; live AKS and cert-manager proofs require separately available authorized infrastructure."
baseline_kind: git
baseline: "b53b4eaef6b3374e7387e9bbb065b57533248445"
graph_build_id: "sha256:bf7b91eedf53d5acb325e7a28a86adbe0cbd4db60e8b092665eeb97c0a5a3cd5"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:4b0f451d5e3a265eb954d04e5b1febb964a50dabd61c1c3880ebe6d2fb53ac1a"
authority: proposed
---

# CIS-0016: Application identity and security (RM-01)

## Outcome

Import RM-01, reconcile existing identity, native security, REST and lifecycle contracts, and prepare bounded implementation work for three usable profiles, scoped authorization, credential renewal and consumer guidance. Implementation and new dependency decisions await explicit profile and finite allocation approval; live AKS and cert-manager proofs require separately available authorized infrastructure.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.
- The attachment is preserved as requirements at planning/security/sources. Its agent handoff grants no execution or dependency authority. Existing completed owners remain closed.
- Proposed P01–P16 and seven coherent outcomes include affected tests, documentation and review. The concrete optional dependency profile and one shared finite allocation await user approval.
- Preserve canonical environment identity, native authentication/authorization, complete scoped grants, one provider, sequential startup, existing REST/lifecycle ownership, central Metrics limits and the user's untracked launch settings.
- No new identity server, policy language, scheduler, HTTP stack, CA service, audit store, test harness, broad qualification campaign, external deployment, push, merge or release is included.

## Acceptance criteria

- Three real profile implementations use native facilities: explicit Entra workload/JWT, direct certificate mTLS and isolated LocalDevelopment certificates. Package installation is inert; roles and bindings are explicit.
- Trusted immutable snapshots and scoped caller provenance feed one complete-grant native evaluator. Per-operation boundaries reject spoofing, incomplete grants, wrong targets and unusable credentials before protected work.
- Existing REST safely binds profile/origins and at most two credential generations by default, preserving actual response drain, retry/deadline/cancellation ownership and native failure observations.
- Current CountryData service/caller, portable fixtures, profile/deployment guides, three optional-package consumer shapes, bounded instrumentation and checked-in security skill use actual implemented APIs and focused native evidence once authorized.
- AC-02 and AC-24 remain expressly unverified until suitable authorized AKS/cert-manager environments exist; local tests do not mark RM-01 complete.

## Prepared review artifacts

- [Preserved source and current-contract reconciliation](../../../planning/security/README.md)
- [P01–P16, seven outcomes, acceptance ownership and shared allocation](implementation-plan.md)
- [Optional packages and finite source-mapping candidates](dependency-profile.md)
- [Intake evidence and authority boundary](verification.md)
