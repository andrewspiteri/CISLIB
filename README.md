# CisLib

CisLib is a reusable, multi-module, multi-language core library planned around the same API and interoperability across language implementations. Logging is the first module, and C# is the first implementation. Additional languages will have their own implementations and build tools, with shared contracts and conformance cases guiding consistent behaviour. Examples, instructions and agent skills accompany the modules as they are qualified.

The repository currently contains tested configuration resolution/validation and the actual [C# EnvironmentOptions startup API](docs/modules/logging/environment-options.md), alongside [typed C# log values](docs/modules/logging/log-values.md), the internal [exception capture foundation](docs/modules/logging/exception-projection.md) and [bounded JSONL event encoding](docs/modules/logging/event-encoding.md), test projects and executable dependency checks. Shared startup/event/runtime contracts remain unpublished. The bounded logging runtime and Microsoft logger integration are implemented and qualified through their runtime/provider stories. Both ship in `CIS.Core`, using the `CIS.Logging` and `CIS.Logging.MicrosoftLogger` namespaces; [Microsoft logging packaging](docs/modules/logging/microsoft-logging-packaging.md) explains the approved dependencies. The [current review corrections](docs/modules/logging/review-contract-2026-10-15.md) document recovery, deadline and provider edge cases. ConsoleHuman is still an example scaffold; runnable package examples, instructions/skills and complete release qualification remain pending.

## Start with C#

Healthcheck's service, four monitor policies and coherent state are implemented in [HC01](planning/healthcheck/evidence/HC01/README.md), building on the frozen HC00 foundation. [HC02](planning/healthcheck/evidence/HC02/README.md) adds bounded owner-safe collection. [HC03](planning/healthcheck/evidence/HC03/README.md) adds bounded reporting and protocol helpers; [HC06](planning/healthcheck/evidence/HC06/README.md) adds execution-fact binding. HTTP reporting, protected local pipes, the matching probe and the CountryList development example are now implemented; see the [Healthcheck guides](docs/modules/healthcheck/README.md) and [combined candidate evidence](planning/healthcheck/evidence/HC13/README.md). Actual external scheduler/startup integration, Linux execution and full release qualification remain pending.

Install .NET SDK **10.0.401** and PowerShell 7. Core now requires the **Microsoft.AspNetCore.App** shared framework as well as Microsoft.NETCore.App; the pinned qualification runtime is **10.0.12**. The user approved this foundation dependency for [Healthcheck and future integrations](planning/healthcheck/decisions.md). For ordinary development, run from `implementations/csharp` so its pinned SDK is selected:

```powershell
dotnet restore CisLib.slnx --locked-mode
dotnet test CisLib.slnx -c Debug -m:1
dotnet test CisLib.slnx -c Release -m:1
```

Run All in this solution selects self-contained fast product tests and fast architecture rules, without an earlier checkpoint or package. Complete suites are explicit in [CisLib.Verification.slnx](implementations/csharp/verification/CisLib.Verification.slnx). From the repository root, run the current-platform non-mutation regression at a coherent checkpoint:

```powershell
pwsh -NoProfile -File eng/regression.ps1
```

Regression preflights its pinned tools, prepares one matching build/package/compiler candidate and executes the complete product/component/checker projects once. Observations, extended stress, coverage, mutation and release qualification have explicit additional scope. The existing `eng/verify.ps1 -Profile Developer` retains its checkpoint contract; Release retains all six required families. `-PrepareOnly -OutputRoot <new-directory>` prepares inputs without granting a test pass. Read the [focused-test guide](docs/architecture/csharp-focused-tests.md) and [current recovery status](planning/cis/verification-remediation-plan.md) for measured results and budgets.

Current preparation and coverage declare their maintained C# input scope: implementation/tests/build configuration, invocation glue, contracts, conformance, compatibility, documentation, relevant root files and the small explicit plan/baseline/native fixtures read by current tests. They hash dirty and new inputs without traversing historical archives. The general source-inventory and historical replay commands preserve their broader semantics. Complete nonmutation verification on the current Linux server uses `eng/regression.ps1 -NativeWorkers 1 -NativeBudgetSeconds 1500`; the five-minute engineering target remains separately reported. Ordinary launchers use one frozen checker runtime; isolated controls retain actual bootstrap and copied-reader checks. Additional project workers require a suitable measured host.

The separate toolchain qualification command uses a fresh source copy, CLI home and NuGet cache, executes Microsoft coverage and isolated Stryker, checks API compatibility controls, and retains native reports and prerequisite archives:

```powershell
pwsh -File implementations/csharp/eng/verify-toolchain.ps1
```

It also requires .NET runtimes **10.0.12** and **8.0.31**, Git, and HTTPS access to NuGet. Read [C# toolchain qualification](docs/architecture/csharp-toolchain.md) for exact pins, scope and retention limits.

Open [CisLib.slnx](implementations/csharp/CisLib.slnx) for ordinary C# work. The [grouped Healthcheck mutation route](implementations/csharp/verification/mutation/healthcheck/README.md) uses one immutable candidate, unchanged native reports and a conservative complete-scope gate. Its bounded correctness pilot must pass before expansion.

The [verification and source replay guide](docs/architecture/csharp-verification.md) explains structured reports, all six family slots and the required-case inventory. Validate existing Developer evidence with `pwsh -File eng/check-release.ps1 -Profile Developer`, or rebuild it with isolated caches using `pwsh -File eng/replay.ps1 -Profile Developer`. Release verification and replay remain nonzero until their required implementation and evidence exist.

Read the [startup, identity and configuration contract](docs/modules/logging/startup-contract.md) for `CIS.EnvironmentOptions`, immutable outputs and configuration precedence. The current readonly static container supersedes historical startup publication mechanics.

## Repository layout

| Directory | Responsibility |
| --- | --- |
| `planning/` | Source plans, recorded choices, amendments and implementation preparation. |
| `contracts/<module>/` | Language-neutral configuration, events, capabilities and observable semantics. |
| `conformance/<module>/` | Shared input/output cases used by applicable language implementations. |
| `implementations/<language>/` | Language-specific source, tests, examples, package settings and build tooling. |
| `docs/` | Shared architecture and module guidance, with language-specific how-to instructions. |
| `skills/<module>/` | Portable skills and their language-specific references, added after the corresponding API can be exercised. |
| `compatibility/<language>/` | Append-only historical API, package and consumer baselines after publication. |
| `eng/` | Repository-wide verification and release coordination. |

In C#, `src/CIS.Core/Logging/` and `src/CIS.Core/Logging/MicrosoftLogger/` are folders within **one `CIS.Core` project and package**, with root namespace `CIS` and module namespaces such as `CIS.Logging`. Future modules belong beside `Logging` in the same project. Integrations that introduce third-party runtime dependencies, such as a future NLog integration, belong in separately built and packaged projects.

## Planning and decisions

Read [architecture](docs/architecture/repository-layout.md), [recorded decisions](planning/decisions.md) and [logging preparation](planning/logging/README.md). User decisions amend the original source plans through [plan amendments](planning/logging/plan-amendments.md); the supplied files are retained unchanged.

The [C# assembly policy](docs/architecture/csharp-assembly-policy.md) records versioning and strong-name decisions. The [C# platform policy](docs/architecture/csharp-platform-policy.md) separates selected contract targets from actual execution evidence. Track completed roadmap tasks in [implementation status](planning/logging/implementation-status.md).

Core may use explicitly approved Microsoft-maintained NuGet packages. Third-party runtime dependencies must remain in separate optional integrations. A package prefix alone does not establish its provenance or allow it into Core; the actual dependency graph is checked against the allowlist.

CisLib has its own [source repository](https://github.com/andrewspiteri/CISLIB) and release process. CIS will consume versioned guidance and reference bundles; the Core runtime does not depend on CIS. Public package release and complete release qualification remain pending.

## Licence and maintenance

CisLib uses the [MIT License](LICENSE.md), matching Change Impact Studio. Andrew Spiteri maintains the official repository, with external contributions and public posting disabled. See the [maintenance and contribution policy](CONTRIBUTING.md). The licence permits use and modification of your own copies; write access to the official repository remains with the maintainer.

WP02-S02 [event/value contracts](docs/modules/logging/event-contract.md) and shared fixtures are defined and checked by development-only C# validation; owned event capture and bounded JSONL encoding are now implemented internally.

See [event UTC time and W3C correlation](docs/modules/logging/event-correlation.md) for the current owned-facts capture phase and its native bindings.

See [EnvironmentOptions](docs/modules/logging/environment-options.md) for automatic readonly static facts and override behavior.

[Complete event qualification](docs/modules/logging/event-qualification.md) now covers the internal capture-to-JSONL path.

The [runtime-bound logger foundation](docs/modules/logging/logger-foundation.md) now implements logger methods and immutable admission results against an internal output interface.
