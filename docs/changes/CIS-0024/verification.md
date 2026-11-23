---
title: CIS-0024 verification
type: verification-record
status: Draft
change_id: CIS-0024
---

# Verification

RM-07 is implemented locally on `codex/rm07-central-management`, based on
`494e89412a115078a60ca88b8536337db566665f`. The actual user delegated continuation
after scope without another permission pause; the generated profile/allocation
was not separately reviewed by the human. See the implementation plan and
delegated authority for the exact bounded scope. The worktree is uncommitted;
this record does not identify a pushed, merged or released candidate.

## Final focused evidence

Native phase directories below are under `.artifacts/management/CIS-0024/`.
`native-costs.jsonl` retains exact arguments, elapsed execution and exit codes;
`.cis/local/management-intake/metadata-costs.jsonl` separately records CIS work.
The passing selections contain no skipped required cases. Earlier failures and
superseded component receipts remain preserved in the next section.

| Boundary | Actual result | Current or retained native phase |
| --- | --- | --- |
| C# Management units | 163 passed, 0 failed, 0 skipped; zero-warning build | `WORK-006/uncertain-inventory-current-management-units` |
| Ubuntu managed execution | 163 passed, 0 failed, 0 skipped | `WORK-008/ubuntu-current-management-unit-checks`; Ubuntu24.04 ELF .NET10.0.12, existing managed VSTest18.7.0 with ReadyToRun disabled; this alone is not a Linux SDK build |
| Portable schema/OpenAPI | 68 passed, 0 failed, 0 skipped; zero-warning build | `WORK-008/current-portable-management-contract-checks`;64 independent exact-text cases plus4 structural checks |
| TypeScript | Build/browser check passed; native Node90 passed, 0 failed/skipped | `WORK-007/typescript-direct-compiler-build`, `typescript-direct-native-tests`, `typescript-direct-browser-build`; actual Node24.15.0, npm11.12.1, local TypeScript5.9.3 |
| Actual PostgreSQL persistence | 16 passed, 0 failed, 0 skipped | `WORK-003/corrected-real-postgresql-persistence-tests`; real PG18.7, two roots and production durability settings |
| Actual native product hosts | 5 passed, 0 failed, 0 skipped | `WORK-002/complete-native-management-host-tests`; native TLS/JWT and exact resource/listener/cursor boundaries |
| Configured identity profiles | 3 passed, 0 failed, 0 skipped | `WORK-002/actual-configured-native-profile-recheck`; LocalDevelopment/certificate mTLS and configured Entra with owned signed tokens/static metadata |
| Metadata and atomic Audit | 8 real HTTP cases plus2 corrected native completion cases passed | `WORK-004/complete-metadata-audit-native-tests` (8 HTTP passes) and `corrected-native-original-completion-tests` (2 passes); first combined run's incorrect cleanup expectation remains failed history |
| Live delivery handoffs | 3 passed, 0 failed, 0 skipped; zero-warning Integration build | `WORK-008/actual-facade-lost-response-and-node-handoffs`; active public facade registration/real Heartbeat, genuine postcommit HTTP socket/body loss, same-service C#/Node mutation/recovery |
| Outside-friend package handoff | 1 passed, 0 failed, 0 skipped | `WORK-008/current-public-management-package-handoff`; exact current Core/CommonTesting bytes, content ports/scripts and passive explicit registration, dependency allowlist preserved |
| Affected existing lifetimes | 56 native Integration passes and9 unit passes, no failures/skips | `WORK-006/affected-native-hosting-lifetime-regressions` and `affected-application-startup-unit-regressions` |
| Existing API/consumer compatibility | 3 passed, 0 failed, 0 skipped; zero-warning build | `WORK-008/current-core-original-consumer-and-api-compatibility`; two historical consumers plus ApiCompat |
| Linux image | Built successfully from locked graphs; runtime/profile checks passed | `WORK-008/linux-container-declared-lock-build` and `built-linux-image-runtime-profile`; real Linuxamd64 SDK10.0.401 publish, .NET/ASP10.0.12, TypeManager inventory and selected DiagnosticSource asset present |
| Compose/skill | Passed | `WORK-008/local-compose-config-validation` without private env resolution or starting services; `management-skill-validation` used the existing skill-creator validator |

