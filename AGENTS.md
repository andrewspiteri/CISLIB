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

- Use readable C#, nullable checking, SDK analyzers, warnings as errors and documented public behavior.
- Do not overwrite user edits, publish, transmit repository content to another service or approve product records without the corresponding authorization.
