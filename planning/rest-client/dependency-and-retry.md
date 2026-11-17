---
title: RestClient optional dependency and retry proposal
type: planning-reference
status: Draft
---

# RestClient dependency and retry decision — proposed

Planning-only draft against supplied v1.0 specification/guide and main 7b5923c0.
The attachments are reference inputs, not authority to implement or execute.
No dependency approval, native compatibility pass or new work allocation is claimed.

## Package boundary

Recommend an optional CIS.RestClient.Resilience package referencing exact Polly.Core
8.9.1. Its published net10.0 dependency group is empty; its tagged project explicitly
targets net10.0. Root also checked the v3 nuspec. This establishes a compatible
target/dependency candidate, not proof that the complete REST policy works.
Primary evidence: [NuGet metadata](https://www.nuget.org/packages/Polly.Core/8.9.1#dependencies-body-tab)
and [tagged project](https://github.com/App-vNext/Polly/blob/8.9.1/src/Polly.Core/Polly.Core.csproj).

Core retains Logging/DI abstractions 10.0.12 and Microsoft.AspNetCore.App.
The installed framework already supplies Microsoft.Extensions.Http/IHttpClientFactory.
Do not add a duplicate HttpClient package or retry handler beneath the owned executor.
Do not add Polly umbrella/Extensions, the standard resilience default stack, or
Microsoft.Extensions.Http.Resilience merely to obtain a second retry owner.

The original proposed approval scope must include the optional project, central
version pin, exact Polly.Core NuGet source mapping, solution/test references and
normal package locks. Core must not reference the optional assembly or Polly.
If implementation establishes another actual dependency, amend the concrete proposed
closure before approval; no wildcard source mapping or Metrics-exception reuse.
Base/None works without the adapter; a selected unavailable executor fails validation.

## Public-API delay candidate and bounded early proof

Polly can own retry iteration, jitter, attempt limit and cancellable upstream waiting.
Its DelayGenerator receives no computed jittered local delay. The public OnRetry
arguments expose RetryDelay after calculation, and the strategy awaits OnRetry
before its own delay. Its internal RetryHelper is not an extension API.
See [arguments](https://github.com/App-vNext/Polly/blob/8.9.1/src/Polly.Core/Retry/OnRetryArguments.cs)
and [pinned strategy order](https://github.com/App-vNext/Polly/blob/8.9.1/src/Polly.Core/Retry/RetryResilienceStrategy.cs).

Candidate: configure native exponential jitter/local cap, without DelayGenerator.
The attempt returns only a bounded classified outcome after actual response/content
cleanup. OnRetry receives local L, parses retained Retry-After through native parsing,
chooses D=max(L, server minimum) once, and checks D against the remaining entry-based
budget before either wait. If admitted, await native cancellable supplemental D-L
inside OnRetry, then let Polly await its unchanged L. Both use the same operation
token/time provider. Never recompute jitter, clamp the server minimum to five seconds,
or hold an undisposed previous response during either wait.

Insufficient/unrepresentable D must preserve the terminal outcome and stop before
waiting/dispatch. One private adapter control signal plus one retained terminal
summary is a possible implementation; unexpected callback exceptions still escape.
Cancellation must remain distinguishable from that control signal and must settle
all owned work. No response history, custom scheduler or copied backoff algorithm.

This split wait is a proposed compatibility interpretation of one chosen total
delay, not a proven implementation or silent relaxation of the supplied contract.
Before dependent integration, one focused existing-project proof must establish:

- Short server minimum cannot shorten L; long minimum is not capped to five seconds.
- Seconds/date/past/invalid Retry-After and one selected D drive admission and waiting.
- D beyond the remaining budget preserves the last response and sends nothing else.
- Cancellation during supplemental/native wait prevents dispatch and is fully awaited.
- A canceled attempt with held cleanup remains incomplete until actual cleanup releases.
- Same logical deadline, fresh replayable requests, bounded state and final cause survive.

Use public APIs and existing MSTest/time-provider facilities; no new harness,
representative pilot, broad campaign, package restore or native proof at intake.
If this candidate fails, record the concrete public-API gap before changing semantics
or requesting an additional upstream helper dependency.

## Required shared contract decisions

Keep one final-auth boundary after ordinary handler/customization mutations and
final content preparation. Authentication can alter only declared credential fields;
freeze destination/body/signed fields afterward, then count dispatch immediately
before the primary transport. TRACE suppresses inherited authentication and rejects
explicit/body/customized credentials, including declared credential query fields.

Owned SocketsHttpHandler profiles can enforce redirects/cookies/credentials,
infinite HttpClient timeout and host MeterFactory before first use. An arbitrary
borrowed HttpClient/handler chain cannot be inspected reliably for nested retries,
hidden timeout, redirects, credential mutation or meter scope. Document the supported
borrowed configuration attestation/validation limits or reject incompatible profiles;
never claim opaque chains satisfy isolation/security. Borrowed resources stay borrowed.

Expected preparation/authentication failures need typed context completion; programming
exceptions escape. Apply the proposed caller > total > stopping > attempt > actual
precedence to expected outcomes/identified cancellation, as P04 explains. Pin a
single bounded decoder/mapper consumption owner, and ErrorMessageTruncated explicitly.
CorrelationId needs a recorded mapping: recommend valid existing W3C TraceId only,
null when absent, with no generated ID; existing Logging OperationId remains distinct.
