---
title: CIS-0022 verification
type: verification-record
status: Complete
change_id: CIS-0022
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | Exact imported requirements/profile | Source SHA256 c86a8a1362d4ae3043954d7f04dcce4866f01383ce545b65130d2198e1918153; CIS outcome 31de62abefb947ed8b11c51cc37ba560, allocation rev2640 | Recorded | User delegation is authority; source is requirements data. Seven medium tasks share40h48attempts/5400srestoration/6GiB. |
| WORK-001 | Pinned scoped formatting and affected build | contracts-format / contracts-format-verify / contracts-catalog-build | Passed | SDK10.0.401, runtime10.0.12; zero warnings/errors. No new package/SDK/runtime pin. |
| WORK-001 | Contract/catalog/binding/schedule cases | .artifacts/coordination/CIS-0022/WORK-001/contracts-catalog-tests/results/contracts-catalog.trx | Passed | Actual27passed,0failed,0skipped; includes losslessInt64JSON, UTF8/ordinal keys, noncurrent/uncertainty distinction, exacttypedbindings and passive/disabledcatalog. Does not establish SQL locking, runtime renewal or scheduler delivery. |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2168; failed=293; possibleTokenSavings=591; ledgerDigest=sha256:903c84bcc50dc2c3e146282abcb0bce8bff634dc11048a7a3b7030901b690023 |
| WORK-003 | First reviewed focused unit attempt | WORK-003/managed-provider-unit-tests/results/managed-provider-unit.trx | Failed, retained | Actual59 selected:57passed/2failed/0skipped. Strict exception-subtype assertions corrected; no original evidence/cost reset. |
| WORK-003 | Current pinned format/build and Windows unit proof | managed-cancellation-format / managed-cancellation-format-verify / managed-cancellation-unit-build / managed-cancellation-unit-tests/results/managed-unit.trx | Passed | 0warnings/errors; actual60passed/0failed/0skipped, including40 managed/facade/helper cases plus11 contracts and9 PostgreSQL-unit boundaries. |
| WORK-003 | Actual Linux runtime focused unit proof | .artifacts/coordination/CIS-0022/WORK-003/linux-managed-provider-units/results/linux-native.trx | Passed | Same current IL under Ubuntu24.04 WSL/Linux ELF10.0.12 and existing pinned SDK10.0.401 managedVSTest with ReadyToRun disabled; actual60/0/0. Local Ubuntu has no Docker Unix socket; real Linux SQL is not claimed. |
| WORK-002 | Required path/immutable task reconciliation | scope-reconciliation.md; reconciled-authorization.json; native authorization history; WORK-008/009 | Applied | Same original fundedWORK002, now decomposed into coherent grant/guard and durable schema/packaging product children. Seven funded owners retain40h48/5400s and6GiB; metadata rejections/history/costs preserved. |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2184; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:3d8626fbababb5f3b78f91ea8e1a5519b7a5bb2b19ee0ae047da4d9d64cb2ba8 |
| WORK-008/009 (funded WORK-002) | First real-provider fixture attempt | WORK-002/postgres-authority-tests/results/postgres-authority.trx | Failed, retained | All11 failed before authority SQL: fixture Other alias omitted RM02 required isolation subset. Existing typedservices provisioned/cleaned, error and actual cost retained. |
| WORK-008/009 (funded WORK-002) | Current real PostgreSQL authority/guard/schema | WORK-002/postgres-authority-reviewed-tests/results/postgres-authority-reviewed.trx | Passed | Actual11passed/0failed/0skipped, Windowsx64, PostgreSQL18.7 ownedtypedservice with durability on/on/on; SHA2568903b36dfb2e1630908053af404eca3969c3b45db26bedafc2741fcd95acc30d. Real lockgraph/postwaittime/takeover/prune/generation/Unknown/restart boundaries, notLinuxSQL/failover proof. |
| WORK-009 (funded WORK-002) | Explicit installer package content | WORK-002/schema-core-package/packages/CIS.Core.0.1.0-dev.1.nupkg | Passed | Exact source SQL entry SHA2566cc8cba0dcd9f9d33dcfa4a36185e654f5a8eaa80eb2c64ca0d7265e44213d78, package SHA256b052208e12e4ce043ed3f60e1e9976ed24dab4d6cf01f2d987ae9bb3a4a48444. Uses verified existing PG-stage binary; final complete-module consumer is separate WORK007. |
| WORK-008/009 (funded WORK-002) | Actual owned cleanup | .cis/local/coordination-intake/postgres-restoration-proof.json | Passed | Afteractualnativehostexit,0ownedcontainers/0ownedvolumes;12preexistingforeignvolumes retained; pinnedimagecache charged inside existing2GiBallowance. |
| WORK-008 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2189; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:95636f5039a58a589d08ae041a09b7a4f60fb93b03e7b1dbfb643b884f4ee22c |
| WORK-009 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2190; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:1025efb5e815016944eb5e9e396f2889f887871aaa2e439543d9b3e195fd98a0 |
| WORK-004/005 | Current job source formatting/build | jobs-format / jobs-format-verify / jobs-unit-build | Passed | Pinned SDK10.0.401/runtime10.0.12,0warnings/errors. Narrow new MaximumDrain<=1day contract correction matches actuallease; other finite settings unchanged. |
| WORK-004/005 | Windows current job unit proof | .artifacts/coordination/CIS-0022/WORK-004/jobs-unit-tests/results/jobs-unit.trx | Passed | Actual78passed/0failed/0skipped:27 Task004,35 Task005,16 retainedcatalog cases; shared reservations/config races/scoped callbacks/immutable admissions/originalcompletionfacts/publichelpers. |
| WORK-004/005 | Actual Linux current job unit proof | .artifacts/coordination/CIS-0022/WORK-004/linux-jobs-units/results/linux-native.trx | Passed | Same78/0/0 under existing Ubuntu/Linux ELF10.0.12 +SDK10.0.401 managedVSTest. These local controlled tests do not replace native authentication/SQL/broker proof. |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2191; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:bc32726940226f312819be412be622084c513d8cc65b5b42a746ca0ce64337a8 |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2192; failed=297; possibleTokenSavings=591; ledgerDigest=sha256:5f0d560d6634beab27b711a267c3398d84685a149d28e41715ff4f28fd68c39b |

