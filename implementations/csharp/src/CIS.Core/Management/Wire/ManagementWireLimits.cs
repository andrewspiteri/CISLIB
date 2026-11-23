namespace CIS.Management.Wire;

/// <summary>The expected reason a finite management wire value was rejected.</summary>
public enum ManagementWireFailure
{
    /// <summary>The value conforms to the selected v1 shape.</summary>
    None,
    /// <summary>JSON, Unicode, required values or a semantic invariant is invalid.</summary>
    Invalid,
    /// <summary>A request or recorded command uses an unsupported version.</summary>
    UnsupportedVersion,
    /// <summary>The complete body exceeds its selected byte bound.</summary>
    TooLarge
}

/// <summary>Immutable finite wire limits; byte ceilings are separate from scalar and native Jobs bounds.</summary>
public sealed class ManagementWireLimits
{
    /// <summary>Captures validated finite limits without allocating payload storage or performing I/O.</summary>
    public ManagementWireLimits(int maximumRegularRequestBytes = 64 * 1024,
        int maximumInventoryRequestBytes = 4 * 1024 * 1024, int maximumRegularResponseBytes = 1024 * 1024,
        int maximumInventoryResponseBytes = 8 * 1024 * 1024, int maximumJsonDepth = 16,
        int maximumModules = 32, int maximumCategories = 64, int maximumJobs = 1024, int maximumJobObservations = 4096)
    {
        MaximumRegularRequestBytes = Bound(maximumRegularRequestBytes, 1024 * 1024, nameof(maximumRegularRequestBytes));
        MaximumInventoryRequestBytes = Bound(maximumInventoryRequestBytes, 16 * 1024 * 1024, nameof(maximumInventoryRequestBytes));
        MaximumRegularResponseBytes = Bound(maximumRegularResponseBytes, 8 * 1024 * 1024, nameof(maximumRegularResponseBytes));
        MaximumInventoryResponseBytes = Bound(maximumInventoryResponseBytes, 32 * 1024 * 1024, nameof(maximumInventoryResponseBytes));
        MaximumJsonDepth = Bound(maximumJsonDepth, 32, nameof(maximumJsonDepth));
        MaximumModules = Bound(maximumModules, 1024, nameof(maximumModules));
        MaximumCategories = Bound(maximumCategories, 65536, nameof(maximumCategories));
        MaximumJobs = Bound(maximumJobs, 65536, nameof(maximumJobs));
        MaximumJobObservations = Bound(maximumJobObservations, 65536, nameof(maximumJobObservations));
    }

    /// <summary>Gets the ordinary request byte limit, initially 64 KiB and at most 1 MiB.</summary>
    public int MaximumRegularRequestBytes
    {
        get;
    }
    /// <summary>Gets the complete inventory request byte limit, initially 4 MiB and at most 16 MiB.</summary>
    public int MaximumInventoryRequestBytes
    {
        get;
    }
    /// <summary>Gets the ordinary response byte limit, initially 1 MiB and at most 8 MiB.</summary>
    public int MaximumRegularResponseBytes
    {
        get;
    }
    /// <summary>Gets the complete inventory response byte limit, initially 8 MiB and at most 32 MiB.</summary>
    public int MaximumInventoryResponseBytes
    {
        get;
    }
    /// <summary>Gets the complete JSON depth limit, initially 16 and at most 32.</summary>
    public int MaximumJsonDepth
    {
        get;
    }
    /// <summary>Gets the complete reported module bound, initially 32 and at most 1,024.</summary>
    public int MaximumModules
    {
        get;
    }
    /// <summary>Gets the global unique scope/category bound, initially 64 and at most 65,536.</summary>
    public int MaximumCategories
    {
        get;
    }
    /// <summary>Gets the complete unique scope/job bound, initially 1,024 and at most 65,536.</summary>
    public int MaximumJobs
    {
        get;
    }
    /// <summary>Gets the total resource-observation bound across all jobs, initially 4,096 and at most 65,536.</summary>
    public int MaximumJobObservations
    {
        get;
    }

    private static int Bound(int value, int ceiling, string parameter)
    {
        if (value < 1 || value > ceiling)
        {
            throw new ArgumentOutOfRangeException(parameter, "Select a positive finite value within the documented ceiling.");
        }
        return value;
    }
}
