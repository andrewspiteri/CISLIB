# Review 07-corrective-fixes-type-manager-fixes-1: static review of the supplied type-manager fix files

This is a static, read-only review covering only the seven assigned files and the bounded context. Nothing was executed, and no whole-change PASS or release qualification is implied.

## Confirmed findings

### F-1 (Low): the policy hash recorded in the report may not match the policy that was parsed

- **Where:** `TypeManagerExamplesVerification.cs` lines 74–75, introduced in this fix delta. The older lines 63–64 and `TypeManagerMeasurement.cs` lines 19 and 30 share the pattern.
- **What happens:**
  - Line 74 runs `Read(toolchainPath)` to parse the toolchain.
  - Line 75 separately runs `Hash(toolchainPath)` to record `toolchainPolicySha256`.
  - The same split exists for `runtimeDependencyPolicySha256` versus `Read(policy)`.
  - In the measurement constructor, `policy` is parsed separately from `runtimePolicySha256`.
- **Concrete failure:** suppose `eng/toolchain.json` or `eng/runtime-dependencies.json` is edited between the two reads. AGENTS.md notes that other chats share the working tree, so this is plausible. The report would then record a policy hash that does not describe:
  - the pack identities or versions actually copied into the feed; or
  - the `targetRuntime` the measurement children were checked against.

  In measurement, `run` compares `inputs["runtimePolicySha256"]` with its own separately hashed value. That comparison can pass while the parsed policy differs.
- **Why it matters:** this is the same class of defect T4 corrected for producer reports ("pin/parse one owned byte array"). The fix delta adds a new instance of it, even though `Read(path, out string hash)` is part of the same corrective change.
- **Fix:** use `JsonObject toolchain = Read(toolchainPath, out string toolchainHash); report["toolchainPolicySha256"] = toolchainHash;`. Do the same for `runtime-dependencies.json` and the measurement constructor. No new abstraction is needed.

### F-2 (Nit, truthful claims): the acceptance map's date is stale

- **Where:** `planning/type-manager/acceptance-evidence.md` line 3.
- **What happens:** the map still says "14 October 2026" and "current implementation evidence". This delta, however, points its evidence to 2026-10-16 native migration receipts.
- **Fix:** add a revision date or a note so the document's date matches the evidence it now cites.

## Checks on the known fixes in this bundle

**T1: the acceptance map points at native runtime and inventory commands.**
- The two Python reader references are replaced.
- The relative links resolve correctly from `planning/type-manager/`.
- Whether the target files exist was not verifiable.

**T2: the examples cache.**
- The verifier now iterates `frameworkDownloads.packages`.
- It requires each pack version to equal `targetRuntime`.
- It records `toolchainPolicySha256` and has no network fallback.
- The README text matches this behaviour.
- The README's relative links resolve correctly.

**T3: `processWallObservation`.**
- `TypeManagerMeasurement.Run` times `input.Run`, which covers the child process, strict UTF-8 decoding, the stdout/stderr digests, and the log and process JSON writes.
- That matches the stated definition.
- The report stays `qualified=false`, and no threshold is added.

**T4: pinned, single-buffer reads.**
- `ReadPinned` hashes and parses one byte array. It is used by measurement `run`, by both control drivers, and by mutation preparation.
- Measurement `prepare` records `producerHash` from the parsed bytes.
- The compatibility fixture pins `producerBytes` and keeps `Unchanged()` rechecks.

**Expected-reason controls (static reasoning):**
- Each of the seven measurement controls and four mutation controls changes a single field.
- If the guard a control targets were removed, that control would either:
  - succeed outright, so the expected failure would not occur; or
  - fail with a different message, as with "Frozen deployment changed" or "Deployment Core mismatch".
- Either way the expected-reason check would catch the regression.

**Counts derived from the code:**
- The example count of 66 is 2 lanes × 2 hosts × 4 layouts × (1 pass + 3 fault cases) = 64, plus 2 HTTP checks.
- With one warmup round, measurement runs (1 + 12) × 2 × 2 = 52 processes, of which 48 are measured.
- Both match the claimed evidence.

## Questions (not confirmed defects)

1. **BOM handling has changed.** `Read` moved from `File.ReadAllText`, which strips a UTF-8 BOM, to `JsonNode.Parse(byte[])`. I believe span-based parsing rejects a leading BOM, but I have not confirmed that. If so:
   - any BOM-prefixed repository or SDK JSON these verifiers read is now rejected where it was previously accepted;
   - this matters most for the generated `<host>.types.json`, since A30 says the runtime accepts a BOM.

   Please confirm whether this is an intended tightening.
2. **Pack-set parity is unverified.** The old hard-coded list had seven pack IDs. The README claims `frameworkDownloads.packages` contains the same seven, but `toolchain.json` was not supplied, so I cannot check this. The verifier also does not enforce the set or its size, so the README could drift silently.
3. **The candidate report has no independent pin in two places.** Measurement `prepare` and the compatibility fixture (when `CIS_COMPAT_REPORT` is set) accept a self-consistent candidate report without an independent hash pin. Mutation preparation, by contrast, requires `--expected-candidate-report-sha256`. Is this asymmetry intended?
4. **Some A25/A27 entries may be stale.**
   - The A27 row says "56 fresh processes … Five harness controls". The current native driver has 7 controls; 56 processes corresponds to its default of 2 warmup rounds.
   - The A25 evidence list still names `check-type-manager-compatibility.ps1`.

   Is this historical-checkpoint wording deliberate, or stale after the native migration?

## Limits and missing context

The following were not supplied, so their effects are unverified:
- `toolchain.json`, `runtime-dependencies.json`, `Directory.Build.props` and `Directory.Packages.props`;
- the `ProcessRunner` source;
- the `TypeManagerCompatibilityInputs` source, including `Producer`, `Baseline` and `ModuleSources`;
- the example host and project sources and the `ExampleLate` fixture;
- the measurement and native-verification guide READMEs;
- the linked evidence and migration receipts;
- raw logs and reports.

**Python-reader parity:** the bundle notes say the baseline-deleted readers are complete, but they are not inlined here. I therefore cannot assess parity with the deleted Python readers beyond the wording claims above.

No test execution was performed or is implied.