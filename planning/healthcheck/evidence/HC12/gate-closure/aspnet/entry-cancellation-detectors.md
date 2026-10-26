# Entry cancellation detector addition

The current-a1 omission guard's actual row 207 removes the request-entry
`RequestAborted.ThrowIfCancellationRequested` in `HandleAsync`. It survived the
earlier harness: cancellation after capture exercises a later boundary and does
not prove that an already canceled request avoids application callbacks.

`HealthcheckHttpTransportTests.CancellationOnEntryAbortsBeforeSourceAuthorizationOrResponseChanges`
adds six in-process rows: GET and HEAD for health, readiness and enabled details.
Each uses the installed real middleware, actual Healthcheck service/source,
observed request lifetime and real memory stream. Cancellation precedes entry.
Assertions require Abort, no source/authorization/business invocation, unchanged
existing status/content metadata and all headers, no response start and no body.
Each row then uses the same middleware for an uncanceled positive request,
requiring exactly one real capture, details-only authorization, a complete GET
snapshot or an empty HEAD body with a positive formatted content length, and
unchanged shared state revision. There are no fixed sleeps or private/prose
assertions, and no production or child-proof changes.

The existing class filter includes all six rows. The fresh expected selection is
69 HTTP cases plus the one scheduling witness, total 70. Authoring is not native
evidence: snapshot04 and the frozen r4/r5 runs retain their original 64-case
contexts and receive no credit for these additions. A fresh baseline and exact
row-207 replay belong to the coordinator after capture05.
