# T04 Claude review disposition

The user directly authorized the exact prepared T04 payload in reply to its approval question: "we discussed you will no longer ask me and send imeaditly." Automatic approval review then allowed that unchanged transfer. The retained payload contains 20 source/test/guide files, 136,940 bytes, SHA-256 `90cbe48e2e4b41b45591638539483fa23105659998d119dd6eae82456475aed2`. Type Manager implementation, credentials and binaries are excluded. Native Claude ran read-only with tools, MCP, delegation, settings sources and persistence disabled; its process exited 0.

The actual verdict is **CHANGES_REQUIRED**. The retained response is not a final PASS. Required findings are addressed locally:

| Finding | Focused resolution |
| --- | --- |
| R1 — weak diagnostic context | Every row supplies its expected context and requires that code/context plus exactly one ARCH diagnostic. Contract/output additionally requires its actual runtime target. Wrong project/member/asset or an unrelated failure cannot satisfy the row. |
| R2 — unverified source mutations | Exact-once declaration/assignment replacements reject silent no-ops. Member insertion requires the pinned file-scoped namespace, exact intended class and single top-level close. ApplicationName is moved into a private mutable field and the row requires that exact field diagnostic; it no longer passes solely because a nested type was added. |
| R3 — inherited Git hooks/configuration | Git uses empty global configuration, disabled system configuration/prompts, empty templates, no-verify commit and cleared inherited repository/config overrides. |
| R4 — ancestor imports/feeds for the foreign PE | The tiny foreign project has local empty build/central-package imports, cleared feeds/fallbacks, copied global.json, warnings as errors and a lockfile. It builds from its own pinned directory. |

The five should-fix comments are also addressed: the guide calls the disguised test binary an unknown-origin PE; the fixture requires the Release checker and the normal Developer order builds it first; the outer inspection timeout is ten minutes; project-boundaries qualification text references T04; positive assertions require actual target, contract-use and per-assembly type-use facts. Child builds additionally clear inherited MSBuild SDK/executable overrides. Cleanup is idempotent if the owned tree is already absent.

Optional comments are dispositioned within scope. Source copying uses the frozen maintained-source verification checkout, whose records are compared independently across native lanes. The initial fixture commit deliberately precedes uncommitted negative mutations, and the compiled-reference row deliberately evaluates the project after removing its build-time reference; the guide states both limits. Scratch assembly observations are not successful inspection reports and may remain after rejection; inspection.json must be absent. No release source/build provenance or reflection-proof ownership claim is made. The existing strict owned cleanup behavior remains; a cleanup failure fails the test instead of silently passing with leaked resources.

Final code builds with zero warnings/errors. The full native Windows/Ubuntu profiles each pass all 5,488 tests; the direct definition/execution-ID audit proves all fifteen fixture rows passed per lane with their expected diagnostics. No second external transfer or external follow-up PASS is claimed.

The first complete native run also exposed the old consistency test's expected text and the shared five-minute family timeout. The test now requires ARCH-PROJECT-INVENTORY, and only architecture selects a dedicated twenty-minute session; all other family limits remain unchanged. The actual failed/aborted run is retained under preflight. The corrected native Ubuntu Developer profile passes all 5,488 tests including all fifteen new fixture executions. The final Windows Developer profile also passes all 5,488 tests.
