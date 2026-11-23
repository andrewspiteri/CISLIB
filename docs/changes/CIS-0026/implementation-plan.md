# RM-07 review corrections

The pasted review of `fe029a85741705dc51c1093403248fc4aa2920ca` requests two
P2 corrections. Current execution uses the earlier RM-07 no-permission-pause
delegation and the user's supplied request. Codex selected the finite allocation
below; its exact numbers were not separately human-reviewed. CIS-0024 and
CIS-0025 remain complete. The existing feature branch remains unmerged.

| Task | Change, tests and documentation | Bound |
| --- | --- | --- |
| WORK-001 | Close/invalidate managed admission on canceled public startup, including initial observation/inventory reporting; join accepted work and preserve unexpected notification faults. Existing lifecycle tests and registration guide. | 2.5 hours, 3 attempts, including 300 restoration seconds |
| WORK-002 | Classify expected native-fetch response-body and cleanup failures at the transport boundary, preserving cancellation/deadline and original mutation recovery identity. Native HTTPS and bounded fault/cleanup regressions; client documentation. | 3 hours, 4 attempts, including 300 restoration seconds |
| WORK-003 | Accurately categorize the native Node regression, preserve the .NET-only fast recipe, and select it in the existing provisioned CI job; matching prerequisite docs. | 0.5 hours, 1 attempt, no task restoration reserve |

The two defect tasks and supporting prerequisite task include intake,
implementation, documentation, tests and review. The
six active hours, eight logical attempts and 600 restoration seconds are shared
by root and all agents. Independent source work may run in parallel; root
serializes CIS mutations, builds and native checks. Runtime Startup stays
sequential. No completed owner is reopened.

Review found that the new native test needs truthful dependency routing. A new
supporting scope preserves the existing started scopes and contracts; thirty
ordinary minutes and one unused attempt move from WORK-001 to WORK-003. Shared
totals and both original protected task reserves stay unchanged. This updates
the existing CI selection and documentation without adding a job or test harness.

The existing physical six-GiB ceiling and 64-MiB reserve are unchanged. Retained
Docker assets keep their existing two-GiB counted allowance; no container/image
work is needed. The retained 144-MiB native cache and conservative eight-MiB TLS
backing allowance remain counted. Native-fetch regression uses the existing
private-directory/PEM fixture and a joined Node HTTPS child, with no trust-store
or machine-key changes. No new dependencies, pins, test harness, database access,
backend work or broad qualification campaign is included.

Acceptance requires cooperative and held/uncooperative startup cancellation at
both initial reporting boundaries, closed admission and no recurring subscription
after canceled startup. Native fetch must return structured GET disconnect,
timeout and caller cancellation results and PUT uncertainty with the exact
original key after response headers. Actual cleanup must settle; unexpected
programming faults must still escape. Reuse the existing affected regression
suites and pinned formatting/build/browser commands. Record actual failures and
evidence limits without replaying unrelated backend qualification.

Preserve the unrelated CountryData launch settings file (SHA256
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`).
This correction does not include commit, push, merge, release or deployment.
