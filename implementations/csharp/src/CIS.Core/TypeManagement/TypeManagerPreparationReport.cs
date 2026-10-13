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
}
