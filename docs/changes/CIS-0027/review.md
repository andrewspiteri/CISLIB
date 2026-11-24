---
title: RM-08 implementation review
type: review-record
status: InProgress
change_id: CIS-0027
---

# RM-08 independent implementation review

## WORK-001 — shared contracts and wire boundary

Reviewer: `rm08_authority_store_intake`, independent of the Configuration and
Management contract authors and root's canonical schema/vector authoring. The
review used actual source, callers, public ports, the accepted profile, canonical
OpenAPI/schema and independently authored vectors. Native outcomes are recorded
separately in verification; an author's readiness report is not a passing test.

Final contract verdict: **accepted; no remaining required or optional finding**.
Review covers the contract freeze, not later backend, activation or provider proof.

Required findings addressed during initial internal review:

- Match the canonical 64-issue and 1,024-character problem-detail limits; retain
  future response intents explicitly while keeping mutation inputs closed.
- Keep metric dimensions fixed; IDs/reasons belong only in appropriately
  authorized bounded audit/status data. Assign the shared wire codec to001 so
  backend work has no hidden dependency on a later client implementation.
- Preserve trusted desired identity only after the whole identity/outer metadata
  fence. Carry identified local rejection through the public failed-call result
  without inventing HTTP failure or a remote Problem; arbitrary substitutes do
  not authenticate themselves by constructing that data.
- Represent source-pair and complete-map preview separately, including exact
  current precondition, removed/retained overrides and paged advisory recipients.
- Make actual source IDs discoverable through authorized HTTP rows. Common rows
  reveal no application binding; application rows identify their own binding and
  host. One immutable schema can serve several host scopes.
- Permit metadata-only discovery without raw default values. Omitted defaults
  are redacted; explicit null is malformed. Trusted provisioning retains the
  complete immutable defaults contract independently of this read projection.

Root's promised-workflow review traced discovery → source ETag → publication and
binding → map ETag → release/assignment, plus preview and original-key recovery.
These steps drove the finite source-row and preview corrections before backend
implementation. The later real service tests must establish actual authorization
before paging and durable effects; static fixtures establish representation only.

Necessity and scope: **continue the direct design**. New bounded DTOs/strict
codecs and companion interfaces preserve old stores, pipelines and
`IManagementClient`. The existing development-only schema fixture gained only
`oneOf`/`not`, needed for exact absent/present and result/rejection invariants;
this adds no general validator, runner or test harness. No product responsibility,
dependency, provider, timer, authentication system or automatic rollout was added.
The small public content-rejection boundary exposes an already required local
fact. Native and source costs remain inside the original shared allocation.

Provider transactions, value-gate acceptance, startup/credential handoff, actual
transport settlement and TypeScript parity remain the later owners' obligations.

## WORK-005 — native credential preparation and adoption

Reviewer: `rm08_configuration_intake`, independent of root's PEM and the client
agent's Entra implementations. The existing repository Security skill and actual
native ownership paths were used. Final verdict: **accepted; one required finding
resolved, no remaining required or optional finding**. Review cost18 active minutes.

The original Entra retirement called the existing runtime's disposal under the
preparation gate. Its synchronous cancellation could invoke an SDK callback under
that new gate. Retirement now publishes its retained promise under the gate and
starts disposal outside it. The independent reviewer verified both the correction
and its new cross-thread callback test: obtaining the retained retirement task does
not block, while actual held SDK work still prevents cleanup from completing.

Review traced exact immutable policy/binding identity, one final-provider claim
before DI obtains disposal ownership, borrowed versus owned credentials/keys,
passive construction, fail-closed preparation, cancellation and failed-Build drain.
PEM reuses captured fixed trust, retires the bootstrap transport and reads the
current leaf into the actual final metered transport. Existing Heartbeat renews
that transport. Entra reuses the same native SDK credential sequentially across
bootstrap and final runtimes. Native verification is separately recorded below;
source inspection alone was not accepted as TLS or SDK proof.

Necessity and scope: these are explicit preparation/adoption owners over existing
native implementations. No temporary provider, authentication framework, token
cache, retry loop, timer, trust-store installation or deployment claim was added.

## WORK-004 — paired Configuration acceptance

Root independently reviewed the Configuration agent's implementation through the
actual pipeline/value gates, paired caches, epoch/session coordination, failures,
event delivery, reader ownership and byte/materialization limits. One required
finding was corrected: a retained failed reconciliation task previously masked
later ordinary poll recovery. The short control seam now observes completed
readiness before returning that stale completed failure, while retaining an
in-flight owner and coalescing an incomplete gap. A focused regression checks
recovery and no additional reads. Native results remain separately recorded.

