# Finite native child-process mutation proof

This nonshipping diagnostic uses real Stryker 4.16.0 mutators, compiler, VSTest
runner and reports. It deliberately generates four conditional mutations or three
registration-guard mutations before compilation, avoiding unrelated block/statement
instrumentation. It does not satisfy the complete automatic mutation gate.

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
actual guard mutants. Conditional proof requires recorded `additional-timeout`
10000; registration uses 60000. The recorded registration experiment selects one
existing real HTTP case plus the scheduling control, avoiding cumulative native
session deadlines while preserving all application/child timeout checks.

The producer verifies Stryker's real `.stryker-unchanged` backup against the freshly
built source assembly. It retains instrumented/original/restored hashes. Normal
native restoration must pass; exceptional use of the public restoration API keeps
the run failed. Always run the separate child audit and require its success too.
Native kills alone do not establish child origin, terminal settlement or release
qualification. Retain failed attempts and run a fresh unmutated build/test baseline
afterward. See the [current owner-aware evidence](../../../../../../planning/healthcheck/evidence/HC12/release-next/matrix/child-owner.md) and [frozen earlier proof](../../../../../../planning/healthcheck/evidence/HC12/release-next/matrix/child-origin.md).
