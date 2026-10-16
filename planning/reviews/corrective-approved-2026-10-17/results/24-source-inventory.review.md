# CISLIB source-inventory migration review (a4df38e0 plus task changes)

This was a static, read-only review of the supplied text. I executed nothing, used no tools and changed no files. Line numbers count from the first line of each supplied file.

## Findings

### F1 (Medium–High): A real Git root silently falls back to copy enumeration when its path is spelled differently

**Evidence:** `SourceInventory.cs` L38–55.
- The Git branch runs only when `rev-parse --show-toplevel` succeeds and its output equals the normalized root string (L40–42).
- The `else` branch throws only when `repository.ExitCode != 0` (L50).
- When Git succeeds but the strings differ, the code falls through to `EnumerateCopy` (L54), even if the root itself contains `.git`.

**When this happens:** Git returns a resolved top level. Each of these makes the strings differ even though the root is the repository root:
- a symlinked checkout path on Ubuntu;
- a junction or `subst` drive on Windows;
- an 8.3 short temp path such as `C:\Users\FIRSTN~1\...`;
- a `\\?\` prefix.

**Impact:**
- The inventory silently switches contract. It now includes gitignored files outside the five excluded directory names, and tracked/untracked status no longer applies.
- Nothing signals the switch to the caller.
- The documentation calls copy mode a "directory outside its own Git root" mode and says it "does not meet the full profile's Git HEAD prerequisite". Here it is applied to the Git root itself.

**Recommended fix:**
- Decide Git-root status without comparing path strings, for example `git -C <root> rev-parse --show-prefix`, which outputs an empty line exactly when the cwd is the top level.
- Alternatively, throw whenever `<root>/.git` exists but the Git branch was not taken.
- Add a test that passes a differently spelled root (a symlink on Linux, or a junction on Windows).

### F2 (Medium): The bridge passes `$Root` unresolved to a child process

**Evidence:** `Verification.psm1` L10 runs `& dotnet $checker source-inventory $Root`. The checker then calls `Path.GetFullPath(root)` (`SourceInventory.cs` L36), which resolves against the child's process working directory.

**Impact:**
- PowerShell's `$PWD` is not the process's current directory.
- A relative path resolves against the wrong directory.
- A PSDrive path such as `~`, `TestDrive:` or a custom drive fails or misresolves.
- The bridge test only passes an absolute path (`Join-Path $PSScriptRoot 'input'`).

**Recommended fix:** In the bridge, resolve the path first, for example `$Root = $PSCmdlet.GetUnresolvedProviderPathFromPSPath($Root)` (requires `[CmdletBinding()]`) or `(Resolve-Path -LiteralPath $Root).ProviderPath`.

**Limit:** whether any current caller passes a relative path cannot be confirmed, because `verify.ps1` and `replay.ps1` were not supplied.

### F3 (Medium–Low): Git discovery inherits ambient Git environment variables

**Evidence:** `SourceInventory.cs` L83–84 calls `ProcessRunner.RunAsync("git", ["-C", root, ...])` with no environment overrides.

**Impact:**
- If `GIT_DIR` is set without `GIT_WORK_TREE` (for example inside a Git hook or some CI wrappers), `--show-toplevel` reports the cwd. The string check at L40 then passes.
- `ls-files` then reads another repository's index against this root, producing a mixed inventory with no error.
- `GIT_INDEX_FILE` and `GIT_WORK_TREE` have similar effects.
- A user-level `core.excludesFile` also makes `--exclude-standard` machine-dependent.
- This conflicts with the documented guarantee that "repository discovery cannot borrow the enclosing checkout's Git identity" in a replay context.

**Recommended fix:** Clear at least `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_COMMON_DIR` and `GIT_OBJECT_DIRECTORY` for these child processes, using the per-child environment overrides that ProcessRunner is documented to support.

**Limit:** this is probably inherited from the pre-migration implementation, which was not supplied. Treat it as hardening rather than a regression unless the baseline differed.

### F4 (Medium–Low): The native command test exercises a prebuilt binary that may be stale

**Evidence:**
- `SourceInventoryTests.NativeCommandWritesJsonAndRejectsMissingRoots` loads `eng/CIS.DependencyChecker/bin/<configuration>/net10.0/CIS.DependencyChecker.dll` and fails if it is absent.
- `CIS.Core.ArchitectureTests.csproj` only compiles linked source files (`<Compile Include=...>`). It has no project reference to the checker.

**Impact:**
- A focused `dotnet test` on a fresh checkout fails.
- After source edits without a rebuild, the test validates an old binary. It then proves the CLI behaviour of a previous build, not of the current `SourceInventory.cs`/`Program.cs`.

**Recommended fix (pick one):**
- Add `<ProjectReference Include="../../eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj" ReferenceOutputAssembly="false" />`, so the test build always builds the checker in the same configuration.
- Have the test use the tool that the bridge test bootstraps.

### F5 (Low): Checker failure detail is lost from the stage record

**Evidence:** `Verification.psm1` L11 throws only `'Unable to capture source inventory.'`. The checker's stderr (for example "Source root does not exist…" or the Git error text) goes to the host and is not captured.

**Impact:** `Invoke-VerificationStage` records `$_.Exception.Message` as the stage `detail`, so retained evidence carries no cause.

**Recommended fix:** Capture stderr separately, without merging it into the JSON stream, and include it in the thrown message.

### F6 (Low): Git-branch entries that are not regular files fail with a generic error or hash the wrong content

**Evidence:** `SourceInventory.cs` L44–46 and L58–61 pass every listed path to `File.OpenRead`.

**Cases:**
- **Submodule gitlinks:** `--cached` lists them as directories, so `File.OpenRead` fails.
- **Untracked nested repositories:** `--others` lists them as `dir/`, which also fails.
- **Skip-worktree/sparse entries:** `--deleted` does not report them, so a missing file fails.
- **Tracked symlinks:** the target's bytes are hashed, not the link. A dangling or directory link fails.

**Impact:** all failures are fail-closed, which is acceptable. However, they surface as generic IO messages, and none of these cases is documented or tested.

**Recommended action:** Either document these as unsupported with an explicit error message, or confirm the repository has none of them.

### F7 (Low): Copy mode follows file links

**Evidence:** `EnumerateCopy` (L66–74) skips only directory reparse points (L71). `Directory.EnumerateFiles` returns symlinked files, and `File.OpenRead` follows them.

**Impact:**
- Content outside the input root can enter the inventory under an in-root path. This conflicts with the replay rule that paths must stay inside the input root.
- No test covers reparse/symlink behaviour, although the documentation claims "not traversing directory links".
- Minor: the excluded directory names are matched case-insensitively on Linux too (L15). The documentation does not state this.

### F8 (Low): Inaccurate provenance on the new case-inventory group

**Evidence:** `ARCH-VERIFICATION-SOURCE-INVENTORY` has `"source":"Passive correction regression coverage; no planned release requirement replaced."`. This text is copied from the unrelated `UT-ENV-*` entries.

**Impact:** The `source` field does not describe the source-inventory migration. The entry also has no `title` and no `requirements`; other entries without these fields exist.

**Recommended fix:** Use migration-specific source text.

The six bindings themselves match the test method names and namespace (`CIS.Tests.ArchitectureTests.SourceInventoryTests.*`) exactly. `method:null` is present as stated.

### F9 (Low): Test coverage is narrower than the documentation claims

The documentation says SourceInventoryTests cover "both branches … command JSON/error output and a fresh tool bootstrap". The following behaviours are not covered:
- the `rev-parse`-fails-with-`.git`-present throw at L50–53 (the corrupt-index test fails later, in `ls-files`);
- the path-spelling mismatch from F1;
- reparse-point non-traversal;
- the usage path that returns exit 2;
- non-ASCII paths through the CLI or bridge (only `CaptureAsync` is tested directly);
- a Git-root capture through the bridge;
- the bridge's failure propagation when the checker exits nonzero.

## Limitations (not findings)

- **Missing files:** these were not supplied:
  - `ProcessRunner` and `TemporaryDirectory`;
  - `CIS.DependencyChecker.csproj` and `Directory.Build.props`;
  - `.gitignore`, `verify.ps1`, `replay.ps1` and `check-release.ps1`;
  - `families.json`, the evidence validator schema;
  - the a4df38e0 `Get-SourceInventory` implementation.

  As a result:
  - "Preserved Git/index and copy behavior" was compared only against the retired fixture script and the documentation. The retired assertions (tracked plus untracked, deletion excluded, index unchanged, current digests) are all covered, and more strongly: raw index bytes are compared instead of `ls-files --stage` text.
  - I could not confirm that `--outputEncoding` exists on `ProcessRunner.RunAsync`.
  - I could not confirm that `.artifacts`, `bin` and `obj` are gitignored. The Git branch applies no directory exclusions, so this matters.
  - I could not confirm that nothing still invokes the retired script.
  - I could not confirm that the `method:null` correction satisfies the validator.
- **Bootstrap side effects (to verify, not established):** each `Get-SourceInventory` call runs `dotnet build` with restore and writes `bin`/`obj` into the tree that holds the module. If replay's before/after checks call it, those builds may:
  - use the caller's NuGet and CLI caches rather than the fresh caches;
  - write into the caller checkout.

  The inventory excludes these outputs in copy mode, and in Git mode only if they are gitignored. The replay script is needed to judge this.
- **Linux lane:** Ubuntu behaviour (symlink attributes, case-sensitive comparison, `pwsh` on the bridge test) was reasoned about but not observed.
- **No full-profile inference:** nothing here establishes a passing Developer or Release profile. A focused SourceInventoryTests pass would not imply one either, and the separately recorded full reruns were not available to me.

## Consistent with the stated scope

- Inventory logic is now in C#. PowerShell keeps only bootstrap and invocation glue, and build output is captured away from the JSON stream.
- `Program.cs` adds a minimal dispatch.
- Null-delimited, strictly decoded Git output is used.
- Failures exit nonzero with no stdout, and the output is ASCII-escaped JSON.
- Paths are unique, use `/`, and are ordinally sorted; hashes are lowercase SHA-256.
- No changes to the library runtime or Type Manager are visible.