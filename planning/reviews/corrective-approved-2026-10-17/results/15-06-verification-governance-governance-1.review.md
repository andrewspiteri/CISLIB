# CISLIB review: subpass 06-verification-governance-governance-1

**Scope:** `.cis/graph-inputs.json` and `AGENTS.md` at the current head. I used the three inlined context files only as bounded context. This was a static review only: nothing was executed and no artifacts were opened. Line numbers count from the supplied text.

## Confirmed findings

**G1. Low: `AGENTS.md:29` still presents the stop on Claude reviews as the current direction.**
- **What it says:** the 15 October direction to stop Claude reviews is written as current guidance. The repository records no later direction.
- **What has changed:** the latest human authorization resumes review out-of-band. Line 29's own "unless the user explicitly changes this direction" clause allows that, so the file is not self-contradictory.
- **Failure:** an agent that has only the repository will refuse or halt the pending work. That work is the 325 unreviewed paths and the review of the corrective fixes, which `corrections.md` and the checkpoint list as outstanding.
- **Related tension:** line 49 refers to "this repository's Claude read-only review scope" as if an active scope exists, while line 29 says reviews are stopped.
- **Fix:** add a dated entry recording the resumption and its read-only, bounded scope. Keep the original 15 October line unchanged.

**G2. Low: `AGENTS.md:14` uses "CIS" ambiguously.**
- **What it says:** "Keep Core independent of CIS, application infrastructure and collector SDKs."
- **The clash:** line 12 makes the library `CIS.Core` with root namespace `CIS`, and the graph sections (lines 36–50) use "CIS" for the CIS CLI/tooling product.
- **Failure:** an architecture rule or a reviewer can read line 14 as forbidding something the library necessarily is. The intended dependency boundary (no reference to the CIS tool, its CLI or its packages) is not enforceable as written.
- **Fix:** name the CIS tooling/product explicitly, for example "independent of the CIS tooling and CLI".

## Context observations (outside the assigned files; low)

**C1. Stale correction pointer in `contracts/logging/v1/runtime-contract.json`.**
- **What it says:** `fileRecovery.qualification` says current corrections are tracked by `planning/logging/reviews/2026-10-15`, and `executionLimit` points to "the dated review receipt".
- **What it misses:** the 16 October R1/R2 corrections changed this contract's incoming-observation text, and they are tracked in `planning/reviews/corrective-2026-10-16/corrections.md`. The contract does not reference that file, so its provenance trail omits the latest contract-affecting correction.
- **Confirmation needed:** please check whether the `2026-10-15` receipt itself links forward to the 16 October corrections. That file was not supplied.

**C2. Claim arithmetic is consistent and needs no change.**
- **Developer run totals:** 3,710 + 2,085 + 619 + 24 = 6,438.
- **Failed first attempt:** 618 of 619 architecture cases passed and business did not run.
- **Finding count:** eleven, from R1, R2, F1–F3, F5, F6 and T1–T4.
- **Labelling:** scaffold, executed-evidence and release labels stay distinct ("Developer uses Release build configuration; it is not the full Release profile"). No whole-change PASS is claimed.

## Questions and limits

1. **Graph exclusion of `implementations/csharp/verification` (`.cis/graph-inputs.json`).**
   - If that directory holds maintained C# verification utilities, graph queries cannot find their symbols or callers. That would weaken `AGENTS.md:43` ("Find exact symbols/tests") for exactly that code. Those utilities are the native replacements for the deleted Python readers (mutation preparation, measurement, compatibility reader, examples verifier).
   - The checkpoint's graph figures (10,976 nodes, zero diagnostics) do not mention the configured exclusions. Its disclaimer that "navigation does not establish complete semantic or test coverage" partly mitigates this.
   - The directory contents were not supplied (only `contracts/startup/runtime-surface.json` is referenced), so I cannot confirm this.

2. **`AGENTS.md:32` and the C# verification decision.**
   - The line allows existing script-based checks to stay until a separate cleanup. Your decision says maintained verification logic uses C#.
   - The checkpoint lists "substantive PowerShell policy assessment" as pending. If `eng/*.ps1` contains verification logic beyond invocation glue, the guidance may be looser than your decision.
   - `planning/decisions.md` was not supplied, so I cannot confirm this.

3. **Schema versions.** `graph-inputs.json` uses `schemaVersion` 1, while the checkpoint reports graph schema 2. I assume these are different artifacts (input configuration versus graph store); please confirm.

4. **Missing context:**
   - `planning/decisions.md` and `planning/logging/plan-amendments.md`
   - `.github/instructions/cis-import.instructions.md` and the `.github/skills/cis-graph-context/SKILL.md` graph skill
   - `planning/cis/initialization.md`
   - the contents of the excluded verification directory
   - the `2026-10-15` review receipt and `docs/modules/logging/review-contract-2026-10-15.md`
   - the `eng/*.ps1` entry points
   - the raw reports and archives behind the cited hashes

   None of the reported execution, hashes or parity of the deleted Python readers can be verified from this bundle. This subpass contains no reader or replacement source, so it does not review parity or required case bindings.

Within the supplied scope there are no findings beyond G1 and G2. This is not a whole-change or release review.