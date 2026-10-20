# Healthcheck development

HC00 is complete. The Developer checkpoint passed 7,163 native cases, including 59 Healthcheck foundation cases, with zero failures or skips. Current development contracts are revision 0.2; this work is uncommitted and unpublished.

* [Technical Intent](technical-intent.md), [contract catalog](contract-catalog.md), [concrete HC00 surface](foundation-surface.md), and [execution-fact validation](execution-facts.md).
* [User decisions and reconciliation](decisions.md), [task plan](task-plan.md), and [acceptance traceability](acceptance-traceability.md).
* [Frozen input/artifact receipt](baseline.json), [ownership](ownership.json), and [shared changes](shared-changes.json).
* [Original attachments and source register](sources/source-register.md).
* [HC00 evidence and limits](evidence/HC00/README.md).

Only configuration/value behavior and compiler contracts exist at HC00. HealthcheckService, policy state, collection execution, reporting codec, HTTP branch and pipe server/client are not implemented. Their compile-only facade is excluded from shipping Core. Real Heartbeat/startup-coordinator integration and full module release qualification remain pending.
