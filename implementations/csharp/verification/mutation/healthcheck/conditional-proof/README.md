# Finite native child-process mutation proof

This nonshipping diagnostic uses real Stryker 4.16.0 mutators, compiler, VSTest
runner and reports. Its finite modes generate four conditional mutations, three
registration-guard mutations, one missing-pipe Boolean mutation, or one Windows
descriptor pin-release statement mutation before compilation. Each mode avoids
unrelated instrumentation and does not satisfy the complete automatic mutation gate.

Copy `Program.cs` and `Launcher.csproj.template` into a new artifact directory,
renaming the template to `Launcher.csproj`. Build from a working directory governed
by the repository's `global.json` (SDK 10.0.401), with `StrykerToolDirectory` set to
the existing exact 4.16.0 `tools/net8.0/any` closure. No package restore dependency
or solution project is added. Execute on .NET 10.0.12 with roll-forward disabled.

Use an isolated, frozen Healthcheck source copy. Prepare an actual child session
with `healthcheck-mutation-children prepare`, then export its session.json path as
`CIS_HEALTHCHECK_MUTATION_SESSION`. Set `CisMutationTesting=true`. Run the launcher
with the **working directory at that copy's `src/CIS.Core`**; Stryker resolves the
configuration's relative test projects from the process working directory.

Use a short Windows evidence root such as `.artifacts/hcc/<run>`. Owner-bound
stage paths include a PID, GUID and full Core hash. The executed deep-root Windows
attempt rejected the runtime entry-assembly path; the shorter-root retry passed
with unchanged identity checks. Keep the staged executable and runtime filenames
below the Windows legacy path boundary rather than relaxing origin comparison.

Arguments are absolute language root, configuration JSON, a new output directory
and pinned Stryker tool directory. Optional `--registration` selects the three
actual guard mutants; `--probe-null` selects the actual missing-pipe false-to-true
return mutation. Conditional proof requires recorded `additional-timeout`
10000; registration and probe-null use 60000. The recorded registration experiment selects one
existing real HTTP case plus the scheduling control, avoiding cumulative native
session deadlines while preserving all application/child timeout checks.

`--pin-release` applies only on Windows and permits only
`Healthcheck/Pipes/HealthPipeEndpoint.cs` in `mutate`. The adapter delegates to the
pinned tool's real `StatementMutator` and requires exactly `pinned.Free();` to `;`
in `CreateWindows`'s `finally` block. Its recorded `additional-timeout` is 10000.
Select the existing integration class's
`WindowsPipeEndpointDescriptorsReleasePinsAfterActualSettlement` case plus the
mutation scheduling control. That case invokes `pipe-pin-release` in the real
HTTP fixture executable, before ordinary HTTP initialization. In its isolated
process, eight public one-slot servers warm up native creation and settlement;
32 more unique servers each start, stop and dispose. Blocking full collections
and pending-finalizer settlement precede both public
[`PinnedObjectsCount`](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.pinnedobjectscount?view=net-10.0)
observations. The integration assertion permits no retained growth and adds no
tolerance. The API reports pins observed by that GC, so this measurement requires
repeated unmutated Windows baselines and the actual exact-mutant control before
it establishes a valid detector. No such execution is implied by adding this
mode. The real child-origin audit, selector/runtime binding and existing 15-second
child deadline remain required. Unix explicitly reports this native Windows
resource case as inapplicable; it supplies no Unix mutation evidence.

The producer verifies Stryker's real `.stryker-unchanged` backup against the freshly
built source assembly. It retains instrumented/original/restored hashes. Normal
native restoration must pass; exceptional use of the public restoration API keeps
the run failed. Always run the separate child audit and require its success too.
Native kills alone do not establish child origin, terminal settlement or release
qualification. Retain failed attempts and run a fresh unmutated build/test baseline
afterward. See the [current owner-aware evidence](../../../../../../planning/healthcheck/evidence/HC12/release-next/matrix/child-owner.md) and [frozen earlier proof](../../../../../../planning/healthcheck/evidence/HC12/release-next/matrix/child-origin.md).
