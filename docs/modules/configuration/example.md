---
title: Configuration in CountryData
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.example
---

# Configuration in CountryData

The canonical [CountryData application](../../../implementations/csharp/examples/CountryData/README.md)
uses [CISApplication](../application/core.md) to prepare its complete Type Manager
and retained Configuration roots before the one Build. Constructors receive
initialized roots; Configuration activation precedes application IStartable work.

[CountrySettings](../../../implementations/csharp/examples/CountryData/CountrySettings.cs)
is passive data with a source URL, hourly refresh interval, 30-second overall REST
timeout and explicit retry selection. Without a supplied store, the facade uses
initialized defaults without external storage, watch or polling. Host
appsettings.json does not silently populate these objects. Configuration scope is
the host assembly name CIS.CountryData; display-identity overrides do not redirect it.

CountryCatalog captures all operational settings consistently through
IConfigurationReader in its constructor. These values remain fixed for its
lifetime; changes require restart in v1. It does not reschedule Heartbeat or
replace the collector on live configuration changes.

External bootstrap remains explicit:

```csharp
builder.Configuration = new ConfigurationOptions("CIS.CountryData", store)
{
    IsHealthCritical = true
};
```

Here store is the host's actual IConfigurationStore with its documented
notification/reconciliation mode and, when required, an explicit positive polling
interval. The facade awaits reads/validation before Build and installs the
retained roots. The caller-supplied store remains caller owned. See
[hosting](hosting.md), [mapping](mapping.md) and [refresh lifetime](refresh.md).

From implementations/csharp, use the pinned SDK/frameworks:

```powershell
dotnet run --project examples/CountryData/CountryData.csproj -c Release -- --urls http://127.0.0.1:5087
```

Reload, masked overrides, events, retained identity and watch/provider drain remain
dedicated module tests. The minimal Type Manager fixture separately checks an
initially-unused assembly, defaults before construction and shared scoped roots.
CountryData has no demonstration store or embedded self-test/report mode. Earlier
[CIS-0008 observations](../../changes/CIS-0008/verification.md) retain their
historical inputs; they are not verification of the new application.
