# Incoming logging queue

Public `CoreLogRuntime.Create()` and `Create(writers)` use two bounded stages. The caller performs early threshold/state checks, invokes an interested lazy factory at most once, and copies occurrence time, correlation, scopes, metadata and mutable event content into owned facts. It then attempts nonblocking incoming admission. One background worker encodes and prepares records before independent output queues deliver them. Microsoft provider calls use this same path.

`LoggingOptions.QueueCapacity`, captured at startup, bounds the incoming buffer and each enabled output buffer separately. Each worker can additionally hold one active record. A full incoming buffer rejects the new event without evicting earlier work or waiting for capacity. This is memory buffering, with no crash durability or retry promise. Registration remains startup-only.

## Acceptance and delivery

Before this correction, an accepted `Write` result described synchronous per-output admission after encoding. It now describes incoming acceptance: `Outcome=Accepted`, a positive contiguous `IncomingSequence`, and an empty `Outputs` collection. Writer tickets and delivery are observed later through diagnostics or flush/stop. `Rejected` with `IncomingQueueFull` means incoming capacity was unavailable. Early filtered, faulted and stopped decisions still avoid capture; invalid captured data still returns `InvalidEvent`. Caller factory exceptions retain their existing behavior.

`IsTruncated` reports omissions already known during caller capture. Additional encoding/rendering omissions appear in the wire marker and later diagnostics/flush/stop counters. No synchronous result is retrospectively mutated. `PartiallyAccepted` and the original per-output admission results remain available to the internal output-stage qualification seams; public incoming acceptance does not predict their later outcome.

`Incoming` observations on diagnostics, flush and stop expose:

| Counter | Meaning |
| --- | --- |
| Accepted | Number of incoming acceptance tickets through the observation boundary |
| Processed | Events dispatched through writer admission, whether writers accepted or rejected them |
| Failed | Background preparation/dispatch failures |
| Abandoned | Accepted events discarded before dispatch when shutdown expired |
| Pending | Queued events or active background preparation |
| QueueFull | New incoming events rejected for lack of capacity |
| RejectedByWriters | A subset of Processed: events rejected by at least one interested writer |

`Accepted = Processed + Failed + Abandoned + Pending`. Writer accounting remains independent. Processing does not mean delivery. A covered accepted event rejected by a writer makes flush/stop `Succeeded=false` and `LossObserved=true`, even if every event that reached that writer subsequently completed. Incoming rejections are also visible as loss; they have no acceptance ticket.

## Flush and shutdown

Flush captures the current incoming sequence. The worker processes that prefix, registers its resulting writer boundaries, and only then processes later inputs. Flush waits for both stages using one original monotonic budget. If it times out before the input prefix reaches writer admission, its incoming observation retains pending work and its output list is empty: no downstream boundary is invented. A timed-out flush leaves accepted work running. Caller cancellation detaches that waiter while preserving shared controls.

Stop closes incoming admission and drains accepted work through the same two stages. Its one captured shutdown budget includes background processing, output draining and cleanup. At expiry, queued input and preparation that has not committed to writer admission are counted as abandoned. Preparation that finishes later cannot send that discarded event to a writer. Already-started writer I/O may remain pending; cancellation is cooperative and resources are never disposed beneath it. `Completion` settles only when actual processing, I/O and cleanup end, possibly after the immutable stop result has been returned.

This implementation preserves the existing file recovery, retention, borrowed-console, provider ownership and fixed output-boundary behavior. Output-only internal test factories are explicitly marked as such; their historical results do not claim incoming-stage coverage. `IncomingEventQueueTests`, `IncomingRuntimeTests`, the public shutdown children and the exact-package pipe-pressure consumer qualify the new boundary directly. Common built-in adapter composition, filters/interceptors, duplicate policy and convenience calls remain separately tracked corrective work.