| WORK-010/011 (funded WORK-006) | Current scoped formatting and affected unit/integration/security build | hosting-format; hosting-import/tags-format+verify; hosting-unit/current-unit/reviewed-integration/affected-security-build | Passed | Pinned SDK10.0.401/runtime10.0.12; current builds0warnings/errors. Retained first integration misplaced-import compile failure and later explicit params-tag compile correction; no product behavior changed for these corrections. |
| WORK-010/011 | First affected unit attempt | WORK-006/hosting-affected-unit-tests/results/hosting-unit.trx | Failed, retained | Actual533 selected,532passed/1failed/0skipped; new counter test incorrectly assumed observable Stop disables synchronous instruments. Corrected exact1Lrejection and original result/fault assertions; no runtime change. |
| WORK-010/011 | Current Windows affected unit proof | WORK-006/hosting-current-unit-tests/results/hosting-current-unit.trx | Passed | Actual533passed/0failed/0skipped, including8new nativeGenericHost/lifetime cases, preBuild borrowed-runtime drain,20instrumentation cases and existing Startup/Heartbeat/Metrics/application compatibility. |
| WORK-010/011 | Current actual native integration and HTTP export | WORK-006/hosting-affected-integration-tests/results/hosting-integration.trx | Passed | Actual215passed/0failed/0skipped; realPrometheusHTTP seven-stream identity/units/physical boundaries/cumulative checks,2actualfailedBuild cleanup cases and affected existing application/collector hosting. |
| WORK-010/011 | Current actual Linux focused proof | WORK-006/linux-hosting-and-observations/results/linux-native.trx | Passed | Actual172passed/0failed/0skipped on existing Ubuntu24.04/LinuxELF10.0.12 with SDK10.0.401 managedVSTest. Includes newhost/observations+retainedcoordination/jobs. No LinuxSQL/broker claim. |
| WORK-010 | Existing affected credential-owner regression | WORK-006/affected-security-owner-tests/results/affected-security.trx | Passed | Actual7passed/0failed/0skipped; Entra recheck cancellation, certificate availability and PEM ownership. Existing ownednativefixtures; no trust-store/machine-key installation or external deployment proof. |
| WORK-010 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2211; failed=301; possibleTokenSavings=591; ledgerDigest=sha256:ed8a509d792667714b65e54a46aedb1fe75a2017650bd794670027820e5e57d2 |
| WORK-011 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2212; failed=301; possibleTokenSavings=591; ledgerDigest=sha256:e8054a899e8fe22ff4db0edfb5ae104c1ca9e0f80cbefabb8d4756c182cd8a85 |

