# Shared coordination — portable contract v1

RM-05 provides one authoritative current temporary grant per key. It does not
physically stop paused or uncooperative code. Correctness-sensitive database
effects require the same-transaction ownership guard; remote effects require an
explicit recipient-enforced fencing or durable-operation contract.

The [supplied requirements](../../planning/coordination/sources/CISLIB_RM05_Shared_Coordination_Specification_v1.0.md)
and [CIS-0022 profile](../../docs/changes/CIS-0022/implementation-plan.md) govern
this additive Core module. Job definitions and scheduled integration are described
in [jobs](jobs.md). These contracts do not deliver a production recurring scheduler.

## Identity and representation

The key is four separate, ordinal, case-sensitive valid Unicode values. No
trimming, case conversion or Unicode normalization occurs. Empty/all-whitespace,
control characters and unpaired UTF-16 surrogates are rejected before store I/O.

| Component | Maximum UTF-8 bytes |
| --- | ---: |
| Authoritative environment | 1,024 |
| Execution scope | 256 |
| Stable duty ID | 128 |
| Resource ID | 512 |

The explicit resource `default` identifies an unpartitioned duty. Category,
display name, schedule/revision, occurrence, owner and generation are outside
the key. The 1,920-byte complete key fits the selected PostgreSQL ordinary 8 KiB
block profile with index-tuple overhead. The provider validates its storage profile.

Canonical key bytes are the environment, scope, duty and resource in that order.
Each component is prefixed immediately by its four-byte unsigned big-endian UTF-8
byte length, then its exact UTF-8 bytes. SHA-256 of that representation is an
optional bounded exact-resource identifier, not a capability or authentication.
PostgreSQL persists the separate components with `COLLATE "C"`.

Owner application is bounded to 1,024 UTF-8 bytes; process instance to 128.
The C# integration captures the existing authoritative application/environment/
process identity. Every fresh acquisition uses a new nonempty UUID, which also
serves as GrantId. Renewal preserves acquisition and generation. A grant carries
the installed authority UUID, full acquisition, positive Int64 generation and
observed UTC expiry. Independent authority installations require distinct UUIDs.

Generations use **decimal strings on JSON boundaries**, including values above
9,007,199,254,740,991. They are ordering values, never bearer credentials. Portable
duration units are integer milliseconds; C# provider/lifetime APIs use `TimeSpan`.
The module has no remote endpoint or automatic JSON protocol for runtime results.

## Acquisition and uncertainty

Acquisition distinguishes Acquired, Busy, Unauthorized, InvalidRequest,
Unavailable, CapacityExceeded and Indeterminate. Only a committed, confirmed
Acquired result carries a usable grant/managed lease. Busy may carry a separate
observed-owner diagnostic, never a usable grant. A missing diagnostic is unknown,
not a fabricated owner or zero generation.

Unavailable means this acquisition is known not to have committed. Indeterminate
means it may have committed. An indeterminate mutation is not blindly replayed.
Bounded exact-acquisition observation may adopt a matching live committed grant
only while the attempt remains locally eligible and has a safe confirmed budget.
NotObserved proves only that a matching live grant was not observed at that instant.
Abandonment is permanent; late responses cannot start work or renewal.

Renew/release match exact authority, key, owner, acquisition and generation.
Renewal also requires current unexpired authority. Successful renewal must contain
the same immutable grant identity and positive remaining validity. Grant-free
Succeeded is valid for release/prune, not renewal. AuthorityUnavailable is not a
NotCurrent observation. Expired/released grants cannot revive, and delayed release
cannot modify a successor.

## Managed timing and lifetime

Defaults are lease 30 s, renewal 10 s, request timeout 2 s, safety margin 5 s,
maximum graceful drain 30 s, exact-acquisition recovery 5 s, observation delay
250 ms and renewal retry delay 500 ms. Renewal interval + request timeout + margin
must be strictly less than lease duration. Durations are positive and bounded to
one day; recovery/retry intervals must fit their configured windows.

The local deadline uses monotonic request timing and acknowledged remaining
validity. A response does not start a fresh full lease duration. Unconfirmed
renewal cannot extend the deadline. An acknowledgement arriving after the old
deadline cannot revive authority even if a delayed scheduler has not yet emitted
the loss transition.

The local states are Acquiring, Owned, Draining, Lost and Released. Lost and
Released are terminal. Loss notification has one stable reason and runs outside
state/store locks. The cancellation signal/termination task does not prove that
independently spawned work has stopped. Direct consumers drain their actual
business tasks and scopes before release; job adapters own that responsibility.
Unresolved operations and unfinished callbacks remain observed and counted.

Managed capacity defaults to 64 live/unresolved attempts or handles, eight
acquisition requests and eight independent renewal requests. Snapshot pages
default to at most 100 entries. Native availability/ownership observations are
cached diagnostic facts; scrapes and health reads perform no database work.

## Original-transaction guard

The provider's short coordination transactions do not own business completion.
The guard uses the application's original RM-02 connection and transaction, exact
database alias, ReadCommitted isolation and installed authority. It locks the key
without an early expiry predicate, obtains actual current authority time after
the lock, validates exact live identity and retains the row lock through application
commit/rollback. Every established rejection marks that context rollback-only.

Existing lock order is inbox (if applicable) → RM-03 journal claim → coordination
row → application rows → outgoing records. RM-03 calls the handler outside native
callbacks; await the guard inside that handler before protected effects. Do not
recursively enter the same ADO.NET callback or open a hidden second transaction.

A transaction admitted while valid can commit after nominal expiry: its row lock
delays takeover until completion. Keep business transactions short, outside idle
waits and long remote API calls. Preserve Committed/NotCommitted/Unknown and
cleanup diagnostics separately. Lease loss is not rollback. Read-only authorized
result recovery remains available after loss; a new protected effect still needs
current ownership. Valid committed outgoing intent remains deliverable after the
originating lease ends.

The C# [contracts](../../implementations/csharp/src/CIS.Core/Coordination/Contracts/CoordinationProvider.cs)
are implemented under the [module guide](../../docs/modules/coordination/README.md).
Native qualification and remaining limits are recorded in
[CIS-0022 verification](../../docs/changes/CIS-0022/verification.md); source and
contract tests alone do not prove SQL locking, authentication or broker durability.
