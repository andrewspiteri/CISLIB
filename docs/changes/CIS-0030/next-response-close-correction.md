# Pinned Next response-close correction

The Configuration matrix exposed the remaining shutdown blocker after the first
three-case qualification passed. A retained portal GET had zero active BFF work,
status200 and headers sent, but its native response was closed without `finish`.
Next16.4.6's `createWriterFromResponse.close()` awaited a promise resolved only
by `finish`. Its abort path could therefore wait for an in-flight close that
could never finish. The actual handler remained owned and the parent test failed
at its existing180-second deadline. Those failed runs remain retained.

The correction resolves that private transport await on the actual response
`close` event as well as `finish`. The existing response AbortController still
distinguishes premature close from successful finish. Actual producer
`waitUntilForEnd` work remains awaited. No synthetic event, forced successful exit
or dropped handler/native callback is used.

This changes one installed file for the existing pinned dependency, prepared by
the ordinary portal build. Preparation admits only Next16.4.6 and the exact
original SHA-256
`bedc0f8cafeb73b6e48dcfd509ca21b42b9b2a5234aa7925a2283185ae486d07`;
the corrected source must have SHA-256
`a41ad25a8622cf75beec29c1f62a265aad76da33c12f23d9f049d7db6b1d28b2`.
An already corrected file is idempotent. Unknown versions/bytes fail closed.
Preparation writes a newly owned temporary file and atomically replaces only
this exact dependency file; normal startup verifies the corrected digest without
writing deployment files. Dependency upgrade/removal of the correction requires
explicit source review. Package versions and lockfiles remain unchanged.

This is a necessary refinement of the earlier application-only shutdown plan.
The pre-handoff bodyless-cancellation guard remains useful, but cannot settle a
framework pipe already running after handoff. The change is bounded to this
response lifetime defect, its actual HTTP regression, build/startup verification,
the affected Configuration journeys and documentation.

`next-response-drain.test.mjs` runs against the installed framework and a real
loopback HTTP server. Its normal control checks exact response bytes and actual
`finish`. Its failing-path control destroys the real response inside its public
`end()` call, joins every actual client/socket/server close, then awaits Next's
real pipe promise. The uncorrected source must fail with the promise pending;
the corrected source must settle while `finish` remains false. This directly
tests the cycle without pretending that an aborted response finished.

`rm09c-next-response-drain-red` reproduced the actual native-close facts and
required recorded failure restoration of its still-pending test worker. The
bounded regression then reported1 passed /1 cancelled /exit1 after5 seconds on
the original source. `rm09c-configuration-final-pure` passed131/131, including
that same bounded HTTP test on the corrected source: native closure joined,
`finish=false`, actual pipe settled in8.5812ms. The ordinary finished control
also passed with exact bytes. Both deadlines and failures remain retained.

`rm09c-next-response-drain-final-build` passed production compilation,
TypeScript and route generation. The rebuilt candidate's five Configuration
journeys have complete passing native executions, with independent SQL,
Operations, Audit and actual cleanup. See
[the testing results](configuration-testing.md) for exact reports and the
separate Windows process limitation. Earlier checkpoints remain separate.
