# RM-04 review correction plan

The pasted review of `0a77508e` requests three fixes before merge. All three are confirmed by independent source analysis. Keep CIS-0019's completed result and history; CIS-0020 owns this bounded correction.

One medium implementation task covers the three related consumption/readiness defects, their regressions, documentation, independent review and restoration. Codex selects one shared six-hour/eight-attempt allocation, including 900 restoration seconds, under the user's continuing explicit instruction to complete RM-04 without further authorization requests. This is delegated execution, not a claim that Andrew separately reviewed the generated numeric proposal. The physical 6 GiB ceiling and 64 MiB reserve remain unchanged, with the existing 2 GiB Docker allowance inside that ceiling.

1. Atomically acquire input and prospective response encoded capacity before decoding. The second request waits with no partial byte lease; cancellation and subsequent delivery must settle normally.
2. Preserve a misrouted Reply as opaque Rejection evidence. Only actual Reply records write reply correlation columns. Keep schema v2 unchanged and verify rejection publication followed by a valid Event with the same MessageId through the same consumer.
3. Apply the captured broker-readiness switch to subscriptions and typed requests after successful startup. Preserve initial startup, staging-only, store, loop-fault and shutdown gates.

Independent source edits may overlap with explicit file ownership. Shared builds/native lanes, runtime startup and SQL callbacks remain sequential. Reuse the existing native fixtures and pins; no new dependency, framework, harness, schema migration, tutorial or broad qualification campaign. Tests must establish resolution and the next legitimate operation, not just the initial hold/rejection. Record why each scenario escaped the original handoff.

Perform focused Windows/Ubuntu tests, current source/binary/evidence review, exact fixture cleanup and supported CIS completion. Retain the unrelated Heartbeat intermittent observation. Preserve the untracked user launch settings. Push, merge and release are excluded from this correction request.
