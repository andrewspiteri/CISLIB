# Final closure review: boundary verifier, Web sample, verifier excerpts, checker diff

**Verdict:** I found no blocking defects. The F1–F7 changes do what they claim. Five low-severity points remain. Two are real safety or diagnostic gaps, and three are hardening.

## What holds up

- **Checker diff.** The extra allowlist only applies when `role -eq 'example'` and the key is present. The `IsImplicitlyDefined -ne 'true'` guard still applies to every framework, including allowlisted ones, so an explicit `<FrameworkReference Include="Microsoft.AspNetCore.App"/>` in Web is still rejected. Baseline enforcement for other projects is unchanged.
- **Control design.** Together, `reject-unregistered-web-framework` (the allowlist matters) and `reject-policy-*` with `IsImplicitlyDefined=true` (the role gate matters) separate the two guards correctly.
- **Role-gate mutant.** It is a sound differential test: same driver, same mutated inputs, and only the anchor differs. Requiring the anchor to occur exactly once prevents a silent no-op replacement.
- **Hidden policy-location dependency.** If the checker read policy from `$PSScriptRoot` instead of `-LanguageRoot`, the unregistered-Web control would fail and the mutant (running from `$OutputRoot`) would fail. Either failure is loud, so no such dependency can be hiding.
- **Failure handling.** `Where-Object` returning no entry or duplicate entries throws under StrictMode, so it cannot pass silently. Restoration happens in `finally`, then all hashes are re-checked. The count of 11 matches 5 baseline + 2 + 1 + 2 + 1. On any throw, `complete` stays false.
- **Web sample and HTTP check.** Within-scope identity, cross-scope distinction, a fresh consumer GUID per request, and manager and query identity are all asserted in-process and over HTTP. The no-proxy opener and `errors='replace'` polling are correct.
- **Verifier isolation.** The environment allowlist is reasonable. `packageFolders` includes fallback folders, so checking it is enough. The transform requiring exactly `(1, 1)` removals fails closed on any path-spelling drift.

## Findings

1. **Low: links below the isolated root can still reach canonical files.**
   - The reparse check only walks ancestors of `$LanguageRoot`.
   - A symlink or junction for a descendant (e.g. `src`, `src/CIS.Core`, `eng`), or a hardlinked `.csproj`/`.json`, could point back into `implementations/csharp`.
   - In that case `Copy-Item`, `$xml.Save`, and the policy rewrites would mutate canonical files until `finally` restores them.
   - This is transient, but it is exactly the case the canonical-directory refusal is meant to prevent.
   - Fix: for each mutated or copied path, walk from the file up to `$LanguageRoot` and reject any `ReparsePoint`. Hardlinks are not visible through attributes, so either check the link count or document that hardlinks are out of scope.

2. **Low: the policy key on non-example roles is silently ignored.**
   - `additionalImplicitFrameworkReferences` on Core or test-support is inert. The control proves it cannot widen anything.
   - However, a misleading allowlist entry in `project-boundaries.json` still passes the checker.
   - Failing closed (rejecting the key when `role -ne 'example'`) would be cleaner.
   - If you adopt this, the `reject-policy-*` expected message and the mutant anchor would need to change. Treat it as a follow-up, not a reason to reopen this closure.

3. **Low (latent): the driver may not propagate an `exit N` from the checker.**
   - Per the `pwsh -File` docs, the driver's process exit code is 0 on normal termination, even if `& $Checker` ran `exit 1`.
   - The rejection controls observe non-zero exits today, so the checker currently signals failure by throwing, and nothing is masked.
   - A future change to `exit 1` in the checker would make the baseline checks pass falsely, while the rejection controls would fail loudly.
   - Fix: `& $Checker ...; if (-not $?) { exit 1 }` inside the `try`.

4. **Low: spurious failure under some PowerShell versions or preferences.**
   - `#Requires -Version 7` admits 7.0 and 7.1. There, `*>` redirection of native stderr under `$ErrorActionPreference='Stop'` throws, which would break every rejection control.
   - Similarly, an inherited `$PSNativeCommandUseErrorActionPreference = $true` turns the child's expected non-zero exit into a throw before `$LASTEXITCODE` is read.
   - Both fail closed, but they produce false failures.
   - Fix: use `#Requires -Version 7.2` and set `$PSNativeCommandUseErrorActionPreference = $false` locally.

5. **Nit: the empty-output check ignores hidden entries.**
   - `Get-ChildItem -LiteralPath $OutputRoot` without `-Force` skips hidden entries.
   - The Python verifier's `iterdir()` includes them, so the two "empty output" rules are inconsistent.

## Not verifiable from the excerpts

- The excerpt is truncated before any `http_check` call, so I can't confirm it runs for every Web layout in both lanes.
- As you noted, the native runs aren't finished, so the claims about the source and package lanes remain unconfirmed.
