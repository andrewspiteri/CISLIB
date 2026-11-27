# Configuration automated testing

The supported portal flow is Save version, explicit Deploy and rollback by
deploying an older saved version. The coverage inventory is
[the26-row matrix](configuration-coverage-matrix.md). It combines valid source,
assignment, permission, session and recovery states with explicit refusal tests;
it does not promise every possible JSON value or timing interleaving.

The focused command from the portal directory, with the pinned Node on PATH, is:

```text
node --test --test-concurrency=1 tests/configuration-*.test.mjs tests/command-receipts.test.mjs tests/session-context.test.mjs tests/portal-session.test.mjs tests/portal-response.test.mjs tests/next-response-drain.test.mjs
```

Use the repository's current Node/npm pins and setup in the
[TypeScript guide](../../../implementations/typescript/README.md). The command selects the existing ordinary
Node tests for Configuration, command receipts, session context/ownership and
canceled-response handoff. The real HTTP close regression requires the ordinary
portal build's [pinned Next correction](next-response-close-correction.md).
There is no new testing framework or package.

The existing `CIS.Core.IntegrationTests` project contains these named real-host
Configuration journeys, using the existing Playwright/OIDC/PostgreSQL/native
fixture. Build the project in Release before `--no-build` execution and use the
current repository pins with the existing Edge and Docker prerequisites.

| Named test suffix | Main contract |
| --- | --- |
| `ActualVersionPageSavesWithoutDeployingComparesDeployedAndRollsBackExactPairs` | New and existing saved versions; exact JSON, formatting/diff, current deployed baseline, Save without assignment, explicit Deploy and rollback, browser window resizing and dirty navigation. |
| `ActualFirstVersionSavesAnExistingApplicationBindingWithAnAvailableCommonStream` | Existing empty typed binding with an available Shared stream; first version admits its exact draft and remains undeployed. |
| `ActualNewConfigurationSaveRecoversEachOriginalWithoutRepeatingSetupPublicationOrRelease` | Invalid/duplicate/oversized local JSON; real setup, publication and release commits with lost response delivery; explicit recovery of each original without automatic replay or deployment. |
| `ActualVersionSaveReconcilesAClosedReleaseConflictWithoutRepublishingItsFrozenJson` | Actual competing map decision, finalized release conflict, deliberate fresh reference-only continuation, retained Shared/App publication pair and no repeated publication. |
| `ActualVersionDeployClearsPinsConflictsAndRecoversCommittedDeliveryLoss` | Existing canary/Finance pins, actual concurrent map conflict, exact Deploy, committed response loss, reload/same-key recovery and older-pair rollback; retired view discards a held map response. |
| `ActualMetadataOnlyVersionDeploysWhileReadonlyOwnSavedVersionCannot` | Genuine saved version without ContentRead can Deploy; a separately authorized readonly application's real saved version remains protected and cannot Deploy; cross-application access stays denied. |
| `ActualInstanceStatusRemainsReadOnlyForHistoricalReportsAndAnAdoptionOnlyOperator` | Instance/version/status projection, historical reports, restricted metadata, collapsed diagnostics, exact registered rows and no Configuration effects. |
| `ActualPortalCreatesSavesAndDeploysConfigurationBeforeAnyInstanceAndRefusesUnretainedOriginals` | Catalog-provisioned application with zero registrations; New, Save and Deploy use four exact native originals without enrollment; quota/readback failure prevents writes. |
| `ActualVersionComparisonShowsChangedSharedReferencesWhenEveryDisplayedJsonByteIsIdentical` | Two real Shared publication references with identical Shared bytes and one unchanged App publication; the deployed-only diff shows the Shared-reference notice, unchanged displayed JSON and no effects. |
| `ActualConfigurationCallersRefuseStorageFailuresAndRetireDraftsAcrossRealSessionChanges` | Current Save/Deploy reject synthetic storage throw/readback failure; real A-to-B cookie/session change retires a held A preview and draft; clearing browser credentials causes an actual unauthenticated refresh. |
| `ActualInstanceStatusPreservesMixedObservationsAcrossPagingAndBrowserReadBoundaries` | Actual fresh applied/pending/restart/rejected/faulted reports, naturally stale and closed registrations, two pages and unregistered targets; desired/history distinctions, retired late read and controlled unavailable/401 delivery with unchanged native fingerprints. |

Each browser journey must also pass its C# parent SQL, Operations and Audit
assertions and actual process/stream/IPC/database cleanup. Controlled delivery
cases begin with real `route.fetch` calls; no invented successful mutation is used.
Storage failures are finite browser-storage controls, with actual callers and
native no-effect checks. Validation/preview reads may still occur before a
blocked write; these cases do not claim zero HTTP traffic.
Receipts must precede dispatch and retain only bounded original references,
without JSON, notes, ETags or credentials. Failed and earlier partial runs remain
in evidence.

