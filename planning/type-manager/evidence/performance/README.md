# Type Manager development performance observations

This measures the unmodified frozen Core candidate
`d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`.
It is development evidence for TM-030, not an application latency guarantee or
final exact-package release qualification. No absolute timing threshold is a gate.

## Workload and comparison

The small synthetic catalog has two initially unloaded libraries with 32 classes
each. The large catalog has eight with 512 classes each. Each selection also
contains the measurement host, its three-type contract assembly, Core and Core's
two approved runtime dependencies. This includes the normal fixed library overhead
instead of selecting only synthetic types. Every second
class implements the prepared interface; every third implements the lazy interface.
Classes are nonpublic and have private constructors with a counted side effect.
The final small/large inventories contain 7/13 assemblies and 334/4,366 types,
including the real Core/dependency definitions. The two Microsoft dependencies
are deliberately first loaded during preparation; a host that loads them earlier
will move some of that work elsewhere.
Correctness checks require the exact match counts, complete per-library catalogs,
loaded/reused report counts, cached object identity, exact lookup success and zero
constructor activations before a sample can be accepted. These sizes demonstrate
two workloads; they do not represent every application's type relationships.

Both modes execute the identical factory, options, manifest and first prepared
query. Eager mode prepares before the synthetic bootstrap boundary. Deferred mode
crosses that boundary without a manager and performs the same preparation at first
use. No application/DI work is simulated between those points. Compare the
bootstrap and first-use regions separately and their sum: this control relocates
work; it is not a different discovery implementation or proof of a speedup.

The `preparation` interval wraps the first factory call in each fresh process. Core
loading, option creation and measurement-helper initialization precede the baseline
in both modes. `bootstrapBoundary` therefore is not total process startup.
`firstPrepared` records the first public query for the already-precomputed contract;
`firstUncached` records the first query for the other contract and can include its
delegate's first JIT. `processWallMilliseconds` includes runtime/process startup,
forced collections, warmup waits, all measurements, checks, serialization and exit;
it must not be presented as preparation latency.

## Sampling and counters

Each platform runs two retained warmup rounds followed by 12 measured fresh
processes per catalog/mode group: 56 processes total, 48 used in summaries. Mode
order alternates every round and catalog order every two rounds. The summaries
retain minimum, median and maximum across processes. No batches are treated as
independent process samples and no confidence interval is inferred from 12 points.

Inside each process, the repeated queries use the same delegates and measurement
helpers for two warmup passes, each followed by a 250 ms quiet interval. Twenty batches of 10,000 calls
then rotate the order of prepared-cache queries, lazy-cache queries, exact lookup
and a reference-only loop control. Every batch is retained. SDK-default tiered
compilation and PGO are explicitly enabled; these are warmed cached-query batches,
not proof of a stable final JIT tier. The loop control is reported without
subtracting it. Cached calls are not repeated catalog matching or enumeration.
Process-wide JIT method-count and compilation-time snapshots bracket the batch
phase, making overlapping compilation visible without attributing it to a specific
query or method. The current query implementation uses synchronous loops and a
lock; the thread-local allocation metric does not cover hypothetical future workers.

Preparation/bootstrap/first-use allocation counters are process-wide managed
allocations and include background/runtime work across async continuations. Query
counters cover the calling thread during the synchronous measured loop. The scope
is encoded in each metric. They are not interchangeable measures of total process
allocation; runtime/native allocations are outside both counters.

Managed memory is sampled after forced full collections at baseline, after the
first prepared query, and after all queries. The final delta is a whole-process
observation containing runtime/reflection caches, loaded metadata, cached query
results and retained measurement state. It is not an isolated manager object size.
Negative deltas, if observed, are retained. Working set and private-memory API
values are supplemental process observations. Windows private bytes and Linux
`VmData` have different meanings; the latter is virtual data mappings, not private
committed memory. The distinction is recorded in each sample and follows the
[.NET Linux process implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/ProcessManager.Linux.cs).

## Reproduction and limits

Build fixtures with `implementations/csharp/eng/measure-type-manager.py prepare`,
passing repository/output paths, the frozen Core, its completed runtime producer
report and the independently selected Core SHA-256. Run with the `run` operation,
the fixture root and the independently recorded SHA-256 of its producer report.
`checkpoint.json` records those identities and exact commands. Both operations
require empty output directories. Linux uses its native SDK `dotnet` via `--dotnet`.

The producer checks Core/module source provenance and the two runtime dependencies
against the earlier producer record. Replay checks every deployment byte, the exact
file set, fixture source and runtime policy before running, and rechecks deployment
bytes afterward. Each child must report the pinned runtime and x64 OS/process.
Builds use private caches and cleared package sources. The same Windows-built
managed deployment is replayed on native Ubuntu under WSL, not rebuilt for Linux.
Linux reads it from the mounted Windows filesystem under `/mnt/c`; these are not
bare-metal Linux or ext4 measurements.

Both lanes use this workstation; they are not independent hardware samples. CPU
affinity, power state, background host load and the operating system's file cache
are uncontrolled. Process-fresh does not mean disk-cache-cold. Preparation does not
pre-execute arbitrary application JIT, static constructors, serializers or resources.
Do not infer cross-machine thresholds, future application latency or a promised
percentage improvement from these observations.

The separate [runtime instrumentation checkpoint](../runtime-acceptance/README.md)
qualifies exactly one enumeration per selected assembly/preparation and no query
rescans, with deliberate double/query-enumeration regressions. Its producer/module
hashes match this Core provenance. Those counters belong to a test-only source copy
and its own fixtures; they are not counters inside the timed DLL or these catalogs.
The existing unmodified-runtime architecture checks complement that evidence.
