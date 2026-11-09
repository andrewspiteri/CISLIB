# Logging contracts

The unpublished v1 catalogs specify [current environment facts](v1/environment-contract.json), [configuration](v1/configuration.json), [event wire schema](v1/event.schema.json), [value/event bounds](v1/event-contract.json), [interpolation](v1/interpolation-contract.json) and [runtime ownership/results](v1/runtime-contract.json). C# types live in the implementation; [current amendments](../../planning/logging/plan-amendments.md) supersede conflicting historical startup/passive-store proposals.

[EnvironmentOptions](../../docs/modules/logging/environment-options.md), [runtime guidance](../../docs/modules/logging/runtime-contract.md) and [incoming frontend](../../docs/modules/logging/incoming-pipeline.md) describe current behavior. Actual native tests determine which checks were executed; fixtures/specification text alone supplies no pass or release claim. Other languages must declare capabilities and execute their applicable cases before support is advertised.
