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

The [CIS installation/build instructions](https://github.com/andrewspiteri/CIS/blob/d4f36080d820745dee0907d0f9d87b669a3428fc/README.md#2-quick-start) explain how to obtain the tool. This setup used a framework-dependent build from CIS commit `d4f36080d820745dee0907d0f9d87b669a3428fc`, requiring .NET 10. The [initialization record](../../planning/cis/initialization.md) retains the exact build command, source checkout, copy destination and tool hashes. Reproduce that commit to obtain the qualified tool; recheck behavior when selecting another version. The optional local layout is `.artifacts/cis/cli/cis.exe` on Windows or `.artifacts/cis/cli/cis` elsewhere, with the complete published tool directory. To select an installed executable explicitly, set its real path in the current shell:

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

The last command locates the next exception task's Draft route. Replace T04 with T03 to inspect the completed task. The [task index](references/traceability-matrix.md) covers WP04-S02 only; earlier and future tasks remain in the original roadmap. Draft rows provide navigation and do not establish CIS product acceptance.

Use the full returned node key to select related, caller or test queries. A key selects a graph node; it cannot separate declarations the tool has already merged. For the current internal capture method:

```powershell
pwsh -NoProfile -File eng/cis.ps1 context callers --repo . --id 'cislib::symbol::cis-core/csharp/method/CIS.Logging.Exceptions.ExceptionProjection.Capture%28Exception%3F%29' --limit 20 --format agent
```

Check freshness and truncation in the output. If truncated, search for a more specific qualified symbol and query that returned key, or increase the limit; do not call a bounded result complete. `callers` currently returns neighboring relationships in both directions, including outgoing calls. Inspect each traversal direction before treating it as an incoming caller. `tests-for` returns declared graph verification edges. It does not discover every indirect test call or prove a parameterized row executed.

## Current limits

Initialization reports 39 C# ownership ambiguities and 272 symbol/test nodes without a confirmed component. Reference-only API types, retained source copies and separate project entry points share names with live code. CIS merges these declarations, including their members. Inspect the actual project and source before relying on ownership or caller coverage for an affected symbol. The current tool has no repository graph-exclusion setting; original specifications, evidence and namespaces remain preserved. Non-strict graph validation succeeds with 40 warnings; strict validation fails. Recheck current diagnostics as the code and tool change.

Anonymous/lambda callers are also merged per component. Their node location can point to an unrelated function, even when the target method is unambiguous. For actual call sites, use the paths and line locators in `traversals[].edge.observations[]`, then inspect those source lines. An exact target key does not repair this caller-node limitation.

Import did not generate test bindings because package declarations are conditional in `implementations/csharp/tests/Directory.Build.targets`. Use the established `pwsh -NoProfile -File eng/verify.ps1 -Profile Developer` harness. Public logger/event/scope/output behavior and release qualification remain later roadmap work. Installed AI/provider and retention templates are Draft proposals; initialization does not approve their settings or adopt imported application/browser baselines. Draft is documentation metadata, not proof that CIS agent or retention commands ignore these files. This setup runs graph and validation operations only; review those settings before any separate agent/retention setup.

## Source replay

A source replay captures checksummed code, tests, configuration and verification inputs in two temporary directories. The reference copy stays untouched and is never built. Only the working copy is rebuilt and tested, with separate caches. The harness checks hashes before and after so changed inputs or old build output cannot silently support the result.

These copies live under ignored `.artifacts/csharp/replay/`; authoritative code stays under `implementations/`. The current input inventory includes retained historical evidence, which is also copied and checked and adds preparation time. The script retains these ignored run directories; it does not automatically delete them. Replay is a separate verification run, not an automatic consequence of a graph rebuild. It does not certify a published release or rerun every platform suite.

To request a source replay explicitly, run `pwsh -NoProfile -File eng/replay.ps1 -Profile Developer`. This is a separate, potentially long verification run; ordinary `eng/verify.ps1` does not perform the copied-source replay. See the [replay implementation](../../implementations/csharp/eng/replay.ps1) for its input and lane options.