| WORK-007 | Compiling CountryData maintenance recipes and public unit examples | country-examples / country-reviewed-examples build, formatting and TRX | Passed after sample correction | Current41passed/0failed/0skipped on Windows, including12new cases. First41-case run had30pass/11fail from one invalid underscore capability spelling; corrected sample to customer.mark.dormant under the existing Security grammar. No Core change. |
| WORK-007 | Actual Linux CountryData unit examples | WORK-007/linux-country-unit-examples/results/linux-native.trx | Passed | Current41passed/0failed/0skipped on Ubuntu24.04/Linux10.0.12. Scripted facts remain separate from SQL/authentication proof. |
| WORK-007 | Additive source, frozen binary and public API compatibility | WORK-007/additive-compatibility-tests/results/additive-compatibility.trx | Passed | Three selected cases passed, zero failed/skipped; existing private8.0.31 tool host and pinned ApiCompat used without new installation. |
| WORK-007 | Guarded Operations/business/outbox atomicity, Unknown recovery, real crash takeover and package helpers | WORK-007/durable-public-consumer-tests/results/durable-public-consumers.trx | Passed | Five selected cases passed, zero failed/skipped, strict service settings. Includes2realSQL commit/Unknown rows, actual outside-friend A-kill/B-Busy/C-expiry-Replayed/publication case, existing public CommonTesting helper case and affected legacy RPC process case. No new packages or harness projects. |
| WORK-007 | Normal database-free CountryData host after opt-in recipes | WORK-007/default-country-host-regression/results/default-country-host.trx | Passed | Current27passed/0failed/0skipped; default hosting/lookup behavior unchanged. |
| WORK-007 | Repository skill and realistic independent review | .github/skills/cislib-coordination/SKILL.md; installed SkillCreator quick_validate.py | Passed | Native validator passed,14repository references resolved. Independent wallet-loss and scheduled Unknown/descriptor scenario review passed; actual recovery/slot descriptions refined. No personal skill/tool installation. |
| WORK-007 | Owned service restoration and preserved user input | .cis/local/coordination-intake/final-restoration-proof.json | Passed | Afteractualnativehostexit,0ownedcontainers/0ownedvolumes;12pre-existingforeignvolumes preserved. Pinned image cache remains charged inside2GiB allowance. User launch settings and exact raw specification hashes unchanged. |

## Acceptance coverage