The built local image is `cislib-management:cis0024-2af6f3d2714e`, immutable
manifest `sha256:b56190a38e0769be9b393d1bf35708af3808b27257eeff7d6cd3e1c83c703b95`.
Registry receipts retain the exact Microsoft tag/index/amd64 child relationships.
The existing service CI workflow now installs/builds/checks the pinned TypeScript
workspace before the native service selection. That authored change is not a
remote CI result.

## Acceptance coverage

| Source criterion | Evidence and practical boundary |
| --- | --- |
| AC01 | Independent product project, native product roots and real Linux image build; no CIS runtime or portal dependency. |
| AC02 | Real native TLS/JWT, configured mTLS/Entra profiles, invalid/unmapped/wrong-environment and forged identity refusal. Local metadata is not external Entra federation or cert-manager proof. |
| AC03 | Real shared PG/host registration replay, changed-content/foreign ownership refusal and separate caller UUIDs across roots. |
| AC04 | Independent monotonic sequences, immutable equal replay, terminal close, bounded cleanup/unknown rejection; no RM05 ownership inference. |
| AC05 | Nine Jobs adapter cases include500 jobs across all pages, scoped category identity, exact native byte bounds, configuration revisions/resource observations and whole-catalog refusal. Live facade uploads through the actual service. |
| AC06 | Current actual resource grants on direct/list/page/hint/mutation/recovery paths, actor-bound opaque cross-replica cursors and hidden resources. |
| AC07 | Distinct real transaction keys with the same old metadata ETag yield one applied and one native rejected result; telemetry does not change the metadata ETag. |
| AC08 | Required real Audit/outbox stage shares the original transaction/journal. Actual staging-capacity refusal rolls back; matching replay produces no second effect/Audit. No Audit destination database is supplied. |
| AC09 | Exact-key fixture middleware aborts the real socket after commit when result bytes would be written. Native SDK returns transport failure with the original key; healthy replica lookup and explicit same-key replay recover one result/effect/journal/Audit. Native commit-acknowledgement loss is separately exercised. |
| AC10 | Signed native human/delegation projections and actor-separated result scope; another operator and forged headers cannot recover the original actor's result. |
| AC11 | Shared exact-text fixtures, signed Int64 decimal strings beyond2^53, seven-fraction UTC timestamps, exact Unicode and response compatibility on both clients. |
| AC12 | Built C# and Node clients call the same actual authenticated service; native body loss, original-key recovery, held cancellation/cleanup and finite read-retry boundaries. |
| AC13 | Browser DOM/types:[] compilation, no external runtime dependencies, per-user credential separation and safe server projection tests; no Next/login/UI runtime. |
| AC14 | Direct required/optional startup, finite initial budget, latest-only reconnect/coalescing, actual native hosting barriers/cleanup and quiet bounded diagnostics. Unconfirmed inventory reversion has three explicit regressions. |
| AC15 | Real PG reconstruction through fresh service roots, persisted sequence/result protection, current outside-friend packages and public CommonTesting. No process-memory write is treated as durable. |

