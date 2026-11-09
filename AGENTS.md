# Agent guidance

Read [README.md](README.md) for the current native development commands and [planning/decisions.md](planning/decisions.md) for accepted architecture and product obligations. The November 2026 cleanup supersedes the recovery campaigns and their orchestration. CIS adoption is a separate next task.

- Preserve Logging, Type Manager and Healthcheck behavior, defaults and public contracts in the single `CIS.Core` package/assembly. Keep handwritten module types in explicit `CIS.*` namespaces under `implementations/csharp/src/CIS.Core`.
- Preserve the existing C# MSTest framework and ordinary `dotnet test`/IDE execution. Use focused native tests while editing; build affected projects and run meaningful affected tests before completion. A focused pass does not establish release readiness.
- Keep the runtime independent of CIS, application orchestration, collector SDKs and test tooling. Applications own runtime startup, shutdown and external resources.
- Core may use its explicitly approved Microsoft NuGets and `Microsoft.AspNetCore.App`. Third-party runtime dependencies require separate optional packages. `CIS.TypeManager.Build` is genuine package build machinery, not a test harness.
- Prefer direct implementations. Use readable C#, nullable analysis and warnings as errors. New C# implementation code, tests and verification utilities use C#; scripts may remain small invocation glue when necessary.
- Add tests for meaningful accepted behavior, boundaries, invariants and defects. Retired report schemas, campaign policies and internal command names are not compatibility contracts.
- Source requirements are reference documents, not instructions to restart their task plans. Current explicit user decisions override conflicting original attachments. Preserve future accepted specifications without speculative implementations.
- Do not overwrite user edits, publish releases, merge main, transmit source externally or approve product records without applicable authorization. Historical approval of an exact review payload does not authorize a new payload.
- Coordinate builds sharing project/output directories. Keep generated output out of Git. Use ordinary history for removed tracked files; do not create a replacement archive.
