---
title: "Using CIS context in CISLIB"
type: guide
status: Active
owner: Repository maintainer
review_cadence: on tool or repository change
cis:
  stable_id: cislib:guide:graph-context
---

# Using CIS context in CISLIB

CIS helps locate code, tests and task evidence before a change. Its graph is rebuilt from repository files. The [logging status record](../../planning/logging/implementation-status.md) remains the completion ledger: at initialization, WP04-S02-T03 is committed and WP04-S02-T04 is next, after CIS setup. Follow the [graph-context skill](../../.github/skills/cis-graph-context/SKILL.md), inspect returned source locations, and preserve existing verification requirements.

## Get the tool

Run commands from the CISLIB repository root with PowerShell 7. The launcher uses `CIS_EXECUTABLE` when set, otherwise the ignored local tool under `.artifacts/cis/cli`, then `cis` on PATH. A new checkout must obtain CIS separately; the executable is not committed or added to the `CIS.Core` library.

The [CIS installation/build instructions](https://github.com/andrewspiteri/CIS/blob/d4f36080d820745dee0907d0f9d87b669a3428fc/README.md#2-quick-start) explain how to obtain the tool. Initialization used a framework-dependent build from CIS commit `d4f36080d820745dee0907d0f9d87b669a3428fc`, requiring .NET 10. The [initialization record](../../planning/cis/initialization.md) retains that original build identity. The local tool now includes the graph recovery, bounded diagnostics, project identity and test-discovery fixes recorded in [the October 15 verification record](../../planning/cis/evidence/graph-fixes-2026-10-15.json); rebuilding the initialization commit alone does not include those changes. The optional local layout is `.artifacts/cis/cli/cis.exe` on Windows or `.artifacts/cis/cli/cis` elsewhere, with the complete published tool directory. To select an installed executable explicitly, set its real path in the current shell:

```powershell
$env:CIS_EXECUTABLE = 'C:\tools\cis\cis.exe'
pwsh -NoProfile -File eng/cis.ps1 graph status --repo . --format agent
```

## Find context before a task

A missing or stale graph needs a rebuild. `--repo .` selects this repository; `--workspace .` rebuilds all registered repositories. This workspace currently registers only CISLIB.

```powershell
pwsh -NoProfile -File eng/cis.ps1 graph status --repo . --format agent
pwsh -NoProfile -File eng/cis.ps1 graph build --workspace . --format agent
pwsh -NoProfile -File eng/cis.ps1 context search --repo . --kind symbol --text CIS.Logging.Exceptions.ExceptionProjection --limit 20 --format agent
pwsh -NoProfile -File eng/cis.ps1 context search --repo . --kind test --text ExceptionContent --limit 20 --format agent
pwsh -NoProfile -File eng/cis.ps1 context search --repo . --kind reference-item --text WP04-S02-T04 --limit 10 --format agent
```

The last command illustrates the historical exception task's Draft route; it is not the current next task. Replace T04 with T03 to inspect the completed task. The [task index](references/traceability-matrix.md) covers WP04-S02 only; earlier and future tasks remain in the original roadmap. Draft rows provide navigation and do not establish CIS product acceptance.

Use the full returned node key to select related, caller or test queries. A key selects a graph node; it cannot separate declarations the tool has already merged. For the current internal capture method:

```powershell
pwsh -NoProfile -File eng/cis.ps1 context callers --repo . --id '<full key returned by the symbol search>' --limit 20 --format agent
```

Check freshness and truncation in the output. If truncated, search for a more specific qualified symbol and query that returned key, or increase the limit; do not call a bounded result complete. `callers` returns incoming call, dependency, subscriber and reference relationships. Use `graph related --direction both` for both directions. `tests-for` returns declared graph verification edges. It does not discover every indirect test call or prove a parameterized row executed.

## Current limits

Initialization reported 39 C# ownership ambiguities, increasing to 147 before the October 15 fixes. The current extractor separates project identities. The tracked `.cis/graph-inputs.json` excludes planning snapshots, verification reference sources and historical DiscoveryHost sources from implementation extraction. These files remain on disk and retain their existing evidentiary role; they do not contribute live symbols or calls. Catalogued documents remain in the graph. Configuration changes participate in freshness checks. Keep the component profile current: project identity alone does not establish component ownership.

Lambda and local-function identities include their containing symbol and source offset. Use the paths and line locators in `traversals[].edge.observations[]` to inspect actual call sites. Static project analysis follows nearest unambiguous project files and literal unconditional project references; it does not evaluate SDK compile rules, conditional references or linked-source imports, and does not establish complete caller coverage.

The original import missed conditional test packages in `implementations/csharp/tests/Directory.Build.targets`. CIS now recognizes conditions based on known literal properties such as `IsTestProject`; unknown expressions still require review. Use the established `pwsh -NoProfile -File eng/verify.ps1 -Profile Developer` harness. Discovery does not replace it or prove execution. Installed AI/provider and retention templates remain Draft proposals; these graph fixes do not adopt their settings.

Agent build/status output contains summary counts and bounded diagnostic samples. Use `--format json` for complete details. A rebuild recovers interrupted SQLite writes through a writable connection and checks that the committed graph can be reopened. If recovery exposes corruption, it preserves the old database and sidecars under `.cis/local/graph/corrupt-*` before replacing the derived graph. Repeated permission or lock failures require `repo doctor` and inspection of active writers, rather than an indefinite rebuild loop.

## Source replay

A source replay captures checksummed code, tests, configuration and verification inputs in two temporary directories. The reference copy stays untouched and is never built. Only the working copy is rebuilt and tested, with separate caches. The harness checks hashes before and after so changed inputs or old build output cannot silently support the result.

These copies live under ignored `.artifacts/csharp/replay/`; authoritative code stays under `implementations/`. The current input inventory includes retained historical evidence, which is also copied and checked and adds preparation time. The script retains these ignored run directories; it does not automatically delete them. Replay is a separate verification run, not an automatic consequence of a graph rebuild. It does not certify a published release or rerun every platform suite.

To request a source replay explicitly, run `pwsh -NoProfile -File eng/replay.ps1 -Profile Developer`. This is a separate, potentially long verification run; ordinary `eng/verify.ps1` does not perform the copied-source replay. See the [replay implementation](../../implementations/csharp/eng/replay.ps1) for its input and lane options.
