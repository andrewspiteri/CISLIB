# Compile-only startup specification

This WP02-S01 fixture declares the proposed `CIS.EnvironmentOptions` surface. It is outside shipping Core. The project templates are materialized only under ignored verification artifacts, with no package dependencies. `ProduceOnlyReferenceAssembly` generates a reference assembly; InternalCall declarations describe signatures and supply no runtime implementation. Consumers are compiled and are never executed.

The positive consumer checks every named setup/output property, facade getter and diagnostic constructor parameter. Six independent negative consumers require specific compiler errors for immutable snapshots/logging/metadata/issues, absent Reset and absent `EnviormentsOptions`.

Run `implementations/csharp/eng/check-startup-contract.ps1`, or the root Developer verification command. Later shipping implementation must match this reviewed specification; this is not an archived release baseline. See [startup contract](../../../../../docs/modules/logging/startup-contract.md).
