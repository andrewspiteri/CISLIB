# Native mutation inventory diagnostic

This standalone nonshipping launcher uses the existing pinned Stryker4.16.0
public runner and delegates generation, all built-in mutators, compilation,
compiler rollback, deployment and native filters to its real
`CsharpMutationProcess`. It records an inventory, not a mutation score or kill.
No maintained project, shipping code, test closure or dependency version changes.

Materialize `Launcher.csproj.template` as a private project outside the maintained
source tree, copying this Program.cs unchanged. Build only with the existing
`StrykerToolDirectory` DLL closure and pinned SDK/runtime. Run with five arguments:

```text
<frozen-language-root> <actual-config.json> <empty-output-root> <pinned-Stryker-tool-directory> <maintained-inventory-Program.cs>
```

Use the coordinator's prepared `CIS_HEALTHCHECK_MUTATION_SESSION` for that exact
source/config and `CisMutationTesting=true`. The actual .NET10.0.12 host/CoreLib,
Stryker.Core4.16.0, source-input manifest, supplied tool DLL closure and launcher
source/binary are hashed. Config keeps the full actual test projects/filter,
Complete mutation level, coverage off, concurrency2, disabled bail, native budget,
reporters and90% thresholds. The launcher accepts explicit Healthcheck source
paths only. It does not shorten the selected native baseline. Initial baseline
tests still run; mutant test execution is intentionally suppressed.
Launch from the frozen `src/CIS.Core` directory; the launcher verifies that working
directory and the actual source SDK10.0.401/disabled-roll-forward configuration.

`all-generated.json` captures every generated Core definition immediately after
the delegated native Mutate call, before native scope/filter omission. It retains
full per-file source and source/file hashes, actual global IDs, mutation type and
display name, byte-independent character span/line coordinates, original slice,
replacement and actual post-compilation status/reason. Original node text must
equal its exact source substring.

The public Mutate method performs compilation internally. This wrapper has no
public precompile callback; `preCompileStatusObserved=false` records that limit
explicitly. No Pending value or compiler outcome is fabricated as an earlier
observation. The native debug generation/compilation log is retained separately.

`inventory.json` captures selected sources after delegated native FilterMutants,
plus actual postcompile/postfilter states for every global ID and counts before
suppression. Only then does the wrapper mark Pending mutants Ignored with the
explicit inventory-only reason. Existing CompileError, Ignored and NoCoverage
statuses/reasons remain native and must remain unchanged. Successful inventory
requires no Pending/Killed/Survived/Timeout outcome, no logged mutant-test session
and zero actual native kill outcomes. Standard Stryker reports produced afterward
reflect intentional pending suppression; they are not qualification reports.

Real `.stryker-unchanged` backups must equal the freshly built normal Core target
and any existing live injection target before mutation. Source and backup hashes
are retained and rechecked. Since an intentionally empty pending queue can skip
the runner's ordinary restoration, inventory mode now has an explicit cleanup
phase: it observes whether automatic restoration already occurred, invokes the
actual public RestoreOriginalAssembly API, then verifies every live target,
retained backup and source Core exactly. Wrong/missing backups or failed exact
cleanup still fail. All pinned source/config/tool inputs must remain unchanged.

The v2 receipt separates `automaticRestorationObserved`,
`explicitCleanupRequested`, `explicitCleanupCompleted` (the public call returned)
and `exactRestorationVerified`. A successful inventory-only diagnostic may report
automatic=false, explicit=true and exact=true; it never calls that normal/native
restoration. This intentional cleanup is not a mutation/test/child/gate pass.
The original i1 v1 attempt failed its then-required automatic restoration and is
preserved as failed, even though its fallback cleaned up exactly. Its real
generation data may guide diagnosis after independent identity checks, without
retroactively making i1 successful. `results.json` retains exact inventory hashes,
observed test messages and failures alongside these explicit cleanup facts.

Before associating another run's provisional log IDs, compare complete production
source hashes, actual pinned tool/runtime, unmodified generation/filter options,
global count/order and the exact ID/type/span/original/replacement. A matching
total alone is insufficient. Out-of-scope inventory statuses are not native test
outcomes, and live success hints are not final Survived statuses. This diagnostic
creates no kill, equivalence waiver, release claim or child propagation proof.
