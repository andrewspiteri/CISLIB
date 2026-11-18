---
title: RestClient optional retry execution
type: document
status: draft
cis:
  stable_id: cislib.document.modules.restclient.resilience
---

# Optional retry execution

CIS.RestClient.Resilience uses exact Polly.Core 8.9.1, separately from Core.
The optional package registers one executor and one through thirty-two explicit
immutable policies. Registration makes names available; it does not enable
retries for a client. Core's default and explicit None still mean one attempt.
Use [client registration](core.md) to select a named policy. The executor
registration below alone does not create a client or send requests.

~~~csharp
using CIS.RestClient;
using CIS.RestClient.Resilience;

services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
// Select the registered policy explicitly in the actual client/call options:
var selection = RestRetrySelection.Named("transient-http");
~~~

Install once, with distinct explicit names and no competing executor or policy
registry. Selecting an unavailable policy/executor is a configuration failure.
One pipeline is built per registered policy, with the root TimeProvider or
TimeProvider.System fallback. Operation and executor must use that same object.
There is no per-URL, response-type or operation-instance pipeline cache.

## Eligibility and ownership

The preset permits two retries for GET, HEAD and OPTIONS and statuses
408/429/502/503/504. It starts native exponential jitter at a nominal 500 ms and
caps local delay at five seconds. Transport, decoding, authentication and
business failures are not implicitly retried. A named policy with zero retries
uses an upstream empty pipeline and invokes the attempt once.

Eligibility uses each completed attempt's finalized method, not merely the public
verb before customization. Unsafe methods need both policy inclusion and explicit
replay assurance. Content must be recreatable and the actual attempt owner must
preserve method/destination/payload/key equivalence. A callback receives the
zero-based policy iteration; result AttemptCount counts actual primary dispatches.

Custom finite status/failure/code selections can enable documented outcomes.
Transport always needs a listed portable transport category. The asynchronous
decision callback only narrows eligibility and cannot dispatch or schedule work.
Successful/stopping outcomes never retry. A real post-headers processing failure
requires explicit full-operation body replay and assurance. Merely consuming an
HTTP-error diagnostic prefix does not turn a completed HTTP rejection into a body
failure. The actual attempt owner alone owns any optional attempt timeout.

Every attempt must finish cleanup before returning its detached summary.
Polly receives no native response, stream, typed success value or history.
Thrown programming defects remain faults, including when cancellation also fires.
The executor awaits callbacks and waits; it never races and abandons owned work.

## One delay and one total deadline

Polly chooses local jitter L once. Its public OnRetry callback exposes that value.
CIS parses Retry-After using native HTTP parsing, including seconds, dates and
past dates. Invalid values fall back with one bounded value-free diagnostic;
an unrepresentable integral minimum stops rather than causing an early retry.

On pinned .NET 10, Task.Delay truncates its requested interval to milliseconds.
CIS chooses the representable D=ceil_ms(max(L, server minimum)) once and checks
that exact D against the remaining public-entry budget before either wait.
It awaits supplemental D-floor_ms(L), then Polly awaits its unchanged L.
The actual native scheduled total is D, preserving both minimums without
resampling jitter or copying a backoff engine. The local cap never caps the
server minimum. A delay that cannot fit preserves the terminal cause and records
InsufficientTimeRemaining, with no further dispatch.

Both waits share the operation token and clock. Cancellation prevents another
attempt and remains awaited through actual cleanup; timeout is not permission to
dispose an owner still used by a callback. The outer client owns cancellation
origin precedence and final result commitment. This package adds no independent
HTTP handler retry stack, total timeout, scheduler, provider or collector.

The bounded supported-API proof, focused policy cases, composed public operations
and actual versioned package consumers passed on the recorded Windows runtime.
See [CIS-0010 verification](../../changes/CIS-0010/verification.md) for exact scopes,
counts and failed attempts. Those checkpoints do not establish full platform,
coverage, mutation or release qualification.
