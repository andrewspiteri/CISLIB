# Application and logging ownership

EnvironmentOptions automatically populates readonly application/process/runtime facts in its static constructor, applies known environment overrides once and interns validated common identities. Its application-selected EnvironmentKind, CommitId and BuildId remain mutable, as do the logging module settings. Complete those assignments before starting consumers; concurrent mutation is not an atomic setup transaction.

LoggingOptions holds caller-owned mutable values. Prepare validates the selected settings and captures a normalized file path without creating outputs. Logging runtime composition then owns copied settings and EventMetadata captures readonly allowlisted facts; later caller assignments do not change an existing runtime or captured event.

Actual internal Core runtime composition owns independent bounded workers, file handles/inventory and its periodic timer. Borrowed console resources stay open. The public shared StopAsync operation now closes admission and captures one original bounded observation. Public creation, deadline abandonment/cancellation, DisposeAsync integration and eventual safe resource release remain required in WP05-S10; the internal composition already performs actual output work.

See [current environment guidance](environment-options.md) and [shared shutdown preparation](../../../planning/logging/shutdown-implementation-notes.md). WP03-S04 receipts preserve historical qualification of the former unpublished snapshot model; they are not current initialization or lifecycle instructions.
