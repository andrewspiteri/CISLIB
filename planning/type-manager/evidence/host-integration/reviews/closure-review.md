# Type Manager follow-up review

No sample correctness bug found. The M1–M3 fixes look sound as described. The remaining findings are mostly about evidence strength for the new checker rule.

## Medium

**F1. The "never shipping" role gate is untested.**
The claim that additional frameworks apply only to `role=example` has no control behind it. If you delete `$project.role -eq 'example' -and` from the checker, all 8 cases still pass:
- `reject-framework-CIS.Core` adds ASP.NET to Core but never adds `additionalImplicitFrameworkReferences` to Core's policy entry, so the gate is never reached.

Fixes:
- Add a control that puts the key (with `Microsoft.AspNetCore.App`) on Core's policy entry and adds the reference. Expect `Forbidden framework reference`.
- Ideally add the same control for the `test-support` ExampleLate entry. The count becomes 9–10.
- Consider having the checker throw when the key appears on any non-`example` role. Today it is silently ignored. That fails closed, but it hides a policy mistake.

**F2. The new inventory-equality check applies to every registered project, but only example projects were exercised.**
`FrameworkReference.Count -ne $approvedFrameworks.Count` is a tightening for all roles, not only examples. So "existing dependency policy unchanged" holds for the data, not for enforcement.
- Run the full checker across all registered projects and report the result.
- Projects at risk include the build-tool project and any non-`net10.0` or multi-targeted project with zero or duplicate implicit `FrameworkReference` items.
- Also dedupe `$approvedFrameworks`. An example entry that lists `Microsoft.NETCore.App` would otherwise cause a spurious count mismatch.

**F3. The boundary verifier has no input provenance.**
It accepts any pre-existing `$LanguageRoot` and records only the checker hash. A stale isolated copy would produce a "passed" report for code that is no longer current.
- Either create the copy itself, or record SHA-256 hashes for:
  - the five checked `.csproj` files plus Core's
  - the copied policy files
  - `Directory.Build.props/targets`, `Directory.Packages.props` and `global.json`
- That would match the attestation style of the example verifier.
- The `$LanguageRoot -eq $canonical` guard misses subdirectories, junctions and trailing-separator variants. Prefer a containment check on resolved paths.

## Low

**F4. Scoped vs. transient is not distinguished.**
Distinct `consumerId` values across two requests, and distinct consumers across two in-process scopes, would also pass if `CatalogConsumer` were registered as transient. To actually prove scoped lifetime:
- In `/catalog`, resolve the consumer a second time from `HttpContext.RequestServices` and return `sameConsumerWithinRequest`.
- Do the same within one in-process scope.

**F5. Ambient environment scrubbing is prefix-based, but MSBuild imports every environment variable as a property.**
Variables such as `Platform`, `Configuration`, `OutDir`, `BaseIntermediateOutputPath`, or any `Cis*` property read under a `'$(X)' == ''` condition can still leak in. `Platform` is a known OEM machine hazard.
- Prefer an allowlist: `PATH`, `SystemRoot`, `TEMP`/`TMP`, `HOME`/`USERPROFILE`, `DOTNET_ROOT*`, plus the declared settings.

**F6. NuGet user and machine configs still merge in.**
`<clear/>` handles `packageSources`, but not `fallbackPackageFolders` from `%APPDATA%\NuGet` or machine config. `DOTNET_CLI_HOME` does not relocate that on Windows.
- Add `<fallbackPackageFolders><clear/></fallbackPackageFolders>`.
- Assert that `obj/project.assets.json` `packageFolders` is exactly `root/packages` and that its sources are exactly the feed.

**F7. The expected-error match is generic.**
`'Forbidden framework reference'` would also match an unrelated framework rejection. Match `"from $project: Microsoft.AspNetCore.App"` instead.

## Nits

- **Package transform:** `endswith('CIS.Core.csproj')` is weaker than "removes one specific Core reference". Match the exact relative `Include`. Also, ElementTree drops comments and the XML declaration, so "replaces only" overstates it slightly. The recorded hashes do cover the actual result.
- **`http_check` log polling:** it reads with strict UTF-8 while the child is still writing. A split multibyte sequence would raise. Use `errors='replace'`.
- **Hard-coded flags in `--verify` JSON:** `rootAndScopesShareManager`/`queryIdentityStable = true` are constants that are reachable only after the in-process asserts. The README could say so, so readers don't take them as independently measured.
- **Check counts:** the 66 total (64 layout/fault + 2 HTTP) and the docs' relative links are correct.
