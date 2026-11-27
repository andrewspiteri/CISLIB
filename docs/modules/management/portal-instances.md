# Current and past instances

The application Instances tab has two expanded sections. **Current instances**
contains the loaded page's unclosed native registrations, with their own reported
application versions. Recent observations say **Recently seen**. Stale
observations say **Not recently seen**; a missed report does not prove that a
process stopped. Future presence values stay **Unknown** rather than success.

**Past instances** contains only explicitly closed retained registrations. It
uses the native closed time and each registration's own reported version. An old
last-seen time, a reported lifecycle of Stopped or an unfamiliar future token
does not move a row into this section. Reused reported instance names are not
deduplicated; selection and panel identity remain the exact registration.

Both sections and their counts describe the authorized page, with one existing
bounded pager. An empty section says **on this page**. This is neither a fleet
total nor complete execution history; native retention can remove old records.
Actual timestamps remain labelled source observations; exact identifiers are in
the selected panel's closed Technical details. Session/access changes and superseded requests retain the
portal's existing protected read and callback-settlement behaviour.

The pure `instance-groups.test.mjs` cases cover uncertain state, age/lifecycle
non-authority and duplicate labels with distinct versions. The existing native
Management/browser fixture seeds an actually registered-then-closed earlier
instance and checks that Current and Past both remain expanded. The corrected
R08 native desktop case passed, including the earlier closed row's actual 0.9.0
version/Closed assertions. Its first locator failure remains retained and was
corrected without weakening those assertions. The three pure grouping cases are
already included in the 144 portal checks, not an additional total. See the
[R08 checkpoint](../../changes/CIS-0030/r08-implementation-checkpoint.md).
The R08 screenshot decision approves presentation only; native evidence is
recorded separately.
