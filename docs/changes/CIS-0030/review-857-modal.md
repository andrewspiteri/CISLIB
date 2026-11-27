# Review 85711214: suspend Deploy modality during session checks

Owner: CIS-0030/WORK-201. This corrects the second finding in the supplied review
without changing the approved Configuration screen or deployment contract.

The native Deploy dialog now closes while the existing session boundary is
checking or unavailable. Its selected saved version and deployment note remain
in the same component state. A layout effect removes native top-layer modality
before the session notice is painted, allowing ordinary interaction with Retry.
Only a verified, unblocked current session may restore the modal; a changed
session changes the existing deployment scope and discards that selection.

Programmatic suspension does not cancel deployment state. There is deliberately
no unconditional native `close` handler that clears the retained selection.
Idle user Cancel/Escape still dismisses it; busy or masked cancellation events
remain prevented. Live session checks guard opening, preview and admission after
the awaited preview. Already issued operations retain their original cleanup
and result custody.

The existing headed version-page journey calls the focused
`native-deploy-session-checks.mjs` helper after saving its second real version and
before its first actual deployment. Two owned tabs and CDP sessions exercise
actual browser focus with Playwright focus emulation disabled. For pointer and
keyboard recovery separately, a real authenticated focus-read response is held,
then delivered as unavailable. Assertions require the native dialog to be both
closed and nonmodal while checking and unavailable, and require real click or
Tab/Enter Retry to validate the same server session and restore the exact version
and Unicode note. Cancellation then closes the restored dialog normally.

The helper observes zero portal mutations, including preview requests; both
held responses join and dispose before interception is removed. Its owned CDP
sessions restore/detach and its additional tab closes before returning proof.
The C# parent requires those exact flags and retains the existing immutable
publication/pair, assignment, original-operation and Audit assertions for the
unchanged subsequent Save/Deploy/rollback journey. The actual extended native
case passed 1/1 with no skips, including both Retry methods and all normal nine
effects. The affected C# project built with zero warnings/errors, the corrected
production portal build passed, and all 215 portal pure tests passed. Strict
native guards, frozen source/build identities and independent source/result
review passed. Exact phases and retained failures are in the
[verification record](review-857-verification.md). This is Windows evidence;
Ubuntu CI remains unverified and this payload has not been published.
