# Agent guidance

## Scope and authority

- CisLib is a separate repository for a multi-module, multi-language core library. Logging and C# are the first implementation scope.
- Read `planning/decisions.md` and `planning/logging/plan-amendments.md` before using the original logging plans. Explicit user decisions take precedence over attached design documents.
- Distinguish scaffold, implemented behavior, executed evidence and published contracts. Do not claim passing tests from empty projects or generated reports.

## Architecture

- Put shared module contracts in `contracts/` and shared cases in `conformance/`. Keep C# public types and implementation details in `implementations/csharp/`.
- Build the C# library as one `CIS.Core` project/package/assembly, with root namespace `CIS`. Use explicit `CIS.*` namespaces for handwritten module types. `CIS.Logging` and `CIS.Logging.MicrosoftLogger` are namespaces and folders inside it. Future modules are siblings of Logging.
- Approve actual Microsoft-maintained package identities and dependency closures explicitly. Isolate third-party runtime dependencies in separate optional projects/packages.
- Keep Core independent of CIS, application infrastructure and collector SDKs. The application owns its runtime lifecycle.
- Keep language build settings local to `implementations/<language>/`. New runtimes declare supported capabilities and conformance applicability; do not promise C# binary/API mechanics in another language.
- Preserve published contracts and historical baselines. Library naming is selected; resolve the remaining publication choices before the first release.

## Verification and examples

- Run `pwsh -File eng/verify.ps1` for scaffold verification. The release profile must continue to fail until required release evidence exists.
- Add tests when real behavior is implemented. Follow the six verification families in the logging plan and keep development tools outside shipping dependencies.
- Keep developer source-reference examples explicit. Release reference examples must consume the exact verified packages.
- Avoid public placeholder APIs and simulated logging implementations. The console host currently identifies that logging is not implemented.
- Runnable skill/API examples must be verified against their matching package version before activation.

## Changes

- The user requests Claude review when available. Before committing concrete changes, run a read-only Claude review and address actionable findings. Check the host environment outside the sandbox when needed, including the VS Code extension CLI; a missing sandbox PATH entry does not establish that Claude is unavailable. Report an unavailable route honestly.
- Keep each implementation within the requested task/module scope. Prefer direct implementations; add abstractions only for a concrete requirement.
- Use readable C#, nullable checking, SDK analyzers, warnings as errors and documented public behavior.
- User direction: use C# for new C# implementation code, tests and verification utilities. Do not add another implementation language for C# project work. Existing script-based checks can be migrated in a separate cleanup; keep established shell entry points as invocation glue.
- Do not overwrite user edits, publish, transmit repository content to another service or approve product records without the corresponding authorization.

<!-- cis:repository-guidance:start -->
## CIS integration

For CIS commands and artifacts, read `.github/instructions/cis-import.instructions.md`. Existing project directives remain authoritative; unresolved overlaps are recorded in `.cis/local/import/report.json`.
<!-- cis:repository-guidance:end -->

## Graph context for the logging roadmap

- Use `.github/skills/cis-graph-context/SKILL.md` for graph queries. `pwsh -NoProfile -File eng/cis.ps1` resolves the selected local CLI; append the skill's command arguments. Before a roadmap task, check `graph status --repo . --format agent`; rebuild a missing or stale graph with `graph build --workspace . --format agent`. Find exact symbols/tests before requesting bounded related/caller/context results, then inspect the referenced source. Exact keys cannot separate merged declarations; for anonymous/lambda callers, inspect edge observation paths/line locators rather than the merged node location.
- Refresh derived graph context after code or canonical documentation changes. Do not hand-edit or commit `.cis/local/` graph state. Record missing bindings, compiler diagnostics and truncation instead of implying full coverage.
- The existing roadmap/status, case inventory, native reports and commits remain completion evidence. CIS initialization does not migrate those tasks into approved CIS plans or approve product records.

## CIS initialization scope

- Imported provider, AI and retention templates under `docs/cis/references/` remain Draft proposals. They do not authorize CIS agent execution, change this repository's Claude read-only review scope, or adopt imported application/browser requirements.
- `.cis/local/import/report.json` is local-only. New checkouts can read `planning/cis/initialization.md` and the tracked assessment under `planning/cis/evidence/initialization/` for the setup findings and their limits.
