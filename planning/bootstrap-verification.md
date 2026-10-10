# Bootstrap verification

10 October 2026. This record covers the initial directory/project scaffold, not implemented logging behavior or a complete foundation/release milestone.

| Check | Result |
| --- | --- |
| Local Git initialization | Initialized on `main`; no commits or remote. |
| Source-input validation | Passed for 49 stories, 224 tasks and 211 acceptance criteria; original guide hash and source IDs retained. |
| Locked NuGet restore | Passed for Core and ConsoleHuman; project lockfiles retained. |
| Runtime dependency inspection | Core restored exactly `Microsoft.Extensions.Logging.Abstractions` and `Microsoft.Extensions.DependencyInjection.Abstractions`, both 10.0.12. |
| Formatting | `dotnet format --verify-no-changes --no-restore` passed. |
| Release build | Both projects built with zero warnings and zero errors. |
| Console host | Exited successfully and explicitly reported that logging implementation is pending. |
| Local package | `CisLib.Core.0.1.0-dev.1.nupkg` packed and inspected; only the expected `Core.dll` shipping assembly and approved dependency IDs. |
| Dependency negative control | A synthetic unapproved package in a copied restore graph is rejected with exit 1. |
| Incomplete release control | The root Release profile rejects missing logging/test/replay evidence with exit 2. An initial exit-code propagation defect was corrected before this handoff. |
| Git hygiene | Build/package artifacts are ignored; original source evidence is exempt from line-ending conversion. |

The executed lane is Windows with .NET SDK 10.0.401 and `net10.0`. Linux, logging tests, mutation/coverage, API compatibility, package-based consumer execution and the full release replay are pending. The release profile deliberately fails until their required evidence is available.

Generated binaries, NuGet packages, dependency-negative fixtures and local results stay in ignored directories. No package was published.
