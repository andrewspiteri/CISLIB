---
title: WORK-005 independent source review
type: review
status: Complete
---

# WORK-005 independent review

The assigned reviewer inspected the frozen Operations observation source,
passive hosting wiring, optional Metrics composition/settings/catalog changes,
the focused Unit and Integration test source, and the operation metric contract
and observation guide. The review used AGENTS.md, the actual Metrics skill and
Core/collection contracts, the Operations coordinator guide, implementation plan
and reconciliation P14. This reviewer is independent of the source author.

No material source correction was identified in the assigned observation scope.
Actual focused native evidence was subsequently aligned as recorded below.

## Assessed boundaries

- The scoped decorator resolves only a registered immutable definition before
  capture. Unknown aliases produce no observation. It captures one complete
  optional bundle before entering the coordinator, therefore before resolution
  or authorization, and completes in finally after the inner method has actually
  returned or faulted. Lookup timing consequently includes the coordinator's
  awaited read-owner rollback and disposal. Late attachment cannot replay the
  active call or divide it between host bindings.
- The sequential coordinator remains the protocol owner. The decorator neither
  authorizes, normalizes, arbitrates, retries, commits nor settles transactions.
  Prepared and replayed business decisions map to distinct fixed outcomes;
  Prepared never becomes a committed transaction observation. RM-02 retains
  native phase, commit, cleanup and transaction-duration responsibility.
- The observation retains only the bounded alias, method/option flags, captured
  logger/metric bundle, clock and local timing/completion state. It retains no
  context, request, caller, scope, key, fingerprint, business result, exception
  or event history. The first completion fixes at most one recording attempt.
  A missing/throwing start or end timestamp, nonfinite duration or negative
  duration omits the timer instead of manufacturing zero.
- Ordinary timestamp, logger factory/enablement/write and metric failures are
  guarded. Completion diagnostics in finally cannot replace an original
  authorization/audit, handler/codec/native, cancellation or cleanup fault.
  Requested cancellation is classified using the supplied token; an unexpected
  cancellation fault remains failed. The wrapper does not catch authorization
  or audit work as optional diagnostics. Fatal process/runtime failures remain
  outside the ordinary-diagnostic guard.
- LogCalls is copied into immutable definition metadata and defaults false.
  Normal summaries are Debug only when selected. Failed/rollback-only summaries
  contain only registered alias, method, fixed outcome and nullable seconds,
  without an exception or sensitive values. Expected cancellation stays quiet
  by default. No native transaction error facts are repeated in that summary.
- The optional context derives selection from final passive composition, then
  checks the exact registration-owned instrumentation identity before attaching
  its one host runtime. The two conditional timers use only operation/outcome,
  the existing central identity path and duration buckets. The catalog reserves
  16 x 13 = 208 execute and 16 x 9 = 144 lookup combinations; their sum is 352.
  Existing default Core/inbound/RM-02 composition therefore changes from
  24 streams/3,456 combinations to 26/3,808. Explicit undersized operation,
  stream-count or total-capacity profiles fail rather than silently growing.
  The global limits remain unchanged. Disabled collection/Core opt-out adds no
  automatic operation streams or operation handles.

## Necessity and responsibility

The direct two-method decorator is necessary to observe the complete public
boundary without coupling the pure sequential coordinator to diagnostics. One
passive singleton binding owner, two reusable handles and one small call-local
observation implement the accepted host/timing requirements directly. Existing
MetricsAttachment, MetricsRuntime, central identity, SDK aggregation/exporter
and native transaction observations are reused. No second provider, aggregator,
scheduler, transaction framework, DI framework, lifecycle framework or runtime
dependency was introduced by this observation change.

The tests remain ordinary C# MSTest methods in existing projects. The bounded
boundary stub tests fixed outcome mapping; the existing actual RM-02 owner and
controlled journal/data source exercise orchestration and cleanup; the SDK test
uses the existing real exporter/Kestrel route. These are focused seams with
named observation responsibilities, not a SQL simulator, alternative application
or test harness. Their controlled journal result does not establish committed
PostgreSQL effects or durable atomicity.

## Evidence boundary

The initial source review inspected authored tests, including
finite outcome mapping, actual cleanup gating, entry capture/late attachment,
ordinary diagnostic faults, quiet logging and option capture, clock failure,
cancellation, registration orders, explicit capacity rejection, absent/disabled
collection, exact owner mismatch, exporter identity and host isolation. No test
was launched by this reviewer. Authored assertions alone were not treated as
passing evidence.

The reviewer subsequently independently aligned all fourteen current source/doc
hashes in `.artifacts/operations/CIS-0018/WORK-005/observations-receipt.json`, the
source-output/Unit/Integration Core copies, optional collector source-output and
Integration copy, both current test assemblies and all six exact TRX hashes.
No mismatch was found. Current Core SHA-256 is
`f2576bc062d64f95180bd79e644e56865a8f69d8fe12ff97a13ac6a33f988329`;
collector SHA-256 is
`8687e1c6b3dc40b6333e542c827c8db9b416ed3e9efae399e9a1f8529127c071`.

Both actual Windows and Ubuntu24.04 lanes contain exactly 116 passing Operations
Unit cases (32 observation cases plus 84 retained foundation/coordinator cases),
11 passing OperationCollectionTests SDK cases and 31 passing affected catalog
cases (Transaction 10, RestClient 11, Security 10). All six reports have zero
failed/not-executed cases. TRX result-to-definition class membership/counts match
the receipt, including all eight Operations Unit classes. This verifies actual
test outcomes rather than inferring them from authored methods.

The current SDK test source asserts 26 streams/3,808 configured combinations,
the exact 208/144 reservations and standard buckets in both registration orders,
explicit small-profile failures, opt-outs, owner mismatch and actual exporter
identity/Prepared meaning/cleanup timing/host isolation. These are the asserted
profile and behaviors of the eleven passing SDK cases. The unchanged pinned
global.json selects SDK10.0.401, and both current test runtime configurations pin
.NET/ASP.NET10.0.12 with Disable roll-forward. The coordinator reports clean
affected Unit/Integration Release builds and scoped formatting/verification;
this independent evidence pass aligned current bytes and TRX rather than
rerunning those commands.

This observation checkpoint does not establish PostgreSQL atomicity, retention
maintenance, real network-fault behavior, final WORK-006 package consumption,
deployment, platform-wide, coverage/mutation, performance or release
qualification. The WORK-004 retention and final WORK-006 outcomes remain separate.
Earlier WORK-001/002 evidence retains its exact historical candidate and scope.

Reviewer accounting: estimated ten active source minutes, including instruction
reading, source/test/document assessment and this record, within the assigned
fifteen-source-minute cap and existing shared WORK-005 allocation. No product
native execution, Git/CIS mutation or additional logical work attempt was
performed by this reviewer. Only this review document was written.

Subsequent receipt/current-byte/TRX alignment and this update used an estimated
three additional active source/evidence minutes within the separately assigned
three-minute cap, charged to the same WORK-005/shared allocation. No native
execution was repeated.
