---
title: "Implement the owner-safe Heartbeat module"
type: change-proposal
record_format: proportionate
status: "Closed"
change_id: CIS-0006
outcome: "Applications register recurring asynchronous work by subscriber identity through an owner-bound CIS.Heartbeat service in CIS.Core; the shared scheduler honors readiness, fixed-delay timing, bounded fair dispatch, weak idle ownership, safe unregister and shutdown barriers, and detached Healthcheck facts using the existing repository conventions."
baseline_kind: git
baseline: "099a25f202935d14ee4c396abd51ebbff6c1e4f1"
graph_build_id: "sha256:06f14646a170f396110b04ddcf3342a23e6cf3b2c06e4137426252028ceaf388"
impact_roots: "[{\u0022Id\u0022:\u0022cislib::source-file::implementations/csharp/src/CIS.Core/Healthcheck/Contracts/Execution/HealthExecutionFact.cs\u0022,\u0022Kind\u0022:\u0022source-file\u0022},{\u0022Id\u0022:\u0022cislib::source-file::implementations/csharp/src/CIS.Core/Environment/EnvironmentOptions.cs\u0022,\u0022Kind\u0022:\u0022source-file\u0022}]"
repository_baselines_path: creation-inputs.json
repository_baselines_sha256: "sha256:be944a2fdb2ab2866315ccd8adcef63cd041edac370be79480932a3138069ccf"
authority: derived-outcome-authorization
---

# CIS-0006: Implement the owner-safe Heartbeat module

## Outcome

Applications register recurring asynchronous work by subscriber identity through an owner-bound CIS.Heartbeat service in CIS.Core; the shared scheduler honors readiness, fixed-delay timing, bounded fair dispatch, weak idle ownership, safe unregister and shutdown barriers, and detached Healthcheck facts using the existing repository conventions.

## Impact roots

- `cislib::source-file::implementations/csharp/src/CIS.Core/Healthcheck/Contracts/Execution/HealthExecutionFact.cs` (source-file)
- `cislib::source-file::implementations/csharp/src/CIS.Core/Environment/EnvironmentOptions.cs` (source-file)

## Constraints

- Necessary tasks and supporting work derive from the recorded outcome authorization.
- Changes outside its scope or overall limits require a specific decision.

## Acceptance criteria

- AC-1: Applications register actual subscriber instances with an optional condition and name. Valid duplicates preserve all existing bindings; completed unregister permits sequential reuse. Default interval is 10 ms, with the source's retained completion-delay assumptions visible.
- AC-2: Before readiness no user work runs. Eligible work progresses fairly within validated MaxConcurrent (1–16), with one accepted operation per instance and no more than MaxConcurrent unfinished dispatched operations. Conditions, callbacks, error handling and cleanup never overlap for one instance.
- AC-3: Ordinary unregister and owner closure drain accepted work. Global shutdown suppresses unstarted phases and waits for actual running work, handlers, cleanup, reports and wrappers. Wait cancellation cannot release ownership, reopen starts or claim disposal safety.
- AC-4: Idle registration does not root subscribers, bound conditions, owner scopes or request context. Accepted work acquires a real owner hold before queueing; owner and enclosing scope/root disposal wait for its true settlement. Collection/unusability retires safely.
- AC-5: The adapter binds existing Healthcheck policies and reports detached facts, including distinct dispatch and neutral suppression observations, without failure arithmetic, manufactured callback outcomes or recursive OnError. Existing public enum values and older wire payloads remain valid.
- AC-6: A developer can use the hosted polling example from source or the development package with explicit readiness and safe scope/root shutdown. Focused native tests cover all applicable mapped acceptance cases and existing affected contracts on the actual execution platform; unexecuted platform and release obligations remain visible.
- AC-7: The single CIS.Core package, approved dependencies, actual EnvironmentOptions architecture and existing module behavior remain intact. Necessary documentation, review, verification and settled accounting use this change's separate approved allocation; completed owners stay unchanged.

See the [source register and design](../../../planning/heartbeat/README.md),
[acceptance mapping](../../../planning/heartbeat/acceptance.md),
[implementation scope and limits](implementation-plan.md) and
[actual authorization](authorization.json).
The recorded user go-ahead authorized this outcome and shared finite limits. The original [authorization proposal](authorization-proposal.json) remains historical input; actual observations and remaining obligations are in [verification](verification.md).

