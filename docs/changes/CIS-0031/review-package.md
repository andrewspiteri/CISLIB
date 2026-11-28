---
title: RM11 backoffice screen review
type: review-package
status: Approved
change_id: CIS-0031
---

# RM11 backoffice screen review

The existing Management application context opens the ruleset workspace under Capabilities. These screens use synthetic definitions, executions and instance reports. The review covers navigation, operator controls, visible target and state, and desktop layout.

| Preview | Interaction to review |
| --- | --- |
| [Rule engines](assets/rm11-rulesets--primary--desktop.png) | Select a registered ruleset in the current application and environment. |
| [Rules grid](assets/rm11-grid--primary--desktop.png) | Edit an explicit typed condition, order rules, and save a new immutable version. |
| [Narrow rules grid](assets/rm11-grid--primary--narrow.png) | Keep Save, scope and the selected-cell editor reachable while scrolling wide conditions. |
| [Saved versions](assets/rm11-versions--primary--desktop.png) | Inspect an immutable version and choose an explicit edit or deployment. |
| [Read-only saved rules](assets/rm11-versions--read-only--desktop.png) | Inspect exact saved predicates and rule metadata before choosing Deploy. |
| [Deploy an older version](assets/rm11-deploy--primary--desktop.png) | Confirm the version and exact target before submitting rollback through Deploy. |
| [Instance adoption](assets/rm11-adoption--primary--desktop.png) | Distinguish requested version, last accepted version and each instance's observed state. |
| [Hit counters](assets/rm11-counters--primary--desktop.png) | Inspect counts, reset timestamps and freshness in the ruleset scope. |
| [Counter reset confirmation](assets/rm11-counters--reset--desktop.png) | Confirm the selected counters and expect an outcome for each. |
| [Executions](assets/rm11-executions--primary--desktop.png) | Find attempts and distinguish incomplete or failed attempts from a completed zero-match result. |
| [Execution detail](assets/rm11-execution--primary--desktop.png) | Inspect typed inputs and presence, the observed rule trace and the actual returned order. |
| [Expanded condition trace](assets/rm11-execution--expanded--desktop.png) | Inspect individual operators, presets, observed inputs and condition outcomes. |
| [Save conflict](assets/rm11-conflict--primary--desktop.png) | Compare the current version and the retained draft before a fresh Save. |

The [wireframes](wireframes.md) contain the full behavior, permissions, recovery and keyboard paths. The [CIS design record](design.md) holds the exact renderer and PNG digests for combined approval. Live browser and Management behavior are verified in the implementation tasks.

CIS validated all 13 rendered previews. Andrew Spiteri approved manifest `sha256:d76b1fd3b0bb2ccd785e91d4dd2005fa2d0f255a05df269adc189d846692d1c0` on 2026-11-27 at 21:13 UTC. [CIS decision](design.md)
