# Combined transport and application candidate

The user requested the HC03/HC06 commit followed by the remaining work in parallel. Commit `f73ec9224c9263c3ca2e3bf929fcbac0e6d655fc` establishes that baseline. Four contributors then authored HTTP reporting (coordinator), pipe/probe implementation, independent transport tests, and documentation/CountryList source in disjoint files. The coordinator alone changed projects, solution, dependency policy and lockfiles, integrated the application process stories, and ran native verification. The current checkout was reused with one serialized build stream.

The candidate implements the reserved ASP.NET branch and the protected local pipe server using the existing Core codec and immutable snapshots. The nonpackable probe executable shares Core's strict response reader. CountryList demonstrates reporting before required startup, actual in-progress priming, explicit usable-data publication, failure retention, recurring collection and actual owner-lease drain. It consumes source projects and is not an exact-package release example.

Independent review found and resolved two runtime defects before final verification: ordinary pipe accept/disconnect errors could stop the entire server, and an unrelated snapshot-source cancellation aborted a live HTTP request. Native regressions now exercise per-slot recovery and bounded HTTP reporting errors. Windows response teardown waits asynchronously for peer closure within the existing request deadline so unread response bytes are not discarded. The endpoint factory supplies a protected owner ACL and the Windows remote-client rejection flag; remote-host and alternate-identity rejection remain unexecuted claims.

Focused Release verification passed 137 real transport cases and four real CountryList process stories on Windows x64, .NET 10.0.12, with no failures or skips. The first solution build also passed with zero warnings or errors. The complete final Developer-02 checkpoint passed all 16 stages and 7,839 native cases: 4,494 unit, 2,258 integration, 1,058 architecture and 29 business, with zero failures or skips. All 735 Healthcheck case IDs (584 retained and 151 new) passed in the actual full native reports. All 953 captured C# inputs match the final candidate. See [receipt.json](receipt.json) and [native ID reconciliation](native-case-match.json).

| Task | Current scope |
| --- | --- |
| HC04 / HC05 | HTTP and pipe/probe implementation; focused native transport verification passed. |
| HC07 / HC08 | Existing policy/state and collection tests are retained; combined re-execution passed in the current checkpoint. |
| HC09 | 137 native transport cases passed: HTTP 45, pipe 37, probe 55. |
| HC10 | Existing execution-fact binding cases retained; four new application lifecycle stories passed. Real external Heartbeat/startup integration remains pending. |
| HC11 | Five guides and a runnable source-reference example; four self-test modes verified through native process stories. |
| HC12 | Architecture gate and ten new fixture controls verified; complete release qualification remains pending. |
| HC13 | Combined local Windows candidate integrated and verified; acceptance boundaries and pilot limits reconciled. |

See the [qualification boundaries](../HC12/assessment.md) and [acceptance gap audit](../HC11/implementation-notes.md). Existing source cases and contract baselines are preserved; no blanket acceptance of all HC-AT obligations follows from these runs. Linux execution, differing identities/remote-host access, required coverage/mutation/compatibility lanes, exact-package examples, genuine publication history, and absent external integrations require their own evidence. No push, publication, deployment, or external review was performed.

The pilot demonstrates concurrent authoring with serialized integration and checks. It has no matched serial baseline, so no causal speedup is claimed. Agent token/cost telemetry is unavailable. Retained dispatches, build logs and native timestamps establish the observed boundaries without inventing active-work measurements.

The first complete attempt stopped before native execution on an example-only framework-provided role check. The coordinator repaired the architecture collector/policy for the explicitly declared HTTP process fixture, preserving exact package/framework/SDK/origin checks; independent positive and negative controls passed. Failed attempts are retained in [attempts.json](attempts.json). Current source acceptance boundaries are reconciled in [acceptance-status.md](acceptance-status.md).
