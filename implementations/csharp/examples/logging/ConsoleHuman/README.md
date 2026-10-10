# ConsoleHuman example host

This initial console application targets .NET 10 and references the local Core project. It builds and exits successfully, identifying that logging functionality is pending.

From `implementations/csharp`:

```powershell
dotnet run --project examples/logging/ConsoleHuman -c Release
```

Once the logging module exists, this application will initialize identity/configuration, own one logging runtime, run a contextual operation, demonstrate a controlled failure and inspect its final stop result. The release example will consume an exact verified Core package instead of the development project reference.
