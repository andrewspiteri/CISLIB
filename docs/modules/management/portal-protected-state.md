---
title: "Portal protected state lifetime"
type: guide
status: Active
owner: Repository maintainers
last_reviewed: 2026-11-26
review_cadence: on portal session, request or editor ownership change
cis:
  stable_id: cislib:guide:management-portal-protected-state
---

# Portal protected state lifetime

[`ProtectedStateOwner`](../../../implementations/typescript/applications/management-portal/src/protected-state.ts)
owns one memory-only protected view and draft. Its request identity includes the
current opaque operator, session and Management service associations, together
with the exact request key and any selected application, environment, definition
and schema version. The request key includes the path, filters and page cursor;
equal URLs do not make different sessions or definitions interchangeable.

The shared [`PortalSessionContext`](../../../implementations/typescript/applications/management-portal/src/portal-context.ts)
contains three bounded opaque associations and an expiry time. These values are
neither credentials nor native grants or backend authority. They must come from
the protected same-origin BFF boundary, which independently validates the current
credential. The browser does not manufacture that authority from an application
UUID, configured URL or reported identity. This state primitive adds no account
endpoint, authentication mechanism or Scheduler runtime.

The server caps the database session and cookie at the earlier one-hour maximum
or the validated Management access-token expiry minus a 30-second safety margin.
Activity and protected reads do not extend that deadline. Expiry requires ordinary
reauthentication; the portal does not request or retain refresh tokens. The
explicit local review fixture has a separate finite host lease: its synthetic
tokens use only that lease's remaining time, and signing in does not restart it.
See the [manual review data guide](portal-manual-review-data.md) for its isolated
identities and the distinction between authored assertions and actual live proof.

An ordinary Sign out activation first captures its same-origin sign-out URL,
clears protected local state, and navigates to that captured destination. This
keeps a synchronous header update from replacing the destination with Sign in.
The maintained confirmation form and CSRF-protected POST still revoke the actual
session; the local state clear is not server revocation. Modified clicks and
links targeting another browsing context keep their native browser behavior.

## Protected reads and session changes

Set the exact target with `selectTarget`, then use `request` with a callback that
returns the protected BFF reply. Each callback receives a frozen ticket containing
the captured context, target, generation and abort signal. The callback promise
must include response decoding and all owned cleanup. A read-only bootstrap can
start with unknown context; a current, validated, unexpired reply can establish
the association. Only a matching `ready` reply supplies a visible snapshot and
opens the local freshness gate.

Starting a newer request invalidates and aborts older tickets. Changing a target,
definition, schema or actual session also invalidates earlier tickets. A late
reply cannot refill the old user or environment, including when the URL is
unchanged. Callbacks remain actually owned until their promises settle; abort is
not reported as settlement. At most four callbacks can remain unsettled. At that
bound another request is blocked and is never queued or invoked automatically.
Unexpected callback faults propagate after the actual request settles.

`view` is a readonly snapshot of the owner. It exposes the current state, context,
target, dated allowed snapshot, raw-text draft, dirty-navigation predicate and
pending count. Values are copied from already projected JSON and deeply frozen,
with a finite 8 MiB plus 4 KiB wrapper allowance so valid whole inventory responses
are retained without truncation. This private memory bound does not increase any
native response, field or collection limit.

The owner applies these lifetime rules:

| Observation or action | Protected values and draft | Original-command references |
| --- | --- | --- |
| Actual sign-out, explicit unknown context or session expiry | Clear; invalidate and cancel old requests | Clear the fixed local receipt entry and block recovery |
| Different operator or Management service association | Clear before accepting fresh data | Clear local references from the previous association |
| Same-user session-marker renewal | Clear; cancel and join old work; require a fresh read | Retain same-user payload-free references; cancel the old recovery selection |
| Permission loss, denied target or inaccessible resource | Clear affected values and draft; disable local submission | Retain only the existing same-user payload-free references; every later lookup reauthorizes |
| Transient outage with a valid current context | Retain only the same-scope dated allowed snapshot and draft; show stale and disable submission | Preserve original keys; queue nothing |
| Invalid clock | Clear protected content and block the context | Preserve stored references; clock uncertainty does not establish a backend outcome |

