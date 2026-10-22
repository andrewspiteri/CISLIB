# Retained Windows Pipes survivors and timeouts

This is a source-bound review of the initial Windows report, not a new native
mutation result. The [machine-readable inventory](historical-remaining.json)
retains **all 139 requested records: 114 Survived and 25 Timeout**, with exact
historical file, native span, mutator, original source slice, replacement,
assessment and next evidence. It covers Budget 5, Endpoint 14, Server 84 and Probe 36.
Every original is extracted from that report's embedded source; changed current
files are not used to reconstruct historical mutations.

The retained report has 89 Killed, 114 Survived, 25 Timeout, 60 CompileError and 75
Ignored. Its child audit was invalid: 165 stage collisions, 105 missing terminal
receipts and one temporary artifact. These defects prevent blanket credit for
even the native Killed labels. The review changes no status or denominator,
requests no exclusion, and does not establish the strict 90% gate. Compiler errors,
unknown child failures and native timeouts are never promoted to assertion kills.

| Historical input | SHA-256 |
| --- | --- |
| [Native report](../../../../../../.artifacts/healthcheck/release-next/pipes-windows-01/native/reports/mutation-report.json) | `29424c1ebfea6a595163a04c67941d2ae71ddf7321fb6e798f61eb0aa17fd8fb` |
| [Actual config](../../../../../../.artifacts/healthcheck/release-next/pipes-windows-01/config.json) | `1a8f81da060bbcfd22a58fe5ea99aa2307a33c6a466700a5c9a2103a1baff532` |

## Concrete detector work

The following historical IDs motivate meaningful receiving/output/lifecycle
assertions. Authoring a detector does not claim the corresponding mutant was
executed and killed.

| IDs | Observable obligation and detector |
| --- | --- |
| 2705,2707|Caller cancellation and exact total monotonic boundary. Root internal budget cases and real server 999/1000/1001 cases separate delayed timer delivery from elapsed-time expiry.|
| 2713,2714|Owned linked wait handle and real TimeProvider timer are disposed after actual settlement; caller-owned handle remains open. Root internal seam plus real server timer case.|
| 2749,2764,2781,2946|Safe stop before start, terminal admission, typed disposed receiving boundary and harmless repeated disposal. Repeating only Stop before disposal would mask 2946's later Cancel on a disposed source.|
| 2751,2752,2755,2758|Public null/parameter and nonpositive clock-frequency constructor validation before opening an endpoint.|
| 2777|Dispose must remain pending while the actual synchronous source is held behind an unreleased gate. The approved new case asserts immediately while that source has entered, then releases and joins actual disposal, stop and request. It is later than snapshot 03.|
| 2971,2972,2973|Internal probe caller-resource guards. Actual executable arguments/console resources are always nonnull; root internal tests exercise this distinct seam.|
| 2977,3007,3017|Errors retain a bounded meaningful reason after an optional program label, without asserting an exact cosmetic line or unpublished English sentence.|
| 2999|Successful actual probe stdout equals the selected snapshot bytes followed by one newline. JSON parsing alone misses the deleted newline.|
| 3058|Valid inclusive timeout 60000 and max-report-bytes 16777216 CLI values succeed with actual complete responses.|
| 3064|An actual unavailable caller TextWriter cannot replace documented exit 2 with an unhandled error. Root internal tests use real failing writer operations.|

The four [source-partition diagnostic configs](../../../../qualification/gate-closure/pipes-parts/README.md)
retain all mutator kinds and complete disjoint coverage of the same four source
files. The server-side partitions select the full real pipe class, internal unit
seams and actual selected-view probe rows. The probe partition selects its full
real process class plus internal seams. They support bounded iteration, not a
qualification shortcut. Each partition needs its declared exact closure, passing
baseline, current child-origin audit, raw status inventory and final full-scope
reconciliation; the closures intentionally differ.

Two further concrete requests were sent to the coordinator:

- For 2721/2735, create one real endpoint with maximumClients 2 and first:true,
  then attempt another first:true claim while capacity remains. A public duplicate
  server often fills all instances first, letting the variant fail for capacity
  even when ownership protection is missing.
- For 3001, run the real internal probe over a real BufferedStream with capacity
  larger than the small selected snapshot, then inspect its underlying stream
  immediately after the returned exit. This observes delivered output before
  test-owned flush/disposal, rather than asserting that a private Flush method
  happened to be called.

## Narrow source proofs and candidates

The inventory explicitly restricts source equivalence to the affected domain:

- 2727 preserves inheritance on a newly constructed PipeSecurity before any ACE
 is added. There are no inherited ACEs for either choice to retain.
- 2787/2788 change only initial capacity of a growable List; successful creation
 still adds the same actual slots.
