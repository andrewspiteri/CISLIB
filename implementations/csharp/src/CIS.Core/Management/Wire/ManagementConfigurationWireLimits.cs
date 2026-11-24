namespace CIS.Management.Wire;

/// <summary>Configuration-specific finite limits; ordinary Management wire defaults remain unchanged.</summary>
public sealed class ManagementConfigurationWireLimits
{
    /// <summary>Captures positive bounds at or below the portable configuration ceilings.</summary>
    public ManagementConfigurationWireLimits(int maximumBodyBytes = 1024 * 1024,
        int maximumSourceBytes = 256 * 1024, int maximumJsonDepth = 16, int maximumDefinitions = 256,
        int maximumOverrides = 16)
    {
        MaximumBodyBytes = Bound(maximumBodyBytes, 1024 * 1024, nameof(maximumBodyBytes));
        MaximumSourceBytes = Bound(maximumSourceBytes, 256 * 1024, nameof(maximumSourceBytes));
        MaximumJsonDepth = Bound(maximumJsonDepth, 16, nameof(maximumJsonDepth));
        MaximumDefinitions = Bound(maximumDefinitions, 256, nameof(maximumDefinitions));
        MaximumOverrides = Bound(maximumOverrides, 16, nameof(maximumOverrides));
    }

    /// <summary>Gets the complete encoded request or response ceiling, at most one MiB.</summary>
    public int MaximumBodyBytes
    {
        get;
    }

    /// <summary>Gets the exact decoded source JSON UTF-8 ceiling, at most 256 KiB.</summary>
    public int MaximumSourceBytes
    {
        get;
    }

    /// <summary>Gets the JSON and structural declaration depth ceiling, at most sixteen.</summary>
    public int MaximumJsonDepth
    {
        get;
    }

    /// <summary>Gets the complete enrollment/status bound, at most 256.</summary>
    public int MaximumDefinitions
    {
        get;
    }

    /// <summary>Gets the complete cohort override bound, at most sixteen.</summary>
    public int MaximumOverrides
    {
        get;
    }

    private static int Bound(int value, int maximum, string name)
    {
        if (value < 1 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, "Select a positive value within the portable ceiling.");
        }
        return value;
    }
}

/// <summary>Expected decode outcome retaining desired identity only after independent target validation.</summary>
/// <param name="Snapshot">Complete content-validated snapshot on success.</param>
/// <param name="Desired">Independently validated desired state, including on identified content rejection.</param>
/// <param name="Failure">Expected finite failure classification.</param>
/// <param name="FailureCode">Safe stable content/identity failure code; never exception text.</param>
public sealed record ManagementConfigurationSnapshotRead(ManagementConfigurationSnapshot? Snapshot,
    ManagementConfigurationDesiredContext? Desired, ManagementWireFailure Failure, string? FailureCode = null);
