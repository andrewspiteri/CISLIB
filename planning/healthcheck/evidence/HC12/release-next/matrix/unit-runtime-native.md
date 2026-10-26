# Complete unit-runtime mutation: native Windows and Linux

The completed `unit-runtime-windows-02` and `unit-runtime-linux-01` executions
both fail the strict 90% mutation gate. The coordinator reported native exit 2
for both, and both native logs record a final score below the configured break
threshold. This is retained failed qualification evidence. No mutation is
exempted, reclassified or credited as killed by this audit.

Both runs use the exact same `Complete` configuration: 41 Healthcheck sources
outside AspNetCore/Pipes, UnitTests with all Healthcheck classes including the
legacy value-contract namespace, and the scheduling control; Release/net10.0,
VSTest, two sessions, `disable-bail: true`, thresholds 100/90/90, and the installed
`perTest` coverage default. Each native log reports 650 tests found for the initial
run. This is the native mutation-run discovery count; separately retained restored
build/TRX evidence is coordinator-owned and is not asserted complete here.

## Selected 41-source outcomes

| Outcome | Windows 02 | Linux 01 |
| --- | ---: | ---: |
| Killed | 1,843 | 1,844 |
| Survived | 649 | 648 |
| Timeout | 75 | 75 |
| NoCoverage | 128 | 128 |
| CompileError | 362 | 362 |
| Ignored | 589 | 589 |
| All retained selected mutants | 3,646 | 3,646 |
| Strict denominator: Killed + Survived + Timeout + NoCoverage | 2,695 | 2,695 |
| Strict score, without timeout kill credit | 1,843 / 2,695 = **68.3858998145%** | 1,844 / 2,695 = **68.4230055659%** |
| Native timeout-credit score | 71.1688311688% (displayed 71.17%) | 71.2059369202% (displayed 71.21%) |

The native scores use `(Killed + Timeout) / 2695`. The strict scores use
`Killed / 2695`; timeouts provide no kill credit. CompileError and Ignored are
outside this numeric denominator and still require explicit disposition/replay
review. Neither percentage qualifies this scope or full Healthcheck mutation.

All 41 expected source objects are present in both reports. Every report's
original source text, after CRLF-to-LF normalization, matches the corresponding
frozen source hash in
[the scope inventory](../../../../qualification/release-next/scope-inventory.json).
There are zero source omissions and zero normalized source mismatches. The scope
inventory SHA-256 inspected here is
`a5f2c814e27d87fa714bbe83e8495c63396c017801182c2ee9450cb98eff9df3`;
its normalized source/assignment digest is
`03b4f2cbd9c622d77cd63888a3c0b353aab2c2bcd6cd8e339fabb2b1a303a719`.
This report-text comparison does not replace the complete before/after source,
project, fixture, toolchain or candidate-binary inventory.

Twelve selected files generate no native mutants: the eight declaration-only
files already listed in the inventory, plus `HealthExecutionBindingOptions.cs`,
`HealthExecutionFact.cs`, `HealthApplicationIdentity.cs` and
`Internal/State/HealthMonitorEntry.cs`. All remain explicitly selected. Zero
generated mutation points are a native tool outcome, not a source exclusion,
coverage result or killed-mutation credit. The other 29 selected files contain
the retained 3,646 mutant records.

## Whole-Core generator output is a different scope

Both native logs record 10,241 mutants created while processing Core. Their
pre-execution summary contains 1,490 CompileError, 128 NoCoverage, 589
block-covered Ignored and 5,467 mutate-filter Ignored: 7,674 skipped before 2,567
mutants were tested. Those 2,567 tested outcomes are Killed + Survived + Timeout.

The retained JSON has 146 source objects, including 105 outside the selected
41-file scope. Those outside objects retain no mutant records. The selected
report contains **362**, rather than 1,490, CompileError outcomes; the difference
is 1,128 prefilter compile-error statuses outside this selected report scope.
The 5,467 mutate-filter exclusions are also outside the selected mutation scope.
These whole-Core generation counts do not establish whole-Core mutation
execution, and must not inflate Healthcheck's rollback counts or denominator.

All 589 selected Ignored outcomes have the native reason `Removed by block
already covered filter`. All 362 selected CompileError outcomes have the native
reason `Mutant caused compile errors`. Native Safe Mode rolls back whole methods
after compile failures, so that label does not independently prove each affected
mutation intrinsically uncompilable. No rollback or ignored-status waiver is
made here.

## Platform comparison

Matching by normalized source path, full start/end location, mutator name and
replacement text gives 3,646 matching definitions, no duplicate keys, and no
Windows-only or Linux-only definition. Six statuses differ; every difference
involves Timeout:

