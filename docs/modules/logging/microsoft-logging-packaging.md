# Microsoft logging packaging

Logging and its Microsoft adapter ship in the single `CIS.Core` project, assembly and package. The library's root namespace is `CIS`; logging uses `CIS.Logging`, and the adapter uses `CIS.Logging.MicrosoftLogger`. The implementation folders mirror those namespaces under `implementations/csharp/src/CIS.Core/Logging`. There is no separate `Core.MicrosoftLogging` or `CIS.Core.MicrosoftLogging` package. This applies the user's repository/package decision recorded before implementation; the original supplied plan is preserved as source material.

The initial development target remains one portable `net10.0` library asset. The package's current development version is `0.1.0-dev.1`; assembly identity/version and platform qualification remain governed by the existing policies. Neither this task nor a development pack publishes a release or creates a historical supported baseline.

| Dependency role | Package | Verified repository version |
| --- | --- | --- |
| Direct runtime | Microsoft.Extensions.Logging.Abstractions | 10.0.12 |
| Transitive runtime | Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 |
| Host/factory implementation | Supplied by the consuming application; Microsoft.Extensions.Logging is used only by the development fixture here | 10.0.12 in that fixture |

The source of approved runtime identities/versions is [runtime-dependencies.json](../../../implementations/csharp/eng/runtime-dependencies.json). Central package management selects 10.0.12, the checked Core lock file records both exact resolved versions/content hashes, and verification restores in locked mode. The dependency checker evaluates actual project items, resolved target graphs, compiled AssemblyRefs and the generated package. Core's actual restored closure must equal the approved two-package inventory. The Microsoft factory implementation and its additional fixture dependencies remain development-only; third-party logging integrations, if added, require separate optional packages.

A NuGet dependency written as `10.0.12` is an inclusive minimum, not an exact `[10.0.12]` restriction. The current nuspec keeps that minimum-version syntax. Repository verification pins the actual restored closure at 10.0.12 with its lock files and allowlist; it does not claim every application's dependency graph is locked by Core's nuspec. Applications can resolve higher versions through their own dependencies; those graphs need their own qualification. No new upper bound or change to the existing consumer constraint is introduced. See [NuGet version ranges](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning#version-ranges).

An application restoring only `CIS.Core` therefore receives the logging implementation, Microsoft adapter and approved abstraction dependency closure. It does not acquire a separate bridge assembly/package or Microsoft's factory implementation through Core. Using a factory remains an application choice; see [host filtering](microsoft-host-filtering.md) and [host qualification](microsoft-host-qualification.md).

[The S03 scope record](../../../planning/logging/microsoft-packaging-scope.json) retains each original task/criterion and its direction under the user's amendment. T01 records the existing package topology and version policy and produces a checked development pack. T02 qualifies dedicated dependency/asset rejection controls; T03 explicitly includes adapter code in Core coverage/mutation; T04 retains provider consumers in Core compatibility; T05 accounts for all provider material in the eventual single-Core release bundle. Source obligations for behavior, compatibility and release assurance remain. The original separate-package optionality and separate shipping-assembly requirements are superseded, and are not claimed as passing original separate-package scenarios.

The packaged README and description now describe the implemented logging/provider stories. The ConsoleHuman example remains a scaffold until WP08. Full release gates, candidate-package examples, licence/publishing choices and publication remain pending.

Before first publication, the package README must use consumer-focused presentation and links that resolve on a feed, either through a dedicated package README or release-specific absolute URLs. The current root README is an unpublished development package input; resolving this presentation item belongs with WP08/WP09 publication preparation.
