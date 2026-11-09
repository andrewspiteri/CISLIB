# Published compatibility

Preserve every supported contract indefinitely from its first actual publication, including prereleases and intermediate releases. A major-version change does not waive preservation. Keep supported source, binary, configuration, wire, behavior/defaults and target commitments; assess fixes against actual consumers. Do not add warning-producing deprecation to supported calls used by warnings-as-errors consumers.

The [publication ledger](../../compatibility/csharp/publications/ledger.json) currently contains **zero publications**. Development packages, Git checkpoints and frozen initial-development consumers are not released historical versions. Preserve meaningful current API/configuration/wire fixtures without inventing a released baseline.

For C#, compatibility includes public member identities and types, visibility, nullability, interfaces, generic constraints, ordered parameter names/types, named arguments, optional defaults, enum/constant values, supported overload resolution and construction paths. Data/results preserve ownership, immutable observations and bounded behavior. Configuration preserves keys/aliases, defaults, inclusive bounds, precedence and invalid-input behavior. Wire contracts preserve framing, field meanings and supported parsing. Undocumented private-reflection accidents are outside this contract.

The current readonly `EnvironmentOptions`, logging incoming frontend, provider ownership and Healthcheck ASP.NET amendment supersede incompatible unpublished drafts. See [current decisions](../../planning/decisions.md) and [logging amendments](../../planning/logging/plan-amendments.md). Old reference declarations do not reinstate withdrawn unpublished APIs.

When actual releases exist, test genuine prior source/unchanged binaries and supported configuration/wire behavior with the exact candidate package on the claimed platform/runtime. Preserve original published artifacts when their bytes are required. Native API comparison and native consumer tests can perform that work; the deleted publication/replay receipt framework and its invented fixture histories are not requirements.
