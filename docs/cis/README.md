---
title: "cislib CIS documentation"
type: navigation
status: Active
owner: Repository maintainer
review_cadence: on change
cis:
  stable_id: cislib:docs:root
---

# CIS documentation

This is the selected root for new CIS artifacts. Existing project documents remain authoritative until explicitly reconciled.

Import installs CIS runtime configuration, scoped guidance and test bindings where a supported harness was detected. Existing profiles are preserved. Validate inferred commands before use; security scanner bindings, additional specifications, standards and capability packs remain explicit follow-up work.

- [Component inventory](references/repository-profile.md) maps graph ownership to physical code directories.
- [Library usage guides](../modules/logging/) explain the implemented logging pieces.
- [Logging roadmap and completion evidence](../../planning/logging/implementation-status.md) remain the task-status source.
- [User decisions](../../planning/decisions.md) and [plan amendments](../../planning/logging/plan-amendments.md) take precedence over the preserved source plans.

The graph is a disposable source-navigation aid. A symbol or test relationship does not prove a test ran. Native reports, their input checksums and commits establish executed results; CIS product acceptance and governed plan approvals have not been inferred from existing roadmap status.

- [Graph workflow and source replay](graph-context.md) gives the current commands and coverage limits.
- [Current exception task index](references/traceability-matrix.md) routes WP04-S02 IDs to code and retained evidence; its CIS lifecycle remains Draft.

Imported provider/AI, evidence-registry and retention templates are listed as Draft with routing authority in the catalog. Their settings are unadopted proposals; they do not authorize model use, CIS agent implementation, retention actions or new application/browser requirements. Existing AGENTS.md instructions, including read-only Claude review, remain authoritative. Template review dates are retained separately from this initialization's metadata review.

- [Initialization record](../../planning/cis/initialization.md) records the selected tool, review corrections, import proposals and unresolved setup items. Its tracked evidence is available in a new checkout; `.cis/local/import/report.json` exists only on the initializing machine.