<!-- cis:outcome-view -->

## Authorized outcome and limits

Derived from [the canonical authorization](authorization.json), decision `CISLIB-HEARTBEAT-2026-11-16:USER-GO-AHEAD`, recorded for Andrew Spiteri. Tasks reference this shared context; their organization is not a separate human review.

Implement the Heartbeat v0.2 recurring asynchronous-work module in CIS.Core using the current repository configuration/dependency conventions, actual owner lifetime barriers, bounded fair scheduling, safe application lifecycle integration and detached Healthcheck facts; deliver focused native Windows evidence and a usable source/development-package example without claiming release qualification.

- `heartbeat`: Deliver AC-1 through AC-7 in proposal.md using planning/heartbeat and implementation-plan.md. Include public contracts and copied settings, weak instance registration, owner holds, timing/dispatch/execution, unregister/shutdown/settlement, minimal additive Healthcheck dispatch/suppression/result context, explicit Generic Host and owner-scope integration, example, affected native tests/contracts/fixtures, guides, review and truthful accounting. Paths: `.cis/local/testing`, `README.md`, `conformance/healthcheck`, `contracts/healthcheck/v1`, `docs/catalog.yml`, `docs/changes/CIS-0006`, `docs/modules/healthcheck`, `docs/modules/heartbeat`, `implementations/csharp/CisLib.slnx`, `implementations/csharp/examples/heartbeat`, `implementations/csharp/src/CIS.Core/Environment`, `implementations/csharp/src/CIS.Core/Healthcheck`, `implementations/csharp/src/CIS.Core/Heartbeat`, `implementations/csharp/tests`, `planning/decisions.md`, `planning/healthcheck`, `planning/heartbeat`.

- Andrew Spiteri approved the immediately preceding CIS-0006 implementation scope and shared caps (4 active hours, 6 logical attempts, 6 GiB counted storage) by replying: ok let's go aheqd. This input records that actual decision and does not enlarge it.
- Build affected projects and run meaningful focused native tests, preserving ordinary performance/stress exclusions. Record actual environment and outcomes. No forced result reuse, report rewriting, full coverage/mutation/stress/performance campaign or unsupported platform claim.
- CIS requires outside-local counted paths to be marked scratch. The implementations/csharp root is counted only: that schema marker grants no source, user-file or historical-output deletion authority. No cleanup controller is delegated in this input.
- Count existing files in declared roots before reservation. Count-only roots confer no cleanup authority. Preserve historical tooling and reports; native scratch cleanup is limited to generated output actually owned by this change, within verified workspace paths. Stop for a specific new decision if finite capacity cannot support safe settlement.
- Organize concrete executable leaves at most medium and validate task contracts before allocation/execution. All agents share these totals; no task or agent receives a fresh copy of them.
- Preserve every completed CIS-0001 through CIS-0005 owner, approval, report, cost, attempt and limit. This is new shared capacity, never a reopened historical allowance.
- Preserve the single CIS.Core package/assembly, approved runtime dependency closure, actual CIS.EnvironmentOptions model and existing module behavior. No generic startup framework, Heartbeat health arithmetic, watchdog, automatic retry or GmCore compatibility shim.
- Retain source A01-A05 as visible design assumptions, not fabricated prior human decisions. Preserve the supplied specification bytes and current repository precedence.
- Windows implementation/package evidence is in scope. Actual Ubuntu release qualification remains open unless separately authorized with an available environment. No source transmission externally, main merge, package publication or new deployment-profile claim.

The total is 14,400 active seconds and 6 logical attempts, including 300 protected restoration seconds. Storage is 6,442,450,944 bytes including existing files and 67,108,864 protected reserve bytes.
Owned roots: `.artifacts/heartbeat/CIS-0006` (count only), `.cis/local`, `implementations/csharp` (count only).
Controllers: Codex. Task organization: delegated; resource distribution within the total: delegated.
Visual UI: not applicable; manual-test catalogue: no obligation recorded.
<!-- /cis:outcome-view -->
