**Verdict: Sound for the limited development-checkpoint scope. I found no concrete bug in this delta.**

**1. `--no-incremental` build**

- `--no-incremental` maps to the Rebuild target. It applies to the whole ProjectReference graph, so Core is rebuilt too, not just Host.
- The build logs show zero warnings, so the shared `-o` output with a ProjectReference is not causing output-path conflicts in practice.
- The Core SHA staying identical to the prior candidate is the expected result. It also confirms the rebuild is deterministic for this input set.

**2. `sourceProvenanceScope` wording**

- The wording matches what is actually compared: the C# hashes plus the five named files, checked before and after.
- It does not claim full transitive MSBuild input coverage, so the scope is not overstated.

**3. Producer mode and verifier-hash gate**

- It fails closed. A missing `mode` or `verifierSha256` gives `None`, which never matches, so the check raises.
- Placing it after the existing completeness and Core-hash checks is fine, since any failure aborts either way.
- It rejects reports from the run-only mode and from stale producer versions. That is the accidental-mismatch protection the README describes, and no more.
- On Linux, the replay and the Windows producer report hash the same `/mnt/c` bytes, so CRLF/LF cannot cause a false mismatch in these runs.

**Residuals (documentation or operational notes, not defects)**

- **Snapshot copy:** the hash binds to `verify-runtime-processes.py` bytes as seen through `__file__`. If the producer runs a snapshot copy, this relies on the snapshot step copying the file byte-for-byte. Your results imply it does.
- **Imports:** if the verifier imports any local helper module, changes to that helper are not covered by the hash. If there are no such imports, this does not apply.
- **Stale reports:** any edit to the producer verifier, even whitespace, invalidates the trusted Windows report. The Windows producer must run again before a Linux replay will pass. This is intended, and worth one line in the README if it isn't already there.
- **Line endings:** the line-ending invariance holds only for the shared-`/mnt/c` setup. A separate Linux checkout with different line endings would fail the gate. It would fail closed, not open.
