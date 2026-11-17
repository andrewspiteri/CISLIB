---
title: RestClient focused acceptance map
type: document
status: Draft
change_id: CIS-0010
cis:
  stable_id: cislib:change:cis-0010:acceptance
---

# Proposed RestClient acceptance mapping

Planning-only working input for CIS-0010 against main `7b5923c031027b932f730e1cf45b2d51270b17cc`.
The supplied specification and guide are reference inputs; this mapping grants no
implementation or execution authority. No RestClient check below is claimed to
exist, have run or have passed. Canonical documentation and work records remain
owned by the coordinating agent.

## Owners and existing test families

| Label | Coherent owned outcome |
| --- | --- |
| RC1 | Portable contracts, guarded result/context construction, option snapshots and shared normalization |
| RC2 | Native transport/content, codecs, byte limits and request/response resource ownership |
| RC3 | Ordered callbacks/handlers, final authentication and response/business mapping |
| RC4 | Optional upstream retry adapter, explicit policy selection, delay and replay rules |
| RC5 | Logical orchestrator, admission/deadline/cancellation, terminal commit and actual drain |
| RC6 | Logical/payload Metrics binding and bounded host-owned outgoing native selection |
| RC7 | Actual host/DI example and packaged consumers, integrated lifecycle and final public-contract alignment |

Use the existing `CIS.Core.UnitTests` for controlled operation/content/policy cases,
`CIS.Core.IntegrationTests` for real local HTTP, host composition, exporter and
actual package consumers, `CIS.Core.ArchitectureTests` for dependency/public API
boundaries, and `CIS.Core.BusinessCases` for the runnable consumer outcome.
Extend `CIS.Core.CompatibilityTests` only for genuinely affected established
contracts. Reuse `CIS.Core.TestSupport`, existing host/temporary-directory/package
fixtures and ordinary MSTest. No replacement runner or general testing framework.

Task labels are proposed boundaries, not allocated CIS work IDs. Each owner keeps
its necessary tests, documentation, review and completion evidence with the owned
implementation. A case may establish several rows; this is not one test per row
or a demand for a full combinatorial matrix.

## Explicit acceptance mapping

| Source requirement | Primary proposed owner(s) |
| --- | --- |
| REST-01 Native facilities | RC2 |
| REST-02 Portable semantics and adapter boundaries | RC1, RC7 |
| REST-03 Asynchronous verb wrappers/common path | RC1, RC5 |
| REST-04 Authentication/customization options | RC3 |
| REST-05 Ordered extension seams | RC1, RC3 |
| REST-06 Typed result and context | RC1, RC2, RC5 |
| REST-07 Business failures as results | RC3, RC5 |
| REST-08 Explicit policy-based retries | RC4, RC5 |
| REST-09 Actual timeout cancellation and settlement | RC2, RC3, RC4, RC5 |
| REST-10 Automatic logical/native/payload Metrics | RC5, RC6, RC7 |

