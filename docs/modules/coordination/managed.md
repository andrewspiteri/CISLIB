# Managed ownership

`CoordinationRuntime` is one singleton engine over an application-selected
`ICoordinationProvider`. Its constructor copies and validates `CoordinationOptions`
without contacting the authority. `StartAsync` validates the installed authority
once, then starts one bounded local scheduler. The runtime remains independent of
the application facade, hosting adapters and business handlers.

`CoordinationService` is scoped. It asks the application's
`ICoordinationTargetResolver` for the actual key and complete capability target,
checks the authoritative local environment, and awaits the existing scoped
`ICapabilityAuthorizer`. A trusted resolver may map a cross-application shared
target; permission still comes from authorization against that complete target.
The owner application and process instance always come from
`LocalApplicationIdentity`. The singleton retains only the resulting immutable
`CoordinationAcquisition`; it never captures the resolver, authorizer or scope.

## Confirmed budget and uncertainty

Only `ManagedCoordinationResult.Outcome == Acquired` contains a usable lease.
Busy preserves an optional observed-owner diagnostic. Unavailable and
Indeterminate remain distinct from contention and from a business transaction's
Committed/NotCommitted/Unknown result.

For every acknowledgement the engine uses the monotonic request start, clamps
the returned remaining validity to the requested lease duration, and subtracts
the safety margin and all elapsed request time. UTC changes do not extend this
budget. Renewal accepts only the exact installed authority, acquisition and
generation. A grant-free or mismatched successful renewal cannot extend the
prior confirmed stop. Immediately before accepting an acknowledgement the engine
checks the current clock against the old stop, even if the expiry scheduler has
been delayed. Lost and Released never revive.

A timed-out or indeterminate acquisition is never replayed. For the finite
recovery window the engine may issue exact `ResolveAsync` reads. NotObserved
proves only what that read saw; it cannot establish that an unfinished mutation
will never commit. A matching live committed grant is adopted only while the
attempt remains eligible and has positive conservative validity. Caller
cancellation, shutdown or recovery expiry permanently abandons the attempt. A
known late exact grant may receive one best-effort conditional release; it can
never start work or renewal.

## Bounds and actual settlement

The default capacity is 64 managed live or unresolved reservations, eight
physical acquire/resolve/release requests and eight independent physical renewal
requests. Each handle has at most one unfinished renewal. Acquisition pressure
cannot consume renewal capacity. No per-definition timer, unbounded retry queue
or permanent request history is retained.

Local timeout stops waiting for a physical request; it does not pretend that the
provider task ended. The actual task remains observed and retains its physical
slot and managed reservation until settlement. The same rule applies to terminal
cancellation callbacks. A stuck callback does not run on the scheduler or under
the state gate, and unrelated eligible leases can continue renewing. Callback
faults cannot replace the stable terminal state.

`GetSnapshot(skip, take)` reads a detached bounded page of cached facts with no
authority I/O, renewal scheduling or cancellation callbacks. `OwnedLeases` counts
all Owned/Draining handles independently of paging. The page can be stale after a
process pause; it never authorizes an effect. `lease.State` rechecks the monotonic
stop before reporting local admission state. Optional library instrumentation
uses finite outcomes and actual physical call duration and is included in the
runtime's settlement barrier.

## Consumer lifetime

A direct consumer checks for Owned before admitting its next finite unit, passes
`OwnershipLost` into cooperative work, and uses the ownership guard in its
original business transaction for correctness-sensitive effects. A checked local
state or generation is insufficient enforcement by itself.

After admission closes, call `BeginDraining` and join every actual business task
and owned scope before calling `ReleaseAsync`. The handle cannot discover work
spawned independently by the application. Release becomes locally terminal once;
an uncertain remote release cannot restore admission. Calling release on an
already Lost handle reports local Unavailable, rather than inventing an
authoritative NotCurrent observation. Its old authority record expires normally.

`BeginStopping` closes runtime acquisition and begins bounded drain. Renewal may
continue for already admitted work until the earlier confirmed stop or finite
drain deadline. On exceeded drain the handle becomes Lost, signals cancellation
and stops renewing. `StopAsync` and `DisposeAsync` join all actual runtime-owned
provider requests and notifications. They can remain incomplete if a provider or
cancellation callback ignores cancellation. A timeout around an external wait
does not discharge the application's cleanup ownership.

These methods do not own arbitrary business callbacks. The Jobs adapters retain
their own actual callback capacity and same-key occupancy until those callbacks
and scopes settle. Lease loss does not prove rollback or erase a durable
operation result; authorized read-only recovery can proceed after ownership ends.

The [public testing helpers](testing.md) demonstrate local contract behavior;
the [portable contract](../../../contracts/coordination/README.md) and
[verification record](../../changes/CIS-0022/verification.md) distinguish that
evidence from real SQL locking, authentication and deployment guarantees.
