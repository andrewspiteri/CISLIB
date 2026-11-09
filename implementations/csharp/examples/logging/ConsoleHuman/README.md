# Environment configuration example

This small .NET 10 console consumer uses a source reference to `CIS.Core`. It exercises the selected static `EnvironmentOptions`, caller-owned environment kind, logging option preparation and immutable event metadata, then prints the resulting facts. It does not yet demonstrate logging output or runtime ownership.

From `implementations/csharp`:

```powershell
dotnet run --project examples/logging/ConsoleHuman -c Release
```

Core requires the approved .NET and ASP.NET shared frameworks. The planned complete logging example must own a runtime, contextual operation and shutdown observations; a release example must consume the exact tested package. Those acceptance requirements remain pending, rather than being credited by this configuration demonstration. See [current decisions](../../../../../planning/decisions.md).
