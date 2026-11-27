# Instance status wireframe supplement

Pending exact screenshot approval. This focused backoffice revision preserves the
accepted rail, colors, table spacing and right-side panel. It supersedes the
user-facing **Configuration adoption** name and the dense main-table presentation;
the native adoption/reporting contracts and existing approvals retain their
original meaning and evidence.

One table has exactly **Instance | Configuration version in use | Status**.
Names identify the reported registrations; a saved note/date identifies an
authorized immutable accepted version. No inferred v1/v2 numbering, assignment
generation or full native identity is a version label. Missing version metadata
uses **Version details unavailable**. No current accepted version uses **Not
reported**. Neither is an empty JSON value or permission to reconstruct content.

Known report/current-native facts support **Up to date**, **Needs restart** or
**Failed**. A stale, missing or unknown observation remains neutral **Unknown**
(or the existing truthful pending observation); the interface never derives
failure from a notification fault alone. Reported in-use, current desired and
historical last-successful versions stay separate facts. The caption, **Last
reported Configuration per instance**, makes this time boundary visible.

Selecting a row opens the existing right-side panel with simple status, reported
version and report time. Current deployed metadata may appear as a separate
fact. Native IDs, raw facets, generation and historical observations remain in
collapsed Technical details when authorized. Close/Escape and focus restoration
retain the existing panel behavior; no extra inspection screen is introduced.

Protected/version metadata must not expand AdoptionRead-only prerequisites.
Denied labels remain unavailable, and late/session/target/stale observations
retain the existing protected-state rules. This design source creates no
production implementation, runtime guarantee or native verification result.
