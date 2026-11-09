# Repository layout

| Directory | Purpose |
| --- | --- |
| `implementations/csharp/src` | One `CIS.Core` shipping implementation |
| `implementations/csharp/build` | Product Type Manager SDK/build integration |
| `implementations/csharp/eng/CIS.TypeManager.Build` | Product inventory MSBuild task |
| `implementations/csharp/tests` | Native MSTest families and behavior fixtures |
| `implementations/csharp/examples` | Explicit development consumers |
| `implementations/csharp/tools/CisHealthProbe` | Actual matching Healthcheck probe consumer |
| `contracts`, `conformance` | Portable product specifications and behavioral fixtures |
| `compatibility` | Current frozen consumer/contracts and actual publication state |
| `docs/modules` | Current behavior/API guidance |
| `planning` | Accepted source specifications, current decisions and cleanup disposition |

Start from [README](../../README.md). Source plans are historical reference inputs, not active recovery instructions. Future language/module specifications do not justify dormant implementations. CIS adoption is a separate next task.
