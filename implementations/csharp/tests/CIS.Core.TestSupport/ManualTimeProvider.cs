namespace CIS.TestSupport;

/// <summary>Source-compatible repository wrapper over the single consumer-facing manual clock implementation.</summary>
public sealed class ManualTimeProvider : CIS.CommonTesting.ManualTimeProvider
{
    /// <summary>Starts the promoted clock with the supplied UTC value.</summary>
    public ManualTimeProvider(DateTimeOffset initialUtc) : base(initialUtc)
    {
    }
}
