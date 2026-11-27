# Editor Refresh correction — review87d18ba6

The page revision now reaches the existing editor reader without changing its
React key. Local and parent revisions jointly identify the protected read,
including applied context/recovery readiness. The shared hook exposes a busy
read immediately when the requested revision changes, preventing the old reply
from being applied under a new revision before effects start the new request.
Same-target draft ownership and selected immutable Shared provenance stay in
place; refreshed source/map/permissions replace only the current read context.

The actual native regression starts by failing delivery of one real editor GET,
then retries it through the page Refresh. It keeps a draft while a second native
operator publishes App content and changes deployment, holds a fresh editor
response to assert disabled Save, and then saves with those new ETags while
retaining the originally selected Shared publication. Save never deploys.
The parent verifies exact bytes/SHA, immutable pair, current map and four distinct
Operations/Audit originals with their actual two native actors. The concurrent
operator is a distinct scenario-only Refresh identity; the existing lookup-only
actor keeps its negative grants. This native regression passed 1/1 with no
skips, and independent source/result review found no required change. The
affected pure selection passed 212/212; builds and final scoped formatting
passed. Exact phases and evidence limits are in the
[verification record](review-87d-verification.md).
