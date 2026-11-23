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
