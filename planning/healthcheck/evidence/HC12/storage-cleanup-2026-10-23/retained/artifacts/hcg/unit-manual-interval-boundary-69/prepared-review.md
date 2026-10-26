# Native control and finite trial preparation

The isolated original MSTest control passed: one executed and passed case, zero failed, skipped, timed-out or aborted cases; native exit zero. Its build had zero warnings and errors. The unchanged proposed fixture is `662e5599ab62b94f5719f1a4502b0b1ba41bc6c7b43e2671df1c2ca7beb9d56a`. Both the captured original Core and the actual test-directory Core copy are `5589c3bd76c2eee71ccaa2b606766ce4522f0059ca5ced508bf9a171d15dbee2`.

The actual native TRX is `native/results01/original01.trx`, SHA-256 `5199fceaecdd22d0f000d195d947c3da19ec287e5049ca846827a5f7daf659c3`. The control receipt is `native/control-receipt.json`, SHA-256 `bd28e1e80ff4327131faf2e039f5f6250cb6e89d58a4db2438796f1656527274`. The runtime configuration requires Microsoft.NETCore.App and Microsoft.AspNetCore.App 10.0.12 with roll-forward disabled. The SDK is pinned to 10.0.401. Package references are MSTest framework/adapter 4.5.1 and Test SDK 18.10.1; the explicitly invoked SDK VSTest console reports banner 18.7.0. The test project has no project references and links only the proposed fixture.

The finite Stryker trial has been prepared and its controller compiled, with zero warnings and errors. **Its subject has not been started.** There is no `finite/execution` directory. No canonical or remote source was edited. At preparation, the entire private 69 tree contained 1,717 files totaling 28,761,519 logical bytes, including native outputs, two helpers and the 1,474 selected source inputs. No repository/planning tree, build output tree, or NuGet cache was recursively copied: setup copied the exact 1,473 pinned final-60 files and the one proposed fixture.

| Prepared input | SHA-256 |
| --- | --- |
| `finite/plan.json` | `ce9063505ae9fcbe15aa588ae6dfeb971a557ea4d54c2faa052144bb4b4fc11f` |
| `finite/config.json` | `9de6ca3cb4acbcbb7b1429bff8793ba8c9641182c9f405947e0dbfd44d7937f7` |
| `finite/source-pins.json` | `5a3f4d5b1182051cb5375db8b00c7d22cbf402ab64a64bc80b77eaddd34b50ff` |
| `setup/Program.cs` | `35fa1619bc2422cf1c8cc18aebd8f40aa1c1fb8e2c28e00baccbce6cea2bc340` |
| `controller/Program.cs` | `0f5d0c7e0cafc3d67c99f9d921b1d9f0444b54babf11a26ba67c0f1db2c0b45d` |
| `controller/PortablePdbValidation.cs` | `3cbc3b6be65294a5821243bd1448ca06bb197fe0175a510aea3e0c16965c920b` |
| `controller/bin/Release/net10.0/Controller.dll` | `311bbdb7173a681254130dccf2dba2049c53c64cee3dee139eeb70f415ecc9d3` |

The original Unit18 report remains pinned to `056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d`. Setup proved the complete `ExecuteAsync` method text unchanged between that report and current Runner `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`. Old 1511 and 1513 map to the current predicate, with original/replacement text, mutator name, locations and old status recorded in the plan.

The selected outer predicate is UTF-16 `[23556,23618)`. Complete mutation also selects its three sibling definitions 1512, 1514 and 1515. All five are pinned and independently required by the controller; the two requested survivors remain separately marked. The controller requires unique actual IDs and unique old-definition matches and rejects unexpected active definitions. It retains native compiler-rollbacks in the raw report. The two baseline cases must be exactly the proposed manual-interval case and `CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode`, with exact original/fresh GUID/FQN/display cohorts.

The configuration retains Complete/default-perTest coverage, two workers, no mutant mixing, 300,000 ms additional timeout, no bail, and 100/90/90 thresholds. Four configuration keys differ from the exact parent profile: source range, finite test filter, no-mix and additional timeout. The plan's `complete` flag denotes the Complete mutation profile, not an executed campaign.

The reused controller captures actual tool bodies, all eight original/after-native/fresh file roles, actual raw native backups, and the explicit portable-PDB repair before rebuilding. It rechecks all source pins before and after execution and after fresh baseline, requires source and test-copy restored Core DLLs to match the actual native backup, and retains native exit two when a complete finite result misses 90. Its 1,800-second and 2 GB reserve guards stop only its owned process tree. No timeout or crash is promoted to assertion credit; `mutationCredit` and `automaticGateAcceptance` remain zero/false.

The coordinator must review this exact plan and controller before executing it. No native mutant result, gate improvement, or release claim is made here.
