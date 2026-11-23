using System.Globalization;
using CIS.Jobs;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>Explicit passive Jobs projection through public catalog/configuration/inspection APIs.</summary>
/// <remarks>Runtime pages describe available local observations, not an atomic fleet snapshot or permission grant.</remarks>
public sealed class JobsManagementInventorySource : IManagementInventorySource
{
    private readonly IJobCatalog catalog;
    private readonly JobRuntime? runtime;
    private readonly ManagementWireLimits limits;
    private readonly ManagementModuleInventory[] modules;

    /// <summary>Captures selected existing objects without resolving a handler or starting any Jobs work.</summary>
    public JobsManagementInventorySource(IJobCatalog catalog, JobRuntime? runtime = null,
        ManagementWireLimits? limits = null, IReadOnlyList<ManagementModuleInventory>? modules = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        this.runtime = runtime;
        this.limits = limits ?? new();
        this.modules = modules?.ToArray() ?? [new("jobs", typeof(JobRuntime).Assembly.GetName().Version?.ToString() ?? "unknown")];
    }

    /// <inheritdoc />
    public ManagementInventoryContent CaptureInventory(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (catalog.Count > limits.MaximumJobs || catalog.Categories.Count > limits.MaximumCategories
            || modules.Length > limits.MaximumModules)
        {
            throw new ManagementInventoryRejectedException("management.inventory.local_limit");
        }

        JobConfigurationSnapshot? configuration = runtime?.Configuration;
        var observations = CaptureObservations(configuration, cancellationToken);
        ManagementCategoryInventory[] categories = catalog.Categories.Values
            .OrderBy(category => category.ExecutionScope, StringComparer.Ordinal)
            .ThenBy(category => category.CategoryId, StringComparer.Ordinal)
            .Select(category => new ManagementCategoryInventory(category.ExecutionScope, category.CategoryId,
                category.DisplayName, category.Description)).ToArray();
        var jobs = new List<ManagementJobInventory>(catalog.Count);
        int offset = 0;
        while (offset < catalog.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<JobDefinition> page = catalog.Inspect(offset, catalog.Limits.MaximumSnapshotPageSize);
            if (page.Count == 0 || page.Count > limits.MaximumJobs - jobs.Count)
            {
                throw new ManagementInventoryRejectedException("management.inventory.local_invalid");
            }
            foreach (JobDefinition definition in page)
            {
                JobSettings? settings = configuration?.Jobs.GetValueOrDefault((definition.ExecutionScope, definition.JobId));
                string category = settings?.CategoryId ?? definition.CategoryId;
                bool enabled = configuration is not null && configuration.Enabled && settings?.Enabled == true
                    && (configuration.Categories.GetValueOrDefault((definition.ExecutionScope, category))?.Enabled ?? true);
                ManagementJobConfiguration? effective = configuration is null ? null
                    : new(configuration.Revision.ToString(CultureInfo.InvariantCulture), enabled);
                jobs.Add(new(definition.ExecutionScope, definition.JobId, category, definition.DisplayName,
                    Array.AsReadOnly(definition.SupportedModes.Select(Mode).ToArray()), definition.Description, effective,
                    observations.GetValueOrDefault((definition.ExecutionScope, definition.JobId)), definition.ResourceScopeDescription));
            }
            offset += page.Count;
        }
        if (runtime is not null && !ReferenceEquals(configuration, runtime.Configuration))
        {
            throw new ManagementInventoryRejectedException("management.inventory.local_changed");
        }
        return new(Array.AsReadOnly(modules.ToArray()), Array.AsReadOnly(categories), jobs.AsReadOnly());
    }

    private Dictionary<(string Scope, string Job), IReadOnlyList<ManagementJobObservation>> CaptureObservations(
        JobConfigurationSnapshot? configuration, CancellationToken cancellationToken)
    {
        var grouped = new Dictionary<(string Scope, string Job), List<ManagementJobObservation>>();
        if (runtime is null || configuration is null)
        {
            return [];
        }
        int offset = 0;
        var identities = new HashSet<(string Scope, string Job, string Resource, JobExecutionMode Mode)>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pageSize = configuration.Limits.MaximumSnapshotPageSize;
            IReadOnlyList<JobRuntimeSnapshot> page = runtime.CaptureSnapshot(offset, pageSize);
            if (page.Count > limits.MaximumJobObservations - offset)
            {
                throw new ManagementInventoryRejectedException("management.inventory.local_limit");
            }
            foreach (JobRuntimeSnapshot observation in page)
            {
                JobScopeRequest target = observation.Target;
                if (!identities.Add((target.ExecutionScope, target.JobId, target.ResourceId, target.Mode))
                    || catalog.Find(target.ExecutionScope, target.JobId) is null)
                {
                    throw new ManagementInventoryRejectedException("management.inventory.local_changed");
                }
                var key = (target.ExecutionScope, target.JobId);
                if (!grouped.TryGetValue(key, out List<ManagementJobObservation>? values))
                {
                    values = [];
                    grouped.Add(key, values);
                }
                ManagementOwnerObservation? owner = observation.ObservedOwner is { } native
                    ? new(native.OwnerApplication, native.InstanceId, native.Generation.ToString(CultureInfo.InvariantCulture),
                        Timestamp(native.ExpiresAtUtc), Timestamp(native.ObservedAtUtc)) : null;
                values.Add(new(target.ResourceId, Mode(target.Mode), State(observation.State), observation.LastOutcome, owner,
                    observation.LastOccurrenceUtc is { } last ? Timestamp(last) : null,
                    observation.NextOccurrenceUtc is { } next ? Timestamp(next) : null));
            }
            offset += page.Count;
            if (page.Count < pageSize)
            {
                break;
            }
        }
        return grouped.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<ManagementJobObservation>)pair.Value.AsReadOnly());
    }

    private static string Mode(JobExecutionMode mode) => mode switch
    {
        JobExecutionMode.Continuous => "continuous",
        JobExecutionMode.Scheduled => "scheduled",
        _ => throw new ManagementInventoryRejectedException("management.inventory.local_invalid")
    };

    private static string State(JobLocalState state) => state switch
    {
        JobLocalState.Disabled => "disabled",
        JobLocalState.Standby => "standby",
        JobLocalState.Acquiring => "acquiring",
        JobLocalState.Running => "running",
        JobLocalState.Idle => "idle",
        JobLocalState.Waiting => "waiting",
        JobLocalState.Draining => "draining",
        JobLocalState.Lost => "lost",
        JobLocalState.Released => "released",
        JobLocalState.Faulted => "faulted",
        _ => throw new ManagementInventoryRejectedException("management.inventory.local_invalid")
    };

    private static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
