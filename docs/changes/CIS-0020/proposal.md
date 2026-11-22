---
title: "Correct RM-04 consumption continuation and broker readiness"
type: change-proposal
record_format: proportionate
status: Proposed
change_id: CIS-0020
outcome: "Keep valid concurrent RPC consumption live under bounded response pressure, quarantine a misrouted Reply without poisoning canonical state or stopping consumption, and honor the broker-readiness option with focused regression tests and current documentation."
baseline_kind: git
baseline: "0a77508e8a8327760a6cff77b1f5fa0f2f0c454b"
graph_build_id: "sha256:6aabff349f3252510b97ab31b16e35abdbf7377fd26f0db16dcc352abbb77b33"
impact_roots: "[]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:dde07b8da424c73403bdcff627c7c77a2f513929dd7aee89066cc2cad0dcf146"
authority: derived-outcome-authorization
---

# CIS-0020: Correct RM-04 consumption continuation and broker readiness

## Outcome

Keep valid concurrent RPC consumption live under bounded response pressure, quarantine a misrouted Reply without poisoning canonical state or stopping consumption, and honor the broker-readiness option with focused regression tests and current documentation.

## Impact roots

No roots declared. Supply roots to `cis impact analyse`.

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- Deliver the authorized outcome and its task-specific acceptance criteria within the shared constraints and limits.

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-RM04-2026-11-22:review-0a77508:three-required-corrections:delegated-proceed`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Correct the three required review findings at 0a77508e: transient RPC response-buffer pressure must defer bounded admission without permanently stopping consumers; misrouted Reply envelopes must obtain canonical-free durable quarantine and permit subsequent valid delivery; RequireLiveBrokerForReadiness must control post-start broker readiness. Include focused tests, documentation, independent review and exact owned-fixture restoration.

- `messaging-corrections`: The three confirmed review defects and their direct callers, bounded regressions, documentation, CIS lifecycle and owned native fixture proof. Paths: `.artifacts/messaging/CIS-0020`, `.cis/local/messaging-review-intake`, `docs/catalog.yml`, `docs/changes/CIS-0020`, `docs/modules/messaging`, `implementations/csharp/src/CIS.Core/Messaging/Contracts/MessagingOptions.cs`, `implementations/csharp/src/CIS.Core/Messaging/PostgreSql/PostgreSqlMessagingStore.Publication.cs`, `implementations/csharp/src/CIS.Core/Messaging/PostgreSql/PostgreSqlMessagingStore.cs`, `implementations/csharp/src/CIS.Core/Messaging/Runtime/MessageDeliveryDispositionCoordinator.cs`, `implementations/csharp/src/CIS.Core/Messaging/Runtime/MessageDispatcher.cs`, `implementations/csharp/src/CIS.Core/Messaging/Runtime/MessagingResourceBudget.cs`, `implementations/csharp/src/CIS.Core/Messaging/Runtime/MessagingRuntime.cs`, `implementations/csharp/tests/CIS.Core.IntegrationTests/Messaging`, `implementations/csharp/tests/CIS.Core.UnitTests/Messaging`, `planning/messaging/reviews`.

- Current pasted review is the user's request. The preceding RM04 direction explicitly delegates completing the module without further authorization requests. Codex selects this finite follow-up profile under that continuing delegation; no separate human review of these exact generated numeric choices is claimed. CIS-0019 remains complete with all original costs, attempts, evidence and limits preserved; this new correction owner does not reset them.
- Honor readiness option after successful startup for subscription and typed-request responsibilities. Preserve staging-only behavior, initial broker startup, store/loop/shutdown gates and native provider recovery ownership.
- One shared21600active seconds/8logical attempts including900restoration seconds covers all agents, already-performed intake, implementation, tests, docs, formatting, independent review and cleanup. Retain conservative source estimates and measured native elapsed separately from automatic counters.
- Only fresh isolated pinned PostgreSQL18.7 and RabbitMQ4.4.1 Docker fixtures, generated private credentials/TLS, loopback bindings and strict owner labels; same finite512MiB/2CPU/container controls as CIS-0019. No existing DB/broker/operatorcredentials/trust store/key-container access. Verify actual child drain then delete only exact newly owned resources/private inputs, no engine prune.
- Preserve user untracked CountryData launchSettings SHA256ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784. No push, merge, release or external deployment in this correction request.
- Preserve6GiB6442450944physical ceiling and64MiB67108864protected reserve over.artifacts/.cis/local/implementations/csharp, existing8MiBnative+16MiBcache charges, and fixed2GiB Docker allowance INSIDE that ceiling. Measure actual admission and keep all historical/protected/accounting/user material.
- Rejection purpose may retain an original Reply as quarantine evidence; only actual Reply purpose populates reply_to_message_id. Do not broaden Reply into application/retry/terminal/redrive publication or poison canonical inbox state.
- Reuse the current codex/messaging-reliable-delivery checkout and original wire v1/schema v2/public dependency pins. No new package, schema migration, DI/bus/reconnect/retry framework, test harness, tooling/release campaign or maintained example.
- Serialize shared SDK/native builds and runtime startup/SQL callbacks. Safely parallelize independent source files. Focus Windows/actualUbuntu regressions and affected Messaging/Operations units/native cohorts; reuse unchanged material proof honestly. Preserve unrelated Heartbeat intermittency and single-node/LinuxSDK/deployment/release evidence limits.
- Use one atomic normal-byte reservation for input plus prospective response before decoding; retain bounded work/encoded admission, reserved reply lane, original transaction/native authority, exact immutable wire bytes and actual cleanup/drain. Do not await response capacity while holding a separate input lease.

The total is 21,600 active seconds and 8 logical attempts, including 900 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
