# CISLIB review — pass 05-type-manager

## Verdict: FINDINGS

The deleted Python utilities were compared with their native C# replacements. These were the runtime, instrumentation, provenance controls, inventory matrix, package, examples, measurement, mutation preparation and behavior/compatibility tools.

The replacements preserve the original assertions and negative controls. In several places they are stricter: empty-stderr requirements, `--roll-forward Disable`, output-directory emptiness, an SDK pin check, and binary/fixture re-hashing after execution. The 86 compatibility checks are all present. The added `missing-dependency-manifest` control is sound.

I found no confirmed correctness defect that would let a check pass falsely. The findings below are documentation and provenance gaps, plus one change to a measurement metric.

## Findings (prioritized)

### 1. Medium (confirmed) — Current traceability document still points at retired Python drivers

- **Location:** `planning/type-manager/acceptance-evidence.md`, section "Evidence locations".
- **Trigger:** A reader following the map from the current tree. The map still says runtime child processes are "driven by `verify-runtime-processes.py`". It still names the "independent SDK/RID matrix: `tests/fixtures/TypeManager/verify-inventory-matrix.py`".
- **Evidence:**
  - Both files are deleted in this range.
  - The same document was edited here to update row A25, so it is maintained, not frozen. It calls itself "an evolving TM-026 traceability deliverable".
  - The replacements are `type-manager-runtime` and `type-manager-inventory` in `eng/CIS.DependencyChecker`.
- **Smallest fix:** Repoint those two bullets to the native commands and their evidence receipts. Leave historical receipts untouched.
- **Open question:** The same section, and the README in `build/type-manager`, still cite `eng/check-type-manager-compatibility.ps1` and `eng/verify-type-manager-inventory.ps1`. I could not see those scripts.
  - If either one invokes `verify-type-manager-behavior.py` or `verify-inventory-matrix.py`, it is now broken.
  - Check those scripts. Retarget them to `eng/test.ps1 -Project CompatibilityTests` or the native commands, or mark them historical.

### 2. Low–Medium (confirmed) — Documented examples-verifier command does not reproduce the evidence on a typical cache

- **Location:** `TypeManagerExamplesVerification.Setup()` (the hard-coded list of seven `microsoft.*.runtime/host` packs) and the verifier command in `examples/type-manager/README.md`.
- **Trigger:** Running the README command with an ordinary `--dependency-cache <nuget-packages-directory>`. `Setup()` now requires seven exact 10.0.12 SDK pack archives, for both RIDs, on both OSes. That includes `microsoft.windowsdesktop.app.runtime.win-x64` on Linux. A missing pack makes `Copy` throw before any case runs.
- **Evidence:**
  - The examples evidence states that `microsoft.netcore.app.host.win-x64` was absent from the cache. It had to be downloaded manually.
  - The README does not mention this prerequisite.
  - The pack list duplicates `toolchain.json` `frameworkDownloads.packages` instead of reading it.
- **Smallest fix:**
  - Document the required pack archives in the README.
  - Derive the list from `toolchain.json` `frameworkDownloads` so the two sources cannot drift.

### 3. Low (confirmed) — `processWallMilliseconds` changed meaning in the native port

- **Location:** `TypeManagerMeasurement.Run(...)`.
- **Trigger:** Every measured sample. In Python the stopwatch stopped right after `subprocess.run`, before the log was written. In C# the stopwatch wraps `input.Run`. That call also decodes output, hashes stdout/stderr, and writes the `.log` and `.process.json` files.
- **Evidence:** In the C#, `var timer = Stopwatch.StartNew(); var result = await input.Run(...)`. `TypeManagerVerificationInputs.Run` performs `Write` and `WriteJson` before it returns.
- **Impact:** The metric is explicitly unqualified and has no threshold. However, its distribution is no longer comparable with historical Python observations, and the change is not documented.
- **Smallest fix:** Pick one:
  - Measure using the child's own start/exit timing (if `ProcessResult` exposes it).
  - Document that the metric now includes verifier-side logging.

### 4. Low (confirmed) — Pinned inputs are hashed and parsed from separate reads

- **Locations:**
  - `TypeManagerMutationPreparation.Run`: `Hash(producerPath)` then `Read(producerPath)`.
  - `TypeManagerMeasurement.Measure`: `Hash(manifest)` then `Read(manifest)`.
  - `TypeManagerCompatibilityFixture` constructor: the producer file is read for the copy, then pinned.
- **Trigger:** The file changes between the two reads.
- **Evidence:** The deleted Python mutation preparer read the bytes once (`producer_bytes`), hashed them and parsed the same bytes. The deleted replay tool's `pinned_json` worked the same way. The C# port weakens this "independent pin" to a check-then-use.
- **Impact:** The files are local and trusted, so this is a provenance hygiene regression rather than an exploitable defect.
- **Smallest fix:** Read the bytes once, hash them, then parse with `JsonNode.Parse(bytes)`.

## Uncertain questions, not confirmed defects

- **Examples input provenance includes an uncommitted file.** `CopyTree`/`TreeHashes` copy and hash the untracked, user-owned `Web/Properties/launchSettings.json` into the lanes and into `exampleInputs`.
  - The evidence attributes those inputs to base `361c4f40`, so the recorded input set is not reproducible from the commit alone.
  - The file should not affect build or publish output; the Web SDK does not publish launch settings.
  - Consider excluding `Properties/launchSettings.json` from recorded inputs, or noting the deviation explicitly. Do not modify the file itself.
- **Strict UTF-8 decoding of child output.** `TypeManagerVerificationInputs.Run` decodes output with `new UTF8Encoding(false, true)`. The Python tools used `errors='replace'`.
  - On a host whose tooling emits non-UTF-8 bytes (for example, non-ASCII paths under an OEM code page), the verifiers would abort with a decoder exception instead of producing a structured result.
  - This fails closed, not as a false pass.

## Limits and unreviewed context

- No execution occurred during this review. Passing counts are taken from the supplied evidence only.
- Not visible to me:
  - `CIS.TestSupport.ProcessRunner`: timeout and kill semantics, and how null environment values are handled.
  - The `CIS.DependencyChecker` command dispatcher and its README.
  - The PowerShell entry scripts named above.
  - The Stryker configuration.
  - The Type Manager unit and architecture test sources.
  - The contents of the frozen compatibility baselines and `cases.json`.
- I assumed `JsonNode.DeepEquals` compares numbers semantically and objects without regard to order, which is consistent with the passing evidence.
- Deterministic byte-identity of `behavior-original-api-build` depends on template settings I could not see.
- Retiring the one-off 483-mutant replay was accepted as a user decision. No finding is raised about the missing general mutation engine.