`setContext(undefined)` invalidates even an unknown-context bootstrap.
`signOut()` is an explicit lifecycle action. `checkExpiry()` and the `view` getter
apply the expiry gate; the React owner also owns its actual expiry timer and
cross-component request epoch. The shared provider must not adopt an obsolete
reply's context globally after its epoch has changed.

The owner reuses
[`CommandReceiptStore`](portal-command-receipts.md) for local reference clearing
and recovery cancellation. It does not create a second receipt ledger or abandon
that store's pending recovery slot. A canceled lookup, stale response or cleared
browser view cannot erase a previously known committed business result or prove
that a remote operation rolled back.

## Memory-only drafts and fresh submission

`startDraft(text)` captures one baseline and `editDraft(text)` retains incomplete
editing text, bounded to 256 KiB of UTF-8. The primitive does not interpret a
configuration schema, compile controls or validate incomplete JSON. One whole
Scheduled Task draft can therefore retain both Trigger and Job tab values under
the same exact owner context. No payload, baseline or protected snapshot is
written to browser storage.

`view.navigationBlocked` is true while the draft differs from its baseline. The
component owner uses that predicate for real links, route changes, keyboard
navigation and unload protection, and asks for any required discard decision
before calling `selectTarget`. The primitive clears the old draft on a real
selection change to prevent carrying values into another definition. An explicit
`discardDraft` removes only the memory draft.

`view.canWrite` is a local freshness gate. It is not a cached permission or proof
that a worker or settings owner accepted anything. Every actual command still
requires current native authorization, exact target resolution, original-key
handling and native preconditions. Outage, denial, expiry, loading or a context
change closes that gate; reconnect must obtain fresh state before another
explicit submission.

The text/baseline primitive does not retain a native ETag, immutable source
selection or reviewed command. Editors must keep those original preconditions and
the reviewed intent separately in memory, bound to the same exact context and
schema. A refreshed snapshot must not silently replace the draft's original ETag.
Receipt recovery cannot reconstruct the body, adopt newer preconditions or
authorize same-key resubmission by itself.

Focused controlled source cases are in
[`protected-state.test.mjs`](../../../implementations/typescript/applications/management-portal/tests/protected-state.test.mjs).
They exercise same-URL late replies, exact selector changes, session loss and
renewal, expiry, permission loss, outage retention, bounded dirty drafts and actual
callback/recovery joins. Native results and passing counts belong to the
CIS-0030 verification record; the state primitive alone does not prove a complete
editor or browser journey.

## Current portal integration

The existing protected BFF read obtains the opaque context alongside its normal
per-attempt credential/session validation. The request-local adapter captures a
validated database session before the maintained callback receives only its safe
projection. Separate HMAC domains identify the operator, that session and the
configured Management endpoint/scope. No token, raw database user ID or new native
backend-authority assertion is returned. The denied auth/session and account
routes stay closed; initial context comes from an existing protected reply.

After bootstrap, the browser sends the expected context stamp on reads. A changed
stamp is refused before a Management request and returns no protected value.
The shared document coordinator advances its epoch before publishing identity
changes, cancels other read owners, and rejects obsolete initial replies. A
session's expiry timer and explicit sign-out invalidate all registered views.
Scope changes also clear paging memory/trails; persisted navigation carries only
the opaque operator/service association, not a session marker or record data.

`usePortalRead` gates rendering by URL, shared epoch and the exact explicit target.
It retains only a dated same-scope snapshot on transient failure. Clock recovery
requires an explicit fresh read. Dirty owners participate in link and unload
protection; each forthcoming editor still owns its complete route/keyboard
journey and discard decisions. These state safeguards do not deliver the
Configuration editor, Scheduler service or remaining command screens.

The current verification checkpoint has 52 focused unit cases and two existing
native host cases passing, with the pinned production build/strict TypeScript
check. The native cases use actual OIDC/PKCE, database sessions, scoped Management
requests and a desktop browser. Their deliberately controlled paging/delay/401
responses remain UI race evidence, distinct from the real authentication and
provider checks. This local fixture does not establish live Entra deployment or
release qualification.
