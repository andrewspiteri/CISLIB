namespace CIS.Coordination.Hosting;

/// <summary>Explicit hosting policy, separate from managed lease timing.</summary>
public sealed class CoordinationHostingOptions
{
    /// <summary>Gets or sets whether authority unavailability is critical to configured health; initially true.</summary>
    public bool IsHealthCritical { get; set; } = true;
}