## Earlier component checkpoints

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2293; failed=315; possibleTokenSavings=591; ledgerDigest=sha256:30d642467ad07e515cb22590f15c85968458d2ffbef419117a4d15d50d1146e2 |
| WORK-003 | Real PostgreSQL store | `WORK-003/corrected-real-postgresql-persistence-tests` under `.artifacts/management/CIS-0024` | Passed:16, failed:0, skipped:0 | Actual PG18 two roots; registration/report receipts, independent sequences, inventory/capacity, terminal retention, CAS and original completion facts. First shared migration failure retained. |
| WORK-003 | Source quality | `postgres-explicit-file-format-verify` | Passed | Affected Integration build zero warnings/errors; ordinary explicit-file format and verification. Folder-selection failure preserved. |
| WORK-005 | Initial management units | `WORK-005/management-client-focused-unit-tests` | Passed:114, failed:0, skipped:0 | Initial checkpoint; superseded by the129 and163 selections below. |
| WORK-002 | Pure service boundaries | `WORK-002/service-pure-boundary-tests` | Passed:4, failed:0, skipped:0 | Actor scope, cursor binding and deployment limits; does not establish actual host authentication. |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2299; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:11b0c01dd3f83abe1a4760a813723c0f252614fdb82a4cff63c44c6a5c45df19 |
| WORK-005 | Current management units | `WORK-005/rejection-union-management-unit-tests` | Passed:129, failed:0, skipped:0 | Current native rejection union and cancellation/URI boundaries included. |
| WORK-005 | Exact-current package consumer | `WORK-005/exact-current-management-package-test` | Passed:1, failed:0, skipped:0 | Public packaged Core/CommonTesting; exact-byte checks retained. |
| WORK-002 | Authenticated product hosts | `WORK-002/complete-native-management-host-tests` | Passed:5, failed:0, skipped:0 | Two actual product roots, native JWT/TLS, exact capability filtering, listener isolation, registration/report recovery and strict request boundaries. |
| WORK-004 | First full metadata/Audit suite | `WORK-004/complete-metadata-audit-native-tests` | Passed:9, failed:1, skipped:0 | All8 real HTTP cases passed; initial unknown-completion cleanup expectation was later corrected and both direct cases passed. No new combined10-case run is claimed. |
| WORK-007 | Built portable TypeScript | `typescript-direct-compiler-build`, `typescript-direct-browser-build` | Passed | Actual pinned TypeScript5.9.3 compiler; browser DOM/types:[] and no Node/React/Next runtime dependencies. Quiet exit0 recorded in native-costs.jsonl. |
| WORK-007 | Native Node behavior | `WORK-007/typescript-direct-native-tests` | Passed:90, failed:0, skipped:0 | Shared exact-text vectors, independent credentials, bounded reads, cancellation and actual held cleanup. |
| WORK-007 | Same-service CSharp and Node | `WORK-007/complete-real-csharp-node-consumer-tests` | Passed:1, failed:0, skipped:0 | Two actual product roots, PostgreSQL/RabbitMQ, strict native TLS/JWT. Both clients read/mutate/recover, preserving each original result after later edits. |
| WORK-004 | Corrected actual commit/cleanup | `WORK-004/corrected-native-original-completion-tests` | Passed:2, failed:0, skipped:0 | Exact native commit and rollback invocation facts, Unknown plus actual rollback failure, committed plus late disposal failure, original Found/replay remains committed. Together with the earlier8 HTTP passes covers the10 cases; does not claim a new combined run. |
| WORK-002 | Configured certificate / Entra | `WORK-002/actual-configured-workload-profile-tests` | Passed:2, failed:1, skipped:0 | Production certificate/Entra passed; LocalDevelopment fixture selected the native environment too late. Corrected3-case check below passed with the native gate retained. |
| WORK-002 | Configured native profiles corrected | `WORK-002/actual-configured-native-profile-recheck` | Passed:3, failed:0, skipped:0 | Exact LocalDevelopment gate with normal native environment selection, production certificate trust/mapping and configured Entra native signature/issuer/audience/tenant/roles. Owned local proof; no real Entra federation/cert-manager claim. Required Docker categories retained. |
| WORK-006 | First registration candidate | `WORK-006/registration-host-current-management-units` | Passed:160, failed:0, skipped:0 | Initial31 new cases; independent review then found an uncovered inventory-reversion/unknown-response defect. Corrected163-case check below passed with3 new regression rows. |
| WORK-006 | Affected existing unit behavior | `WORK-006/affected-application-startup-unit-regressions` | Passed:9, failed:0, skipped:0 | Existing Application setup/Coordination and Startup-Heartbeat completion. No change to Startup coordinator. |
| WORK-006 | Current corrected registration candidate | `WORK-006/uncertain-inventory-current-management-units` | Passed:163, failed:0, skipped:0 | Three new response-loss/fault/OCE reversion rows; one uncertainty flag preserves the latest state without history. Current Unit build zero warnings/errors; explicit-file format/verification passed. |
| WORK-006 | Existing native hosting regressions | `WORK-006/affected-native-hosting-lifetime-regressions` | Passed:56, failed:0, skipped:0 | Current full Integration build zero warnings/errors; actual existing Startup, Heartbeat, REST shutdown, facade lifecycle, failed Build and alias behavior. |

## Failure history and corrections

Retained native failures include the initial PostgreSQL `regclass` probe,
incorrect direct Heartbeat-service dependency, provider-private field access,
stale Integration Core bytes caught by the package guard, the native Unknown
cleanup expectation, and late LocalDevelopment environment selection. Each was
corrected at its actual boundary; dependency, exact-byte and native authority
checks were preserved. Directory-based formatting did not select contained
files, so explicit-file formatting and verification replaced that invocation.
The local npm child-command PATH problem remains an environment observation;
the installed Node executed the exact pinned compiler, native test and browser
entry points directly. No replacement runner was introduced.

