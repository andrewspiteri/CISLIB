# Native quality checks

From `implementations/csharp`, collect coverage using the ordinary solution:

```powershell
dotnet test CisLib.slnx -c Release --settings verification/coverage/shipping.runsettings --collect "Code Coverage" --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress"
```

The collector includes Core, the packaged Type Manager build helper and real
child processes, without source/function exclusions. Read its native Cobertura
reports against the accepted 95% line and 90% branch requirements. Collection
does not enforce those thresholds or establish full release readiness. Keep OS
lane results separate; performance/stress verification remains a separate
ordinary test selection.

Native CompatibilityTests exercise the retained unpublished development
baselines. The pinned `dotnet apicompat` command remains available for exact
accepted baseline/candidate assemblies; no published consumer version is
invented by this cleanup.

Restore the pinned tools from `implementations/csharp`:

```powershell
dotnet tool restore --configfile eng/Tools.NuGet.config
```

Run Stryker from the product project directory. Set the process-local
`CisMutationTesting` environment variable to `true` for these invocations, then
restore its previous value. This selects serialized MSTest execution while
keeping ordinary IDE and `dotnet test` runs unchanged.

```powershell
# From implementations/csharp/src/CIS.Core:
dotnet stryker --config-file ../../verification/mutation/core-release-config.json --skip-version-check

# From implementations/csharp/eng/CIS.TypeManager.Build:
dotnet stryker --config-file ../../verification/mutation/build-helper-config.json --skip-version-check
```

Both configurations use the retained native test projects, VSTest, Complete
mutation level and a 90% break threshold. Core selects Unit, Integration,
Architecture and Business tests; performance observations and extended stress
runs remain ordinary test obligations. The helper selects the direct
`WriteTypeInventoryTests` in ArchitectureTests. No generated test project,
source snapshot, producer receipt or campaign runner is required.

Core's transport tests start real child processes. A small integration fixture
stages the parent-loaded instrumented Core and actual active-mutant selector
into those child outputs. Coverage analysis is disabled so Stryker runs the
selected tests for every mutant, including child-only behavior. This adapter
does not establish that every child path or release mutation obligation passed.

Review the native JSON/HTML results. The accepted release measure remains
`Killed / (Killed + Survived + NoCoverage + Timeout) >= 90%`; timeouts do not
receive kill credit. Review compiler rollbacks, noncompiling/ignored mutants
and untested child paths explicitly. Stryker's displayed score alone does not
establish this stricter release claim. Keep unresolved product failures visible.

These are native routes for future scoped execution. Cleanup did not run a
whole-library mutation campaign or qualify either supported release lane.
See [Stryker configuration](https://stryker-mutator.io/docs/stryker-net/configuration/)
for the native options.
