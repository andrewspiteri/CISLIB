# Logging review corrections — 15 October 2026

This is the current unpublished behavior addendum for the supplied [static review](../../../planning/logging/reviews/2026-10-15/source-review.md). It supersedes contradictory earlier development descriptions. The review inspected a partial source archive; its proposals are input to this corrective task, not evidence of executed tests or authority to resume the roadmap. Source plans, historical receipts and published baselines remain unchanged. The roadmap remains paused at WP07-S02-T01.

## Existing decisions retained

C# starts in one `CIS.Core` assembly/package, using `CIS` and `CIS.Logging` namespaces. MicrosoftLogger remains a folder/namespace in Core with the explicitly approved Microsoft package closure; third-party integrations remain separate. `EnvironmentOptions` remains a static container: its constructor discovers readonly facts, applies known overrides and interns four bounded identities. Module logging settings are configured before runtime creation. Severity aliases remain accepted. Valid oversized payloads truncate, with a default 64 KiB entry cap and the selected 16 KiB–16 MiB range. Creator-disposes, LIFO scope ownership and the documented .NET inherited-context detection limits remain unchanged.

## File transitions and private candidates

Rotation and partial recovery use the same transaction sequence under the cooperative group lease:

1. Compute the exact next inventory and retired prefix. Check collisions and identity/selected limits before destructive changes.
2. Flush/hash the original active and hash every exact planned retirement. Publish schema-2 intent containing old/new inventories, original active length/hash, and each retired member's exact ID/name/length/hash.
3. Retire only that prefix. Rename the original active to its closed/damaged identity, publish the next inventory, create an empty fresh active and remove the intent.

A full group with N retained members can recover a partial active: retire the oldest owned retained member, preserve the whole partial active as damaged, and create the next active. The committed result still contains N retained members plus one active. A lower startup bound than the pre-existing group continues to fail before committing a new transition.

| Observed restart state | Required action |
| --- | --- |
| Old inventory and original active; none/some/all exact planned retirees missing | Validate all surviving members and bytes, finish exact retirement, rename and publish |
| Old inventory; verified archive present; active absent | Finish the verified plan and publish next inventory/create active |
| Next inventory; verified archive present; active absent/empty | Complete fresh creation if needed and remove intent |
| Missing surviving member, changed retirement bytes, changed original, ambiguous identities or nonempty new active | Fail without guessing or appending |

Legacy schema-1 intents remain readable only with their original already-retired-prefix rule. A missing member without valid intent remains an incompatible group. Once intent has been published, retirement is committed work: a failed attempt can leave the next documented stage for a later owner. Pre-commit rollback and committed recovery are distinct. A deletion failure stops growth and preserves the intent; it does not delete neighbors or silently ignore arbitrary missing files. This is deterministic process recovery, without a power-loss durability or exactly-once ingestion promise.

Each metadata kind uses one private `.candidate.tmp` slot. Only a file successfully created by the current attempt is cleaned on failure. Occupied files/directories are preserved and publication fails with `FileMetadataCandidateUnavailable`. Cleanup failure reports `FileMetadataCandidateCleanupFailed`; the occupied slot blocks further attempts instead of accumulating GUID files. Old private candidates and foreign similar names are never adopted or swept. An operator may inspect and remove a stale private slot after stopping all owners; startup does not guess ownership from its name alone.

## Scope validity and attribute priority

A structured scope's operation_id must be a valid string or null. Null, empty and whitespace mean absent, matching OperationContextData; they inherit the outer identity and are omitted from attributes. Other types, including Guid and integers, reject the event once rather than silently inheriting another identity.

Provider-owned scopes validate/copy at BeginScope and throw on invalid input. Borrowed host scopes validate/copy at event capture: invalid identifiers, malformed Unicode, cycles, non-finite numbers and unsupported root/value types reject that event and increment InvalidEventCount once, without admission or TruncatedEventCount. The formatter retains its existing before-scope timing and exception behavior. Bare strings are unsupported through either route; Microsoft's structured formatted scopes remain supported. Oversized valid scopes truncate.

Resolve key precedence first: explicit event attributes override scopes, and inner scope attributes override outer attributes. Retain event attributes before remaining scope attributes under the aggregate node budget. Within each group, existing input selection bounds remain; byte selection orders retained event keys ordinally before retained scope keys. Nested maps remain ordinal and arrays preserve order. Loss provenance remains sticky.

The provider's numeric `event_id`, including zero and negative IDs, cannot be replaced by user fields and is reserved before node and byte truncation. It remains an attribute in the JSON schema. Direct Core attributes named event_id remain ordinary caller data. Canonical JSON still sorts emitted map keys ordinally.

