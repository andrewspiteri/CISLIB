# Closure review: runtime-acceptance findings

Finding (1) is closed. Finding (2) is substantially closed, but a few concrete gaps remain in what the provenance check actually proves. None of them affect production Core. They are listed below with severity.

## Finding 1: collection mutation checks. Closed.

- **Unit test negative control.** `AssertProtected<int>(new[] { 1 })` works as intended. Calling `IList<T>.Clear` on an array throws `NotSupportedException`, so the first assertion passes. The indexer write then succeeds, so `Assert.Throws` fails. This proves the replacement check is the part that catches fixed-size mutable arrays.
- **Empty collections.** Skipping the replacement check when a collection is empty is sound. A mutable empty `List<T>` still fails the `Clear` check, and an empty array has no elements to mutate.
- **Process host.** `CheckProtectedReport` runs for every successful mode before any mode-specific branch. Across the three successful modes, every report list is nonempty:
  - Selected and Reused are populated by the host and Core in all of them.
  - Loaded is populated in `dynamic-name-collision`, which asserts this through `Contains`, and in `forwarding-and-collisions`.

**Minor, non-blocking.** Nothing asserts that the replacement branch actually ran for each list. If a regression left a list empty, the check on that list would silently weaken. In the unit lane, `LoadedAssemblyIdentities` is always empty, so its replacement check only ever runs in the process lane. A cheap fix: have the host emit the three counts in its JSON, and have the Python verifier assert that each list was nonzero in at least one case. Also, `CheckProtectedReport` has no negative control of its own; it relies on mirroring the unit test helper.

## Finding 2: provenance. Residual issues.

1. **The hashed inputs are not all of Core's build inputs.** The hashed set is `*.cs` files plus five fixed files. It leaves out:
   - non-`.cs` items, such as resources and `.editorconfig`
   - `Directory.Build.targets`, if one exists
   - files imported by `Directory.Build.props`
   - any `Import` elements or `ProjectReference`s in `CIS.Core.csproj`, such as analyzer or generator projects

   The instrumentation script removes exactly the `Import`, `ProjectReference` and `None` elements, which suggests at least some of these exist. Matching hash maps therefore do not prove the two builds used the same compilation inputs. A changed import, for example one setting `DefineConstants` or `LangVersion`, would not show up. Either hash the imports and referenced projects, or state that the hash maps cover only C# sources plus the five listed files.

2. **The Core DLL could come from a stale incremental build.** The producer builds Core through the Host `ProjectReference`, using the repository's shared `src/CIS.Core/obj`. MSBuild detects content changes by timestamp. A file restored with an older modification time, for example by `cp -p` or an archive extract, could leave a DLL that was not compiled from the hashed sources. The check that inputs are unchanged across the build does not catch this. Fix: use `--no-incremental`, or give the producer its own intermediate directory under the output root (via `BaseIntermediateOutputPath`).

3. **Linux replay will likely fail if line endings differ.** In replay mode, both verifiers still hash the working tree and compare against the Windows report:
   - the instrumentation verifier recomputes `source_inputs` and compares them with the producer report, and also checks the hashes of the factory source, the verifier and the templates
   - the runtime verifier compares its own `verifierSha256`

   A Linux checkout with LF line endings against a Windows checkout with CRLF (`text=auto` / `autocrlf`) will fail with "Candidate source provenance differs" or "Replay verifier differs". Before reading the Linux result, confirm that both checkouts are byte-identical, for example by adding `eol=lf` in `.gitattributes` or replaying from a copied tree.

4. **The producer report is trusted without verification.** The instrumentation verifier checks only `complete` and `coreSha256`. It does not check:
   - the producer's `mode`, so a replay report is accepted as a producer report
   - its `verifierSha256` against the current `verify-runtime-processes.py`
   - that all expected modes appear in `results`

   A hand-edited but internally consistent JSON would pass. This is acceptable given the stated scope of protecting against accidents, not tampering. It should still be stated, and checking the producer's mode and verifier hash costs little.

5. **Some rejection paths have no negative control.** Neither `coreProjectInputs` mismatch nor replay `binaryFiles`/`fixtureFiles` mismatch has a control. The two existing controls do correctly show rejection before any `binaries` directory exists.

## Verdict

- **Finding 1:** PASS.
- **Finding 2:** PASS with residuals for the stated scope (source instrumentation, not a release gate).
  - Items 1 and 2 limit how strongly the hash maps can be claimed to bind Core's bytes to its source. Fix them, or narrow the wording of the claim.
  - Item 3 should be resolved before the Linux replay result is taken as evidence.
