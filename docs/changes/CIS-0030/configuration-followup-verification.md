# Configuration reliability and browser follow-up verification

The three requested outcomes are the Windows native crash correction, the
remaining Configuration browser boundaries and supported CIS reconciliation.
This is a bounded Configuration increment, not completion of the future
Scheduler work or a release/remote CI qualification.

## Accepted checks

All phase paths below are under
`.artifacts/management-portal/CIS-0030/`. Old failed reports remain unchanged.

| Check | Actual result | Retained evidence |
| --- | --- | --- |
| Official Node24.15 credential-free HTTP isolation |2 passed,0 skipped; did not reproduce the native crash | WORK-192/rm09c-node-connect-probe-current/results/node-connect.trx |
| Fresh24.15 portal failure | Session/storage fixture interrupted during the existing metadata prelude; actual host exit3221226505 | WORK-193/rm09c-configuration-browser-boundaries; native-host-87aab283-904e-42eb-bcd8-29836cec3692/phase.json |
| Approved private24.16 comparison |2 passed,0 skipped with the unchanged compiled inputs | WORK-192/rm09c-node-connect-probe-comparison/results/node-connect-comparison.trx |
| Four current browser boundaries |4 passed,0 failed,0 skipped in2m41s; independent persisted-state and owned-cleanup assertions passed | WORK-193/rm09c-browser-boundaries-node-comparison/results/configuration-browser-comparison.trx |
| Existing abnormal shutdown regressions on24.16 |2 passed,0 failed,0 skipped; incomplete TLS peer and canceled authenticated Configuration read | WORK-192/rm09c-node-correction-host-shutdown/results/host-shutdown-node-comparison.trx |
| Existing portable build/browser type check | Passed using the selected absolute runtime and checked-in script bodies | WORK-192/rm09c-node-correction-source-checks-absolute/native.log |
| Existing portable SDK tests and focused portal tests |395/395 and131/131 respectively;0failed/canceled/skipped | Same source-check log, separate test summaries |
| Existing strict C# report guard and scoped format verification | Accepted complete totals2/2/4/2;0skips/failures; format verification passed | WORK-192/rm09c-followup-reports-format-verify/native.log |

The four new browser methods cover genuine zero-enrollment creation/Save/Deploy,
Shared-reference-only differences with identical displayed JSON, current
New/Save/Deploy receipt-storage refusal plus real B-context409/unauthenticated401
retirement, and mixed truthful status/paging/history boundaries. The status
unavailable/401 delivery controls are controlled browser boundaries beginning
from actual native replies, not real infrastructure outage/expiry claims.
Read-only paths retain exact native fingerprints; mutations require exact
payload bytes, immutable pairs, distinct original keys and payload-free Audit.

## Candidate, review and resource custody

The ten executed source hashes and approved private runtime hash are retained in
`.cis/local/testing/rm09-completion/configuration-followup-comparison-candidate.json`.
The strict verification checked all ten and the exact nine applied pin-file
hashes, and preserved the protected CountryData launch settings hash. Independent
review confirmed4/4actual test names, matching source/runtime hashes, the four
browser proof files and native SQL/Operations/Audit/fingerprint checks, with no
required finding. Status assertions validate freshness on actual browser replies;
late delivery tolerates only that request's confirmed `net::ERR_ABORTED`.

The small process diagnostic reuses MSTest/ProcessRunner. Browser extensions
reuse the existing host/Playwright/provider owners. No general test harness,
production fake mode or product responsibility was added. The runtime correction
is the explicitly approved exception documented in
[the diagnosis](windows-node-fastfail-diagnosis.md), using the existing physical
pool and a transfer within the existing three-hour diagnosis allocation.
Node24.16 is a finite locally qualified upstream-led mitigation, not a local
native stack/subcode proof. Global Node, npm11.12.1, .NET, Next, dependencies,
security/TLS settings and historical costs remain intact.

Failures are preserved: the first four-case batch2/4failed, a follow-up status
selector failed after its mixed-state/paging checks, and npm's Windows shell did
not resolve the private runtime prefix. The corrected source-check invocation
uses the unchanged script bodies with the absolute executable. These outcomes
are neither rewritten nor counted as passing retries.

This repository has not adopted `.cis/engineering-defaults.json`; the inspected
CIS completion-context template requires that opt-in, while normal completion
review explicitly skips those non-adopted receipt gates. No adoption, coverage
threshold, mutation or provider-execution campaign was introduced for closeout.
Canonical contract/dependency/design/checklist/evidence gates still apply.

## Reconciliation

[The obligation reconciliation](configuration-obligation-reconciliation.md)
records the explicit R09 and Instance status replacements, compatible WORK-168
semantics, preserved failed wizard evidence and proposed supported lifecycle
operations. Final canonical states and actual expired-review pointer cleanup
are recorded there. WORK-194's supported Complete transition passed; this
document itself changes no status.
The checked reconciliation has now completed compatible WORK-168/190, preserved
WORK-180/191Complete, cancelled superseded WORK-181/167/150 with explicit
replacement provenance, and retained WORK-165/170Draft while replacing only the
obsolete167dependency with190. Both expired-review pointers are closed with
original ready snapshots/hashes preserved and all four source receipts unchanged.
The final plan validation and independent actual-state review passed.
WORK-194's final completion is recorded through the supported transition;
future Scheduler implementation and overall RM09/RM10 closure remain separate.