## Rendering, deadlines and observations

A rendering failure leaves an already-faulted destination's original code/time intact. Live metadata publication failures retain their fixed safe candidate-slot categories. Worker budget checks and work selection share one gate section; the monotonic sample remains outside that section, so it records the last observed time and does not promise atomic wall-clock I/O cancellation.

Canonical invalid capture/encoding still produces global InvalidEvent. If a valid canonical event cannot fit one destination's required human envelope, that destination faults with safe `RenderTooLarge`, receives no ticket for that attempt and records unavailable admission. Other interested outputs continue. The result is PartiallyAccepted when any healthy destination accepts it, or Rejected when none does. This does not increment global InvalidEventCount. Runtime diagnostics and the fixed writer-flush result retain the destination failure; recreation is explicit. Dynamic event/component names make the actual event the final rendering check; no worst-case-name startup restriction is introduced. Current automatic host metadata is OS-derived; the extreme host fixture uses the internal metadata validation seam.

Flush/stop timing uses monotonic worker observations of covered record settlement and writer-flush completion, rather than timer callback dispatch or waiter continuation order. Completed requires terminal dispositions before the budget expires; Succeeded additionally requires all covered records written and writer flushing successful. Late work can be terminal with Pending=0 and still report TimedOut/Succeeded=false. Timely work observed by a delayed caller remains Completed. Counts are frozen at the first Core final observation; a delayed callback cannot reconstruct historical counter values at an unobserved nominal instant. UTC remains a label.

The first stop owns one original budget; cancelled/joining callers do not restart it. Workers also check that budget before starting later work, so delayed timer dispatch cannot start later writes/flushes after expiry. Queued work is abandoned; active I/O retains ownership until it actually settles. Shared flush registrations can remain while a covered record still needs its terminal observation, even after the flush itself has failed; retention stays bounded by live accepted boundaries, without per-caller worker lists.

Public immutable diagnostics now include InFlight (0/1 unresolved record write) and Buffered = Pending - InFlight. An outstanding buffer flush alone is not an in-flight record. Accepted = Written + Failed + Abandoned + Pending remains true at every observation.

## Queue transition invariants

All ticket/disposition changes use the same admission gate; I/O and caller callbacks run outside it.

| Transition | Accounting and ownership |
| --- | --- |
| Interested acceptance | Advance one output ticket; Accepted/Pending increase; retain one owned representation |
| Queue full or unavailable | Increase the matching rejection counter; no ticket or existing-record mutation |
| Dequeue/start write | Buffered decreases, InFlight becomes 1; Pending is unchanged |
| Successful complete write | Written increases, Pending decreases, InFlight becomes 0 |
| Failed started write | Failed increases for that ticket; queued tail is abandoned; output admission closes |
| Stop deadline | Abandon only unstarted accepted records; an unresolved write remains Pending/InFlight; no later I/O starts once expiry is observed |
| Late I/O completion | Update live dispositions and release owned resources when safe; keep cached stop result unchanged |
| Buffer flush | No accepted-record partition change; retain its shared boundary until flush and covered records settle |

## Simplicity and capacity

QueuedRuntimeFactory owns acquisition/rollback for production and controlled fixtures. Admission, ticket assignment and queue dispositions stay together under the existing gate. File metadata publication and transition commit are shared, with no public pipeline or new dependency. Immutable resource projection is cached per metadata instance; equal console/file presentations share prepared bytes. Unicode, sizing and recovery validation remain intact.

QueueCapacity limits buffered records per output, not bytes. Allow one additional in-flight record, producer work and worker/control overhead. `QueueCapacity * MaxEntryBytes` is a planning bound for one queued encoded representation, not a managed-memory guarantee. Human outputs can retain both canonical JSON and prepared human bytes; two outputs may share canonical/equal presentation arrays, but capacity planning should not depend on sharing or GC timing.

| Selected settings | One queued representation per output |
| --- | --- |
| 128 records × 16 KiB | 2 MiB |
| Default 1,024 × 64 KiB | 64 MiB |
| Maximum 65,536 × 16 MiB | 1 TiB |

The selected ranges remain independent, with explicit deployment guidance rather than a new arbitrary cross-option restriction. Choose an entry cap for realistic events and a queue capacity for the expected burst and memory budget; size each enabled output independently. For example, a 16 MiB payload budget at 64 KiB per entry allows 256 buffered records for one representation, before reserving extra memory for human bytes, in-flight work and producers. Large caps combined with large capacities require deliberate application sizing. See the [review receipt](../../../planning/logging/reviews/2026-10-15/README.md) for measured allocations/retention and their limits.