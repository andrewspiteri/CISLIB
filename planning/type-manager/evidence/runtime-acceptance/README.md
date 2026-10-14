# Expanded runtime acceptance checkpoint

14 October 2026. Development evidence following package checkpoint `7fdbea45`.
No production Type Manager source changes belong to this checkpoint.

## Unmodified candidate

Core SHA-256: `d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`.
Windows and native Ubuntu 24.04 use SDK 10.0.401 and identical managed outputs.

- `unit-windows/` and `unit-linux/`: 77 native cases each, 68 unit plus 9 runtime
  architecture, with no failures or skips. Linux runs the Windows test assembly.
- `process-windows/` and `process-linux/`: 15 runtime child-process scenarios and
  two generator rejections each. Reports compare Core, fixture, helper and verifier
  hashes. Build logs show the actual producer commands' results; producer source
  uses `--no-incremental` for the real fixture projects, including Host -> Core.
- New cases cover both manifest version-conflict orders, a deployed and bindable
  unselected assembly, later-load fixed membership, generic equivalence, concurrent
  awaiter identity, post-success cancellation and collection element replacement.
  The array negative control demonstrates that rejecting Clear alone is insufficient.

The awaiter tests establish the observable task contract; they do not independently
prove Type Manager's internal scheduling. Exclusive manifest reopen checks cover
the manifest handle, not every possible operating-system resource.

## Test-only instrumentation

`instrumentation-windows/` and `instrumentation-linux/` contain five passing checks
each. An isolated source copy wraps the actual enumeration call and invokes a
callback immediately before the final cancellation check. It records each of the
three selected assemblies once per preparation, including a second preparation
that reuses all three, and no added enumeration during 32 concurrent query batches.
Cancellation at final publication returns a canceled task without a manager;
already loaded assemblies remain loaded.

Three deliberate mutants must fail for their specific diagnostic: enumerate twice,
enumerate during queries, and omit final cancellation. `provenance-controls/`
contains two separate pre-build rejection controls, for changed Core bytes and a
changed source record. These are targeted controls, not full mutation qualification.

The source relationship is deliberately limited. A trusted completed build-and-run
producer report records the unmodified Core hash, every Core C# file and exactly
five project/config files: Core.csproj, global.json, Directory.Build.props,
Directory.Packages.props and NuGet.config. The instrumentation verifier checks this
subset against its inputs, checks copied bytes, and requires the current producer
verifier hash. Imported targets, other non-C# files and the complete transitive
MSBuild input closure are not attested. Instrumentation intentionally changes the
source and project; its counters do not directly observe the unmodified DLL.
Reports protect against accidental mix-ups, not maliciously edited evidence.

Linux uses the same `/mnt/c` workspace bytes, not an independently normalized Git
checkout. Both verifier and full replay binary maps match their Windows producer.
`inputs/` retains exact verifier/test/template bytes used by these runs because
future Git checkouts can normalize source line endings. `checkpoint.json` records
the cross-report consistency checks. No complete generated source copy containing
unrelated logging code is retained here.

## Review, reproduction and limitations

`reviews/` preserves read-only review results. Review led to element-replacement
assertions plus the plain-array negative control, producer/source hash checks,
forced recompilation and narrower provenance wording. The review's suggested
per-report-list branch counters and extra provenance negative controls remain
optional improvements; successful process fixtures require loaded/reused entries.

From the repository root, use an isolated snapshot for the producer to avoid
concurrent build outputs:

```powershell
python implementations/csharp/tests/fixtures/TypeManager/verify-runtime-processes.py --repo-root <snapshot> --output-root <empty-producer-root>
pwsh -File implementations/csharp/eng/verify-type-manager-runtime-tests.ps1 -CoreAssembly <producer-root>/Host/bin/Release/net10.0/CIS.Core.dll -OutputRoot <empty-unit-root>
python implementations/csharp/eng/verify-type-manager-instrumentation.py --repo-root . --candidate-core <producer-core> --candidate-report <producer-root>/results.json --output-root <empty-instrumentation-root>
python implementations/csharp/tests/fixtures/TypeManager/Instrumentation/verify-provenance-controls.py --repo-root . --candidate-core <producer-core> --candidate-report <producer-root>/results.json --output-root <empty-control-root>
```

On Ubuntu, invoke the process verifier with `--replay-root <producer-root>` and
`--expect-core-sha256 <candidate-hash>`; invoke instrumentation with
`--replay-root <instrumentation-root>`. Use native `dotnet vstest` on the identical
Windows unit output and retain its TRX. Every invocation needs an empty output root.
The pinned SDK and approved dependency restore must be available.

The first forced producer run passed all runtime scenarios but the sandbox denied
MSBuild task-host startup; the successful rerun used normal host permissions.
An instrumentation restore was likewise blocked from NuGet audit data, then passed
with network access. Those failed reports remain under `environment-failures/` and
are not counted as passes. One Claude invocation returned a tool-call parsing error.
A full-script retry was canceled after a prolonged wait with no result; a narrower
review of only the final delta follows the completed full harness review.
That final delta review found no concrete bug within the stated scope. Any producer
verifier edit, including whitespace, requires a new producer run before replay.

This candidate includes a coordinated logging working-tree snapshot. Native reports
and source-subset hashes are retained, but this is not a clean release candidate,
full portable binary bundle, complete six-family qualification or release approval.
Prior package and compatibility results have their own candidate hashes and must
not be silently merged with this checkpoint.
