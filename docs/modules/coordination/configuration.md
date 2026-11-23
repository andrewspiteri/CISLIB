# Job configuration adoption

`JobConfigurationSubscription<TConfiguration>` connects one existing retained
Configuration object to a `JobRuntime`. It uses the published
`IConfigurationReader` and `ConfigurationChanged` event. It selects no backend,
parser, polling interval or additional configuration owner.

Construction is passive. The application supplies its existing root, its owning
reader, and a synchronous whole-snapshot capture:

```csharp
await using var binding = new JobConfigurationSubscription<MaintenanceSettings>(
    runtime, configurationReader, retainedSettings,
    (current, revision) => new JobConfigurationSnapshot(
        revision,
        enabled: current.Enabled,
        categories:
        [
            new("wallet-service", "maintenance", current.MaintenanceEnabled,
                current.MaximumMaintenanceInvocations)
        ],
        jobs:
        [
            new("wallet-service", "wallet.compaction",
                new(JobExecutionMode.Continuous, idleDelay: current.IdleDelay),
                enabled: current.CompactionEnabled)
        ]));

binding.Connect();
await runtime.StartAsync(cancellationToken);
```

`MaintenanceSettings` represents the application's own retained configuration
type. It is not a new Core configuration root. The existing Configuration module
must finish initial loading before this connection is made. Use the existing host
integration for the actual startup and shutdown order; do not introduce another
startable discovery convention or service provider for this subscription.

Connect subscribes before the initial coordinated read. Initial values use local
revision zero. A change observed while that first read is waiting can adopt its
positive revision first; the later initial capture cannot overwrite it. Repeated
Connect calls join the same connection rather than registering duplicate handlers.
An invalid initial snapshot fails connection and removes the actual subscription.

For each accepted Configuration notification, the capture receives exactly that
event's positive local revision. Its returned `JobConfigurationSnapshot.Revision`
must match. The entire capture executes inside one `IConfigurationReader.Read`
callback under the existing root's value gate. Read and copy all required fields,
lists and nested values there. The snapshot constructor detaches its category/job
collections before returning.

The capture must remain a short pure synchronous copy. Do not mutate the retained
root, perform I/O, await, read another configuration root, start business work, or
call connection/disposal/runtime control from inside it. Validation and adoption
occur after the coordinated reader returns. Event add/remove accessors and
capture code run without the subscription's private lifecycle gate.

Use `ConfigurationObject`, or forward a direct `IConfigurationObject` event's
accessors to the existing helper:

```csharp
public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
{
    add => ConfigurationEvents.Add(this, value);
    remove => ConfigurationEvents.Remove(this, value);
}
```

A disconnected field event does not satisfy the Configuration contract. Continue
using the existing module's actual commit/publication path; changing the root
directly and raising ad hoc notifications is not a replacement configuration
provider.

## Atomic updates and operational boundaries

`JobRuntime.TryApplyConfiguration` validates the complete detached candidate
against registered definitions, categories, bounds and active capabilities before
adopting a strictly newer revision. An invalid category reassignment, unsupported
mode or malformed capacity update leaves every accepted setting unchanged.
Capture failures, null snapshots and revision mismatches also preserve that last
valid snapshot. `RejectedConfiguration` retains the rejected revision and a
bounded code such as `jobs.configuration.capture_failed`; arbitrary configuration
values and exception text do not enter that diagnostic record.

Host/category/job gates still compose: a job cannot override category disable or
its hard local ceiling. A category or display-name change preserves stable job,
resource and ownership identities. Reduced limits prevent new admission and do
not erase actual running callbacks. Continuous settings are adopted between
iterations; mode/source changes retain the runtime's drain/reactivation rules.

These revisions describe local observation and convergence. They do not establish
instantaneous cluster-wide disable, authorize another execution scope, or admit a
scheduled occurrence. A scheduler retains responsibility for authoritative
revision/enablement checks during occurrence admission. Already-admitted work
keeps its immutable execution snapshot and original operation/occurrence identity.

## Owned subscription lifetime

`DisposeAsync` closes callback admission, unsubscribes from the actual retained
object and joins all already-admitted coordinated reads/captures. A read finishing
after closure skips adoption. Completion waits for admitted work to settle, so no
update can arrive after disposal completes. The subscription never disposes its
borrowed root, reader, Configuration source or job runtime.

Disconnect this binding before draining/disposal of its runtime and before the
Configuration owner is disposed. A synchronous capture must not wait for its own
subscription disposal. The ordinary host adapter owns that ordering; no new
configuration event queue, background thread or refresh worker is introduced.

Focused tests use the real Configuration catalog reader and helper-backed event,
including an event delivered after registration while the initial read is blocked
outside its value gate. They also cover atomic rejection, coordinated capture,
initial cleanup and disposal joining an admitted read. Actual execution results
remain in the change verification record.
