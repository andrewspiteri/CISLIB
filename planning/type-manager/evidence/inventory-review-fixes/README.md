# Inventory review fixes

14 October 2026. The read-only source-inventory review found a missing rejection
for `CopyLocalLockFileAssemblies=false`, dependence on the task host's current
directory for relative publish paths, and stale-output/broad-error test hazards.
The raw findings are retained in `claude-findings.txt`.

The source target now rejects disabled package copying and normalizes `PublishDir`
against the host project directory. Invalid executable metadata includes the
selected filename in its diagnostic. The lifecycle verifier deletes expected
manifests before generation checks, removes a prior successful result at startup,
requires an incremental generator message and matches specific failure reasons.
It adds default-directory publish without `-o` and disabled-copy rejection.

The updated Windows lifecycle passed 32 checks in a separate source snapshot.
`windows-lifecycle.json` records the cases; full logs remain under
`.artifacts/type-manager/inventory-review-qualified/windows/`. The four source
hashes match the executed snapshot. A separate negative control seeded an old
publish manifest and disabled the publish target: the harness deleted that file
and correctly failed because the current command generated no inventory.
`stale-publish-negative.log` preserves that expected failure. The snapshot target
was restored after the control; canonical targets were not mutated by it.

Web SDK evidence is explicitly limited to CLI publication with `-o`.
Visual Studio/FileSystem `PublishUrl` copying has not been qualified. The matrix
now records this limit in `publishScope`, and the source README states it.
Neither the isolated package-restore prototype nor packaged delivery is adopted.

The combined Developer checkpoint predates these build-only corrections. Runtime
Core source was not changed by them; no whole-solution rerun is implied here.

The native Ubuntu Web SDK Debug matrix passed all nine build/publish/RID cases
with the corrected helper/targets. A separate follow-up passed default relative
publish without `-o`, executed its Linux host and rejected disabled package copying.
The first follow-up invocation parsed MSBuild's mixed path separators incorrectly;
publishing succeeded but the verifier read the wrong path. The corrected verifier
normalizes that property, deletes the actual expected manifest and reruns publish.
Only `linux-publish-fixes.json` is the passing result; the failed attempt remains
under the local artifact root and is not counted.

The targeted read-only Claude follow-up returned PASS for S1/S2/V1/V2/V3. Its scope
and the status/matrix wording it could not independently inspect are retained in
`claude-followup.txt`. The `publishScope` value is visible in the retained native
matrix result; the canonical status table also states the CLI-only limitation.

Git attributes preserve the input pack, raw evidence and frozen compatibility
baseline bytes. The six baseline hashes were checked after exporting the actual
index, catching checkout normalization rather than merely hashing the working tree.

The exported index also built the complete solution in Release with zero warnings
or errors, using the committed logging baseline rather than the logging chat's
uncommitted changes. Its 63 Type Manager unit and 14 Type Manager architecture
cases passed, with strict native TRX validation and no skips. `indexed-checkout.json`,
`indexed-build.log` and the two indexed TRX files retain that scoped result.
This does not claim another full Developer or release-family run.