| Native ID on both lanes | Source / line | Mutation | Windows | Linux |
| --- | --- | --- | --- | --- |
| 1064 | `HealthCollectorRunner.cs:60` | Remove `if (!closeTimer)` body assigning the created timer | Killed | Timeout |
| 1184 | `HealthCollectorRunner.cs:170` | Remove the no-current-operation branch assigning `Due = now` | Timeout | Killed |
| 1423 | `HealthCollectorRunner.cs:402` | Change timestamp conversion division by frequency to multiplication | Timeout | Killed |
| 1614 | `Internal/Contracts/HealthCollectionReservation.cs:74` | Change the Retired disposition equality | Timeout | Killed |
| 1939 | `Internal/State/HealthStateCoordinator.Collection.cs:254` | Remove operation cancellation request | Killed | Timeout |
| 4029 | `Reporting/HealthReportProtocol.cs:310` | Replace one optional diagnostic field name with an empty string | Survived | Timeout |

The one-net-kill difference does not resolve these six different outcomes.
They require retained isolated replay and actual timeout/termination evidence;
neither lane's result substitutes for the other. There are 72 shared Timeout
definitions and three additional Timeout definitions on each lane.

The largest Windows survivor groups are Protocol 175, State.Execution 94,
CollectorRunner 93, Formatter 69 and State.Collection 60. Selected compile-error
groups are identical on both lanes:

| Source | CompileError |
| --- | ---: |
| `Contracts/Collection/HealthCollectionResult.cs` | 3 |
| `Contracts/HealthDiagnostic.cs` | 1 |
| `Contracts/Options/HealthcheckOptions.cs` | 6 |
| `Execution/ExecutionFactValidation.cs` | 9 |
| `Execution/HealthExecutionBinder.cs` | 39 |
| `HealthCollectorRunner.cs` | 123 |
| `Internal/Contracts/BindingRequest.cs` | 2 |
| `Internal/Contracts/HealthCollectionReservation.cs` | 2 |
| `Internal/State/HealthStateCoordinator.Collection.cs` | 23 |
| `Internal/State/HealthStateCoordinator.cs` | 35 |
| `Internal/State/HealthStateCoordinator.Execution.cs` | 58 |
| `Reporting/HealthReportFormatter.cs` | 10 |
| `Reporting/HealthReportProtocol.cs` | 51 |
| Total | 362 |

## Exact retained inputs and reports

Paths below are repository-relative retained local artifacts. Each SHA-256 is of
the actual file bytes inspected in this audit, without line-ending normalization.

| Artifact | SHA-256 |
| --- | --- |
| `.artifacts/healthcheck/release-next/unit-runtime-windows-02/config.json` | `331bb05cfce76e8bd5d861daf7f6f15c5566c3bc88a6a9d603e8c61e04de2118` |
| `.artifacts/healthcheck/release-next/unit-runtime-linux-01/config.json` | `331bb05cfce76e8bd5d861daf7f6f15c5566c3bc88a6a9d603e8c61e04de2118` |
| `.artifacts/healthcheck/release-next/unit-runtime-windows-02/native/reports/mutation-report.json` | `c07ea375e6b0495d9929283ab962b4ae2a03d9502d6991c41b003f8849b9fd6e` |
| `.artifacts/healthcheck/release-next/unit-runtime-linux-01/native/reports/mutation-report.json` | `15b8d7b8dcd1b4a39da775ae5798028b9e0cbee079d28ce108de5e15c9e6596a` |
| `.artifacts/healthcheck/release-next/unit-runtime-windows-02/native.log` | `9c6cc861d62623496fb68cc78c253228de58fd4b39c2885452b9658b1092ebfe` |
| `.artifacts/healthcheck/release-next/unit-runtime-linux-01-native.log` | `17ad26a05b92e7fa038f6443cfb2bc2597c50330acab9382343ad55b74e2ccc9` |

The reports' actual project roots are respectively
`C:/miscwork/portfolio/cislib/.artifacts/healthcheck/release-next/unit-runtime-inputs-02/source/implementations/csharp/src/CIS.Core`
and
`/var/tmp/cis-healthcheck-0bf0c4c8-20261021/release-next-unit-runtime-inputs-01/implementations/csharp/src/CIS.Core`.
Different absolute native roots are normalized only for source/mutant matching;
the original report bytes and paths remain retained.

## Remaining qualification gaps

The failed strict scores require real test-detector improvements and new native
executions. Each surviving, NoCoverage and Timeout result needs contract-based
review and disposition; this note supplies no equivalence exemptions or private
state manufactured to force a kill. Review/replay the 362 selected compiler
rollback outcomes and 589 block-covered outcomes separately. Preserve failed
attempts, exact scope, native output, source/candidate/tool pins and all corrected
run identities.

Complete source-before/after verification and restored native build/TRX receipts
are coordinator-owned follow-up evidence. The new HTTP detector tests and their
fresh native snapshot are separate from these completed unit-runtime inputs;
they provide no retroactive credit here. ASP.NET/Pipes reports, actual child-origin
and exit audits, full Healthcheck automatic closure, whole-Core Release gates,
external integrations and actual publication-history obligations remain separate.
No release or historical replay qualification follows from these two runs.
