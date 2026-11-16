# Heartbeat polling example

The real Generic Host/DI example returns false, throws a temporary exception,
awaits recovery, then reports a successful refresh. Its actual scope dependency
remains usable until Heartbeat work settles and the wrapper disposes it.

From `implementations/csharp`, using the pinned SDK/runtime:

```powershell
dotnet run --project examples/heartbeat/Polling/Polling.csproj -c Release
```

Expected output includes `negative result (no exception handler)`,
`awaited recovery`, `refresh succeeded`, and
`heartbeat drained; scoped dependency disposed safely`.

For a local development package, build/pack Core, then restore this same source
in package mode and publish it normally:

```powershell
dotnet build src/CIS.Core/CIS.Core.csproj -c Release
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release --no-build -o ../../.artifacts/heartbeat/CIS-0006/feed
dotnet restore examples/heartbeat/Polling/Polling.csproj -p:CisHeartbeatUsePackage=true -p:CisHeartbeatPackageVersion=0.1.0-dev.1 --source ../../.artifacts/heartbeat/CIS-0006/feed
dotnet run --project examples/heartbeat/Polling/Polling.csproj -c Release --no-restore -p:CisHeartbeatUsePackage=true
dotnet publish examples/heartbeat/Polling/Polling.csproj -c Release --no-restore -p:CisHeartbeatUsePackage=true -o ../../.artifacts/heartbeat/CIS-0006/publish
dotnet ../../.artifacts/heartbeat/CIS-0006/publish/CIS.Heartbeat.Examples.Polling.dll
```

The feed also needs the two approved Microsoft 10.0.12 packages, or they must
already be in the selected cache. The package test uses an isolated cache and
local-only feed and compares actual Core hashes. A development package is not
a published release. Restore again when switching source/package modes.

The [module guide](../../../../../docs/modules/heartbeat/README.md) explains owner
association, host ordering, configuration and cancellation.