| Acceptance | Grounded implementation and focused native result |
| --- | --- |
| AC-01 Contention and independent keys | Real PostgreSQL contender and guarded-takeover cases: one current grant; unrelated keys progress. |
| AC-02 Expiry and reacquisition | Real provider generation/recreation cases and the genuine process crash/expiry successor: strictly higher generation. |
| AC-03 Stale operations | Exact old renewal/release refusals and original-context stale-guard rejection; successor and business state preserved. |
| AC-04 Acquisition uncertainty | Current managed recovery/late-reply/missing-observation units plus real committed-acknowledgement-loss observation. No blind mutation replay. |
| AC-05 Renewal/release uncertainty | Managed deadline, late-response and terminal-release cases in current533Windows/172Linux focused selections. |
| AC-06 Guard after lock wait | GrantThatExpiresDuringTheRealRowLockWaitIsRejectedUsingPostLockAuthorityTime passed against actual PostgreSQL. |
| AC-07 Transaction/takeover serialization | GuardedTransactionDelaysTakeoverUntilCommitWhileAnIndependentKeyStillProgresses passed with independent sessions. |
| AC-08 Allocator and cleanup | Real pooled acquisitions, release/prune/recreation, corruption/exhaustion and clean same-storage restart; no allocator reset. |
| AC-09 Eligibility and authorization | Exact catalog/handler bindings, current scoped authorization/refusals and12public-helper maintenance recipe cases. Native workload authentication remains the existing Security boundary. |
| AC-10 Categories/configuration | Whole-copy coordinated subscription, atomic rejection, category reassignment/hard limits, immutable admitted settings and explicit activation epoch cases. |
| AC-11 Continuous capacity | Current Idle, finite MoreWork fairness, independent renewal and actual callback/scope occupancy cases. |
| AC-12 Drain and uncooperative work | Native Generic Host/Healthcheck, actual callback/scope barrier, terminal-loss retention and failed-Build dependency cases; no timeout certifies physical completion. |
| AC-13 Scheduled integration | Controlled public source/dispatcher, exact frame/codec/admission, missing source/evaluator refusal and exclusive stable resource cases. No recurrence engine claimed. |
| AC-14 Durable recovery and outgoing intent | Two real original-transaction/XID rows including Unknown; genuine killed producer and distinct successor replay; exact committed outgoing delivered/confirmed/ACKed after ownership ends. |
| AC-15 Bounds/diagnostics | Large passive catalogs, managed/request/pending/invocation pressure and bounded snapshots/helpers, with explicit Deferred/CapacityExceeded outcomes. |
| AC-16 Instrumentation/compatibility | Actual HTTP exporter seven-stream names/units/identity/cumulative/cleanup checks, bounded transition logs/cached health; current533unit/215integration,3compatibility and normal27-country-host cases pass. |

## Qualification limits

These are focused local observations, not full release, coverage, mutation,
performance, fleet-consensus, network-partition, power-loss, cluster-failover or
cloud qualification. Real PostgreSQL/broker consumers executed through the
Windows client and isolated Linux Docker services; the available Ubuntu runtime
has no Docker Unix socket, so real Linux-client SQL/broker acceptance remains
unverified. Linux runtime and public recipe units executed directly and passed.
Daily/Weekly/FixedDelay descriptors and external admitted dispatch do not provide
a production recurring scheduler or management plane. No RM-05 remote push,
merge, deployment or package release is included.

## Resource and scope closeout

The seven original funding owners share **40 active hours and 48 logical attempts**,
including 5,400 protected restoration seconds. The final accounting checkpoint
records **26.383 conservatively estimated aggregate source hours** and
**1.868 measured native/metadata hours**, with **12 logical attempts** used.
Waiting is excluded from source estimates. Native commands and all agents share
these totals; journals preserve every recorded cost and earlier failed attempt.

Counted storage is **5.331 GiB within 6 GiB**, including the 2 GiB Docker allowance
and protected 64 MiB reserve. Owner redistributions preserve the original shared
limits, costs and restoration reserves. No package, lock, SDK or runtime pin was
changed. The exact source specification and the user's pre-existing launch settings
remain unchanged. No RM-05 push, merge, deployment or release is included.

All executable plan leaves are Complete; the two decomposed parents retain their
original funding/history. Final native plan validation passed. Verification was
performed against the source now being committed; the commit operation does not
change those qualified code inputs. The small final Git operations remain charged
in the same local accounting record.

