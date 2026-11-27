# Readable portal navigation and exact JSON formatting

Portal links use readable labels in the path while retaining exact native selectors in the query:

```text
/applications/country-data/instances?applicationId=<application UUID>
/applications/country-data/configuration?applicationId=<application UUID>
/applications/country-data/capabilities?applicationId=<application UUID>
/applications/country-data/jobs?applicationId=<application UUID>
/applications/country-data/configuration/country-settings?applicationId=<application UUID>&configurationId=country.settings&schemaVersion=1
```

The Configuration base path opens its detail view. `/edit`, `/assignment`, and `/adoption` select authoring, whole-map decisions, and adoption respectively. An explicit `source=common` remains a Common read selection; it does not select an application draft or grant shared-source mutation. Reported job details use `/applications/<label>/jobs/details` with the exact registration, inventory sequence, job scope/ID and optional resource/mode selectors. The jobs list and a pinned job report are different views.

Labels are cosmetic, bounded Unicode slugs. Application labels come from the currently authorized display name or canonical application name; Configuration labels currently come from its portable Configuration ID. Duplicate names, lossy slug collisions and applications in different environments stay distinct through their exact application IDs. A label alone never selects a resource, resolves a name, or authorizes a read. The actual returned environment is authoritative; a retained application-list environment filter only restores the list. Stale labels remain valid when their exact selectors are valid, so a display-name change does not invalidate a bookmark. UUID-looking application labels are prefixed to keep them distinct from the legacy UUID-path grammar. A real Configuration named `new` uses the cosmetic `configuration-new` path; `/configuration/new` remains the explicit creation screen.

Existing `/applications/<UUID>?tab=...`, `/applications/<UUID>/jobs?...` reported-detail links, and `/applications/<UUID>/configurations/<Configuration ID>/{edit,assignment,adoption}` remain accepted. Contradictory path/query IDs, duplicate selector parameters, missing exact selectors and invalid schema-version strings are refused. Cosmetic paths do not change native BFF resource IDs, operation-receipt identities, authorization, or original-key recovery.

`portal-routes.ts` contains the small pure link helpers. `page-input.ts` captures the same exact selectors on direct navigation or refresh. The focused `portal-routes.test.mjs` cases cover deep-link round trips, legacy selectors, duplicate/contradictory selectors, mutable/colliding names, UUID-looking display labels, explicit Common selection and pinned reported-job identities. Actual Next/browser execution is recorded separately in the change verification evidence.

Opening Configuration JSON updates its shareable detail address while retaining the list and focus context. Browser Back recaptures exact selectors and the retained page cursor. After a known native creation, **Continue to publication** replaces the address with that actual Configuration's readable `/edit` URL while preserving its still-mounted JSON draft in memory. Neither history operation stores the JSON, a precondition, credentials or an original operation body. Refreshing a page can restore its exact native target and minimal original references; it cannot restore an unpublished memory-only draft.

## JSON formatting

`formatConfigurationJson` in `configuration-draft.ts` validates the existing exact source text, changes whitespace outside strings only, and copies every value token verbatim. It never serializes parsed numbers. Large integers, exponent spelling, negative zero, literal Unicode, Unicode escapes, object member order, unknown fields, explicit null, arrays and empty objects retain their original meaning and token spelling.

The result is either `{ valid: true, json }` or `{ valid: false, message }`. Incomplete/invalid JSON and formatted output over the existing 256 KiB UTF-8 source limit yield no replacement text. The caller retains its original text. Formatting adds no schema interpretation, source composition or defaults authority.

A display can format a derived view without changing the retained exact publication bytes. Formatting an editable draft is a deliberate edit and must invalidate its earlier validation/review: whitespace changes its payload digest. Existing source publication and coherent release confirmation remain separate operations. The focused draft tests cover exact numeric/Unicode preservation, idempotence, invalid input and valid compact input whose expanded format exceeds the limit.
