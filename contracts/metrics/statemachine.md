# State Machine observations — v1

This catalog accompanies the [accepted profile](../../docs/changes/CIS-0014/implementation-plan.md)
and [C# observation guide](../../docs/modules/metrics/statemachine.md). Required
names and authored tests are contracts, not a claim of passing exporter or
platform qualification. Actual native results remain separate.

| Logical name | Kind / unit | Canonical keys | Reserved normal combinations |
| --- | --- | --- | --- |
| `cis.statemachine.trigger.duration` | Timer / Seconds | machine, outcome, source, trigger | 2560 |
| `cis.statemachine.active.attempts` | Up/down counter / Count | machine | 16 |
| `cis.statemachine.handler.duration` | Timer / Seconds | handler, machine, outcome, phase | 384 |

Trigger outcomes are `success`, `rejected`, `busy`, `error`, `cancelled`.
Handler phases are `execute`, `error`; their outcomes are `success`, `error`,
`cancelled`. Business codes, exceptions, IDs and payloads are never dimensions.
Every sample retains authoritative central `application` and `environment`,
including histogram and overflow points.

Every valid public Trigger call captures one complete three-handle binding and
records its known duration through final result publication and actual cleanup.
This includes Busy and pre-cancelled calls. Active attempts add one only after
the instance gate is acquired, and subtract one after all owned work settles.
Admitted mapping rejections count as active; Busy/pre-cancelled calls do not.
Closed-host lifetime misuse is not an operational trigger attempt.

Only entered Execute/Error methods produce handler observations. Direct mappings,
construction, disposal and never-entered methods do not invent handler samples.
A successful forward phase remains successful if later compensated. Each
duration is the runner's exact monotonic measurement, shared with its report and
recorded through `ITimer.Record`, without an independent timer. Unknown durations
are omitted; active work still balances. Histograms supply completed counts.
There is no duplicate completion counter, per-entity gauge or history.

Build transactionally admits a union of at most sixteen machine aliases,
512 `(machine, declared-source, trigger-label)` tuples and sixty-four
`(machine, handler-alias)` pairs per owner. Tuple candidates contain the complete
declared state/trigger product, including `invalid` for every declared source,
so rejected combinations are budgeted too. Repeated builds reuse metadata;
different definitions under an alias add only their distinct label tuples.
Failed admission changes none of the retained sets. These rules apply with
disabled/unselected Metrics and retain no enum types, context or machine instances.

Names are nonempty printable Unicode within 256 UTF-8 bytes. Undefined runtime
trigger values use literal `invalid`, never numeric formatting. A declared
member named `invalid` uses `enum:invalid`, which cannot collide with another
C# member name. Unaliased handler metric values use `handler`; explicit On aliases
select their own finite values. Default CLR display names remain in reports/logs.
No truncation, hashing, unknown-label cache or correlation label is introduced.

The conservative domains are `5 × 512`, `16` and `6 × 64`, reserving 2,960
combinations. Standard fourteen duration bounds apply unless an ordinary fixed
stream override selects another permitted profile. Smaller capacity overrides
fail setup. SDK zero-dimension/overflow points remain additional.

`AddCisStateMachines` installs its own passive marker. It reuses the existing
workflow host barrier but neither opts into Pipeline collection nor changes
Pipeline's single-use contract. Final metadata is inspected before provider
activation in either Metrics registration order; the registered instrumentation
owner then attaches once. Disabled Metrics or `CollectCoreModules=false` does
not attach it. Runs begun before attachment are not replayed or partly rebound.
One mutable owner cannot attach to a second Metrics host.

With default Core/incoming selection, State Machine alone has 22 Core
reservations, 24 streams and capacity 6,288; adding REST/outgoing Auto has 26 Core
reservations, 30 streams and capacity 7,376. With Pipeline too, these become
25/27/6,736 without REST or 29/33/7,824 with REST. Hosts without State Machine
opt-in retain their existing profiles. Application instruments are additional.
Global 96 definitions, 128 streams, 16,384 total capacity and four-MiB response
limits remain unchanged. Profile arithmetic is not a heap/footprint claim.

Expected exported families are `cis_statemachine_trigger_duration_seconds` and
`cis_statemachine_handler_duration_seconds`, with `_bucket`, `_sum`, `_count`
children, plus `cis_statemachine_active_attempts`. Verify actual exposition before
claiming those names as observed output or building deployment queries on them.
