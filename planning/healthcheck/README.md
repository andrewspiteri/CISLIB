# Healthcheck development

HC00–HC03 and HC06 are committed through `f73ec92`. HTTP reporting, protected local pipes and the matching probe, transport tests, and the CountryList application example are now implemented in the next combined candidate. Its focused Windows runs passed 137 transport cases and four application lifecycle cases, with zero failures or skips. The complete Developer checkpoint passed 7,839 native cases with zero failures or skips; see [current integration evidence](evidence/HC13/README.md).

* [Technical Intent](technical-intent.md), [contract catalog](contract-catalog.md), [concrete HC00 surface](foundation-surface.md), and [execution-fact validation](execution-facts.md).
* [User decisions and reconciliation](decisions.md), [task plan](task-plan.md), and [acceptance traceability](acceptance-traceability.md).
* [Frozen input/artifact receipt](baseline.json), [ownership](ownership.json), and [shared changes](shared-changes.json).
* [Original attachments and source register](sources/source-register.md).
* [HC00 evidence and limits](evidence/HC00/README.md).

The [API and deployment guides](../../docs/modules/healthcheck/README.md) describe the implemented runtime. Frozen task receipts preserve what each earlier candidate actually established; their historical status text is not the current task status. Real Heartbeat/startup-coordinator integration, Linux execution and full module release qualification remain pending. The current candidate is uncommitted and unpublished.
