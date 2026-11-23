# @cislib/client-core

Portable ES modules with declarations and no runtime dependency. The small
`ClientTransport` port, per-call `CredentialProvider` and `executeRequest` provide
the bounded HTTP facilities used by RM-07. Native fetch is the default portable
transport; custom server credentials require an explicit server transport.

`executeRequest` has one cooperative deadline, at most two explicitly selected
GET retries and joined response cleanup. It returns exchange, status, typed
value/problem, attempts, cleanup and uncertainty separately. It preserves
unexpected programming faults. Optional observation callbacks see only a finite
operation alias, method, attempt, status, duration and outcome; they cannot change
the call result and receive no credential, body, URI or durable operation scope.
There is no automatic console logger or retained request history.

A custom transport must honor the supplied signal, stream bytes without an
unbounded buffer, reject redirects and join its real request/socket work in
`dispose`. Its `ClientTransportFailure` is an expected exchange/cleanup failure;
an arbitrary thrown error remains a programming fault. Cooperative timeout does
not authorize abandoning a transport or claiming remote rollback.

The default native fetch adapter classifies network loss while reading or
canceling its response stream and joins the actual native cancellation. Expected
body failures keep structured timeout/cancellation/transport facts, the original
operation key and mutation uncertainty. A rejected native cleanup is retained in
`cleanupSucceeded`; it does not replace those facts with a duplicate exception.
An explicitly injected fetch implementation must return native response streams
with their native failure marker or use `ClientTransportFailure` for an expected
exchange failure. An ambiguous `TypeError` from an injected stream remains a
programming fault. Reader-state operations and decoding faults are outside native
error classification, and genuine operation/cleanup faults still propagate.
