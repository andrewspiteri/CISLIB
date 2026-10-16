# Static review: 02-values-rendering-configuration-1

This covers only the supplied assigned files and the bounded context. I executed nothing. This is not a whole-change PASS and does not qualify a release.

None of the eleven known fixes (R1/R2, F1–F3/F5/F6, T1–T4) touch the files in this bundle, so I had no fix to re-check here.

## Findings

### 1. Medium: path oracles expand Windows 8.3 aliases, while production normalizes lexically (regression from the deleted Python readers)

**Locations:**
- `ConfigurationProcessTests.cs:55`: `Path.GetFullPath((string)wanted!, directory.Path)`
- `ConfigurationPathProcessTests.cs:118`: `Path.GetFullPath((string)relative["input"]!, captured)`
- `ConfigurationValidationProcessTests.cs:40`: `Path.GetFullPath(relative, before)`

**Concrete failure:**
- On Windows, `Path.GetFullPath(path, basePath)` expands any existing `~` segment through `GetLongPathNameW`.
- `LoggingFilePath.Normalize` masks `~` specifically to prevent that expansion.
- Suppose the temporary root has an 8.3 alias, as on GitHub-hosted Windows runners where `TEMP` is `C:\Users\RUNNER~1\...`:
  - The child's captured cwd keeps `RUNNER~1`.
  - Production returns `...\RUNNER~1\...\before\<input>`.
  - The test expects `...\runneradmin\...`, so every relative-path valid row fails.
- The reverse case is worse. If production lost its masking, these oracles would agree with the expansion and hide the regression on such hosts. Only the three `ExistingAlias…` rows would still catch it.

**Parity:** the deleted `verify-configuration.py`, `verify-paths.py` and `verify-validation.py` used `ntpath`/`posixpath.normpath(join(...))`, which is lexical and matches production.

**Fix:** build the expectation lexically, as `ExistingAlias…` already does with `Path.Combine`. Alternatively, have the harness use a temporary root without `~` and leave alias coverage to the dedicated test.

**Condition:** this depends on where `TemporaryDirectory` is rooted, which was not supplied.

### 2. Low: the "validated working directory retained" assertion only checks a fixture variable

**Locations:** `ConfigurationPathProcessFixture.cs:16`, `:34` and `:38`; `ConfigurationPathProcessTests.cs:98` and `:121`.

**Problem:**
- `validatedWorkingDirectory` is set to `snapshot is null ? null : captured`, where `captured` is the fixture's own local variable.
- `capturedDirectory` is that same local.
- So `Assert.AreEqual(captured, result["validatedWorkingDirectory"])` only proves the snapshot is non-null. It does not prove production stored the base.
- The deleted Python reader had the same weakness ("Validated state did not retain the capture"). This is a truthful-claim issue, not a regression.
- Real retention evidence comes only from `absolutePathBefore`/`absolutePathAfter`.

**Fix:** project the stored base from `PreparedConfiguration` if it exposes one. Otherwise, drop or relabel the assertion.

### 3. Low: the leak check is blind to escaped characters outside the diagnostic fields

**Location:** `ConfigurationValidationProcessTests.cs:62`.

**Problem:**
- `result.ToJsonString().Contains(forbidden)` searches the re-escaped JSON. The default encoder writes non-ASCII, controls, `+`, `<`, `>`, `&` and `'` as `\uXXXX`.
- A forbidden value such as `a+b` or `invalid\0path` that leaked into `issues[].key`/`code`, `values` or `filePath` would not be found.
- Line 63 catches leaks only in the message, exception text and explanations.

**Parity:** Python searched raw stdout, which the fixture had escaped the same way, so this is not a regression.

**Fix:** search the decoded string values of all nodes.

### 4. Low: shape checks accept missing keys where Python required them

**Problem:** `JsonObject[...]` returns null for an absent key, so these assertions pass when the key is missing:
- `ConfigurationProcessTests.cs:40–41`: `filePath`, `workingDirectory`
- `ConfigurationPathProcessTests.cs:108–110`
- `ConfigurationValidationProcessTests.cs:42–43` and `:53–54`
- `EnvironmentOptionsProcessTests`: `result["values"]` and `result["metadata"]` in the invalid branch, and `metadata["Logging"]`

In Python, `value["key"] is None` raised `KeyError` for a missing key, and `"Logging" not in metadata` required absence.

Also, `ConfigurationValidationProcessTests.cs:58` silently skips a non-object `expected["values"]`, where Python's `subset` would fail.

**Fix:** assert `ContainsKey` together with the null value, and assert `!ContainsKey("Logging")`.

## Questions and limits

- **`FixtureProcessChecks.RunAsync` was not supplied.** The deleted readers required all of the following for every process:
  - exit code 0 and empty stderr;
  - `--fx-version 10.0.12 --roll-forward Disable`, with runtime description `.NET 10.0.12`;
  - the OS family;
  - `fixtureSha256`, `coreSha256` and `inputsSha256` matching the host, `CIS.Core.dll` and the inputs.

  None of the supplied C# tests check these directly. If `RunAsync` does not, source/artifact identity parity is lost. Note that `EnvironmentOptionsProcessTests` passes no inputs path and the constructor fixture emits no `inputsSha256`.
- **`FixtureProcessChecks.Cases`, `Issues`, `Subset`, `Lane` and `Family` were not supplied.** I cannot confirm parity for the `schemaVersion == 1`, `caseCount` and unique-ID checks. `Single(...)` partially covers duplicate IDs.
- **`ConfigurationValidationAdapter.Execute` and `Project` were not supplied.** If `Project` takes `workingDirectory` from its `initialDirectory` argument rather than from the snapshot, the check at `ConfigurationValidationProcessTests.cs:41` has the same weakness as finding 2.
- **The Windows `ExistingAlias…` rows hard-fail when 8.3 names are disabled** (for example, ReFS, Dev Drive or `NtfsDisable8dot3NameCreation`). Should this be reported as an explicitly unexecuted prerequisite, like the symlink case?
- **Interning is verified only for `ApplicationName`.** Production also interns `EnvironmentName`, `BuildVersion` and `ServiceVersion`. Python had the same gap.
- **Required-case bindings for these `DynamicData`/`DataRow` cases on both lanes were not supplied.**
- **Possible stale references are unverified.** Maintained docs, ledgers or entry points might still cite the deleted `verify-*.py` scripts or the `cis.*-process` summaries as current evidence. The importers of `historical/configuration_expectations.py` were all deleted, and I treat that file as frozen history.

**Missing context:**
- `FixtureProcessChecks`, `ConfigurationReadExpectations`, `TemporaryDirectory`
- `ConfigurationFixtureAdapter`, `ConfigurationValidationAdapter`
- the configuration fixture's entry point
- `LoggingOptions` and `Prepare`, `EventMetadata`
- the fixture catalogs and contract `configuration.json`
- required-case binding files and raw logs