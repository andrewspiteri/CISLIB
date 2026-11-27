# Host shutdown correction

The user explicitly requests fixing the host shutdown timeout so the complete
first-version integration test passes. Continue WORK-190's existing owned-native
cleanup and verification outcome. Earlier two failed tests and their passing
browser checks remain retained; they established no blocked internal stage or
successful graceful shutdown.

Use the ordinary portal host and existing real browser/provider fixture. First
record a bounded set of fixed shutdown phases and the current handler count in
existing stdout/phase evidence, without credentials, identities or request
content. Identify the blocked actual await before a causal fix. Preserve HTTP
admission closure, cancellation, real handler/transport/Next/runtime settlement,
error outcomes and actual process/stream/IPC joins. Never clear retained promises
or report success while owners remain active. Keep runtime/SDK pins, deadlines,
native auth/permission checks and independent SQL/Operations/Audit assertions.

Then implement the smallest ownership correction, a meaningful regression and
affected documentation/review in this same task. Root serializes shared native
work; parallel agents inspect disjoint source concerns. No UI change, preview,
new package/framework, generic harness or broad qualification campaign is needed.
No push, merge or release is included.

Under the existing delegated controller, transfer two unused hours from the
completed WORK-183 allocation (10h to8h), preserving its23,262,583ms actual spend,
status and accepted results; no execution or reopening of that owner occurs.
WORK-190 becomes9.25h. Transfer two unused logical attempts from WORK-100-BACKOFFICE
(28 to26,24 used) to WORK-190 (6 to8). Logical attempt7 owns stage diagnosis;
attempt8 owns the causal correction and full verification. Add1.5 conservative
source hours for all agents, intake, implementation, tests, docs and review:
WORK-190 source estimate6h, plus actual native/metadata costs. All historical
consumption, shared120h/128 attempts, restoration3h/3 attempts, physical6GiB/64MiB
reserve and contained Docker2GiB remain unchanged.

The user subsequently confirmed that49291 could stop and use fresh data. Its
normal runner exited0 and actual fixture cleanup completed; the replacement
decision and closed-session pointers are retained. No manual portal is currently
live while the shared build and native checks execute.

## Correction and regression boundaries

The custom server now owns each accepted native connection from its public
`connection` event through its actual `close`. Stopping closes admission, aborts
the runtime, destroys owned inbound transport (including a peer that has not
completed TLS), and joins listener/connection closure, actual Next handler
promises, Next cleanup and finally its OIDC/PostgreSQL dependencies. It does not
clear pending handlers, synthesize `finish`, force a successful exit or extend
deadlines.

`ActualHostStopsWithAnUnadmittedIncompleteTlsPeer` holds a real127.0.0.1 TCP
peer after its first TLS byte. It failed on the original owner and passed with
native connection ownership. Its watchdog can only fail and restore the probe;
only a real server-initiated close qualifies the assertion.

A separate observation exposed an aborted portal GET with its response already
closed but its Next promise retained. The API handoff now awaits the original
BFF callback and cleanup, then cancels and joins its unsent body if cancellation
is already known, returning a bodyless499. Live responses and errors retain their
identity. This is a boundary for cancellation known before handoff; it does not
claim to fix arbitrary cancellation inside Next after handoff. Credential
validation and native result/recovery contracts remain intact.

`ActualHostJoinsACanceledAuthenticatedReadBeforeDisposingItsSessionStore` uses
real OIDC, PostgreSQL and the ordinary Configuration release-list GET. An owned
session-table lock proves that the authenticated request reached native SQL
before the client cancels. The test observes nonzero BFF/handler ownership during
stop, releases and joins the real SQL gate, then requires exit0 and zero handlers
and requests. Seven pure tests additionally hold the actual callback and body
cancellation across abort, preserving cleanup failures.

`rm09c-canceled-response-next-build` and the scoped integration build passed with
zero C# warnings/errors; `rm09c-canceled-response-pure` passed7/7. The earlier
`rm09c-host-stop-active-request-observation` full first-version run passed1/0/0
with independent SQL/Operations/Audit and actual cleanup. The handoff candidate
then passed all three host/first-version cases in
`rm09c-host-shutdown-qualified-admitted`.
Earlier180-second failures and Windows native Node crashes remain retained; a
partial browser completion or diagnostic-only passing run does not replace them.

The wider Configuration matrix subsequently exposed a pipe already running
after handoff: zero BFF requests, closed response, headers sent, retained Next
handler. The [exact pinned response-close correction](next-response-close-correction.md)
addresses that actual framework transport wait. Its HTTP regression failed on
the original source and passed with actual close and no synthetic finish.
On the final rebuilt candidate,
`rm09c-configuration-host-backend-final` passed6/6, including all three
host/first-version cases and three native creation/authority boundaries.
