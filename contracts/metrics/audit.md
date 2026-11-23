# Audit capture metric

Explicit Audit composition and `CollectCoreModules=true` add one counter,
`cis.audit.capture.results` (the qualified CIS name for `audit.capture.results`).
Its count unit exports as `cis_audit_capture_results_total`. The two dimensions
are `category` (`business`, `administration`, `security`) and `outcome`
(`captured`, `rejected`, `faulted`): exactly nine accepted combinations. The
optional existing collector preserves central `application` and `environment`
on every sample. Absent Audit composition or disabled Core collection adds no
Audit definition or stream reservation. Overrides cannot shrink below nine.

The boundary is one actual local capture attempt, including its selected detail
mapping and bounded encoding. Captured means immutable source data was formed;
it proves no stage, commit, broker confirmation, processing or archive storage.
Rejected means invalid data, a known capacity/lifecycle refusal or identified
caller cancellation; faulted means another ordinary capture/mapping fault.
The binding is captured at entry, so later attachment does not replay an active
capture. Optional telemetry never changes its actual result or original fault.

Definitions and handles are reused for the exact registration/host owner. There
is no observation history, extra queue, callback or scrape-time source work.
Audit adds no duplicate Messaging backlog, age, retry or failure stream. Event
types, subject/resource/operation keys, AuditIds, correlation, payload and native
exception text are excluded from labels and routine logs. Capture success is
quiet; selected failure warnings share a lifetime budget of eight reports with
at least thirty monotonic seconds between them, including across success flaps.

Shutdown joins actual captures and their optional diagnostic calls before
stopping the existing Metrics provider and Messaging dependencies. Existing
Messaging observations retain their own delivery meaning and ownership.