An initial review suggestion to echo a token on `Unchanged` was withdrawn after
checking the complete frozen factory: the result deliberately asserts equality
with the supplied request and cannot carry a foreign token. The exact retained
cache/session and cancellation fences already apply. No extra public contract was
added to address that mistaken premise.

The direct companion/pipeline design preserves legacy independent reads and
watch ownership. Limits cover logical source/default/mapping ownership rather
than arbitrary host callbacks or the CLR heap. Final native checks and bounded
adapter integration remain required; this review does not claim a working host.

Further006 composition review identified two requirements before004 closure:
healthy readiness observations must use retained aggregate state, with terminal
setter failure invalidating it under the value gate; and kicking the one retained
reconciliation task must not synchronously sweep roots in the short caller.
Both final corrections are accepted on source review. Readiness copies retained
aggregate state; actual completion scans roots outside the observation gate and
rechecks session/epoch before publication. Permanent setter failure closes the
shared flag under the value gate. One queued retained task owns the actual sweep,
including rebind and Stop drain. Three regressions passed, and the final793-case
Configuration run passed with no failures/skips or build warnings. No remaining
required or optional finding in004; actual facade integration remains006.

## WORK-002 — catalog provisioning and immutable sources

Reviewer: `rm08_configuration_intake`, independent of the backend author;25 active
minutes. No required product finding in this bounded story. Actual resource
authorization precedes exposed rows, paging and raw defaults/content; original-key
arbitration precedes mutable precondition/current-manifest evaluation. Effects,
required payload-free Audit and original results share the existing original
transaction. Newly prepared success requires known commit; replay/lookup retain
original facts. Session native cohorts are fixed on admission and lock order is
consistent with cleanup. Migration preserves existing authority/application rows
and creates only the empty catalog; feature discovery reflects installed handlers.

Root required additional configuration-specific native uncertain-completion proof
rather than inferring it from older metadata tests. Four cases use the
existing actual native commit/disposal boundary and pass in the final49-case
PostgreSQL run. The first build failed a raw-string indentation
check; the author corrected whitespace only. Existing guides now link the actual
manifest/quota/apply recipe. No new store, journal, framework or test harness was
added. Release/map/adoption/retention functionality remains with later owners.

A separate final-delta review by `rm08_clients_adoption_intake` required the
manifest UTF-8 allocation check before encoding. That correction is accepted.
The author also identified legal literal input whose response escaping exceeded
one MiB. The fixed publication normalizer validates its exact logical input
without expanding it before arbitration; the original effect rejects an
unrepresentable publication before insertion, revision or quota changes. The
raw-HTTP regression proves original rejected/replayed/found facts, one original
journal/Audit effect and no publication effect. Exact403/emptybody operational
denial and the owned corruption fixture preserve production behavior. Final
verdict: **accepted; no remaining required finding**. Optional deeper duplicate-key
examples remain optional; no further campaign is required. The two delta reviews
cost10 and8 active minutes; actual native checks are root-owned.

## WORK-008 — portable client and request ownership

Root independently reviewed the TypeScript agent's actual codec, per-call target
capture, immutable retained snapshot validation, POST effect classification,
async preparation/decode and existing response drain paths. No required finding.
Commands remain closed; bounded future response values remain explicit unknowns.
WebCrypto runs and is joined inside the entry deadline. Conditional304 is scoped
to an exact retained target/session/ETag and empty response; other redirects remain
blocked. Only GET retries, and readonly validation/preview do not report mutation
uncertainty. Existing synchronous callbacks and default non-GET classification
remain compatible. Actual181-case/browser/compile evidence is recorded separately.

The existing parent HTTPS fixture gained assertions for its twelve GET/PUT/readonly
POST/mutating POST cases; actual native execution passed after correcting the
expected-first operands in the extra coverage assertion. Necessity/scope:
additive typed ports and compatible async hooks reuse native fetch/WebCrypto and
the existing request owner; no runtime dependency, broker, Node browser import,
generic mapper, polling owner or test harness was introduced.

## WORK-003 releases, maps and authoritative snapshot review

Root independently traced native resource authorization and original-key
arbitration before mutable preconditions/references. Catalog-first locking
serializes complete map decisions and reference installation; release creation
does not activate a map. Whole replacements preserve explicit override removals,
advance positive generations even for identical decisions and refuse exhaustion.
Preview is actor/proposal/map-bound, side-effect-free and redacted without content
authority. A nullable empty source list now fails closed rather than implying
value permission; normal manifests provision at least one application stream.
The other backend author independently reviewed frozen native session/cohort,
one joined map/release/two-source observation, strict content scope/digests and
fresh reads with actual rollback/disposal ahead of either200 or304. Neither
snapshot result nor304 refreshes control authority. No remaining blocker found.
The58 affected native cases pass. Initial compile errors used a named lambda
parameter `_` where discards were intended. Four initial snapshot test failures
used POST instead of canonical PUT in setup; method corrected without weakening
any assertion or route. All failure receipts and costs remain retained.

