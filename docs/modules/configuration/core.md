---
title: Configuration retained objects and read/event ownership
type: document
status: draft
cis:
  stable_id: cislib.document.modules.configuration.core
---

# Configuration retained objects and read/event ownership

Configuration supplies concrete POCOs whose root identity survives accepted
updates. The user selected P01–P08 under the [Configuration outcome](../../changes/CIS-0008/authorization.json).
The work in this guide establishes the object, identity, read and event boundaries.
See [mapping](mapping.md), [refresh](refresh.md) and [hosting](hosting.md) for
loading and runtime composition. The production adapter remains host-supplied.
Actual native observations belong
in the [verification record](../../changes/CIS-0008/verification.md).

## Define passive data

Implement `CIS.Configuration.IConfigurationObject` through the optional base:

```csharp
public sealed class CountrySettings : ConfigurationObject
{
    public int PageSize { get; set; } = 50;
    public int TimeoutSeconds { get; set; } = 5;
}
```

Direct interface implementation is equally supported. Forward both event
accessors to the helper using the same object:

```csharp
public sealed class CountrySettings : IConfigurationObject
{
    public int PageSize { get; set; } = 50;
    public int TimeoutSeconds { get; set; } = 5;

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
    {
        add => ConfigurationEvents.Add(this, value);
        remove => ConfigurationEvents.Remove(this, value);
    }
}
```

Choose one definition. A field-like event alone cannot be published by the
Configuration owner. Preparation checks both the add and remove connection and
rejects unusable wiring. The helper does not require inheritance, inspect private
event fields or generate proxies. A factory cannot share the same live object
between separate owning preparations.

Configuration discovers concrete closed classes through the already prepared
[Type Manager](../type-management/configuration-boundary.md). Its general query
can include nonpublic classes and types without usable constructors. Configuration
validates eligibility rather than changing discovery or silently omitting a type.
The default path uses a public parameterless constructor, including on a nonpublic
class. `ConfigurationOptions.Factories` supplies exceptional exact-type default
factories; returning null, an unrelated object or a derived replacement is invalid.

Constructors and factories establish defaults without storage I/O, DI resolution,
timers or application service startup. Mapped data uses public non-init read/write
properties and supported passive scalar/object/collection shapes. Indexers,
read-only mapped properties, recursive graphs, custom serialization activation
and executable/throwing setters are outside this profile. Live roots and detached
default/candidate data have separate ownership; event subscriptions and runtime
state do not become mapped values.

Mapped property/JSON names are ordinal and case-sensitive: `Value` and `value`
remain distinct; exact duplicate names are rejected. Supported dictionary defaults
use ordinary `Dictionary<string, T>` storage with the default or explicit ordinal
comparer. Case-insensitive/custom comparers and arbitrary collection wrappers are
outside this data profile.

Defaults are captured once from the retained root and detached from later live
mutations. Later candidates are composed from that captured baseline and current
source records. They must never replace the registered root or recompute its
defaults on every refresh. Nested data/collection references may be replaced;
consumers should capture values rather than depend on a nested object's identity.

## Identity and composition

The registry uses the actual CLR type. Its persisted definition includes the full
type name and defining assembly name/culture/public-key token, omitting assembly
version. Type Manager's exact CLR/assembly-qualified identities remain unchanged.
Distinct discovered types colliding under the persisted identity are rejected;
there is no short-name fallback or automatic key migration.

The host explicitly supplies `ConfigurationOptions.HostApplicationName` once.
It selects the application override alongside reserved `CommonConfiguration`.
An overridden `EnvironmentOptions.ApplicationName`, library, replica or machine
name does not redirect that scope. The common name cannot be used as the host's
application identity. No new environment option, default polling interval or
default health criticality is introduced.

Retained objects belong to one preparation/runtime. Their concrete DI resolutions
share the same root across scopes; a separate host gets separate objects and
synchronization gates. Existing concrete-type registrations and synchronized
reader registrations, including keyed ones, are rejected before registration is
changed. The catalog performs no storage I/O during object construction or DI
resolution. Await `ConfigurationRuntime.PrepareAsync` before Build and install
`AddCisConfiguration` before dependent Startup participants; the
[hosting guide](hosting.md) describes exact ordering and ownership.

Under the selected initial-load contract, the host awaits Type Manager and then
Configuration loading before constructing consuming services. The existing
[Startup integration](../startup/README.md) constructs all scheduled services
before their sequential start methods, so a Stage 2-only load cannot promise
constructor-ready values. Startup/Heartbeat integration activates/reconciles
refresh under their existing lifetimes; Configuration does not own application
readiness or replace either module.

## Read a consistent view

Consumers keep injecting the concrete object. Use `IConfigurationReader` when
several values must come from one accepted state:

```csharp
var settings = configurationReader.Read(
    countrySettings,
    current => (current.PageSize, current.TimeoutSeconds));
```

The reader accepts only the exact retained instance belonging to its preparation.
A newly constructed candidate, an equal-by-value object or another host's root
is rejected. The private per-entry gate is shared with commits; locking the public
POCO is not the synchronization contract.

Keep the read callback short and synchronous. Copy needed scalars or detach
collections inside it, then perform asynchronous work afterward. Do not mutate
configuration, perform I/O, await, request synchronous refresh or acquire another
configuration's gate inside the callback. Returning a live mutable child for later
use does not preserve a snapshot. Raw property reads remain possible but provide
no cross-property consistency guarantee during updates.

## Observe accepted changes

`ConfigurationChangedEventArgs` identifies the actual CLR type and a positive
host-local accepted revision. The sender is the retained root. It is not a store
revision, a historical value snapshot or a durable delivery acknowledgment.

The selected commit contract publishes after releasing the value gate. Initial
population is a baseline, not a reload event. Unchanged effective values, invalid
input and unsuccessful/pre-commit-canceled refreshes emit no change notification;
an accepted commit cannot lose its notification merely because cancellation then
arrives. The mapping/refresh work must establish those commit facts; the event
primitive alone is not evidence that a refresh occurred.

Subscribers are snapshotted and called in subscription order. Adding/removing a
handler during publication affects later publications. Duplicate add/remove
operations follow normal event semantics. A throwing synchronous handler is
reported in isolation and does not roll back values or prevent later handlers;
failure of the diagnostic observer also cannot skip those handlers. Handlers can
read through the synchronized reader, should be brief and synchronous, and must
not synchronously wait for their own refresh. Async-void continuation failures or
indefinitely blocked consumers have no forced-settlement guarantee.

The [acceptance map](../../../planning/configuration/acceptance.md) separates
object/core evidence from actual provider, mapping, host, package and release
evidence. W1 uses controlled catalog inputs plus a real Type Manager query whose
selected valid candidate enters the object primitive. The Unit assembly also
contains deliberately invalid marker types; that subset check is not a claim of
full production discovery or a replacement for the later real application fixture.
