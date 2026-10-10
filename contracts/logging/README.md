# Logging contracts

Shared, unpublished v1 contracts define [startup metadata](v1/startup-contract.json), [configuration](v1/configuration.json), the [event schema](v1/event.schema.json) and [event/value semantics and bounds](v1/event-contract.json). Language-specific types stay under implementations. The preserved source guide is amended by current user decisions.

[Startup guidance](../../docs/modules/logging/startup-contract.md) and [event guidance](../../docs/modules/logging/event-contract.md) distinguish defined contracts, executed C# validation and pending runtime work. Shared fixtures live under conformance. Other languages must declare capabilities and execute applicable cases before claiming support.

WP02-S03 adds [admission/lifecycle/ownership semantics](v1/runtime-contract.json). The [runtime guidance](../../docs/modules/logging/runtime-contract.md) defines truthful results and cancellation/deadline boundaries, carrying payload truncation into diagnostics. C# API shape stays implementation-local; portable examples describe specified observations, not an executed logger.
