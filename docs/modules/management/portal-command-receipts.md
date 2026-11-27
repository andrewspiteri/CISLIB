---
title: "Portal original-command references"
type: guide
status: Active
owner: Repository maintainers
last_reviewed: 2026-11-26
review_cadence: on portal command or authenticated-context contract change
cis:
  stable_id: cislib:guide:management-portal-command-receipts
---

# Portal original-command references

The browser-safe [`CommandReceiptStore`](../../../implementations/typescript/applications/management-portal/src/command-receipts.ts)
retains a bounded original-key reference before an operator command can be
submitted. It is a local `sessionStorage` primitive. It does not submit commands,
store drafts or command bodies, establish native authority, or record backend
outcomes. Persisting a reference proves neither dispatch nor acceptance: a crash
can occur between retaining the key and sending the request.

The primitive is currently independent of portal authentication, BFF handlers and
mutation screens. Its owner must supply an authenticated context through the
protected application boundary. The existing `/api/auth/session` route remains
denied; this work adds no account endpoint or browser context producer. The
server-only callback's `operatorId` is not by itself a browser-authority proof.

## Retained identity and limits

Each reference contains the original operation family, exact target, original
operation key and creation time. One envelope associates those references with a
current opaque operator ID and Management service association. These two bounded,
nonsecret values are supplied by the owner; a configured URL association must not
be presented as proof of native backend authority. Missing or different
associations block preparation and recovery rather than treating old work as
absent.

The fixed supported identities are:

| Native operation family | Exact retained target |
| --- | --- |
| `management.metadata.replace` | application UUID |
| `management.configuration.source.publish` | source UUID |
| `management.configuration.release.create` | application UUID, Configuration ID, schema version |
| `management.configuration.assignment.replace` | application UUID, Configuration ID, schema version |
| `management.scheduler.trigger.write` | application UUID, trigger UUID |
| `management.scheduler.task.write` | application UUID, task UUID |
| `management.scheduler.run.write` | application UUID, run UUID |

Scheduler families are passive contract identities. Recognizing them here does
not implement a Scheduler backend or make an unavailable operation executable.
Catalog deployment, event submission and attempt reporting are outside this
receipt primitive.

The store admits at most 16 references and 32 KiB of UTF-8 JSON, with a maximum
reference age of 24 hours. It reuses the SDK's bounded identifiers, positive
Int64 schema strings and strict `OpaqueJsonObject` lexical validation. Loaded
storage rejects duplicate JSON names, duplicate original keys, extra fields,
malformed Unicode and invalid targets. Returned references and their targets are
detached and frozen. No body, ETag, credentials, claims, user labels, draft or
outcome is retained.

Capacity and unavailable, denied or unverified storage block preparation. An
unexpired reference is never automatically evicted to make room. Expiry removes
only local UI references and returns an explicit `references-expired` blocked
observation on that call. Expiry is not a backend retention guarantee or evidence
that the original operation failed. A future-dated reference or invalid clock
blocks access without deleting questionable references. No blocked result
authorizes a replacement key.

## Owner API and recovery lifetime

Create one store for the tab/document, supplying `context` explicitly. Storage is
accessed lazily, so browsers denying the `sessionStorage` property return a
blocked result instead of enabling an unretained submission.

| API | Owner obligation and behavior |
| --- | --- |
| `load()` | Returns current immutable references, or an explicit blocked reason. Foreign or invalid storage is not exposed as an empty list. |
| `prepare(intent)` | Returns `prepared` only after the new original key is stored and exact bytes are read back. The owner must stop dispatch for every other result. |
| `recover(receipt, lookup)` | Runs one typed, freshly authorized original-result lookup using the retained key and target. The callback's promise must include the SDK call and all owned cleanup. |
| `cancelRecovery()` | Requests cancellation; it neither releases the slot nor reports remote rollback. The owner still awaits the actual recovery promise. |
| `setContext(context)` | A changed or unknown persistent association invalidates the selection and requests cancellation. It retains local references. |
| `forgetExact(receipt)` | Explicitly removes one exact local reference. It refuses to remove the currently pending selection. |
| `clearLocalReferences()` | An explicit lifecycle reset removes only the fixed local receipt entry, invalidates the selection and requests cancellation. It cannot infer any backend outcome or create a key. |

Only one recovery selection can be active. It remains busy through cancellation,
timeout and context changes until the real callback settles. Context invalidation,
explicit cancellation, expiry or clock uncertainty prevents a late callback
value from being disclosed as a current result. Unexpected callback faults
propagate after the selection actually settles. An explicit local reset also
retains custody of any pending callback until settlement.

The persisted operator/service association is deliberately separate from a
volatile session or request generation. Same-user reauthentication against the
same service does not change `setContext` equality. The authenticated owner must
invalidate its own request generation and call `cancelRecovery()` when its
validated session marker changes, then join the pending work before replacing
that owner. It may retain the payload-free references for same-user recovery.
The primitive persists no session marker and creates no cross-service recovery
guarantee.

Recovery forwards observations without rewriting their meaning. Timeout,
NotFound, denial, an unknown completion or a committed result with failed cleanup
all preserve the original reference and key. In particular, NotFound after reload
is unresolved; it is not permission to send a new key. Any explicitly permitted
same-key resubmission requires the unchanged original body and preconditions
retained elsewhere, current backend permission and the native operation's own
replay rules. A reference alone cannot reconstruct or authorize that submission.
Canceled, stale and expired observations describe the lookup or local reference;
they are not original business outcomes and must not erase a previously known
committed result.

Focused source cases are in
[`command-receipts.test.mjs`](../../../implementations/typescript/applications/management-portal/tests/command-receipts.test.mjs).
They cover finite targets, minimal storage, byte/count/age bounds, context and
storage failures, explicit expiry, original-key preservation and joined recovery.
Actual command results and passing counts belong to the CIS-0030 verification
record; authored source cases alone are not execution evidence.
