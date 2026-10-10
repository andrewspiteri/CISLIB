# Environment and identity

Shared startup configuration, validation and immutable application/runtime identity belong here. Apply the plan amendments before exposing public APIs; the selected facade name is `EnvironmentOptions`.

WP03-S01-T01 implements internal application build-identity discovery under `Discovery`, using the `CIS.Configuration` namespace. Supplied metadata and the selected application assembly provide identity; optional entry-assembly fallback cannot substitute Core identity. [Behavior and scope](../../../../../docs/modules/logging/application-identity-discovery.md) describe the executed unit controls and remaining resolver/publication work.

Environment setup must not accumulate unrelated mutable module/application state. WP03-S01-T02 adds captured process/host/OS/runtime facts with nullable discovery seams; [runtime discovery guidance](../../../../../docs/modules/logging/runtime-metadata-discovery.md) separates controlled family recognition from executed platform support. Public startup types and the logging runtime remain unimplemented.

WP03-S01-T03 adds version selection in `Resolution` and initialization UUID/time generation in `Discovery`. [Initialization guidance](../../../../../docs/modules/logging/initialization-identity.md) distinguishes raw selected values and generation from later validation/publication.
