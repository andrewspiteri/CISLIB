# Complete Healthcheck mutation inputs

These are authored Stryker.NET 4.16.0 inputs, with no native execution or
qualification credit. The [scope inventory](scope-inventory.json) partitions all
46 current shipping Healthcheck C# files exactly once: 41 in
[unit-runtime.json](unit-runtime.json), one in [aspnet.json](aspnet.json), and four
in [pipes.json](pipes.json). All eight declaration-only files remain explicitly
included. No executable file, mutation kind or method is intentionally excluded.

Every configuration selects `Complete`, two parallel test sessions,
`disable-bail: true`, Release/net10.0/VSTest, the native JSON/HTML/progress/cleartext
reporters, and thresholds 100/90/90. These are three independent native reports;
authoring them does not establish full Healthcheck mutation qualification.

| Input | Mutated sources | Selected native tests | Coverage analysis |
| --- | --- | --- | --- |
| unit-runtime | All Healthcheck files outside AspNetCore/Pipes, including Execution and Reporting | Every `CIS.Tests.UnitTests.Healthcheck.*` test, legacy `CIS.Tests.UnitTests.HealthcheckValueContractTests.*`, and the exact scheduling control; historical expected baseline 649 + 1 = 650, requiring current confirmation | Installed 4.16 default `perTest`, option omitted |
| aspnet | All `Healthcheck/AspNetCore/*.cs` | Integration `HealthcheckHttpTransportTests` and scheduling control | Explicit `off` |
| pipes | All `Healthcheck/Pipes/*.cs` | Integration `HealthcheckPipeTransportTests` and `HealthcheckProbeProcessTests`; Unit `HealthReportProtocolTests` and `HealthReportFormatterTests`; scheduling control in both projects | Explicit `off` |

The filters use the inspected complete class namespace followed by a dot, so a
similarly prefixed class is not selected. The scheduling control uses equality.
Native discovery remains authoritative for actual per-project test identities
and counts. Stryker's `test-projects` array supports the two-project pipe closure.
The [unit class inventory](test-class-inventory.json) reconciles all 15 current
Healthcheck test source files against the retained historical 649 expanded
cases. Fourteen classes use the Healthcheck namespace; the legacy value-contract
class uses `CIS.Tests.UnitTests` and contributes 28 historical expanded cases.
The retained integration receipt has 45 HTTP, 37 pipe and 55 probe cases.
Accordingly, the new ASP.NET baseline is expected to contain 46 cases including
its scheduling control. The mixed Pipes baseline is expected to contain 93
Integration cases and 183 Unit cases (152 protocol, 30 formatter, one scheduling
control), totaling 276. Both assemblies execute their own scheduling control.
These are historical expectations requiring new native confirmation.
The installed input metadata and exact tool binary hashes are retained in
[tool-metadata.json](tool-metadata.json). Its help states that `off` runs every
selected test for every mutant, and that `perTest` selects covering tests.

The current unit-runtime configuration SHA-256 is
`331bb05cfce76e8bd5d861daf7f6f15c5566c3bc88a6a9d603e8c61e04de2118`.
The earlier `3c3679614f1fc01cf5d4f2b0441ee7daa4934483f63cc3bb32468e782111ebc9`
filter omitted that legacy class. The coordinator retained and stopped the real
`unit-runtime-windows-01` attempt before completion. That superseded input and
attempt cannot supply current complete unit-runtime mutation evidence; the
corrected configuration requires its own actual execution.

## Execution inputs

Use an isolated, complete, independently pinned source copy and a new output
directory for each run. Run from `implementations/csharp/src/CIS.Core`; the
project and test-project paths resolve from this directory, as in the existing
Core release mutation configuration. The native coordinator owns execution.

```powershell
$env:CisMutationTesting = 'true'
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
dotnet exec --fx-version 8.0.31 --roll-forward Disable <pinned-4.16.0-Stryker.CLI.dll> --config-file ../../../../planning/healthcheck/qualification/release-next/unit-runtime.json --output <new-unit-runtime-output> --skip-version-check --log-to-file
```

The example uses placeholders for paths selected and pinned by the coordinator;
it is not an executed receipt. Substitute `aspnet.json` or `pipes.json` and a
separate new output for each child scope. Subject SDK/runtime pins remain SDK
10.0.401 and Microsoft.NETCore.App/Microsoft.AspNetCore.App 10.0.12, distinct from
the Stryker .NET 8.0.31 tool host. The scheduling control must confirm that each
selected test assembly was built with serialized mutation scheduling.

For both child scopes, prepare the existing `healthcheck-mutation-children`
session using the exact configuration and current source/tool inputs. Supply its
`session.json` through `CIS_HEALTHCHECK_MUTATION_SESSION` to the native invocation.
The [child support handoff](../../evidence/HC12/release-next/children/implementation.md)
documents the coordinator-owned linking and prepare/audit entry points. Audit the
ASP.NET run with `--require-kind http-host` and the Pipes run with
`--require-kind probe`; known-mutant control runs also retain their exact
`--require-active` value. These actual propagation audits must pass in addition to
the native Stryker reports. No VSTest child coverage is assumed. Abnormal exits,
staging/observer failures or wrong selected Core/controller/selector identities
cannot silently provide credited kills.

## Reporting and real probe closure

The shared formatter and protocol implementation belong exclusively to the
unit-runtime source partition. Its independent unit fixtures exercise parsing,
formatting and framing directly. The pipe configuration also selects those two
unit test classes alongside actual server/probe integration tests. This keeps
the direct protocol oracle in the same mixed-project baseline as CLI/transport
observations without double-mutating Reporting sources or assigning a fixture
observation to the wrong source partition. In that run, Reporting is an unchanged
dependency; actual pipe/probe mutations are tested by their receiving transport
and real executable. A passing direct codec test does not prove a child loaded
an instrumented assembly.

Only the real native outcomes establish which assertions kill which mutations.
Unit-only Reporting mutations are not represented as child-process executions.
Any uncovered source behavior, surviving mutation, rollback or excluded native
result requires review and, where needed, corrective tests or a further native
execution. The partition is an input inventory, not a claim that these tests
already achieve complete behavioral coverage.

## Source pins and acceptance

The inventory has ordinal forward-slash repository paths, raw file SHA-256,
CRLF-to-LF normalized source SHA-256, and one configuration assignment per source.
Its normalized scope digest includes each path, normalized hash and assignment;
each configuration's exact LF/UTF-8 bytes has a separate SHA-256. Raw hashes stay
available for same-checkout equality; a Linux checkout retains its own raw source
inventory and compares normalized hashes when Git changed line endings. These
46 source pins are deliberately bounded: they do not replace the execution
manifest for projects, tests, fixtures, generated child helpers, lockfiles,
toolchain, runtime, package provenance or the whole repository.

Before execution, reject missing, duplicate, newly added or changed source paths
and changed configuration bytes. Preserve actual before/after source equality,
passing current native baselines, all logs/reports, restored builds/tests and
canonical source preservation. Review compiler rollback, ignored, compile-error,
timeout, no-coverage and surviving outcomes; they do not become killed mutations.
Use strict `Killed / (Killed + Survived + Timeout + NoCoverage)` with a minimum
90%, and retain Stryker's displayed score separately if it credits timeouts.
The native threshold alone does not enforce that conservative formula.

Full Healthcheck automatic mutation stays pending until all three actual scopes,
required child-origin controls and all result/rollback dispositions close.
Broader whole-Core Release gates, external Heartbeat/IStartable integration and
publication-history obligations remain separate. The actual empty publication
ledger does not create a released baseline or historical replay.
