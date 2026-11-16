# Environment container

EnvironmentOptions is a static container with an automatic static constructor. It reads entry-assembly and host/process defaults, applies known overrides through the internal parser, validates selected identities and interns four bounded common identities. Sixteen properties are getter-only. EnvironmentKind, CommitId and BuildId remain caller-owned policy properties. LoggingOptions and HeartbeatOptions remain mutable; each module owns preparation/runtime and copies its effective settings.

Configure deployment variables before first access. CLR static-constructor failures remain failed in the load context and require corrected configuration plus restart. There is no public Initialize, reset, readiness, defaults helper or override helper. See [current usage](../../../../../docs/modules/logging/environment-options.md).