Independent review found a genuine reporter defect after the initial160 passes:
confirmed inventory A, an unconfirmed B upload, then local reversion to A could
discard the latest pending value. One uncertainty flag now survives admission,
lost response, original fault and unrelated cancellation until exact receipt.
Three regression rows require A/B/A uploads at increasing sequences without a
history queue. The corrected163-case selection passes.

The first real Docker build failed NU1004 because global restore `-r linux-x64`
changed referenced projects' checked-in lock graphs. The recipe now restores
their declared graphs unchanged, publishes the service explicitly for Linuxx64,
and publishes the portable managed probe without an apphost. The second build
passed; no package pin, mapping or checked-in lock was relaxed for it.

## Candidate, accounting and restoration

The final product/test/guide source manifest contains139 changed/new files,
excluding CIS closeout documents and the pre-existing user launch settings:
`WORK-008/final-source-and-resource-snapshot/product-source-hashes.csv`, SHA256
`8bc39b3485c5993a8576fd7bdb193e883a64fb59ae2e3b3ceef5865cde131995`.
Native receipts preserve the actual baseline, source, toolchain and lane limits;
no new Git commit or remote CI receipt is fabricated.

At the19:55 snapshot, source estimates plus actual native/CIS elapsed work total
about24.61 shared active hours and25 logical attempts, before the final CIS
transition, within48hours/56attempts including restoration. All eight owners use
the same finite allocation. Direct SDK/npm/Docker work and source estimates stay
in their journals; zero native CIS batches would not mean zero execution cost.

The retained counted footprint was6,061,718,719 bytes against6,442,450,944 bytes,
including the fixed2GiB Docker charge and64MiB protected reserve, with380,732,225
bytes of additional headroom. No allocation increase was applied.

**Storage deviation:** after building, Docker reported2.102GB of build cache in
addition to1.06GB of image storage and144.3MB of pre-existing volumes. This
temporary intermediate-cache retention exceeded the planned Docker allowance;
the fixed-charge guard did not measure that growth. These logical readouts do
not establish an exact peak physical footprint, so this record does not claim
the physical ceiling was verified throughout the build. After actual build and
container drain,11 exact inspected cache IDs created by these builds were
removed. The completed image, all images and every existing volume were retained.
Fresh native inventory reports410.3MB cache,1.06GB images,144.3MB volumes and no
running containers: their conservative sum is below the2GiB allowance. Exact
ownership, selections and restoration output are retained in
`WORK-008/owned-image-cache-accounting` and `owned-build-cache-restoration`.
No global prune, volume deletion or foreign-resource cleanup was performed.

The pre-existing untracked CountryData launch settings remain byte-identical,
SHA256 `ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.
Actual selected native fixtures and private-file/key owners settled; no running
task-owned container remains. No existing database or credential was selected,
and no trust-store installation or machine key was performed.

## Practical limits

This is focused module acceptance, not broad unchanged-module, performance,
coverage/mutation or release qualification. Windows exercised the actual native
services and clients; Ubuntu exercised managed units; the pinned Linux image
was built and its runtime/publish closure checked with networking disabled.
The full Compose deployment was not started. Real external Entra federation,
AKS, cert-manager issuance and an Audit destination archive remain unverified
or explicitly outside RM-07. The authored GitHub workflow was not run remotely.
Nothing was pushed, merged, released or deployed to external infrastructure.
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2301; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:7709c6f560c4d3b88e6f993d635265a63c4cc6ba85545d5b17bab92198742e8e |
| WORK-007 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2303; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:7418dede3ca30b3e748d83df4bb0fb3355da21a549719d5102291155fc566f7c |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2304; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:61de64fc8fe53494289ee9d70f7ff1a9bcba159dd54a43a0e2df0f7b7e38e7c1 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2305; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:7d05d3bf08b4ff87419e4475ea407318e36a55ecf5d18d04965b67f96766c85c |
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2306; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:07d90283955ff4b8941d738a8c48609b8eb61efd331943edd89304cb2551bc70 |
| WORK-008 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=2308; failed=316; possibleTokenSavings=591; ledgerDigest=sha256:9b02a76fc7e7040a4805cdf4e7047e98c9b6d33844a9bafd5fdae149e20c3789 |
