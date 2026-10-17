# Native release-evidence migration — 17 October 2026

The existing release-evidence policy now executes in `implementations/csharp/eng/CIS.DependencyChecker/VerificationEvidence.cs`. No library runtime or package dependency changes were made. PowerShell entry points remain bootstrap/invocation, JSON output, reference and copy glue; no second policy validator was added.

The port preserves TRX/discovery parsing, exact native identity matching, known legacy display-name escaping, historical integral decimal counts, hashes and confined paths, case/group minima, six-family validation, run/source/profile/lane reconciliation, and fail-closed planned-case/release gates. Native paths use the host's case rules. Fractional counts and missing reconciliation identity fields are refused. The two isolated Type Manager scripts now copy and hash the managed reader and runtime files beside the module, preserving source-independent reader execution; their longer frozen-candidate workflows were not separately requalified here.

## Executed checks

The original 55 C# controls pass before migration. The final suite has **77 passing cases on each native lane**, retaining those 55 and adding 22 controls for malformed/hash/path/counter/identity evidence, historical counts, launcher acceptance/refusal, copied-reader deployment, family discovery and mutation-slice refusal. Assertions and fixtures are C#; tiny shell probes only invoke the established entry points. Bootstrap probes sharing checker output run serially. `ARCH-NATIVE-EVIDENCE-MIGRATION` binds 60 parser/discovery/reconciliation/migration rows; existing positive/negative, command-log and checkpoint-guard IDs remain unchanged.

| Lane | Unit | Integration | Architecture | Business | Total |
| --- | ---: | ---: | ---: | ---: | ---: |
| Windows x64 | 3,750 | 2,120 | 665 | 24 | **6,559** |
| Native Ubuntu 24.04 x64 | 3,750 | 2,122 | 665 | 24 | **6,561** |

Both complete Developer profiles return **exit 0**, zero failures/skips/validation issues, and complete required-case reconciliation. Windows run `2b63048a9bb54d398855c9ef35b84340`; Ubuntu run `6e561158f8fa47ca9f7c511a3ebacfce`. Focused runs: Windows `774ac55e92ba40ef91c886a9a4fe8621`, Ubuntu `356b006ee06246269a0197fdd69ca7e2`.

The [C# retention audit](RetainCheckpointEvidence.cs) verifies all six family records, explicit NotRun reasons and zero credit for mutation/compatibility, execution/inspection/package/report/reconciliation pins, the 60 required migration bindings, and identical complete sets of **853 executable/build/configuration/shared-contract inputs**, including **633 C# files**, in both recorded inventories and both live source trees. [Audit](audit.json), [input identities](matching-executable-inputs.json), raw [Windows summary](windows-summary.json)/[validation](windows-validation.json) and [Ubuntu summary](ubuntu-summary.json)/[validation](ubuntu-validation.json) are retained here. Full native reports, logs, inventories and packages are under `.artifacts/corrective/release-evidence-migration-2026-10-17/{windows,ubuntu}-Developer` and corresponding `*-packages` directories.

Windows executes base `4a263fd3` plus the task inputs pinned above; native Git metadata remains `a4df38e0`. Lane-local packages and differing Git metadata are not one unchanged cross-lane release candidate. Documentation metadata written after execution and the unrelated user-owned Web launch settings are outside executable-input equality.

## Local review, preliminary attempts and limits

Local review corrected a missing-identity parity gap, with three regression cases. Initial smoke fixtures also exposed unsupported custom arguments on the root launcher, Unicode loss in the smoke transport, and concurrent bootstrap builds on Ubuntu. The fixtures now use the existing language launcher for custom manifests, explicit UTF-8 transport and serial shared-output probes. Failed focused attempts remain in their unique focused directories/logs and earn no pass credit. An early retained-report smoke wrote to its default validation slot; the original committed bytes were restored and independently matched SHA-256 `ef4b67392fb7611ed6ad4dfa4f8f5b916535d50cb40ae5e01f6c742ccf21adfb`.

The preliminary Windows run `6c6e5eaa24b34056b792ab2ab2a52a2f` was stopped before test execution after local review found the identity gap. Its incomplete summary/logs are preserved under `windows-before-identity-review`; old slots in that incomplete directory are not evidence for that attempt. The final complete runs above supersede it.

CIS graph build `sha256:e54ffa58cfe5aef87a04a788af9f4ea471827573f59be4ddd8f9e36a5401fe15` is fresh, with 11,233 nodes, 55,840 edges and zero diagnostics; this migration status check is non-strict. The validator class resolves uniquely and untruncated in `cis-dependencychecker`, at its canonical source line 12. Graph navigation does not prove complete call/test coverage. The preceding [CIS guidance batch](../corrective-cis-guidance-2026-10-17.md), commit `4a263fd3`, has its own strict structural graph validation.

No Claude service was used; the user has put further Claude work on hold. The original roadmap goal remains paused. This ports existing checks and does not implement the pending full mutation validator, qualify complete Release, refresh coverage, run cumulative compatibility or complete offline replay/publication. Publication validation is the next bounded PowerShell migration, outside this batch.