- 2810/2811 depend on a false return from a successfully returning generic queue
 call. Both success branches of the pinned runtime return true. Queue operations
 can still throw, so the partial-enqueue cleanup paths2827/2829/2833 remain real
 exceptional obligations. This finding follows the pinned [runtime implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs).
- 2874 redistributes exceptions between two terminal catches. With cancellation
 false both initiate BeginStop. Cancellation true is possible only after this
 private owner set stopping, so an extra BeginStop immediately returns. The same
 finally/drain path remains.
- 3020 changes a field of an unused initial settings allocation in the historical
 parser. A false parse discards it and a successful parse replaces it.

These are reasoning records, not status edits or denominator waivers.
Allocation pressure, other runtime versions and changed control flow are not
silently included in the narrow proofs.

Historical 3032/3036/3041/3044/3047/3048/3051 are only version-bound equivalence
candidates: early true returned the then-initialized default settings with empty
pipe name, which still failed into documented malformed-input exit 2. The approved
nullable-out cleanup now removes that unused allocation, so the same syntactic
mutation may instead cause a null-settings managed failure. Keep the original
definitions intact. A later native Killed label receives no credit until complete
actual Core/selector/MVID/async-frame attribution and positive/negative controls
prove that production failure. Unknown child termination remains invalid.

Other candidates remain deliberately weaker. The invalid-handle disposal2738
closes no valid OS pipe handle, but managed SafeHandle bookkeeping may differ.
The ordinary token can give2724 the same default owner; that is not proof for all
supported Windows token configurations.2853/2873 have already failed replacement
publication because stopping won, so extra loop/error routing appears terminally
dominated; the inventory does not claim a global equivalence result.

The ConfigureAwait candidates retain caller-context limits. Actual CLI and
ThreadPool-launched slot workers ordinarily have no special context, but arbitrary
caller/user callback contexts can differ. The public contract does not promise
that callers need not pump a supplied context. No non-pumping-context test or
scheduler-order assertion was added solely to kill 2778 or related continuations.
The actual Timeout on 2978 is not proof that this continuation caused it.

2711/2759/2768 change private English diagnostic text; 3062 removes only a cosmetic
CLI label while leaving the reason. No exact private sentence/prefix requirement
was invented to improve a mutation percentage.

## Open behavior, platform and resource evidence

Unix option mutants 2718/2719, first-instance 2720/2721, rejected-peer continuation
2847 and client options 2981 need actual platform/identity evidence. In particular,
permissive 0777 socket mode with other-UID denial, same-owner success before/after,
exact loaded Core and no capture on denial can distinguish peer checks from
filesystem permissions. Windows survival does not exercise the Unix branch.
Windows first-instance 2735 has its own actual name-ownership obligation.

Recovery mutants 2848,2850/2854,2868,2870,2891,2895,2899/2900,2902–2909 require
actual accept/disconnect failure or overlapping recovery. Successful ordinary
client churn does not establish that a specific OS error/recreation branch ran.
The removed semaphore/replacement disposal operations are real ownership concerns,
not automatic equivalents. No invented native error or fake production transport
was introduced.

2742 leaks a real descriptor GCHandle on successful Windows creation. The new
timer/linked-handle tests do not observe this pin.2747 and the partial-enqueue
cleanup affect resource release under actual constructor/queue failure; absence
from a normal run is not proof those failures are impossible. Server-private
resource deletions 2784/2785 and replacement/slot cleanup likewise remain open
where no trustworthy public ownership observation exists. Private reflection,
mirrored counters and noisy process-global pin measurements were not used.

Post-stage budget checks 2984/2989/2991 may be dominated for the final healthy
stdout result by later guards, but also bound further request/read/parse work.
2992 follows synchronous parsing and precedes stdout writes that use the caller
token rather than the budget token, so a late parse can make it consequential.
No approved deterministic probe clock seam exists; sleep-only tests do not prove
these individual boundaries.

3013 narrows the error filter: the and-pattern combines two unrelated exception
types, removing their standalone alternatives. SocketException is not covered
by IOException; its pinned [declaration](https://raw.githubusercontent.com/dotnet/runtime/v10.0.12/src/libraries/System.Net.Primitives/src/System/Net/SocketException.cs)
derives from Win32Exception. Applicable real socket/permission failures still need
source-matched execution and complete terminal evidence.

All 25 retained Timeouts remain explicitly uncredited, including plausible broken
start/stop/drain accounting.2744 changes the wrapper's **isAsync:true** argument
to false; isConnected is already false. The exact historical span prevents
misidentifying it as a connected-state mutation. Fresh real evidence is required
before any later strict gate result can replace this diagnostic snapshot.