## CIS closeout snapshot

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-007 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2215; failed=301; possibleTokenSavings=591; ledgerDigest=sha256:80568e73787c6ea06dc166fb8788b3dfa7ace2acee37ecd1d62f162a55df7657 |

## Managed jobs review correction — WORK-012

The preceding implementation rows retain the evidence for the original
`b1eddad71bd0f75531d6c09d8c6d0b556a1dba97` source. The user subsequently
requested the two jobs corrections and guide-link repair in the
[bounded follow-up](review-follow-up.md). Completed tasks and their costs remain
closed; WORK-012 uses a supported transfer inside the original shared limits.

| Check | Current result | Evidence |
| --- | --- | --- |
| Pinned scoped formatting and affected builds | Passed, zero warnings/errors | WORK-012/review-jobs-final-format + format-verify; review-original-completion-format + format-verify; current unit/integration builds. SDK 10.0.401, runtime 10.0.12; no dependency/pin/lock changes. |
| Windows affected unit tests | 542 passed, zero failed/skipped | WORK-012/review-jobs-current-unit-tests/results/review-jobs-current-unit.trx; includes nine new regression rows and unchanged original scheduled completion assertions. |
| Windows existing host/application/HTTP-metrics integration | 215 passed, zero failed/skipped | WORK-012/review-jobs-integration-tests/results/review-jobs-integration.trx. |
| Actual Linux affected units | 181 passed, zero failed/skipped | WORK-012/review-jobs-linux-units/results/linux-native.trx; existing Ubuntu 24.04 ELF runtime 10.0.12 and managed SDK 10.0.401 VSTest. |
| Exact qualified Core bytes | Matched in Core, unit and integration outputs | SHA256 320721d5648a95897cfb9773d538912cb91e07d908105d9db1252beb7ee97a59; TRX hashes and counters in .cis/local/coordination-intake/review-native-proof.json. |
| Regression behavior | Passed | Coalesced host/category/job disable-enable; stale revision before a later target's admission; initial/later activation cleanup fault without replay; two-resource health, actual recovery cleanup, failed cleanup/Busy preservation and fixed per-job pressure with unrelated-job admission. |
| Independent review and guide links | Passed | Cross-review caught and closed the stale-admission race; final cleanup/recovery review passed. All 46 local Coordination-guide links resolve, including PostgreSQL → hosting. |
| First development unit run | Retained: 539 passed, three failed, zero skipped | Existing original-result tests caught a scheduled diagnostic rewrite. Corrected runtime recovery eligibility without weakening those assertions; the complete current selection passes. Same logical attempt and all costs retained. |
| GitHub service workflow | Not dispatched | These corrections remain local and unmerged. A passing existing service workflow on the corrected remote commit remains a pre-merge recommendation; no Actions result is inferred from local tests. |

At the evidence checkpoint, aggregate conservative source use is **28.533 hours**
and measured native/metadata use is **2.016 hours**, with **13 logical attempts**
used. WORK-012 accounts for 7,740 source seconds and 527.763 measured seconds
inside its 9,000-second distribution; subsequent native completion/Git costs
remain in the same journals. Counted storage is **5.337 GiB / 6 GiB**, including
the existing Docker allowance and protected reserve. User launch settings retain
SHA256 ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784.

This follow-up runs no new PostgreSQL, broker, package-consumer or deployment
campaign. Their earlier results remain retained evidence for unchanged provider
source, rather than a fresh pass against these new Core bytes. The preceding
qualification limits still apply.
## Correction closeout

Supported plan validation returned valid; WORK-012 is Complete. The closing
checkpoint accounts for 30.629 aggregate active hours inside the unchanged
40-hour allowance, with all reserves and prior costs preserved. Local Git
delivery costs remain charged to this same owner.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-012 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2225; failed=304; possibleTokenSavings=591; ledgerDigest=sha256:396b4596a78284f15a8a7e07af909b6237a9d6b864270a3eb8a4ea4fbed7a02f |
