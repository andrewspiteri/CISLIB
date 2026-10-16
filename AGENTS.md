# Agent guidance

## Scope and authority

- CisLib is a separate repository for a multi-module, multi-language core library. Logging and C# are the first implementation scope.
- Read `planning/decisions.md` and `planning/logging/plan-amendments.md` before using the original logging plans. Explicit user decisions take precedence over attached design documents.
- Distinguish scaffold, implemented behavior, executed evidence and published contracts. Do not claim passing tests from empty projects or generated reports.

## Architecture

- Put shared module contracts in `contracts/` and shared cases in `conformance/`. Keep C# public types and implementation details in `implementations/csharp/`.
- Build the C# library as one `CIS.Core` project/package/assembly, with root namespace `CIS`. Use explicit `CIS.*` namespaces for handwritten module types. `CIS.Logging` and `CIS.Logging.MicrosoftLogger` are namespaces and folders inside it. Future modules are siblings of Logging.
- Approve actual Microsoft-maintained package identities and dependency closures explicitly. Isolate third-party runtime dependencies in separate optional projects/packages.
- Keep Core independent of Change Impact Studio tooling/CLI, application infrastructure and collector SDKs. The application owns its runtime lifecycle.
- Keep language build settings local to `implementations/<language>/`. New runtimes declare supported capabilities and conformance applicability; do not promise C# binary/API mechanics in another language.
- Preserve published contracts and historical baselines. Library naming is selected; resolve the remaining publication choices before the first release.

## Verification and examples

- During an edit, run selected native tests with `pwsh -File eng/test.ps1 -Project UnitTests -Class <class>` (or `-Category` / `-Filter`). This incrementally builds that project and rejects empty, failed or skipped native results. See `docs/architecture/csharp-focused-tests.md` for prerequisites and scope.
- Run `pwsh -File eng/verify.ps1` at a coherent checkpoint and the required release profile before release qualification. A focused pass does not satisfy unexecuted families, coverage, mutation or release gates. Coordinate shared build/output directories with other active chats.
- Add tests when real behavior is implemented. Follow the six verification families in the logging plan and keep development tools outside shipping dependencies.
- Keep developer source-reference examples explicit. Release reference examples must consume the exact verified packages.
- Avoid public placeholder APIs and simulated logging implementations. Logging and Type Manager have implemented behavior and native tests; preserve the distinction between that evidence and outstanding corrective/release work.
- Runnable skill/API examples must be verified against their matching package version before activation.

## Changes

- User direction (15 October 2026): stop Claude reviews rather than request further review permission. Use local review and appropriate native verification; do not send further Claude review payloads unless the user explicitly changes this direction.
- Later explicit user authorization (17 October 2026): the exact 24-bundle read-only Anthropic Claude review set is approved, manifest SHA-256 66bea29723b9aa2a039df2934c69d3dc0c0b25f189c95ee557eb1d8a4972a46a. Seventeen bundles completed; seven remain after a service quota refusal. Do not ask again for those unchanged payloads. Preserve raw attempts and read-only/no-tools/no-delegation restrictions; changed payloads are outside this specific authorization. See planning/reviews/corrective-approved-2026-10-17/README.md. The original roadmap goal remains paused.
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
