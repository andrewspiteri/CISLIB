# Environment and identity

Shared startup configuration, validation and immutable application/runtime identity belong here. Apply the plan amendments before exposing public APIs; the selected facade name is `EnvironmentOptions`.

WP03-S01-T01 implements internal application build-identity discovery under `Discovery`, using the `CIS.Configuration` namespace. Supplied metadata and the selected application assembly provide identity; optional entry-assembly fallback cannot substitute Core identity. [Behavior and scope](../../../../../docs/modules/logging/application-identity-discovery.md) describe the executed unit controls and remaining resolver/publication work.

Environment setup must not accumulate unrelated mutable module/application state. Public startup types and the logging runtime remain unimplemented.
