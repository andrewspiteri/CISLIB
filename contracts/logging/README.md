# Logging contracts

Define language-neutral event/configuration contracts here before spreading implementation across languages. Versioned contracts will live under `v1/` after their semantics are reviewed.

The source guide supplies the starting event fields, level meanings, configuration precedence, value encodings and lifecycle outcomes. Publish capability descriptions for host-specific features. WP02-S01 defines a proposed v1 [startup surface](v1/startup-contract.json) and [configuration catalog](v1/configuration.json), with [contract guidance](../../docs/modules/logging/startup-contract.md). Configuration, metadata and lifecycle expectations live under shared conformance. These are unpublished contracts; runtime resolution/publication and event schema implementation remain pending.
