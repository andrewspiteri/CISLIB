# Mutation-child Windows process error mode cohort

This helper change is a process-local verification setting for newly prepared
Windows child sessions. It is not a production change, a native mutation pass,
a timeout waiver or proof that Windows Error Reporting caused the earlier delays.
Existing raw sessions and their failures remain unchanged.

## Observed failure and causal limit

The retained [a1 child audit](../../../../../../.artifacts/hcg/a1-audit/results.json)
rejects three actual launches. Read-only receipt inspection found132 managed
registration-failure receipts overall:43 for mutant195,46 for196 and43 for197.
Each of those IDs has one timed-out launch; one also required a forced stop.

| Launch | Mutant | Observed start to actual exit (s) | Managed receipt timestamp to actual exit (s) | Actual exit | Forced | TimedOut |
| --- | --- | --- | --- | --- | --- | --- |
|47aa0e1b5d9c47e6afa08b3e0520e044|195|28.0113142|26.8154220|-532462766|false|true|
|bd8e296b693341c49e6f430cddfea990|196|30.0906252|28.9963148|-1|true|true|
|d350609ff75444859a01b1f4d07e4469|197|16.4665299|15.6958444|-532462766|false|true|

Across all132 managed receipts, that latter timestamp-to-exit interval ranges
from6.0840974 to28.9963148 seconds, averaging7.9146344 seconds. These timestamps
support investigation of work after unhandled-exception notification. They do not
measure callback return, identify a WerFault process, separate filesystem receipt
write delay from later OS work, or establish a WER causal trace. The three launches
remain invalid regardless of their exact allowed Core throw receipts or eventual
natural nonzero exits. The15s child bound remains enforced.

## Primary-source basis

Microsoft documents `SEM_NOGPFAULTERRORBOX` (0x0002) as preventing WER invocation,
while distinguishing it from suppressing WER UI alone. Error mode belongs to a
process and is inherited by descendants; it is not a machine registry setting.
The existing bits must be preserved. [SetErrorMode](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-seterrormode)

The API to read the current process flags is separately documented.
[GetErrorMode](https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-geterrormode)

Pinned CoreCLR10.0.12 sends the AppDomain unhandled-exception notification before
its last-chance OS handling. Its ordinary managed-unhandled path continues to the
OS, and its own DisableOSWatson routine uses this same error-mode bit. This supports
preserving the actual managed notification and exception while changing optional
fault reporting. It is not a guarantee of a particular numeric exit or latency.
[Runtime source](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/coreclr/vm/excep.cpp)
(3159–3167,3250–3253,4119–4148)

## Implementation and audit binding

The existing v4 child protocol remains decodable for historical evidence.
Session appends optional `RequireWindowsErrorMode=false`; historical missing
properties decode to that legacy value. New Prepare sets it from the actual
Windows platform for automatic, finite instrumented and explicit manual-source
replay sessions. Source/session hashes distinguish this new cohort from previous
same-schema runs. Older sessions are not retroactively described as controlled.

The observer activates only with the existing validated child-specific launch
context. A parent-session variable alone still cannot activate it. After verifying
the session, stage, selected Core/controller, actual runtime and executable, and
before fixture Main runs, a required Windows child:

1. Reads the actual process flags.
2. Requests exactly the prior flags OR0x0002.
3. Retains the actual previous value returned by SetErrorMode and a fresh readback.
4. Rejects any disagreement or unrelated bit change.
5. Writes the PID, Before, SetReturnedPrevious, Requested, After and observation
   time into its existing hashed origin receipt.

There is no call when the session does not require the policy, and a required
policy refuses a non-Windows child. Ordinary runs with no child launch context do
not enter the observer. The test parent and machine/global configuration are not
changed. The setting remains for the child lifetime; restoring it during exit
could re-enable reporting before the actual fatal termination.

For a required session, audit demands the witness, exact PID agreement, matching
previous/before values, requested=before OR0x0002, readback=requested and a witness
timestamp between launch creation and the origin receipt. Its full-bit comparison
preserves unrelated existing flags. The witness is reported with each audited
row and the required policy with the session result. Legacy sessions retain their
prior proof scope. New non-Windows preparation does not require or emit a witness.

All existing exact source/package dependencies, Core hashes/MVID, stage owner,
frozen active selector, mapped first throw frame, actual parent PID/argv/exit,
normal exit agreement, forced-stop/timeout markers and cleanup requirements remain.
No exception is caught to manufacture a successful kill, no Environment.Exit is
introduced, no exit-code constant is invented and no deadline is increased.

## Required native controls and alternative scope

The copied-receipt controls add a missing-mode-bit rejection to an actual positive
launch. The derivative clears only the actual witness's readback bit and preserves
the source evidence. Managed-probe derivatives recompute the origin receipt hash
in the copied managed failure receipt, so the error-mode guard is reached without
an unrelated hash failure. No positive witness is fabricated.

New Windows-required normal controls therefore have10 cases; legacy/Linux normal
controls retain9. Windows-required managed-probe controls have4 cases; legacy/Linux
managed-probe controls retain3. Each result records its expected case count and
policy. These are parser controls, never native process or kill evidence.

The coordinator must first execute actual finite registration and probe failures
with complete normal/managed origin and parent-exit evidence. Compare natural
termination intervals and actual readback bits to the retained uncontrolled
cohort; an already-set Before bit is material evidence, not something to erase.
Then repeat the actual full selected closure under unchanged15s child deadlines
and strict status accounting. A faster isolated run alone does not establish
behavior under the repeated full mutation load. Missing/forced/timed-out/unknown
terminations remain invalid even if an allowed managed receipt exists.

A separate registration-only in-process test partition could avoid repeatedly
crashing unrelated children for the same registration guard. That is a different
declared test closure requiring exact source union, baseline and complete outcome
reconciliation. It cannot silently replace the original full gate or establish
the required actual child propagation/termination proof. The original full gate
remains open until its evidence is valid or an explicitly justified complete
partition matrix is validated.