Two host regressions separately qualify an incomplete TLS peer and a canceled
authenticated Configuration read held in session SQL. They preserve actual
handler/native cleanup and do not replace the Configuration effect assertions.

## Current verification

| Retained phase | Actual result |
| --- | --- |
| `rm09c-configuration-final-pure` | 131 passed, zero failed/cancelled/skipped, including the actual HTTP close regression. |
| `rm09c-next-response-drain-final-build` | Production compilation, TypeScript and route generation passed. |
| `rm09c-configuration-fixtures-build` | Release integration-project build passed, zero warnings/errors. |
| `rm09c-configuration-completion-reports` | New Save recovery, metadata-only/readonly Deploy and the complete original version workflow:3 passed, zero failed/skipped. |
| `rm09c-configuration-interrupted-cases` | Current release-conflict continuation and Deploy/pins/conflict/recovery/rollback:2 passed, zero failed/skipped. |
| `rm09c-configuration-host-backend-final` | Three host/first-version cases plus zero-registration creation, exact application authority and catalog/adoption boundaries:6 passed, zero failed/skipped. |
| `WORK-168/rm09c-configuration-status-final` | Current read-only Instance status with independent Configuration fingerprints:1 passed, zero failed/skipped. |
| `rm09c-configuration-reports-format-verify` | Existing C# TRX guard accepted all four successful reports (3+2+6+1); scoped ordinary C# whitespace verification passed. |
| `WORK-193/rm09c-browser-boundaries-node-comparison` | Four new browser/native parent cases above passed under the verified private Node 24.16.0; total/executed/passed 4, failed/skipped 0. The existing strict report guard accepted this report. |
| `WORK-192/rm09c-node-correction-source-checks-absolute` | Existing portable build/browser type check,395 portable tests and131 focused portal tests passed on the selected runtime;0failed/canceled/skipped. |
| `WORK-192/rm09c-node-correction-host-shutdown` | Existing incomplete-TLS-peer and canceled-authenticated-read tests passed2/2 on the selected runtime, with actual cleanup. |
| `WORK-192/rm09c-followup-reports-format-verify` | Existing strict C# guard accepted complete totals2/2/4/2 from separate actual reports; scoped format, source/pin manifests and protected user launch settings passed. |

The earlier four successful native reports contain 12 distinct selected cases. Each
returned after its actual fixture and assembly cleanup. Scope stays focused;
this is not whole-portal or release qualification. Their exact21-file source
candidate remains retained at
`.cis/local/testing/rm09-completion/configuration-final-candidate.json`.
The handoff manifest `configuration-final-source-handoff.json` additionally
records built-artifact hashes and the final whitespace check: the sole subsequent
source change removed exactly one trailing blank CRLF from `runtime.ts`, with
every other byte verified identical to the executed candidate.

The four-case continuation is a separate later execution:
`.artifacts/management-portal/CIS-0030/WORK-193/rm09c-browser-boundaries-node-comparison/results/configuration-browser-comparison.trx`,
SHA-256 `4a0e2e73c110ed569dead0726d5a143d39e2a1cf55ef12cf95a1455b43c5b954`.
Its passing parent methods include the exact persisted bytes/pairs, original
decisions, Audit or read-only fingerprints appropriate to each case and actual
fixture/assembly cleanup. The old 131 pure checks and 12 native cases remain
their earlier candidate observations; they are not a new combined run on the
changed runtime. Current source/pure checks, the two abnormal-host-close
requalification cases and strict verification passed separately as recorded
above and in [the actual follow-up verification](configuration-followup-verification.md).

N-SESSION changes actual browser credentials between separately authenticated
operators, then separately clears them to obtain a real 401. It does not expire
a provider credential or revoke the server session. N-STATUS substitutes
unavailable/401 delivery after a real current native read to check UI retirement;
those controls do not establish a real infrastructure outage or session expiry.
Its stale/offline observations come from real elapsed receipt time and native
registration closure, not fabricated timestamps or controlled success rows.

The two credential-free Node connection-probe reports separately passed 2/2 on
each runtime. They are bounded transport observations, not additional
Configuration journeys or a proof of general Windows reliability. The
[Windows diagnosis](windows-node-fastfail-diagnosis.md) owns their full
provenance and the runtime correction decision.

### Retained failures and platform limit

Earlier mixed browser batches remain2 passed/3 failed,1 passed/4 failed and
3 passed/2 failed. They include the repaired nested-summary selector, the
causally reproduced Next close wait, and native Windows Node terminations with
exit3221226505 (`0xC0000409`). The complete current successful reports above
do not turn those unsuccessful runs into passes.

The earlier Windows native terminations remain failed observations. The later
Node 24.16.0 comparison and conditional nine-file pin correction follow the
specific user-approved exception; the separate diagnosis record owns the
current cause/correction conclusion. These finite passing cases do not prove
platform reliability, and future native failures remain failures. No assertion
was weakened or grant broadened to manufacture acceptance. The original explicit
permission negatives remain in the new real-host journeys.
