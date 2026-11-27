# Session-scoped peer invalidation

The `8571121` review identified that an obsolete tab could clear another tab's
newer valid session. Peer notifications now name only the retired session and
service associations. Both are existing nonsecret 64-character hexadecimal
associations; the closed 140-character string contains no operator identity,
credential, Configuration value, draft, receipt or operation key.

The coordinator captures that scope before terminal session cleanup. The
provider uses the same production receiver for BroadcastChannel and storage
notifications. A matching old-session peer still clears protected values,
drafts and local operation references immediately. A different session or
service ignores the notification, as do legacy unscoped and malformed messages.
Actual focus verification remains authoritative when a notification is missed.
Existing finite callback ownership, idempotent signout, clock handling and
same-session verification behavior are preserved.

Focused pure regressions use the production coordinator, protected-state owner
and receipt store. They cover both expiry and a changed-session check in an
older document while a newer valid document retains its exact draft and stored
original references. Matching signout still clears an old peer and aborts its
held check without abandoning the actual callback; the newer session is
preserved. Additional assertions reject malformed, unscoped and foreign
session/service notifications and verify independent transport fallback with
only the two permitted associations.

The focused pure batch passed 48/48; the complete affected portal suite passed
215/215 with no failures, cancellations or skips. The extended actual multi-tab
native regression passed 1/1 with no skips, retaining five browser journeys,
all eight required proof flags, zero Configuration mutations and unchanged SQL
fingerprints. Strict native guards and independent source/result review passed.
See [current verification](review-857-verification.md) for exact reports and the
retained compile failure. Earlier WORK-197 results remain separate historical
evidence. Ubuntu CI remains unverified, and this payload has not been published.
