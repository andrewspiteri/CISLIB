---
title: RestClient focused acceptance map
type: document
status: Draft
change_id: CIS-0010
cis:
  stable_id: cislib:change:cis-0010:acceptance
---

# RestClient acceptance and evidence mapping

The approved P01–P12 profile is implemented from baseline main
`7b5923c031027b932f730e1cf45b2d51270b17cc`. Andrew Spiteri approved the bounded plan
and its shared allocation on 17 November 2026; RC1–RC7 correspond to
WORK-001–WORK-007. Supplied documents remain reference material, not execution
authority. This map records implemented behavior and selected evidence.
The bounded implementation and scoped checks are complete. [Verification](verification.md) is the authority
for source identities, execution accounting, currentness and later dispositions.

## Native delivery checkpoint — 18 November 2026

The six retained Windows gates contain **1,915 passed, zero failed and zero
skipped** cases, using SDK 10.0.401 and runtime 10.0.12. These are selected native
component/regression, composition and delivery checks, not 1,915 distinct
RestClient requirements or a full release qualification.

| Gate | Passed | Native execution suffix |
| --- | ---: | --- |
| Selected Unit/component regressions | 1,172 | `8483773105df4b37ae67a35e5f7195b9` |
| Host/runtime/collector integration | 201 | `5f40c6a5bd2a478fbec7ff53a55d6162` |
| Architecture | 361 | `4019e07d7818487483c0fb6d4e47893b` |
| Established compatibility | 179 | `7685e02da348474db66a8a729b84e171` |
| Actual package consumers | 1 | `9852124c6de44d3cab80365c5098d4c6` |
| Actual source example story | 1 | `a488c003f0d2446cb90e42ca855d7442` |

TRX files are retained as `.cis/local/rest-client-intake/rc7-final-{unit,host,
architecture,compatibility,package,business}-pass.trx`, with matching run logs.
The native records report scope complete, zero pending batches and current unit
inputs at their checkpoints, while retaining `qualified=false` and the runner
pilot advisory. Later source/profile currentness is a separate check.

The whole-file `RestResponseMapping.cs` mutation canary recorded 15 killed,
five survived and two ignored mutants: **75%**, below the unchanged 90% gate.
The subsequent 85% observation and final 90% result are retained in verification:
18 killed, two surviving, two ignored, denominator 20; zero timeout/no-coverage or
incomplete outcomes. Normal restoration passed all 134 REST unit cases and Core's
pre-mutation assembly hash matched. The component threshold is met; the canary
remains partial/qualified=false and does not establish whole-library or release
qualification. The selected component and threshold stayed unchanged throughout.

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

Tests use the existing MSTest projects and `CIS.Core.TestSupport`. Necessary
tests, documentation and review remain with their implementation owners. The
following real test families supply the evidence referenced below. A case can
establish several rows; this is not a full combinatorial matrix.

| Evidence family | Actual tests |
| --- | --- |
| U1 — contract and snapshots | Unit `RestResultContractTests`, `RestSnapshotContractTests`, `RestExecutionContractTests` |
| U2 — native content and bytes | Unit `RestHttpContentTests`, `RestHttpResponseTests`, `RestHttpTransportTests`, `RestEncodingOwnershipTests`; actual local Kestrel and controlled streams |
| U3 — ordered extensions | Unit `RestRequestFinalizerTests`, `RestExchangePipelineTests`, `RestResponseMappingTests`, `RestCallbackCancellationTests` |
| H1 — public runtime | Integration `RuntimeResultTests`, `RuntimeCancellationTests`, `RuntimeBoundaryTests` |
| R1 — retry and replay | Integration `ResilienceExecutionTests`, `ResilienceDelayTests`, `RuntimeRetryTests` |
| O1 — composition and shutdown | Integration `RestClientRegistrationTests`, `RestClientShutdownTests` |
| M1 — logical and native observation | Unit `RestClientModuleMetricsTests`; Integration `RestClientMetricsTests`, `Metrics.RestClientCollectionTests` |
| D1 — delivered consumers | Integration `RestClientPackageConsumerTests`; Business `RestApiStories`; actual `examples/rest-client/RestApi` sources |
| G1 — boundaries and preservation | `RestClientArchitectureTests`, shipping dependency/runtime guards, and retained Type Manager/Healthcheck compatibility suites |

## Explicit acceptance mapping

| Source requirement | Primary implementation owner(s) |
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

