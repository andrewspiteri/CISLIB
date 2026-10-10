# Compile-only startup specification

This WP02-S01 fixture declares the proposed `CIS.EnvironmentOptions` surface. It is outside shipping Core. The project templates are materialized only under ignored verification artifacts, with no PackageReference items. WP02-S03 reuses the exact approved, locked-restored Microsoft logging abstraction compile DLL through an explicit noncopying reference. `ProduceOnlyReferenceAssembly` generates a reference assembly; InternalCall declarations describe signatures and supply no runtime implementation. Consumers are compiled and are never executed.

The positive consumer checks every named setup/output property, facade getter and diagnostic constructor parameter. Six independent negative consumers require specific compiler errors for immutable snapshots/logging/metadata/issues, absent Reset and absent `EnviormentsOptions`.

Run `implementations/csharp/eng/check-startup-contract.ps1`, or the root Developer verification command. Later shipping implementation must match this reviewed specification; this is not an archived release baseline. See [startup contract](../../../../../docs/modules/logging/startup-contract.md).

WP02-S02 adds EventSurface/EventConsumer and three rejection controls for immutable values, unsupported object conversion and absent event severity. These remain reference-only declarations; no event runtime is supplied.

WP02-S03 adds RuntimeSurface/ProviderSurface and two positive consumers. Seven additional rejection controls cover immutable write/drain/diagnostic results, extra positional runtime options, an ambiguous factory Write pattern, synchronous runtime disposal and provider owning mode. Twenty-one total controls comprise one reference-only build, four positive consumers and sixteen expected compiler rejections. Native metadata inspection checks exact return/property/parameter types and names, immutable output shapes and ownership interfaces. These declarations remain nonshipping and are never invoked. See [runtime contract](../../../../../docs/modules/logging/runtime-contract.md).
