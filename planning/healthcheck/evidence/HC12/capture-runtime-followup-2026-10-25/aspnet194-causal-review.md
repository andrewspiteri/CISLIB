# ASP194 retained host and child timing review

The current ASP194 campaign remains unqualified. The source-bound report contains 72 Killed, 3 Survived, 5 Timeout and 19 Ignored definitions; the original native audit records 51 direct accepted IDs, 81 hosts, 55 normal hosts and 26 aborted hosts. These are distinct counts. Neither the native 96.25% result nor a normal child exit repairs an aborted parent, an incomplete child journal or the skipped fresh baseline.

This is a diagnostic review of immutable local bodies. No subject, classifier, auditor or campaign was rerun, no original flag changed and no kill credit was applied.

## Retained inputs and executed metadata

The complete original run and payload were received with no exclusions: 42,694 regular files / 863,992,968 bytes. Root's Windows receiving receipt is `../aspnet194-closed-retention01/windows-receipt01.json`, SHA-256 `70f10fee7569af37e9c6d50dfc4884e7cdcd7d0e24fba006f3f7ed82f1dc4df8`. Its manifest SHA is `947630dbc7797be6f7b4696dc596533f94d9d45700761e6c437e3f39a91dd91f`; the retained archive SHA is `1cf7a36c4387d9022d58d3f69e5b710b83355ef5372ab204aed62c1cc31e0c8c`. Earlier selected-body reads were individually checked against that manifest; the full receiving closure was confirmed before this final note.

`Summary.cs` SHA `2e9c86d273969ce1b74278eb3181f4782c84aad98b4d13cd1df0a9211580920d` was built once with SDK 10.0.401, strict warnings and isolated outputs (0 warnings/errors). Metadata entry01 completed once with exit 0 on .NET 10.0.12. Arguments, logs and exits are retained beside this note. `actual01/facts.json` SHA is `0b9d30f46538b31d9b91f92eef8b73f766c1ac58acb3a91e161fac31ce78724b`.

The metadata operation checked all 146 current shipping source bodies, then called only the existing maintained `ParseReport` method (checker SHA `621dc8460e079d056cf148df1b32a6f069ad3bd640dd12f7387cdf6de3b23c19`) with all 46 catalog source rows. It retained all 99 exact source/span/replacement/status tuples and all 26 original abort attributions. For three explicitly selected hosts it retained every returned typed row, completion, timing/start line, bound child intent, available launch/parent-exit and capture-phase body. The recorded audit's direct ID array was copied truthfully; no new direct classification was performed. Existing `Proof.Pair` checked this tool's full four-document PE/PDB/source binding. Selected inputs were rehashed before and after.

The independent source review is `../aspnet194-causal-summary-capture-source-peer01.md`, SHA `629bb9ed57c31bb8723734ca8ea48822b4c734ab08a0c17ef1cb756ef83b06da`. It is a source review, not an independent replay of the metadata result.

## Whole-host deadline and returned work

The actual executable budget is 55,388 ms for the whole 61-case selection. The stale word `82` in an error message is not its executable count predicate. `disable-bail=true` permits the suite to continue after returned assertion failures. That policy was preserved.

All times below are the retained Linux log timestamps on 26 October 2026. Each row is a different actual source mutation; they are observations, not matched performance controls.

| Parent / selected tuple | Actual returned result | Sum of returned case durations | Actual child intents / available completed lifetimes |
| --- | --- | --- | --- |
| 2776801 / 230, before Unit/Pipes | 61 Passed; completion 00:47:55.701; no abort | 45,762.3583 ms | 25 / 25, range 1,088.7612–4,260.5145 ms |
| 2777589 / 231, before Unit/Pipes | 54 returned: 51 Passed, 3 Failed; abort received 00:48:29.512 | 49,596.8540 ms | 19 / 18, range 1,375.3139–5,287.9550 ms |
| 2797218 / 281, after other campaigns started | 51 returned: 49 Passed, 2 Failed; abort received 01:01:23.729 | 50,072.7061 ms | 16 / 15, range 1,972.1768–5,914.5875 ms |

All three completions had `IsCanceled=false` and a null typed completion Error. The two aborted hosts retain `IsAborted=true`. Child lifetime means the actual recorded StartedUtc-to-ExitedUtc interval; this summary does not independently establish capture-chain validity or use those lifetimes to qualify the parent.

The first abort occurred before Unit's approximately 00:49 and Pipes' approximately 00:52 starts. Thus those additional campaigns cannot be the necessary cause of every ASP abort. The reference full suite already took about 51 seconds wall time from request start to completion. This evidence does not exclude later contention or prove a precise throughput benefit from a prospective fixture change.