| ID | Primary owner(s) | Implemented behavior and focused evidence |
| --- | --- | --- |
| A01 | RC1, RC2, RC5 | U1/U2/U3/H1 exercise the shared verb path, real POST bytes, resolved URI/query/header behavior and pre-dispatch rejection of CONNECT/upgrade capabilities. |
| A02 | RC1, RC2 | U1/U2/U3/H1 consume literal result/response vectors and check guarded construction, `0`/`false`, empty JSON/Text/Bytes, present null, malformed/unrepresentable null, required absence and failure default(T). |
| A03 | RC3, RC5 | U3/H1/R1 check HTTP versus business mapping, safe errors and remote codes, unexpected faults, and a later no-response exchange that cannot reuse the earlier 503 status. |
| A04 | RC3 | U2/U3/R1 check awaited ordering, final encoded destinations/native headers, exact bounded signing bytes, sealed ordinary fields and outward/inward handler order. |
| A05 | RC1, RC3, RC4 | U1/U3/R1 check isolated option/header/query snapshots, overlapping callback state, explicit replacement/disable, and inherited/None/replacement retry choices without changing defaults. |
| A06 | RC2 | U2/H1 check real JSON/text/bytes/form/native multipart/stream content, absent/no-value results, type/format checks, malformed JSON, exact/one-byte-over limits, decompression, bounded error prefixes and independent message truncation. |
| A07 | RC2, RC5 | U2/H1 hold actual serialization/body reads and join them through cancellation and disposal. The native flushed-body case proves headers can complete before body settlement; no remote rollback/disconnect-time guarantee is inferred. |
| A08 | RC3, RC4, RC5 | U3/H1/R1 hold callbacks/signing/retry decisions or waits across the entry deadline, observe token provenance and settlement, and prevent further primary dispatch. Declared pre-dispatch failures retain AttemptCount zero. |
| A09 | RC5 | H1/R1 hold acquired cleanup after cancellation/timeout and keep public completion incomplete until release; U2 also checks actual asynchronous owned-stream disposal. |
| A10 | RC5 | H1/R1 establish caller/total/stopping/attempt precedence, caller-associated canceled tasks, foreign or initially unsignaled OCE faults, encoder fault preservation despite later cancellation, isolation and an already committed result remaining complete. |
| A11 | RC1, RC4, RC5 | U1/R1/O1 check default None, policy inheritance/replacement, 503/503/200, exhaustion/actual dispatch count, and missing named-policy/executor configuration errors. |
| A12 | RC4 | R1 uses real Polly timers and native Retry-After parsing for seconds/date/past/invalid/missing values, one admitted representable wait, preserved server minimum and InsufficientTimeRemaining without another dispatch. |
| A13 | RC3, RC4 | R1 checks finite actual-method eligibility, conservative exclusions and replay-assured POST with stable provider key/payload and refreshed credentials. Changing ordinary key/destination or body prevents another send and preserves the prior failure. |
| A14 | RC2, RC3, RC4 | U2/U3/H1/R1 observe fresh contexts/native content, ordered callbacks and prior disposal, borrowed one-shot versus owned fresh factories, exact prepared signing replay and zero-dispatch owned short circuits. |
| A15 | RC4, RC5 | R1/H1 prove attempt timeout waits for real cleanup before another attempt, retains the original total budget, and stops dispatch after total expiration. Canceled acquired one-shot content cannot be replayed after disposal. |
| A16 | RC2, RC3 | U2/U3/R1 check origin/TRACE/credential/query rules, default redirect/cookie behavior, signed ordinary replay identity and safe diagnostic handling. Native TLS validation remains enabled; these are not a general TLS/proxy interoperability qualification. |
| A17 | RC5, RC6 | M1/U2/H1/R1 check balanced one-shot logical completion across success/failure/cancel/defect, entry-to-cleanup duration, actual partial/retried payload counts, zero signing-buffer upload count and no body count from HEAD Content-Length. |
| A18 | RC6, RC7 | M1/D1 execute actual exporter scrapes, mandatory identity, exact root-owned native meters, two-host isolation and foreign/unselected stream filtering. Native header timing/count is kept separate from logical calls and owned dispatches. |
| A19 | RC1, RC6 | U1/M1 check finite client/pair/fallback metadata, conditional profile reservation, old no-REST profile compatibility, disabled collection, capacity overflow and bounded unknown-operation fallback. |
| A20 | RC4, RC7 | D1 packs actual Core/Resilience/Metrics assemblies and builds, publishes and runs separate Core-only and optional consumers from fresh offline caches; DLL/package hashes, dependency closure and loaded paths are asserted. |
| A21 | RC5, RC7 | O1/H1/U2 prove closed admission and actual cancellation/drain before dependency teardown, including held HB-awaiting-REST paths for HB-only/Startup and both Metrics orders. Borrowed transports/streams remain external ownership. |
| A22 | RC1, RC7 | D1 runs the real RestApi source and package consumers; G1 enforces ordinary API/optional dependency boundaries and preserved established contracts. Supplied source hashes remain unchanged; guides reference the implemented APIs. |

## Cross-row lifecycle evidence

`RestClientShutdownTests.OrderedHostStopCancelsRestBeforeWaitingForHeartbeatAndKeepsDependenciesThroughCleanup`
exercises the actual HB-only and Startup bridges in both Metrics registration
orders. A dependency stays live while HB awaits REST; cutoff signals REST before
waiting for that callback, and held acquired cleanup prevents host completion and
dependency Stopping/disposal. Release settles the REST call, then HB and the
existing host barriers, despite an already-canceled host wait budget.

The wiring stays in hosting composition. StartupCoordinator has no REST dependency
and no new lifecycle framework is introduced. Admission and valid acquisition are
distinct: rejected work acquires no caller stream; acquired owned streams are
settled/disposed after authentication rejection, cancellation and faults. Held
test barriers are released and their actual tasks joined before provider cleanup.

## Evidence and platform truth

Component/friend-visible tests, real local HTTP, exporter scrapes, source stories
and actual package consumers establish different facts. Their selected tests and
receipts support this map; aggregate counts alone do not prove every possible
case in a requirement. The package gate is actual package-only public consumption,
not a renamed ProjectReference test.

Ubuntu execution, complete release/platform coverage, full coverage thresholds,
whole-library mutation, broad performance qualification and publication remain
unproven here. The one selected mapping canary does not replace those obligations
and now meets its own 90% threshold. No runner-pilot qualification,
forced reuse, new allowance, main merge or branch closure is implied. Subsequent
execution requires applicable finite capacity and a legitimate current work owner.
Preserve both user launchSettings files and the original source inputs. This
document's Draft state does not imply human approval or release qualification.
