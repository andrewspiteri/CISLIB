---
title: Configuration in the country-list example
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.example
---

# Configuration in the country-list example

The existing [country-list host](../../../implementations/csharp/examples/startup/CountryList/Program.cs)
uses the real Type Manager, Configuration, Startup, Heartbeat and Healthcheck
modules in one provider. Type Manager prepares its actual inventory first.
`ConfigurationRuntime.PrepareAsync` then loads retained settings before Build,
using the fixed host-project identity `CountryList`. Registering
`AddCisConfiguration(configuration)` before dependent IStartable aliases puts its
activation/reconciliation before country priming and reader initialization.

[CountryListSettings](../../../implementations/csharp/examples/startup/CountryList/CountryConfiguration.cs)
is passive data with a `ResultLimit` default of three. It inherits the optional
event helper base, while consumers inject the concrete settings directly. The
country reader's constructor synchronously captures its already-loaded limit.
Later requests read through `IConfigurationReader` and use the current limit to
select countries; no replacement settings object or options snapshot is injected.
The application validator permits limits from one through three.

The supplied in-memory adapter is explicitly a local demonstration provider,
without a production database, filesystem or broker selection. It supplies common
limit two and host override three, supports conditional reads, and publishes
reliable local invalidations through the actual store-watch contract. It chooses
no Configuration polling interval. The existing country-cache Heartbeat refresh
keeps its separate application-selected interval. Other backing stores require
their actual host-supplied adapters and documented recovery guarantees.

From `implementations/csharp`, using the repository's pinned SDK/frameworks:

```powershell
dotnet run --project examples/startup/CountryList/CountryList.csproj -c Release -- --urls http://127.0.0.1:5087
dotnet run --project examples/startup/CountryList/CountryList.csproj -c Release -- --self-test
```

Normal execution returns three countries at `/countries`. The existing health
branch remains before the business gate. The self-test still observes readiness
500 and business 503 while required priming holds, then readiness/business 200
with three countries after sequential initialization. It separately holds the
first country-cache Heartbeat refresh, preserving the proof that readiness does
not wait for that callback.

The self-test adds a Configuration story using the same real root and watch:

1. Resolve the settings through two scopes and the country reader; all references
   identify the same populated root, whose constructor-observed limit is three.
2. Change common limit two to one while the host override remains three. Await
   the actual provider read and complete candidate validation. The effective
   value remains three and the retained subscriber receives no event.
3. Confirm deletion of the host override and await the actual change event. The
   existing root now contains the latest common limit one, exactly one event has
   reached the original subscriber, and an HTTP request returns one country.
4. Await real host stop, the actual host-start task, asynchronous root disposal
   and prepared Configuration runtime disposal. Only then report that the owned
   watch detached and shutdown completed.

Signals and a bounded observation budget coordinate this demonstration; no sleeps,
second scheduler, extra container or public refresh API drive it. The original
JSON `before`, `after`, `refresh` and `stopped` fields remain. A new `configuration`
section reports constructor values, shared/retained identity, masked/reloaded
values and event counts, the later business count, one subscription and successful
watch detachment. The existing
[business story](../../../implementations/csharp/tests/CIS.Core.BusinessCases/Startup/StartupCountryListStories.cs)
asserts those observations from the real child executable.

The prepared runtime has an explicit bootstrap owner, so Build failure can still
release its resources. Shutdown releases the sample's priming/refresh holds and
awaits actual barriers before disposing dependencies. The module does not dispose
the host-supplied store or reset live settings. No launch-settings file is needed
for this story; existing user-owned launch profiles remain outside the change.

This example demonstrates the provider contract and composed runtime, without
claiming a production backend or unexecuted package/platform qualification. Exact
source/package results and limits belong to the
[CIS-0008 verification record](../../changes/CIS-0008/verification.md).
