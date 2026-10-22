# ASP.NET gate-closure implementation batch

This authored batch follows committed baseline `46aff4a1`. It changes the HTTP
adapter and its integration tests only. Native build, baseline and automatic
mutation results for the changed source are coordinator-owned and pending; no
old report, status or denominator is rewritten.

The reviewed HTTP contract remains
[`contracts/healthcheck/v1/README.md`](../../../../../../contracts/healthcheck/v1/README.md)
and C07 in the retained contract catalog. The branch reserves exactly the
`/_health` segment and its descendants, with case-insensitive matching. One
optional trailing slash is accepted for known views; additional slashes remain
unknown reserved paths. Other paths retain business middleware and its actual
completion. Cancellation affects the current request, while snapshot and
collector state remain unchanged.

The adapter now captures `PathString` once and calls its segment-aware
`StartsWithSegments("/_health", OrdinalIgnoreCase)` predicate. This expresses the
same exact/descendant boundary without two string comparisons and a redundant
null-path fallback. Other segment prefixes remain ordinary application paths; the
explicit regression is `/_health-other`, alongside `/_healthcare`, default,
empty, root and ordinary business paths. The existing real HTTP cases retain
case, optional-slash, repeated-slash and unknown-descendant checks.

The private response writer directly returns `Task.CompletedTask` for HEAD or
the actual response stream's `WriteAsync(...).AsTask()` for a body. It performs
the same cancellation check and response metadata assignments in the same
order, with no work after the stream write. Its removed async continuation did
not perform a distinct operation. The enclosing asynchronous request handler
still awaits the actual completion and catches request cancellation, including
a synchronous cancellation throw from the writer. The actionable registration
exception message remains unchanged and unasserted.

Eleven expanded cases are added to `HealthcheckHttpTransportTests`:

- Six in-process nonreserved-path cases verify the same context/path reaches
  business middleware, its status/content type/header survive, no health capture
  or headers appear, and its outstanding completion governs the request.
- Three controlled response-stream cases use real middleware and a real service
  snapshot. The stream entry barrier proves the request remains incomplete
  while the write is outstanding; completion, the exact stream exception and
  cancellation then determine request completion/abort without health mutation.
- One real Kestrel business-path case covers `/_health-other`.
- One in-process HEAD case uses the real snapshot/service/middleware and an
  actual `MemoryStream`, asserting complete response metadata and zero body
  bytes before server-level HEAD suppression can mask a write.

The expected authored count is 63 HTTP cases plus one scheduling control in the
existing exact class filter. The real HTTP child count per complete pass rises
from 45 to 46. These are authoring expectations, not executed test receipts.
All in-process barriers are released in `finally`; those cases own no external
process and add no fixed sleeps or hidden unbounded cleanup wait. Existing GET/HEAD
cancellation-after-capture cases remain the regression for cancellation before
response metadata or body commitment.

The historical mutations 203 and 291 refer to source constructs removed by
these behavior-preserving simplifications. This is no mutation-status
reclassification: a fresh Complete native run must enumerate and test the new
source, with its actual baseline, compiler/ignored outcomes and child audit.
Historical ID 200 remains a pending contract/equivalence review; no assertion of
unpublished message prose or exception-message removal is used to force a kill.
The 72 prior compiler rollbacks and five block-covered outcomes still require
independent replay, as described in the adjacent plan.
