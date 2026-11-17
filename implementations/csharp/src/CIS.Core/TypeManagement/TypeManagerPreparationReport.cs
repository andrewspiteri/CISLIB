using CIS.Metrics;

namespace CIS.TypeManagement;

/// <summary>Immutable preparation facts; these are not live query-cache counters.</summary>
public sealed class TypeManagerPreparationReport
{
    internal TypeManagerPreparationReport(TimeSpan elapsed, int typeCount, int preparedContractCount,
        IEnumerable<string> selected, IEnumerable<string> loaded, IEnumerable<string> reused)
    {
        Elapsed = elapsed;
        TypeCount = typeCount;
        PreparedContractCount = preparedContractCount;
        SelectedAssemblyIdentities = Array.AsReadOnly(selected.ToArray());
        LoadedAssemblyIdentities = Array.AsReadOnly(loaded.ToArray());
        ReusedAssemblyIdentities = Array.AsReadOnly(reused.ToArray());
    }

    /// <summary>Gets preparation duration measured with a monotonic clock.</summary>
    public TimeSpan Elapsed { get; }
    /// <summary>Gets the number of selected assemblies.</summary>
    public int SelectedAssemblyCount => SelectedAssemblyIdentities.Count;
    /// <summary>Gets the number of cataloged type definitions.</summary>
    public int TypeCount { get; }
    /// <summary>Gets the number of distinct precomputed contracts.</summary>
    public int PreparedContractCount { get; }
    /// <summary>Gets selected full assembly identities in ordinal order.</summary>
    public IReadOnlyList<string> SelectedAssemblyIdentities { get; }
    /// <summary>Gets selected identities absent from the default context before the load phase.</summary>
    public IReadOnlyList<string> LoadedAssemblyIdentities { get; }
    /// <summary>Gets selected identities already present in the default context before the load phase.</summary>
    public IReadOnlyList<string> ReusedAssemblyIdentities { get; }

    // Immutable terminal facts may be borrowed by independent hosts. This report
    // retains neither a runtime nor an attachment claim, and never repeats discovery.
    internal void AttachMetrics(MetricsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        runtime.RegisterObservableGauge(new("cis.typemanager.assemblies"), ObserveAssemblies);
        runtime.RegisterObservableGauge(new("cis.typemanager.types"), ObserveTypes);
        runtime.RegisterObservableGauge(new("cis.typemanager.initialization.duration", MetricUnit.Seconds), ObserveDuration);
    }

    private IReadOnlyList<MetricObservation> ObserveAssemblies() => [new(SelectedAssemblyCount)];
    private IReadOnlyList<MetricObservation> ObserveTypes() => [new(TypeCount)];
    private IReadOnlyList<MetricObservation> ObserveDuration() => [new(Elapsed.TotalSeconds)];
}
