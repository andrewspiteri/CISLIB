# Central Management contracts

RM-07 uses a language-neutral HTTP/JSON contract. The independent management
application is a CISLIB consumer; Core contains the typed client and optional
registration integration, not the server database or repositories.

The [v1 contract](v1/README.md), checked-in [OpenAPI 3.1.1](v1/openapi.json) and
[canonical schemas](v1/contract.schema.json) define the wire independently of CLR
or TypeScript metadata. [Shared conformance cases](../../conformance/management/v1/wire-cases.json)
exercise the boundaries. [Implementation profile](../../docs/changes/CIS-0024/implementation-plan.md)
records the accepted scope and actual execution authority; authored contracts
are not passing implementation evidence.
