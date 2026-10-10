# Repository layout and dependency boundaries

CisLib keeps shared observable contracts separate from language implementations. Each module owns its contracts, cases and guidance. Each language owns its source, toolchain, tests, examples and package manifests.

## C# layout

```text
implementations/csharp/
  CisLib.slnx
  global.json
  Directory.Build.props
  Directory.Packages.props
  NuGet.config
  src/
    CIS.Core/
      CIS.Core.csproj
      Environment/
      Logging/
        MicrosoftLogger/
  tests/
    CIS.Core.UnitTests/
    CIS.Core.IntegrationTests/
    CIS.Core.ArchitectureTests/
    CIS.Core.CompatibilityTests/
    CIS.Core.BusinessCases/
    CIS.Core.TestSupport/
  verification/
    mutation/
  examples/
    logging/
      ConsoleHuman/
  eng/
    CIS.DependencyChecker/
```

`CIS.Core` is one assembly and package, with root namespace `CIS`. `CIS.Logging` and `CIS.Logging.MicrosoftLogger` organize its module code; they are not additional projects. The Microsoft integration will map standard `ILogger` calls into the logging module's event pipeline. It must not create a competing pipeline or own a shared application runtime implicitly.

The initial Microsoft abstractions package is part of Core's allowed runtime dependencies. Additional Microsoft packages require an explicit inventory change and examination of their actual target-specific dependency closure. Third-party runtime libraries belong in optional sibling projects, such as a future `CIS.Logging.NLog`, which may depend on Core. Core must never depend back on them.

Future Core modules are added beside `Logging`. Shared environment identity/configuration stays in `Environment`; module-specific logging options stay with Logging. Module dependencies must be recorded as responsibilities emerge rather than introducing circular references or an unrelated mutable global state holder.

Read the [C# dependency policy](csharp-dependency-policy.md) for the evaluated project/package/assembly gates and [maintainability standard](csharp-maintainability.md) for build and review requirements. The test and checker projects are development-only and never flow into Core.

## Other languages and runtimes

Add another implementation under `implementations/<language>/` when its runtime and toolchain are chosen. Keep its build settings there; root automation coordinates the declared language lanes. Host capabilities such as environment discovery, filesystem access and lifecycle coordination are declared per runtime.

Shared contracts specify event meanings, configuration precedence, field encodings and observable outcomes. They do not require another language to reproduce C# type names, numeric limitations, scope storage mechanisms or CLR binary replay. Shared conformance cases carry applicability information; unsupported host-specific behavior needs an explicit capability declaration.

## Examples, skills and CIS

The simple ConsoleHuman host is the first C# consumer. During development it references the local Core project. Published reference examples must restore and verify exact candidate packages, preserving evidence of the package/assembly loaded.

Shared guidance lives in `docs/logging/`, with language-specific run instructions under `guides/`. Portable skills live in `skills/logging/`; actual skills are authored and activated once their APIs can be exercised.

CIS imports versioned descriptors, guidance, skills and example bundles produced by CisLib. CIS CLI modules, orchestration and its own contracts remain in the CIS repository. The runtime has no dependency on CIS.

Read [plan amendments](../../planning/logging/plan-amendments.md) before following the original implementation plan.
