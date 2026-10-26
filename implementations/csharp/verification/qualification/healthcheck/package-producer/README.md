# Local Healthcheck package producer

This standalone, nonshipping C# utility performs a fresh normal Core build and
local pack, then emits the producer JSON consumed by `healthcheck-package`.
It does not publish, run test families, qualify a release, or independently attest
the complete toolchain. It has no Core/tool/NuGet dependency or solution entry.

Copy `Program.cs` and `Launcher.csproj.template` to a new artifact directory;
rename the template to `Launcher.csproj`. Build with SDK 10.0.401 from a working
directory governed by the chosen source's `global.json`. Execute on the existing
Windows/Linux x64 .NET 10.0.12 host with roll-forward disabled:

```text
<dotnet> exec --fx-version 10.0.12 --roll-forward Disable <producer.dll> <absolute-language-root> <new-empty-output> <absolute-existing-dotnet>
```

Use an isolated, frozen source copy. Before invocation, complete the normal locked
restore of Core and its TypeManager build helper with the existing approved cache.
The utility requires those assets: it runs the existing `build -c Release
--no-restore --no-incremental` path with one MSBuild node and shared compilation
off, followed by `pack -c Release --no-build --no-restore`. It checks the actual
SDK with `--version`. The SDK/build/pack bounds are respectively 30/300/120 seconds;
forced cleanup and output drain each have a separate 10-second bound. No shell is
used for child invocation. Do not run within an active mutation session.

Before and after native work, the utility captures every Core `.cs` outside
`bin`/`obj`, Core's project and lockfile, SDK/build/package/NuGet inputs, every
TypeManager helper source/input, and `build/type-manager` inputs. It rejects a
changed path set or hash. Repository README and the supplied dotnet executable
are pinned separately. The metadata reader rejects a Stryker-namespaced fresh
Core, and the produced package's `lib/net10.0/CIS.Core.dll` must exactly equal that
fresh DLL. These checks do not replace a broader dependency/toolchain gate.

Each command retains exact argv, working directory, PID, actual exit, deadlines,
forced/timeout state, and independently hashed stdout/stderr logs. Failed attempts
remain in `results.json`; a failed run does not emit successful provenance.
On success, `package-provenance.json` contains lowercase `packageSha256`,
`coreSha256`, and `sourceInputs[{path,sha256}]`, plus explicit local/unpublished
scope, actual SDK/runtime, timestamp and separately pinned README. `baseCommit`
is null: this utility does not invoke git or invent a commit for working bytes.

Pass the produced package and provenance to the existing exact-package consumer
qualification, then pass its real built fixture/probe outputs to the separate
Linux identity verifier. Producer success alone establishes neither of those
later native scopes.
