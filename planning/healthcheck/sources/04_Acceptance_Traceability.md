# Healthcheck acceptance traceability

**Source:** preserved Healthcheck definition v0.1, HC-AT-01 through HC-AT-59.  
**Status:** All cases are **Not executed** in this planning deliverable. Mapping is not passing evidence.

The source scenario text is retained below. The references in the last column are original requirement IDs; the Technical Intent reconciles obsolete environment/packaging mechanics without removing the behavioral obligation. Native test owner and implementation owner are separate where useful.

| Source case | Scenario / required result | Implementation | Test owner | Original requirement |
| --- | --- | --- | --- | --- |
| HC-AT-01 | Two factory calls with identical options produce independent monitor state and distinct identities. | HC01 | HC07 | HC-OWN-02 |
| HC-AT-02 | A monitor's optional name or class type never merges its state with another instance. | HC01 | HC07 | HC-OWN-02 |
| HC-AT-03 | An active critical Unhealthy monitor makes the aggregate Unhealthy. | HC01 | HC07 | HC-CRT-01; HC-STA-02 |
| HC-AT-04 | A noncritical failure remains detailed while the aggregate is Healthy in the absence of critical failures. | HC01 | HC07 | HC-STA-02 |
| HC-AT-05 | Pending initial state is visible without inventing a failure; incomplete startup still prevents readiness. | HC01 | HC07 | HC-STA-01; HC-APP-03 |
| HC-AT-06 | Threshold three stays below breach for failures one/two and flags on failure three. | HC01 | HC07 | HC-POL-02 |
| HC-AT-07 | Failure, failure, success, failure produces streaks 1, 2, 0, 1 and the selected recovery. | HC01 | HC07 | HC-POL-02 |
| HC-AT-08 | Skip/expected cancellation neither increments nor resets the failure streak. | HC01 | HC07 | HC-POL-03 |
| HC-AT-09 | Numeric equality follows the selected comparison; invalid values publish no fictitious measurement. | HC01 | HC07 | HC-POL-04 |
| HC-AT-10 | Repeated refresh failure after usable data was published does not invalidate availability. | HC01 | HC07 | HC-POL-05 |
| HC-AT-11 | Explicit invalidation flags availability; a new empty cache starts without inherited success. | HC01 | HC07 | HC-POL-05 |
| HC-AT-12 | Success clears only the relevant policy, leaving an independent flagged condition intact. | HC01 | HC07 | HC-STA-04 |
| HC-AT-13 | Dispose the last failing critical monitor; subsequent snapshots omit it and recompute aggregate health. | HC01 | HC07 | HC-DSP-01 |
| HC-AT-14 | Snapshot capture racing disposal sees a coherent complete version on either side of withdrawal. | HC01 | HC07 | HC-DSP-01; HC-SNP-01 |
| HC-AT-15 | Withdrawal before collector admission suppresses invocation. | HC01, HC02 | HC08 | HC-DSP-02 |
| HC-AT-16 | Withdrawal during posted/running collection removes health contribution immediately, suppresses unentered user code, and waits for all admitted wrappers/holds to settle. | HC01, HC02 | HC08 | HC-DSP-02; HC-DSP-03 |
| HC-AT-17 | A late result cannot recreate a disposed monitor or update a new monitor with its old name. | HC01 | HC07 | HC-DSP-04 |
| HC-AT-18 | A weakly lost idle monitor disappears without an inferred missing-critical-service failure. | HC01 | HC07 | HC-DSP-05 |
| HC-AT-19 | Registry, callbacks, diagnostics, and bindings do not root a transient owner or unrelated scope. | HC01, HC02, HC06 | HC08, HC10 | HC-OWN-03; HC-HB-02 |
| HC-AT-20 | Collector dependencies come from their originating owner; disposal waits for its lifetime hold. | HC01, HC02 | HC08 | HC-OWN-04 |
| HC-AT-21 | Canceling a collection/disposal wait does not release actual capacity or dispose dependencies. | HC01, HC02 | HC08 | HC-DSP-03; HC-COL-04 |
| HC-AT-22 | Repeated concurrent collection requests join one running collection and apply one result. | HC01, HC02 | HC08 | HC-COL-03 |
| HC-AT-23 | At capacity, due collectors remain bounded and receive fair admission without one waiting Task per registration. | HC01, HC02 | HC08 | HC-COL-03 |
| HC-AT-24 | A newer direct observation prevents an older asynchronous result from overwriting it. | HC01, HC02 | HC08 | HC-COL-05 |
| HC-AT-25 | A failed optional metric collection preserves the previous value/time and does not independently change health. | HC01, HC02 | HC08 | HC-POL-06; HC-COL-06 |
| HC-AT-26 | Required evaluation failure, invalid collected data, and recovery follow each policy's explicit mapping; obsolete errors cannot overwrite a newer direct report. | HC01, HC02 | HC08 | HC-POL-06; HC-COL-05; HC-COL-06 |
| HC-AT-27 | Capturing/serializing a snapshot invokes no dependency check and changes no counters or flags. | HC01, HC03, HC04, HC05 | HC07, HC09 | HC-COL-01; HC-SNP-06 |
| HC-AT-28 | Concurrent observation commits cannot produce disagreement among policy state, counters, aggregate, HTTP status, and that response body. | HC01, HC03, HC04 | HC07, HC09 | HC-STA-03; HC-SNP-01 |
| HC-AT-29 | Capture time advances without rewriting historical observation times. | HC01 | HC07 | HC-SNP-02 |
| HC-AT-30 | Lifetime counter saturation cannot wrap; wire fixtures preserve large values, and newer observation revisions remain distinct after counter saturation. | HC01, HC03 | HC07, HC09 | HC-SNP-04 |
| HC-AT-31 | Mutating caller data after reporting cannot change a snapshot; retained diagnostics contain no live exception graph. | HC00, HC01, HC03 | HC07, HC09 | HC-SNP-05 |
| HC-AT-32 | Invalid/unsupported Heartbeat policy-input combinations fail binding before activation; absent policy has the documented behavior. | HC06 | HC10 | HC-HB-01; HC-HB-02 |
| HC-AT-33 | Duplicate Heartbeat registration/outcome notifications do not create a second monitor or advance counters twice; retained contradictions fail and obsolete facts cannot change state. | HC06 | HC10 | HC-HB-02; HC-HB-04 |
| HC-AT-34 | Heartbeat false result reports failure without requesting OnError; handler success does not erase an exception outcome. | HC06 | HC10 | HC-HB-03 |
| HC-AT-35 | Settlement updates its retained execution only; late settlement cannot move current execution backward or count another outcome. | HC06 | HC10 | HC-HB-03; HC-HB-04; HC-HB-05 |
| HC-AT-36 | Reporting faults remain integration diagnostics and do not create recursive callback failures or prematurely release owner protection. | HC06 | HC10 | HC-HB-05 |
| HC-AT-37 | Container construction/resolution performs no priming or periodic collection. | HC01, HC02, HC11 | HC08, HC10 | HC-APP-01; HC-COL-02 |
| HC-AT-38 | Successful country priming precedes recurring registration; execution waits for the application activation boundary. | HC01, HC02, HC06, HC11 | HC10 | HC-APP-04 |
| HC-AT-39 | Priming failure stops startup, creates no recurring refresh registration, and remains observable through the coordinator's live monitor. | HC01, HC11 | HC10 | HC-APP-05 |
| HC-AT-40 | A disposed child monitor disappears even while the live coordinator independently records startup failure. | HC01, HC11 | HC10 | HC-DSP-01; HC-APP-05 |
| HC-AT-41 | Required registration failure or later component startup failure prevents overall readiness despite successful country loading. | HC01, HC11 | HC10 | HC-APP-05 |
| HC-AT-42 | A conforming component keeps its StartAsync task incomplete until required priming finishes; while it is incomplete, the coordinator does not advance or publish startup completion. | HC01, HC11 | HC10 | HC-APP-02 |
| HC-AT-43 | The future coordinator invokes each intended instance at most once, including failures, cancellation, and alias registrations. | HC01, HC11 | HC10 | HC-APP-07 |
| HC-AT-44 | All HTTP phase cases in section 10 produce the defined 200/500 results from one snapshot. | HC03, HC04, HC05 | HC09 | HC-HTTP-02; HC-HTTP-03 |
| HC-AT-45 | /_health descendants never reach business logic; /_healthcare remains unrelated; unknown/unsupported requests stay in the branch. | HC03, HC04, HC05 | HC09 | HC-HTTP-01; HC-HTTP-04 |
| HC-AT-46 | GET/HEAD agree on status, disabled details returns 404, and report caching cannot create apparent fresh health. | HC03, HC04, HC05 | HC09 | HC-HTTP-04; HC-HTTP-05 |
| HC-AT-47 | Oversize details fail before HTTP headers or pipe success framing; GET/HEAD decide the same error status, without omitting critical state or mutating health. | HC03, HC04, HC05 | HC09 | HC-HTTP-05; HC-PIPE-03 |
| HC-AT-48 | HTTP and pipe select equivalent results for the same snapshot/view. | HC03, HC04, HC05 | HC09 | HC-PIPE-01 |
| HC-AT-49 | Pipe client handles split reads and exits zero only for a complete valid selected view; unhealthy, malformed, contradictory, missing, and timed-out responses fail the probe. | HC03, HC04, HC05 | HC09 | HC-PIPE-03; HC-PIPE-04 |
| HC-AT-50 | Slow/many pipe clients respect capacity and time bounds without blocking health commits or accumulating report history. | HC03, HC04, HC05 | HC09 | HC-PIPE-05 |
| HC-AT-51 | Linux integration uses the supported matched pipe client and real endpoint/permission context; no native Kubernetes pipe field is assumed. | HC03, HC04, HC05 | HC09 | HC-PIPE-01; HC-PIPE-02 |
| HC-AT-52 | Shutdown makes readiness false, rejects new registrations/activation, accepts final producer reports, and drains owner work and transport handlers before disposing their enclosing scopes/root. | HC01, HC02, HC04, HC05, HC06 | HC08, HC09, HC10 | HC-LIFE-01 |
| HC-AT-53 | Repeated/canceled shutdown waits neither reopen the service nor certify nonexistent settlement. | HC01, HC02, HC04, HC05 | HC08, HC09, HC10 | HC-LIFE-02 |
| HC-AT-54 | Watchdog cancellation or wrapper timeout alone produces no Healthcheck state mutation and no early Heartbeat overlap permission. | HC01, HC02, HC06 | HC10 | HC-WDG-01; HC-WDG-02 |
| HC-AT-55 | Environment precedence, blank/invalid inputs, bounds, immutable publication, and failed initialization follow the core rules. | HC00, HC01 | HC07 | HC-CFG-01; HC-CFG-02 |
| HC-AT-56 | Monitor capacity rejection leaves existing contributions unchanged and creates no partially registered monitor. | HC01 | HC07 | HC-OWN-05; HC-CFG-02 |
| HC-AT-57 | Package/architecture checks prove generic core independence from Heartbeat, ASP.NET, DI-container types, and unapproved runtime packages. | HC00, HC12 | HC12 | HC-SCP-03; HC-ENG-01 |
| HC-AT-58 | All six verification families and historical contract replay provide the required release evidence on claimed platforms. | HC00, HC12 | HC12 | HC-ENG-02; HC-ENG-03 |
| HC-AT-59 | Binding-created monitors retire on unregister, automatic retirement, owner closure, and shutdown; external monitors remain caller-owned. | HC06 | HC10 | HC-HB-02 |

