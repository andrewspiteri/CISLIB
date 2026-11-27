---
title: "cislib Task-Type Capability Selections"
type: task-type-capability-selections
status: Active
owner: Repository maintainer
review_cadence: on task-provider or capability change
cis:
  stable_id: cislib:reference:task-type-capability-selections
---

# cislib Task-Type Capability Selections

This canonical record resolves mutually exclusive extension providers and
explicitly authorizes compatible extension task-type replacement. Core task
types are non-replaceable. Use `cis plan capability select`; do not hand-edit
the managed table.

<!-- cis:task-type-capability-selections:start -->
| Capability | Selected type | Replaces | Reviewer | Timestamp UTC | Rationale |
|---|---|---|---|---|---|
| None | None | None | Pending | Pending | No explicit selections. |
<!-- cis:task-type-capability-selections:end -->

Selection does not migrate an existing task automatically. Use
`cis plan task migrate-type` for each affected task instance so its evidence,
approvals, deferrals, external links, and migration history remain visible.