## WORK-007 CSharp client and adapter review

Independent review of the final changes found no remaining blocker. Per-call
response validation binds original keys, fixed operation families, actual
resources and status sequences. Identified content failures retain only the
trusted desired identity and a finite reason code. Conditional304 preserves the
existing native REST failure/status facts and requires an exact retained target,
source digests, ETag and observed empty response. The central runtime alone owns
and charges parsed and exact source text; the adapter adds no persistent cache.
Legacy reads/watch and borrowed ownership remain explicit. The outside-friend
consumer retains exact restored/deployed package-byte assertions and exercises
the public substitution boundaries, without claiming native provider behavior.
Actual native units and package execution passed. Initial failures were a test
namespace collision, omission of the isolated server's allowed origin and a
fixture Source UUID differing from the independent canonical vector. Production
origin checks and per-resource response validation were preserved.

## Follow-through in WORK-006

The later native preparation/activation integration checks should strengthen the
existing PEM privacy assertion by passing its expected substring first:
`Assert.DoesNotContain(files.Path, error.Message)`. The product currently returns a
constant safe error; the old reversed operands did not meaningfully assert that
boundary. This small existing-test correction belongs with006's already required
native failed-preparation checks and must be rerun there, without another harness.

## Completed host, reporting and retention review

The native handoff review verified one captured policy, credential, clock and
registration; bootstrap transport retirement before Build; real final meter
ownership; and disposal of the captured provider before unclaimed preparation.
Required corrections kept reporting refusals distinct from genuine control gaps
and added one confirmed-recovery retry on the existing queued Configuration
worker. Task, session and epoch fences protect the retry and completion. Both
completed and still-draining offline failures have passing regressions; ordinary
cached behavior is preserved. An independent follow-up found no remaining blocker.

Actual native startup then exposed final security registration displacing the
Configuration startable. The facade now restores that exact descriptor's first
position after all registrations, preserving other relative order and the
existing validation. Explicit native activation still precedes ordinary Startup.
A fixture comparison of the public provider wrapper with its internal root scope
was corrected to compare their shared scope factory and singleton. Five real
SDK/TLS/PostgreSQL cases now pass, including reconnect, live empty/missing state
and held failed-Build disposal. Fifteen actual credential preparation cases and
the existing 64 Application lifecycle cases also passed.

Adoption review required exact aggregate encoded page accounting and the frozen
binding/host match for current convergence. Both are corrected and independently
verified. Historical ineligible sessions remain visible as unknown. Native
Unicode pagination and host-change regressions pass. The first provider run also
exposed a strict completion check demanding rollback after a successful report
commit. Strict writes now require the existing known commit and clean settlement;
strict reads still require confirmed rollback and clean settlement. Three native
commit/read cleanup uncertainty rows preserve the original stored receipt and
single quota charge. Completed feature discovery now expects all 19 additions.

Retention independently preserves eligible-first history ranking, every retained
reference, one shared deletion batch and exact native returned-byte accounting.
Neither cleanup nor adoption changes the original Operations/Audit retention.
No remaining review finding; all 79 selected provider cases passed. These local
proofs do not establish external federation, physical process identity or release
qualification.

## Final consumer review

Independent public-helper/example review found no remaining finding: the helper
is passive, enrolls only existing CountrySettings as RestartRequired, composes
prior validators/factories/store and refuses conflicting host/session/enrollment
choices. It preserves ordinary unmanaged composition. Shared URI, timer and retry
validation keeps central detached candidates consistent with actual catalog input.
The native review corrected the reused fixture credential allowlist to derive
the exact configured management origin, so operator replica0 and consumer replica1
remain a real cross-replica test without relaxing origin enforcement.

The actual native CountryData story,61 example units, public package consumers,
three original binary/source/API checks,1128 current Linux units and151 Linux
contract cases passed. Two pre-existing-style checks failed in the broad consumer
selection and passed unchanged when isolated. Their original receipts and the
limited diagnostic inference are recorded in verification; no definitive cause,
clean first-run suite or separate physical-process identity is claimed.
The current acceptance matrix makes those limits explicit. Guide/catalog/skill
review found no missing selected links or duplicate catalog identifiers.
