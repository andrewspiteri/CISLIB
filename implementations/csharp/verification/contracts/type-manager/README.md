# Initial Type Manager development baseline

`v1-development` retains the specific unpublished API assembly and original source/compiled consumer. `v1-behavior-development` supplies frozen behavior cases and consumer artifacts. Native CompatibilityTests exercise the current executing Core against these inputs through direct builds, unchanged binary execution and pinned ApiCompat. They are development commitments, not published release history.

Run from `implementations/csharp`:

```powershell
dotnet tool restore --configfile eng/Tools.NuGet.config
dotnet test tests/CIS.Core.CompatibilityTests/CIS.Core.CompatibilityTests.csproj -c Release --filter FullyQualifiedName~TypeManagerCompatibility
```

SDK 10.0.401, consumer runtime 10.0.12 and ApiCompat 10.0.401 on runtime 8.0.31 remain pinned. `CIS_COMPAT_API_TOOL` optionally selects the exact ApiCompat entry DLL. The fixture deploys the approved dependency bytes and preserves original frozen source/binary inputs; no external producer receipt or source-copy mutation framework is required.

The consumer exercises interface/query surface, factory options, preparation report, generic extensions, exact lookup and useful missing-file diagnostics. These tests supplement actual runtime/schema behavior; they do not establish exact-package delivery or full release qualification. See [native tests](../../../tests/README.md) and [current compatibility policy](../../../../../docs/architecture/compatibility-policy.md).
