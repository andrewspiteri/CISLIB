# State Machine requirements and reconciliation

The exact supplied 701-line input is preserved as
[the State Machine specification](../sources/CISLIB_Workflows_StateMachine_Implementation_Specification_v1.0.md).
Its SHA-256 is `c68790990bc6912fc6a5839c6bd287dd6cf8832d5499ec4b8ded067a9aa8f8f0`.
The attachment's implementation-agent/skill instructions are requirements/reference
material, not self-granted execution authority.

The current user asked to move to the next Workflows module, then explicitly said
they would be unavailable and asked Codex to auto-approve required time. The
[bounded plan](../../../docs/changes/CIS-0014/implementation-plan.md) records the
controller-selected allocation and ordinary implementation decisions under that
standing direction. This does not grant external publication, dependency expansion
or unlimited work.

CIS-0014 starts at `1e0595f28dd284b365aadae9a7549a0fdb75848a` on local branch
`codex/workflows-statemachine`. Pipeline remains intact on its local parent branch
`codex/workflows-pipeline`; it has not been merged or pushed by this intake.
CIS-0013's completed owners/results/costs are retained and do not fund new work.
The existing untracked CountryData launch settings remain untouched.

## Current-contract mapping

| Concern | Resolution |
| --- | --- |
| Public API/package | New state-machine types in existing CIS.Workflows/CIS.Core, native helpers in CIS.Workflows.Hosting. No external state-machine package or dependency. |
| Lifetime | Pipeline stays single-use/throwing on faults/cancellation; State Machine is reusable with normal per-call operational results, including Cancelled. |
| Context/result | Non-null class context bound to machine; detached guarded immutable result/failure factories support outside-friend mocks and contain no context/providers/handlers. |
| Enums | Distinct declared values across all eight C# integral underlying types, no Flags/aliases or signed64 conversion assumption. Snapshot explicit mappings; terminal-only/self transitions work. |
| Admission/commit | Pre-cancelled and Busy do not replace LastTransition; admitted rejection does. Publish target/result only after successful cleanup, before gate/host release; no queue/history. |
| Cancellation | Exact requested token attribution at entered-method catch; construction/disposal OCE phase faults. No blanket conversion of engine bugs. Independent captured30s cooperative cleanup plus infinity, never detach. |
| Scope | Fresh selected ActivatorUtilities handler per attempt. Reuse narrow activation helper, not Pipeline engine. Borrowed provider passed only to one Trigger adapter, never saved on a reusable definition. |
| Child Pipeline | Fresh child per attempt, inspect Success after normal return; failed child already unwound. Successful child is not automatically compensated; outer owns only its additional effects. |
| Host | Separate explicit AddCisStateMachines; reuse current owner/counter/bridge/capture and final registration order. Closed-host calls throw as out-of-lifetime misuse. No implicit host-token substitution. |
| Logs | Source defaults routineDebug; optional captured success Information, scoped LogCalls. No per-read logs; origin exception once and terminal counts, safe explicit business message, immediate expiry warning. |
| Metrics | One3-handle binding per call, every terminal call measured, admitted-only active. Actual measured seconds match report. No fake phase duration or duplicate completed counter. |
| Metadata | New selected bounds16aliases/512unique machine-source-trigger tuples including invalid/64handlerpairs, transactional union. These numbers are controller choices, not numeric defaults in the exact source. |
| Invalid values | Runtime invalidtrigger metric label invalid; declared invalid member maps enum:invalid, a noncolliding fixed label. Default handleralias handler; CLRnames remain diagnostic only. |
| Capacity | 2560/16/384 reservations,2960total, unchanged globals. Existing Pipeline-only catalogs unchanged; combined Pipeline+REST becomes33streams/7824. |
| Example | Request-local CountryData lookup lifecycle reuses cached Pipeline and keeps all HTTP/cache/refresh semantics. Tests own concurrency controls; Completed means preparation, not transport delivery. |
| Skill/evidence | Extend existing combined skill/metadata/routing only after realAPI/examplecompile; existing native/public package fixtures, no personal install/new harness. |

The current framework documents that enum enumeration can include duplicate values
and sorts by underlying unsigned magnitude, so mapping validation cannot infer
uniqueness or declaration order from numeric adjacency:
[Microsoft Enum.GetValues](https://learn.microsoft.com/en-us/dotnet/api/system.enum.getvalues?view=net-10.0).
Fresh construction follows the pinned platform's
[ActivatorUtilities implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.DependencyInjection.Abstractions/src/ActivatorUtilities.cs).
Cancellation requests require real cooperative settlement:
[Microsoft cancellation model](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads).
For the independent cleanup deadline, native
[CancellationTokenSource.CancelAsync](https://learn.microsoft.com/en-us/dotnet/api/system.threading.cancellationtokensource.cancelasync?view=net-10.0)
sets the requested state synchronously and provides the callback-completion task.
The runner retains and awaits that task before releasing cleanup resources, so a
blocking diagnostic cannot postpone the request and callbacks are never detached.
These primary references support the binding, not an SDK/package upgrade.
