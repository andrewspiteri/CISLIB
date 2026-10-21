# Additional lifecycle native assertions

`HealthcheckLifecycleQualificationTests` authors nine cases in five methods.
The coordinator owns native discovery/execution; no pass is claimed by authoring.

No-work service/monitor disposal checks observe completion before awaiting anything,
including the wait obtained before withdrawal, repeated calls, removed contributions
and stable identity. They create no dispatcher/timer/external resources and use no
await-using cleanup that could hide a missing completion as an indefinite hang.

First-runner admission uses a service that has never claimed a runner, then tests
both stopping and disposed states. This isolates terminal admission from the
separate second-runner guard. New monitor/binding admission is also rejected.

Binding retirement is observed by binding the same registration identity to a new
monitor immediately after withdrawal, before invoking or disposing the old binding.
This prevents the old binding's lazy retirement path from repairing a missed eager
state transition before it can be observed. External monitor ownership remains
checked by a valid report after releasing its replacement binding.

The work-bearing case uses a real runner, owner lease and cancellation token with
explicit work/cleanup barriers. It observes immediate health withdrawal, actual
cancellation, retained monitor/service completion barriers through lease cleanup,
then actual completion and exactly one cleanup. Finally releases both gates and
uses bounded cleanup; no fixed sleep is used.

These tests assert documented receiving/runtime behavior rather than private error
text or internal counters. The extra cancellation sweep in `WithdrawLocked` may be
redundant with `CloseAttachmentLocked` for all legitimately attached operations;
no invalid private state is manufactured to force an observationally equivalent
mutation to fail. Native mutation outcomes remain the coordinator's evidence.
