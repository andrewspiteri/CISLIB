# Cross-tab session correction for `87d18ba6`

The review correctly identified a document-only invalidation boundary: another
tab could keep an earlier operator's Configuration values or draft until an API
interaction or the captured expiry detected the changed shared cookie. Server
authorization continued to protect commands; this correction addresses client
visibility and local draft custody without adding an authentication endpoint.

## Implemented behavior

The existing `PortalSessionCoordinator` owns one coalesced session
check with a 15-second deadline
when a document becomes active. It uses the existing authenticated
`/api/portal/applications` BFF read with the captured opaque context header,
same-origin credentials, no caching and no redirects. The check retains only
the validated context, never the returned application list.

While that check is pending, protected inline content and the existing portaled
details panel are hidden and inert. The normal document wrapper uses
`display:contents`, preserving the approved layout. Inline editing remains mounted,
so a successful check for the exact same session restores the current draft and
version note rather than remounting the editor. The readonly panel temporarily
stops trapping focus while masked so the session-check Retry remains reachable.

An unavailable or otherwise unverifiable result leaves protected content masked
and offers an explicit Retry. It does not claim that the server logged out, and
does not discard the retained draft or known original-operation references.
An actual 401 response, a changed authenticated context, expiry or explicit sign-out uses
the existing invalidation path to clear protected values, drafts and local
references. Late responses cannot restore a retired session. Aborting a check
does not certify callback completion: the coordinator retains its pending
promise until the actual callback settles.

Portal-initiated sign-out sends the fixed `invalidate` notification before
navigating to the existing sign-out flow. Other same-origin documents receive
it through BroadcastChannel and the independent storage-event fallback. The
notification contains no principal, value, note, original key or credential;
it only invalidates and cannot establish authentication. Sign-out is idempotent,
preventing peer notifications from producing an epoch/rebroadcast loop. Channel
and storage failures do not disable authoritative focus revalidation.

## Validation and limits

The focused pure batch
`WORK-197/rm09c-review-87d-session-pure/native.log` passed 46/46, with 0 failed and 0 skipped.
It includes eight new cases in `portal-session-check.test.mjs` for coalescing,
same-session draft/reference preservation, actual invalidation, held callback
custody, unavailable Retry, the finite deadline, trusted BFF context selection
and the closed credential-free peer notification.

The native method
`ActualSharedCookieSignoutAndOperatorSwitchRetireOtherTabsProtectedValuesAndDrafts`
and `native-session-tabs-checks.mjs` passed 1/1 with no skips in the full native
fixture. The complete affected portal pure selection passed 212/212. The
scenario uses two tabs sharing an independently
authenticated A session, so signing out does not revoke the host's separate A
API session. It holds actual focus replies to inspect inline/panel masking and
valid draft preservation, controls one unavailable delivery, signs out through
the ordinary UI while an old response is held, then replaces a fresh A session
with a real B cookie and requires the actual authoritative 409 response to retire A's
draft. Its C# parent requires no Configuration effects and unchanged native
Configuration, original-operation and Audit fingerprints.

The pinned Playwright focus override is disabled through two owned CDP sessions,
so actual focus/visibility transitions drive the check. The sessions are restored
and detached, and held responses are fulfilled/disposed before interception is
removed. Exact proofs and retained failed attempts are in the
[verification record](review-87d-verification.md). The shared expiry continuation
targets the intended existing opaque session rather than every session for that
operator, preserving its real expiry/reauthentication assertions.

Controlled unavailable delivery is not a real infrastructure outage. The correction adds no polling,
second owner pool, authentication endpoint, credential broadcast, new package,
normal screen or general test framework. Existing service authorization and
original-key/commit semantics remain authoritative.
