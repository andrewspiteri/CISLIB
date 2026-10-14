# Delta review: boundary verifier safeguards

**Verdict: no blocking defects in this delta.** One minor non-blocking gap, described below.

## Checked and fine

- **`#Requires -Version 7.2` and `$PSNativeCommandUseErrorActionPreference = $false`.** Setting the preference variable is harmless on 7.2 and 7.3, where it is inert. On 7.4+ it guarantees that `& pwsh` returning nonzero does not throw before `$LASTEXITCODE` is read. The 7.2 floor also matches the `*>` redirection of native stderr no longer tripping `Stop`.
- **Empty-output check.** `Get-ChildItem -Force` now catches hidden or system entries. If `OutputRoot` is an existing file, it counts as 1 and is rejected, which is fail-closed.
- **Descendant reparse walk.**
  - It runs before both `New-Item ... eng` and the policy `Copy-Item`, which is the right ordering.
  - It covers every file that is later copied over or mutated: the three policy JSONs, `CIS.Core.csproj` and `Console.csproj`.
  - It terminates correctly. That includes a drive-root `LanguageRoot`: `TrimEndingDirectorySeparator` keeps `C:\`, and `Split-Path` reaches it.
  - If separator normalization ever kept the loop from matching `$LanguageRoot`, `Split-Path ''` would throw under `Stop`. So it fails closed, never loops forever.
  - Ancestors above the root are already covered by the earlier `DirectoryInfo` walk.
- **Driver `if (-not $?) { exit 1 }`.** This now propagates a checker's nonzero `exit` or non-terminating errors. Every misfire direction fails closed:
  - A spurious failure on a positive case throws "Boundary check failed".
  - A spurious failure on a negative control still needs the exact expected message in the log.
  - A spurious failure on the mutant run throws "masked".
- **Hardlinks.** These are excluded only by the documented precondition. That is consistent with the stated scope. The in-place writes (`Copy-Item`, `Set-Content`, `$xml.Save`, `WriteAllBytes`) would propagate through a hardlink, as acknowledged.

## Minor, non-blocking

**Dangling reparse points slip past `Test-Path`.**
- `Test-Path` follows the link and returns `$false` for a broken symlink or junction. So a dangling link at, for example, `eng/project-boundaries.json` would skip the reparse check.
- The subsequent `Copy-Item` would then write through it to the link target.
- The same `Test-Path` gate decides whether `Directory.Build.targets` gets included.

This is unlikely in a verifier-created copy, but it is cheap to close:

```powershell
$item = Get-Item -LiteralPath $path -Force -ErrorAction Ignore
if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Isolated input uses a reparse point: $path" }
```

`Get-Item -Force` returns the link item itself even when its target is missing.

## Status note

The SDK workload-manifest mismatch occurred outside this script, and the rerun is still in progress. I can't treat the 11-check result as re-confirmed for this delta until that rerun reports `Type Manager example boundary checks passed: 11.` with `complete = true` in `results.json`.
