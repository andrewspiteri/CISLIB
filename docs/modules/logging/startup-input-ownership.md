# Application and logging ownership

EnvironmentOptions is a mutable passive store populated by the application. There is no setup/snapshot publication or guarantee of atomic concurrent updates. Populate values before starting consumers.

LoggingOptions holds ordinary mutable values. Logging prepares selected settings explicitly. EventMetadata captures owned readonly allowlisted facts; later application assignments do not change those facts or captured events. The store creates no metadata and starts no service. Logging runtime/worker ownership remains pending implementation.

See [current environment guidance](environment-options.md). The WP03-S04 task receipts preserve historical qualification of the former unpublished snapshot model; they are not current lifecycle instructions.