For 2777589 the three returned failures were actual MSTest assertions: two expected 404 responses observed 500/200, and one expected zero business calls observed one. Its slow returned rows were successful Kestrel tests, including 5,481.9793 ms and 4,376.075 ms unhealthy GET rows. The last returned reporting-failure row ended at 00:48:28.626; the next row started at 28.636, before the abort at 29.512. There is no returned runtime exception or 15-second child timeout in those 54 rows.

For 2797218 the two returned failures were the explicit HTTP error-envelope assertions. The last returned row ended at 01:01:23.650; DetailsAuthorization started at 23.669, and abort arrived at 23.729. Its successful Kestrel rows reached 6,045.745 ms. These observations support exhaustion of a whole-selection deadline after substantial finite work; they do not prove all 26 aborts have one cause.

The tuples are bound to `Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs`, SHA `8c89ad2f2992b4d0c63c83d372ebb198059ca27d83813fc5a883bec1c9f3870e`:

- 230: UTF-16 [2855,2860), Boolean `false` → `true`, the NotFound `ConfigureAwait` argument; native Survived.
- 231: [2879,2886), Statement `return;` → `;`, the NotFound branch return; native Killed.
- 281: [5627,5632), Boolean `false` → `true`, `FormatError("ReportingUnavailable", pipe: false)`; native Killed. This changes the envelope flag, not ConfigureAwait.

These report-local IDs and statuses do not transfer to another source epoch.

## Missing launch 9f74 is interrupted preparation evidence

The durable intent `9f74f5608a814e109278c45e6a1336cf` was created at `2026-10-26T01:01:23.8579199+00:00`, after parent 2797218 received abort at 23.729 and before its typed completion at 23.904. It binds selector 281, owner `c092aa4b496446a9894284cd97e12de6`, actual Core SHA `d78bf13eb7905cfed5ad568b401bd3ef934601c1e0bf374f326c051c7aaf908f` and the actual HTTP fixture SHA `834c07165f267425536216880b2980fe2db0b482134ec368dc13add996567b5c`. The intent body SHA is `23d8ea2350a7cb4aa0ffc272739c69e9d2172fbebc9e1d80251533f1915ffda3`.

There is no recorded stage-ready phase, launch.json or parent-exit for that ID in the complete regular-file manifest. The same parent's same stage had already served a normal child at 01:00:40.642. `HealthcheckMutationChildSupport.Prepare` writes the bound intent before stage verification/copy, the first phase and launch.json; the Host constructor starts the process only after Prepare returns. The facts therefore locate this gap in preparation interrupted by parent abort, without identifying the exact unrecorded substep. They do not show a launched child hanging. Empty-directory metadata and a complete thread-level child-exec absence proof are outside this summary.

The original child audit remains exit 1 / InvalidChildEvidence. The controller remains `completeExecution=false`, even though nativeExit was 0 and shutdown capture passed; its ObserveActualChildren failure on this missing launch prevented the fresh baseline. That failure is preserved, not backfilled.

## Smallest prospective fixture change

Start with `HealthcheckHttpTransportTests.SelectedHealthOrReadinessMapsTo500` in frozen common194 source lines 868–882, test-source SHA `b800977a11889fc592188339cb23bd5ef148b603a1cea72c036708d148af4dce`. Its three `unhealthy` GET DataRows each start and settle a fresh real Kestrel child. All three passed on each inspected host, consuming 11.532 seconds in total on 2777589 versus 5.238 seconds on the earlier full-suite host.

Replace only those three same-mode rows prospectively with one real owned-host test that issues the same three GET requests sequentially and preserves every per-path status, parsed snapshot, cache/header and zero-business assertion. Keep `not-ready` and `stopping` independently owned. Existing `RealKestrelServesKnownViewsWithCachePreventionAndNoBusinessEffects` at line 844 already demonstrates multiple real paths under one owned host, including per-request observation. Keep actual registration/known-route preconditions, all 15-second operation bounds, original error propagation, and genuine child exit/stdout/stderr/capture cleanup. Do not share a process across mutant requests or use a static host cache.

This removes two repeated cold starts from the full suite; the retained durations do not establish the eventual time saving or prove this alone repairs all aborts. Any implementation requires a new source epoch, actual discovery/family identities and bounded original/inactive-instrumented calibration before the next campaign. No fixture or canonical file has been changed here, no timeout/cancellation/unfinished work is promoted into an assertion, and this review grants zero new score credit.
