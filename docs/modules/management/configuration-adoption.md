# Configuration adoption

Selected central Configuration composition attaches its borrowed runtime and
final typed configuration client to the existing Management reporter before
Startup. There is one reporting invocation, Heartbeat subscription and stop
barrier. Configuration status uses the normal reporting cadence; short control
checks neither recapture roots nor start another reporting loop.

The reporter copies each root's actual acceptance observation. Desired identity,
accepted provenance and local revision come from Configuration's value gate,
including revision zero and valid unchanged acceptance. Hot changes, rejected
candidates and pending restart remain distinct. Subscriber failure is a separate
warning. A disabled root reports `Faulted`, removes current `applied` and retains
`lastSuccessful` as history. Polling failure before a trustworthy new desired
identity preserves prior desired and accepted facts. During registration
replacement, old-session desired identity is not relabeled as new-session
acceptance. Intact values can remain visible while the new session is pending.

`PUT /registrations/{registrationId}/configuration-status` sends one complete,
separately sequenced report of at most 256 unique definition/schema entries.
Sequence and observation time are owned by that reporter. One uncertain capture
is retained with its exact content, sequence and original time. A changed
capture replaces it at a higher sequence. After confirmation the next periodic
capture gets a higher sequence even if values are unchanged, so adoption receipt
freshness can progress. There is no backlog or mandatory shutdown request.
Cancellation signals the existing owner, and stop joins the actual admitted
upload before dependencies are disposed.

The server requires native workload identity, `management.registrations.write`,
the actual open registration owner and its previously captured configuration
session. A report cannot create a session, select a cohort, or report a
definition/schema outside that frozen session. Higher sequence replaces the
whole report. Equal identical replay returns the original receipt without
refreshing its timestamp. Equal conflicting or lower sequence is refused. An
omitted root in a newer complete report becomes unknown rather than retaining
an older per-root success.

Adoption is disposable telemetry in the selected authoritative store. Its body
plus 128 fixed logical bytes are charged to the existing configuration quota;
replacement charges only the exact old-to-new delta. Quota failure rolls back
the report. Registration retention releases session and adoption occupancy in
the existing cleanup transaction. Reported release identifiers are history,
have no content foreign key, and do not pin retained releases/publications.
Reporting changes no operator ETag/generation, registration presence timestamp,
or per-poll Audit record.

`GET /applications/{applicationId}/configuration-status` requires
`management.configuration.adoption.read` on the resolved
`management.application/{applicationId}` resource. Authorization precedes
recipient paging and report-body reads, and every actor/resource-bound cursor
is reauthorized. The response contains current assignment resolution, retained
reported state, reported instance/version metadata and independent
`registrationFreshness` / `adoptionFreshness` facets. Both ages use the selected
service's existing stale interval; a report refreshes only its own receipt.
Closed registrations are explicitly offline.

Pages admit exact encoded entry bytes with a separate finite cursor/wrapper
allowance, including escaped Unicode registration metadata. A changed current
binding host does not rewrite an existing session's captured host contract:
the old registration and report remain visible as history with unknown current
convergence, even when their release/generation happens to match the new target.

`convergence` compares the report with the currently resolved release and
generation. Historical applied state for an older assignment remains pending
even when effective values happen to match. Matching rejected and pending
restart reports remain explicit; a terminal root remains faulted. Unknown modes
or states never become success. Current binding/cohort targets with no observed
recipient report `noInstances`. A semantically matching report can still say
`applied` when either freshness facet is stale/offline: consumers must require
the relevant fresh facets before presenting live fresh convergence. These ages
do not create a new client admission or readiness policy.

Reports and projections contain no payload, effective checksum, source token or
credential. Reported instance labels do not prove separate physical processes.
Acceptance does not prove that every business operation has switched captured
values, that subscriber external effects completed, or that the fleet changed
at one instant. Existing native and public fixtures retain their distinct
evidence limits; authored tests alone are not passing execution evidence.
