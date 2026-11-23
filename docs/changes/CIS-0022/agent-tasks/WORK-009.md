---
title: "WORK-009 Deliver explicit durable authority schema and packaged installer"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-009
parent_id: WORK-002
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: "WORK-002"
instance_key: "outcome:037e912bc9444d0bb078252681962430"
depends_on: ["WORK-001"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0022:outcome:31de62abefb947ed8b11c51cc37ba560"
authorization_scope_ids: ["postgres"]
authorization_scope_basis: "sha256:bd66902a55a0565d4f8d16c016cf5dff750d21449f44af1af101b0b41ad79046"
---

# WORK-009: Deliver explicit durable authority schema and packaged installer

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Deliver explicit durable authority schema and packaged installer

## Acceptance criteria

- [x] Deliver the production explicit authority metadata/grant schema and persistent loggedBIGINT CACHE1 NO CYCLE allocator, required SQL embedding/contentFiles package wiring and public installer/options. Installation refuses existing/damagedallocator ratherthan reset acknowledgedhistory; actualschema validation and cleanup preservegeneration. Include packagedresource/optionboundary units, real installation/corruption/exhaustion/prune/restart proof and deployment/permissionsdocumentation.

## Targeted validation

- [x] Use the same currentreal ownedPostgreSQLfixture and affected build/nativecases with explicit installer/resource extraction. Validate exactlyowned schema/prune/recreate/restart persistence and allocatorproperties; share real provider proof with sibling withoutduplicatingcampaign. Preserve originalparentWORK002cost/attempts and sharedfiniteallocation.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |

| Production/source and independent review | Passed | BCL PostgreSQL provider, exact mutations/observation/pruning, durable explicit schema installer, authority checks and original-context guard; portable/provider guides. Root review and existing native commit-boundary integration included. |
| Pinned scoped formatting/build | Passed | postgres-format/verify, postgres-fixture-format/verify and postgres-fixture-build, SDK10.0.401/runtime10.0.12; zero warnings/errors. |
| Focused public/provider units | Passed | The current WORK003 Windows/Linux60-case reports include9 PostgreSQL option/constructor/guard/cancellation boundary rows. These Linux rows do not use SQL. |
| Real PostgreSQL acceptance | Passed | WORK002/postgres-authority-reviewed-tests/results/postgres-authority-reviewed.trx: actual11/11 passed,0failed,0skipped on Windowsx64 with ownedPostgreSQL18.7 and fsync/full_page_writes/synchronous_commit on. Includes native contention, post-lock expiry, protected commit/takeover, stale operations, exact Unknown recovery, allocator integrity/exhaustion and clean retained-volume restart. |
| Exact packaged SQL installer | Passed | schema-core-package/packages/CIS.Core.0.1.0-dev.1.nupkg includes contentFiles/any/any/CIS.Coordination/PostgreSql/install-v1.sql with exact source SHA2566cc8cba0dcd9f9d33dcfa4a36185e654f5a8eaa80eb2c64ca0d7265e44213d78. Schema packaging only; final full-module package consumption remains WORK007. |
| Failed fixture attempt | Preserved | Initial11-case native attempt failed during fixture registration before authority SQL because the Other alias omitted required isolation declaration. Narrow fixture correction reran the same11 successfully; originalTRX/log/cost preserved. |
| Owned restoration | Passed | After actualtesthostexit,0ownedcontainers/0ownedvolumes; all12preexistingforeignvolumes preserved. Pinned imagecache remains inside charged2GiBDockerallowance for later owned fixtures. No global prune. |

Evidence boundaries: actual Linux ELF unit execution passed, but this Ubuntu instance has no local Docker Unix socket; no Linux SQL or full platform qualification is asserted. The restart is a clean same-container/same-volume restart, not power-loss/replication/split-authority proof. Callback fencing outside the original transaction and final integrated RM03/RM04/package stories remain with existing downstream owners. Both sibling product leaves reuse this current proof under WORK002 funding; no duplicate campaign or cost reset occurs.
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2190; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:1025efb5e815016944eb5e9e396f2889f887871aaa2e439543d9b3e195fd98a0 |
