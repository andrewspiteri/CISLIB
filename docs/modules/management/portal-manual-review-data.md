# Local portal review data

The explicit manual fixture uses two isolated synthetic applications in Development. The display names are **Country Services** (`managed-a`) and **Billing Worker** (`managed-b`), with descriptions beneath their titles. The trusted application identities and exact native IDs remain unchanged. Ordinary automated fixtures and the named Shared-copy regression keep their existing data and expectations.

Operator A can inspect and edit both sample applications through independently configured native grants. Operator B can inspect Billing Worker and its billing JSON, with no mutation grants. The hidden continuity account is separate, readonly and limited to Country Services; it does not rotate Operator A's sign-in.

| Application | Configuration | Assigned example |
| --- | --- | --- |
| Country Services | `shared.features` | Shared-only feature flags: CountrySearch on and Export off. Creating an Application override copies this exact assigned Shared JSON. |
| Country Services | `shared.settings` | A real Shared + Application release: global catalogue defaults with an explicit European greeting and page count. Existing Finance cohort identifiers remain deliberately pinned to that same pair. |
| Billing Worker | `billing.processing` | An Application-only release containing EUR currency, batching, retry and invoice-export settings. No Shared publication is selected. |

These examples are native publications, distinct releases and explicit assignments. Source JSON, release references, assignment generation and adoption remain separate facts. Unassigned older publications and retained historical releases are available for inspection; publishing new JSON alone does not apply it. The manual setup checks exact payloads, committed maps and the native origin summaries before handoff.

Instance names identify regional country APIs, a canary and billing workers. Rows retain their own reported versions. Retired registrations are closed through the native API and appear in Past. A canary registration intentionally has no observation: its only presence evidence is its actual registration receipt, with no invented health/lifecycle report. Other rows report ready/healthy through the native observation API; those snapshots naturally become stale after the service's existing freshness interval. The fixture does not run fake heartbeat loops or alter timestamps to manufacture freshness.

The Country inventory includes a reported **Refresh country cache** job/resource example. Historical Applied/Faulted consumer reports remain separate from the current desired map. Reported inventory is neither a deployed Scheduler backend nor execution history; unsupported Task/Event/history screens retain their unavailable explanations.

All data lives in the existing fixture-owned database/schema and is removed only after native owners drain. The test-only issuer and finite manual lease remain in effect. Authored setup assertions are not passing runtime evidence; actual startup, browser, continuity and cleanup results are recorded by the parent execution owner.

The visible review browser uses its actual window dimensions. Resizing or maximizing it changes the content viewport in both directions. Copy any test JSON you want to keep before the finite instance expires or is replaced; a fresh fixture starts with fresh synthetic data.
