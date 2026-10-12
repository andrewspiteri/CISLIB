# EnvironmentOptions

`CIS.EnvironmentOptions` remains a static container. Its static constructor runs automatically on first access: it populates defaults, applies known environment-variable overrides, validates the selected identities, and interns ApplicationName, EnvironmentName, BuildVersion and ServiceVersion. Getters then return stored values. Construction starts no logger, opens no output and creates no directory.

```csharp
using CIS;
using CIS.Logging;

// First access applies defaults and environment overrides.
EnvironmentOptions.EnvironmentKind = EnvironmentKind.Console;
EnvironmentOptions.CommitId = "your-commit"; // optional caller policy
EnvironmentOptions.Logging.Prepare(Directory.GetCurrentDirectory());
var metadata = new EventMetadata();
```

## Automatic readonly facts

ApplicationName defaults to the entry assembly name. BuildVersion uses its nonblank informational version, then assembly version, then `unknown`; file version and CIS.Core's version are not application build identities. Without an entry assembly, name and build are `unknown`. ServiceVersion defaults to BuildVersion, and EnvironmentName to `unknown`.

HostName (null if hostname inspection is unavailable), OS family/description, OS/process architecture, runtime description and ProcessId come from the current host/process. ProcessStartedAtUtc is UTC, or null when the platform denies or cannot provide it. InstanceId is a new UUID for this static container; InitializedAtUtc records its population time in UTC. Neither is regenerated on subsequent access.

All these facts have getter-only properties. EnvironmentKind, CommitId and BuildId remain ordinary assignable policy values; populate them before consumers start. Logging has a getter-only reference to mutable LoggingOptions, whose validation and runtime belong to logging. It defaults to a 65536-byte maximum entry size; `CORE_LOG_MAX_ENTRY_BYTES` can override it. Oversized log input remains subject to the selected truncation policy.

## Overrides and interning

Discovered/literal defaults precede aliases, which precede canonical variables. Missing, empty and whitespace-only values leave defaults intact. `CORE_SERVICE_NAME` overrides ApplicationName (`DD_SERVICE` alias); `CORE_ENVIRONMENT` overrides EnvironmentName (`DD_ENV`); `CORE_SERVICE_VERSION` overrides ServiceVersion (`DD_VERSION`). Service-version overrides never change the actual BuildVersion. [The configuration catalog](../../../contracts/logging/v1/configuration.json) lists the logging variables and bounds. Each needed variable is read once; a nonblank canonical value prevents reading its alias. Selected malformed overrides fail rather than falling back.

The four common identity strings are interned after selection and bounded Unicode validation. UUIDs, commit/build identifiers, paths, runtime descriptions and log payloads are not interned. Logging metadata reuses the selected identity references while owning its allowlisted facts.

## Static construction and failure

Configure environment variables before the first access. Subsequent environment changes are not reread. An invalid selected override or invalid discovered application identity/build metadata fails the static constructor: the CLR throws TypeInitializationException with CoreConfigurationException as its inner exception. The type remains failed in that load context; correct the deployment configuration or application assembly metadata and restart. There is no reset, refresh, initialization gate or public defaults/override helper.

The [ConsoleHuman example](../../../implementations/csharp/examples/logging/ConsoleHuman/Program.cs) uses this shape and clearly identifies the still-pending logging runtime. [The current contract](../../../contracts/logging/v1/environment-contract.json) supersedes the previous unpublished passive-store candidate. Historical plans and receipts remain preserved; controlled metadata fixtures exercise projection validation without assigning readonly runtime facts.
