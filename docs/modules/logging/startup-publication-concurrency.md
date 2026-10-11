# C# startup publication concurrency

WP03-S04-T03, 11 October 2026, completes the one-publisher synchronization mechanism for [EnvironmentOptions](environment-options.md). [Windows/Ubuntu evidence](../../../planning/logging/evidence/wp03-s04-t03-windows-linux.json) retains the before/after regression, actual concurrent execution and fresh source replay. Original source plans/contracts and historical evidence remain unchanged.

## Application behavior

Initialize remains synchronous. All reads/writes of the published reference use the same private monitor gate. Initialization holds that gate through local input copying, required capture, discovery, complete selected-value validation and construction. It stores Current only after the complete immutable snapshot exists. Other threads trying to initialize or read Current/IsInitialized wait for that attempt to leave the gate.

The first successful publisher wins; the API does not promise a particular winning thread or FIFO ordering. After a winner exists, later non-null calls throw InvalidOperationException before reading their new inputs. A failed validation or propagated required-source failure publishes nothing and releases the gate; a waiting valid contender or later retry can initialize. In a mixed cohort, an invalid caller can receive CoreConfigurationException before publication or InvalidOperationException after a valid caller wins. An invalid caller cannot publish.

Current/convenience reads before success still throw InvalidOperationException, and IsInitialized is false. A reader that waits for a failed attempt may acquire the gate before another valid candidate and see that uninitialized state, or acquire it after publication and see the complete winner. It never sees a partial snapshot. Initialize(null) keeps the setup argument guard even during initialization or after success.

## Metadata callback re-entry

The original private lock prevents simultaneous execution by different threads, but Monitor permits the owning thread to enter it again. The supplied Assembly.GetName method is virtual, and application identity discovery can invoke an override. See Microsoft's [Monitor.Enter documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.enter?view=net-10.0) and [Assembly.GetName declaration](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assembly.getname?view=net-10.0).

The actual pre-guard process control demonstrated two successful publications when GetName initialized a nested application: the inner snapshot was then replaced by the outer one. Invalid outer configuration and a required-source exception also left the nested snapshot published and prevented a later retry. These are retained actual observations, not an inferred-only negative model.

A private initializing boolean now marks construction inside the gate. A same-thread nested non-null Initialize call throws InvalidOperationException before capturing inputs or building a candidate. The flag is cleared in finally on success, validation failure and source failure. Reentrant Current/IsInitialized calls observe the existing uninitialized state; they do not publish. The public exception documentation includes re-entry. The extra private flag is synchronization state and contains no mutable option values or operation context. The CLR static constructor still only allocates the gate and performs no discovery/output work.

## Executed qualification

Ten new native integration cases exercise actual Core in fresh bounded children: 2, 16 and 32 valid barrier-released candidates with four concurrent readers; mixed 16-candidate cohorts; 16 failed invalid candidates followed by a valid retry; a deliberately held metadata call with an observed waiting contender and reader, for both valid and invalid outer inputs; and three reentrant valid/invalid/required-failure cases. Each race reader performs 64 checks against complete candidate identity/options/metadata tuples. Hold controls use finite events, observe actual WaitSleepJoin state after the actors signal they started, and release in finally; they do not depend on a sleep duration.

Every successful result matches Current by reference and stays unchanged after later calls and actual child cwd changes. Every loser follows the documented exception path and safe issue meanings. Existing no-output/worker/network/global-exception guards and caller ownership checks still pass. Five new architecture cases inspect the shared gate around published-reference accesses, source-construction/publication ordering and finally cleanup, with a never-invoked ungated-field control. These direct compiled checks support the runtime evidence; they do not claim a formal proof over every possible scheduler execution.

The same ten scenarios run on Windows and Ubuntu 24.04.4 x64 under WSL2 using identical managed Core/host bytes, the retained .NET 10.0.12 runtime and roll-forward disabled. Independent catalog clearing preserves parent environment/cwd, and owned directories remain empty. This is public startup qualification; Linux SDK/full native families, macOS and the logging runtime remain unqualified.

T03 qualifies the concurrent one-successful-publisher criterion and safe failure/retry mechanism. T04 remains responsible for the complete shared lifecycle/metadata fixture set and independently initialized process identity criterion before the full S04 story is marked Done. No public reset, new dependency, publication or logging lifecycle implementation is introduced.
