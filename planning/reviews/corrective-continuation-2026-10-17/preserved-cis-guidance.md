# Preserved CIS guidance changes

17 October 2026. Review 17-F4/F5 affect two of the five pre-existing user-owned changes. Their current bytes remain untouched and unstaged. The adjacent patch is prepared against those current bytes and passes git apply --check. It preserves the existing graph-recovery guidance and logging-wire component while giving that component a file-specific root, maps Type Manager build/example/fixture and discovery-logger components, removes stale Python/project-count prose and labels the old exception-task example as historical.

The selected CIS implementation supports file-specific roots: GraphBuilder.FindComponentForPath checks exact path equality before directory-prefix matching and prefers the longest root. This is a source-traced basis for the proposed root, not an executed graph result for an unapplied patch.

Input SHA-256: graph-context b07d0d4742f1917dd7c3c2403d2a9bdaf4dda8ab83c52c9d2cca93a733ced166; repository-profile f206536f3663d1eff7223c36671d4300e8d38c55ab5323d0f224920d086853e4. These two findings remain pending application/graph validation; the patch does not claim they are fixed in the live documents. The other three pre-existing user changes are likewise preserved.

Applied 17 October 2026 under the user's subsequent authorization. Existing related CIS edits were retained. See [application and graph verification](../../evidence/corrective-cis-guidance-2026-10-17.md). The patch and original preparation account above are historical.
