---
title: "WORK-008 Settle exact newly owned Windows TLS key containers after native drain"
type: agent-task
status: Draft
task_status: Complete
task_id: WORK-008
parent_id: none
category: implementation
complexity: medium
task_type: core.legacy
task_type_version: 1.0
work_owner_task_id: null
instance_key: "outcome:77a3efa2fdd14216a67cfd3b5e1dfae3"
depends_on: ["WORK-002"]
assignee: "Codex"
authority: derived-outcome-authorization
outcome_authorization_id: "cislib:CIS-0016:outcome:3aafc848f9384e88aef0a33e0fabb780"
authorization_scope_ids: ["rest-binding"]
authorization_scope_basis: "sha256:2d4ffaeb03b359c801ca081933ce1a8995a032e0480238dc59df21dc49dd0082"
---

# WORK-008: Settle exact newly owned Windows TLS key containers after native drain

[Shared outcome, constraints and limits](../proposal.md); organized by Codex.

## Objective

Settle exact newly owned Windows TLS key containers after native drain

## Acceptance criteria

- [x] Under approved W01, after actual handler/server/body drain and successful owned certificate disposal, settle only the exact newly owned recorded software user-key container if native delete-on-close left its backing. Verify provider/container/unique identity before native deletion and exact absence before Removed custody and slot release. Preserve unknown backend, failed disposal, mismatched identity and cleanup faults as unresolved closed scope; never enumerate stores or key directories, access existing credentials, provision trust, force GC or suppress cleanup failure. Preserve completed WORK-002 history.

## Targeted validation

- [x] Keep the actual expired RSA TLS negative unchanged and reproduce its successful cleanup through the native certificate suite. Add a retained-key native regression for exact owned deletion and rerun existing custody failure/reentry rows and successful TLS probe against the corrected helper. Include focused build, formatting, independent source review, custody journal and restoration/history documentation. No new backend pool or additional resource ceiling.

## Completion evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Native regression | Passed:1expiredRSAretained-contextcase | `.artifacts/security/CIS-0016/WORK-008/01-native-cleanup-proof/results/retained-expired-rsa-key.trx` |
| Existing custody/TLS behavior | Passed:3/3nativecustody andmTLSprobe,0failed/skipped | `.artifacts/security/CIS-0016/WORK-008/01-native-cleanup-proof/results/custody-and-tls-probe.trx` |
| Actual certificate suite | Unchanged expired TLS negative and37other cases pass; exact key paths absent | `.artifacts/security/CIS-0016/providers/08-current-inbound-proof/results/certificate-native.trx`; `providers/current-key-custody-audit.json` |
| Build/style/review | Release0warnings/errors; scoped format+verify pass; independent4minute source review found no actionable defect | `01-native-cleanup-proof` receipts; `docs/modules/rest-client/http.md` |
| CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1777; failed=253; possibleTokenSavings=591; ledgerDigest=sha256:df70de7b7a8768abb0c05e3756b02d5741a04ba52ca02dfbf050cc59d98a294e |
