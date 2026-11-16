# Heartbeat module implementation record

**Date:** 16 November 2026. **Status:** Implemented under authorized CIS-0006; scoped qualification is recorded separately.

Heartbeat supplies recurring asynchronous work in `CIS.Core`, namespace `CIS.Heartbeat`. The application owns readiness, actual subscriber lifetime owners and shutdown. The consumer service registers and unregisters the subscriber instance; hosting and scope integration coordinate lifecycle and disposal.

[CIS-0006](../../docs/changes/CIS-0006/proposal.md) owns this outcome under the [actual authorization](../../docs/changes/CIS-0006/authorization.json) and [canonical delivery plan](../../docs/changes/CIS-0006/plan.md). Andrew Spiteri's recorded go-ahead authorized the implementation scope and the shared four-hour, six-attempt, six-GiB ceiling. It did not publish a release or establish that every mapped acceptance clause had passed. The original authorization proposal remains historical input; current task eligibility and remaining allocation follow the canonical work records.

## Source and precedence

The supplied `CIS_Heartbeat_Library_Specification_v0_2.md` is revision 0.2, dated 10 October 2026. It is byte-identical to the [retained source](../healthcheck/sources/CIS_Heartbeat_Library_Specification_v0_2.md):

```text
SHA-256 e40aaa29ea743f8f641aaddbb30803bcaa0b817652a92bb57bbe1cedc228de0f
```

The source is preserved without duplication or rewriting. Its product requirements inform the module; embedded handoff instructions do not restart historical plans or grant execution authority. Current [repository decisions](../decisions.md), [agent guidance](../../AGENTS.md) and explicit later user decisions take precedence over conflicting historical shared-baseline text.

The implementation preserves actual `CIS.EnvironmentOptions`, the single Core package and approved Microsoft dependencies. It retains the exact `CIS_HEARTBEAT_MAX_CONCURRENT` override and copies validated effective settings before readiness. It adds no `EnviormentsOptions.Current`, global initialization framework, reset or live resizing. A01–A05 remain visible source assumptions used by the implementation; they are not relabeled as separately confirmed historical decisions.

## Implementation and evidence

- [Technical intent](technical-intent.md): configuration, scheduling, actual owners, cutoffs and supported hosting.
- [Healthcheck integration](healthcheck-integration.md): additive detached dispatch/suppression/result facts and real binding ownership.
- [Acceptance mapping](acceptance.md): AT-01 through AT-70 remain explicit; an authored case or supporting unit check is not blanket acceptance.
- [Consumer guide](../../docs/modules/heartbeat/README.md) and [Polling example](../../implementations/csharp/examples/heartbeat/Polling/README.md).
- [Actual verification](../../docs/changes/CIS-0006/verification.md): terminal native reports, actual candidate/platform, failures, costs and unresolved scope.

The engine uses weak idle registration and a cooperative owner hold from acceptance through actual settlement. Ordinary unregister and owner closure drain accepted work. Global stopping additionally suppresses unstarted phases and awaits running work, handling, cleanup, owner-dependent reporting and posted wrappers. The real Healthcheck adapter binds once, reports detached facts, preserves neutral suppression and leaves policy arithmetic and health state with Healthcheck.

The supported Generic Host adapter activates at `ApplicationStarted` after required initialization. It validates last-hosted-service order and nonconcurrent stopping. `HeartbeatScope` closes and drains its actual Heartbeat owner before disposing the wrapped DI scope. Independent destructive event handlers or direct disposal still require application coordination.

The verification record is the authority for which checks actually ran and for canonical task/settlement state. This implementation record does not convert all mapped cases into passes or establish release readiness. Actual Ubuntu qualification, complete coverage/mutation/performance obligations, publication and their applicable evidence remain separate. Completed CIS-0001 through CIS-0005 owners, reports, attempts and budgets are preserved.