| ID | Primary owner(s) | Meaningful focused native evidence to plan |
| --- | --- | --- |
| A01 | RC1, RC2, RC5 | A small verb/Send matrix observes the actual method, resolved URI, repeated query/header values and declared body at a controlled transport. Verify wrappers share behavior, relative-base trailing-slash resolution, and unsupported CONNECT/upgrade capability returns before dispatch. |
| A02 | RC1, RC2 | Literal result vectors cover a normal DTO, legitimate `0`/`false`, empty success, representable JSON null, unrepresentable null, required absent value and failure default(T). Distinguish value presence from nullness; property access remains inert and no public construction/copy route can create inconsistent states. Pin Text/Bytes empty-body semantics in the reconciled contract first. |
| A03 | RC3, RC5 | Non-2xx never decodes into the success DTO; a mapped HTTP-200 rejection retains actual status and a separate remote code. Expected transport/malformed-success cases return documented categories; an unexpected callback exception surfaces. Include a later no-response terminal attempt so its status cannot accidentally inherit an earlier response. |
| A04 | RC3 | Signal-held async customization precedes authentication; observe final encoded URI, headers and exact signing representation at the real primary-send boundary. Assert effective token propagation, no post-signing ordinary mutation, and A/B/C outward versus C/B/A inward handler order. |
| A05 | RC1, RC3, RC4 | Two overlapping calls reuse client/options yet retain separate headers, credentials and contexts. Test default inheritance, explicit callback replacement/disable and retry inherit/None/replace without mutating defaults or shared native headers. |
| A06 | RC2 | Independent wire/value cases cover JSON/text/bytes/no-value response modes and JSON/text/bytes/form/stream requests. Check incompatible T/format validation, malformed nonempty JSON, exact limit versus one byte over, and decompressed read limits without trusting Content-Length. Keep error capture bounded and distinguish its truncation from message truncation. |
| A07 | RC2, RC5 | Controlled blocked send/upload and body-read cases observe the actual cancellation token, actual task termination and owned disposal before the public timeout result completes. One focused real local HTTP body/send check confirms native cancellation composition. No assertion of exact server disconnect/rollback timing. |
| A08 | RC3, RC4, RC5 | Hold authentication/customization or an upstream retry delay; expire the total budget and observe cancellation/cleanup with zero additional dispatches. A pre-dispatch preparation failure has AttemptCount zero. |
| A09 | RC5 | A cooperative operation enters deliberately held asynchronous cleanup after timeout. The public task stays incomplete while its owners remain usable; releasing cleanup allows the timeout result. Every test exit releases and joins its gate in finally. |
| A10 | RC5 | Controlled cancellation-source cases distinguish caller cancellation (caller-associated token), total deadline and optional attempt timeout. Two-order terminal cases establish cancellation before commit versus cancellation after completed result; concurrent calls prove isolation. Reconcile shutdown/defect precedence before pinning the simultaneous-source matrix. |
| A11 | RC1, RC4, RC5 | No policy dispatches once; inherited/None/replacement choices have one retry owner. Script 503/503/200 and exhausted 503 to assert fresh dispatch count, terminal status/error and finite stop reason. Selecting retries without an executor fails configuration rather than silently running once. |
| A12 | RC4 | Native parsed seconds/date Retry-After supplies a server minimum, invalid values use configured backoff with bounded diagnostics, and a long server delay yields the last response plus InsufficientTimeRemaining without early send. Observe one chosen wait for both admission and waiting; assert jitter bounds, not vendor random sequences. |
| A13 | RC3, RC4 | The preset refuses unapproved write methods, business/decoding failures and post-headers body failure. An explicitly replay-safe write uses the same supported idempotency key and equivalent payload across attempts; adding an arbitrary key header alone does not enable retry. |
| A14 | RC2, RC3, RC4 | Record distinct native request/content identities, per-attempt callback order and prior response disposal before the next attempt. Exercise one-shot borrowed content versus a fresh owned stream factory; a handler short circuit has zero primary dispatches and still follows decoding/disposal rules. |
| A15 | RC4, RC5 | A controlled first attempt times out and holds cleanup; the next cannot start until it settles. Advance the same entry-based total budget across work and delays so it never resets, and show total expiration prevents another dispatch. No detached adapter-owned work remains. |
| A16 | RC2, RC3 | Controlled origin/redirect/cookie cases prove defaults and concurrent-call isolation. TRACE omits inherited auth and rejects explicit body/credential fields, including mutations and configured credential query keys. Validate signed final destination before auth; retain native TLS validation. Bounded diagnostic vectors exclude sensitive URL/body/auth/stack content. |
| A17 | RC5, RC6 | Actual operation success, returned failure, caller cancellation and thrown defect each complete one logical duration and balanced active work using one entry binding. Retry/wait/cleanup stay inside that boundary. Controlled completed partial reads/writes and repeated transfers establish actual observed payload counts; HEAD Content-Length contributes no consumed bytes. |
| A18 | RC6, RC7 | A real scrape contains the four logical streams and selected exact host-owned outgoing streams, with application/environment on every sample/child/overflow point. Two actual hosts isolate meters and data; foreign meters and unselected keys drop. Do not equate native histogram count with logical calls or exact owned dispatches. |
| A19 | RC1, RC6 | Disabled/absent Metrics and small configured capacity overflow leave HTTP results unchanged. Validate the proposed client/pair/fallback bounds and stream reservations before activation; repeated unknown operation names use a bounded fallback without retaining per-URL names. Existing admitted series continue; no new broad footprint campaign. |
| A20 | RC4, RC7 | A genuine Core-only nupkg consumer runs the None/single-attempt path without optional collector/resilience dependencies. A separate genuine optional-package consumer exercises configured retry and exported Metrics under the pinned runtime. Verify deployed package assets/closure, not just project-reference compilation. |
| A21 | RC5, RC7 | Stop closes admission, signals client-owned cancellation and waits for actual request/body/callback cleanup before dependent Stopping/disposal. Include a held Heartbeat callback awaiting REST: existing Startup/HB hosting must signal REST cutoff before waiting for HB/Configuration, then join real drains; no new coordinator framework. Borrowed native clients/handlers/streams remain caller-owned. |
| A22 | RC1, RC7 | Compile/run the real example and public consumers against implemented APIs. Architecture checks reject native HTTP, Polly and OpenTelemetry types from ordinary consumer signatures while permitting explicit platform configuration seams and normal Task/CancellationToken. Preserve supplied source bytes/hashes and align contract, defaults, examples and agent routing. |

## Cross-row lifecycle evidence

Use one actual host scenario to establish A07/A09/A21 ordering: a dependency stays
live while a Heartbeat callback awaits REST; shutdown first closes REST admission
and signals its owned cancellation, then the REST operation enters held cleanup.
HB/Configuration and host stop remain incomplete, and dependency Stopping/disposal
must not occur. Releasing actual cleanup settles REST, then the awaiting callback
and existing module/host barriers. An already-canceled host wait budget must not
authorize early teardown. Release all test barriers and join their actual tasks
before disposing the real provider or external owners.

Keep this wiring in existing hosting composition. Startup's coordinator must not
acquire transport dependencies; adding a parallel shutdown controller or making
REST wait for HB while HB waits for REST would violate the intended ownership.
Explicit stream ownership also needs an acquisition point: proposed admission
rejection/invalid arguments acquire nothing, while every admitted acquired owned
stream is settled/disposed even if authentication rejects or a handler shortcuts.

## Evidence and platform truth

Native component tests, an actual local server, a real scrape, a source example
and an actual package consumer establish different facts. Identify which one ran,
the selected runtime/platform, actual counts and retained result/input identities.
Never describe a ProjectReference or friend-visible test as a packaged public
consumer. Do not simulate Ubuntu by changing a platform label on Windows output.
Run each required platform lane only under its own applicable new finite work
authority; keep unrun lanes and release qualification explicitly unproven.

The seven owners propose focused affected checks and required repository gates.
There is no default broad performance/mutation/release campaign, no inherited
execution grant from closed prior modules, and no unrelated Metrics primitive
expansion. Source inspection and this proposed table are not passing evidence.
Preserve the user's untracked launchSettings files and all original source inputs.
