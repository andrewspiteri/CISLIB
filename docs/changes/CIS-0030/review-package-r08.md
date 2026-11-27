# R08 — simpler application screens

**Approved by Andrew Spiteri: “Approve R08 screens”.** The exact decision is in
`r08-user-approval.json`. These five synthetic desktop previews
propose the first UX iteration. They are not yet implemented in the live portal.
The approved R07 package and its exact renderer/images remain unchanged.

| Preview | What changed |
| --- | --- |
| [Configuration list](assets/r08-configuration-list.png) | Three simple columns, shared/application origin badges, row actions, direct Override, New configuration. Schema IDs and generations leave the main list. |
| [Configuration details](assets/r08-configuration-details.png) | The selected assigned JSON fills the panel; Technical details is last. Shared and application JSON are identified separately. |
| [Application JSON editor](assets/r08-configuration-editor.png) | A large formatted JSON editor for this application, a small inheritance caption and bottom Technical details. No main source-reference selector. |
| [New configuration](assets/r08-new-configuration.png) | Application-scoped name and a large JSON input before first deployment, followed by explicit review. |
| [Instances](assets/r08-instances.png) | Current and past sections both expanded, per-instance versions and clear presence labels. A missed report does not falsely become a stopped instance. |

Tabs and Configuration details have readable paths, for example
`/applications/country-data/configuration/country-settings`. Exact application,
Configuration and schema selectors remain in the URL where needed; duplicate
or renamed labels do not change the resource being selected. Existing UUID links
remain supported. Clicking Back or refreshing must retain the selected tab.

Creating a genuinely new Configuration name needs a narrow native backend
operation: the current service only publishes to predeclared definitions. The
new creation frame approves the intended experience, not an already working
save. The [bounded implementation follow-up](ux-r08-plan.md) keeps that backend
change with its authorization, conflict/provider tests and documentation.
Shared editing remains distinct from overriding for one application.

The renderer and exact five PNG hashes are recorded in
[the R08 manifest](r08-preview-manifest.json). Root and an independent reviewer
inspected every final frame; both local renders passed dimensions/nonblank checks
using the existing pinned Node/Sharp runtime. These are SVG-rendered design
previews, not Playwright or native provider acceptance. The outstanding
rollout/adoption browser failures remain recorded separately.

This records the screenshot checkpoint Andrew requested. Production changes follow
the exact R08 decision; no approval of runtime Scheduler work, deployment,
push, merge or release is requested or implied.