## External and release evidence is not waived

| Cases | Boundary and disposition |
| --- | --- |
| HC-AT-32–36, HC-AT-59 | Healthcheck fact binding and real monitor behavior can be tested now. Actual scheduler activation, duplicate registration, OnError, final handoff, and owner retirement need the real Heartbeat integration. |
| HC-AT-38 | Use the real Heartbeat activation boundary for full acceptance. A controlled fact producer is not scheduler proof. |
| HC-AT-39–42 | An explicit example host can qualify the Healthcheck side of priming/failure/readiness. The future generic startup coordinator owns its general ordering/instance guarantees. |
| HC-AT-43 | Explicit external dependency: real future IStartable coordinator, including failure/cancellation/aliases and at-most-once invocation. Do not build it inside Healthcheck or silently drop this case. |
| HC-AT-52 | Local drain/reporting can be qualified now; real Heartbeat final handoff and enclosing-owner shutdown require actual integration. |
| HC-AT-54 | Prove Healthcheck has no autonomous watchdog/timeout-to-health authority. Actual nonoverlap after an external watchdog timeout requires that real execution-control/scheduler integration. |
| HC-AT-57 | Generic monitoring must remain independent of Heartbeat/ASP.NET/DI; preserve currently approved Core runtime dependencies rather than applying superseded zero-PackageReference checks. |
| HC-AT-58 | All six families remain required. Historical replay is tied to actual publication history; empty history is not an executed compatibility pass. Supported platforms and exact package candidates need real qualification. |

A case can carry sub-evidence for completed local portions while its overall acceptance remains Pending. No full module/release status may imply these outstanding portions passed.
