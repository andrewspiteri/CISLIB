using CIS.Text;

namespace CIS.Heartbeat;

/// <summary>Schedule input copied and validated before a subscriber registration is published.</summary>
public sealed class HeartbeatRegistrationOptions
{
    /// <summary>Gets the largest accepted interval or initial delay: 4,294,967,294 milliseconds, approximately 49.7 days.</summary>
    public static TimeSpan MaximumDuration { get; } = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>Gets the positive delay after full execution settlement; defaults to 10 milliseconds and cannot exceed MaximumDuration.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>Gets the first eligibility delay, from zero through MaximumDuration; defaults to zero.</summary>
    /// <remarks>The origin is application readiness for earlier registrations, or initial registration time while running.</remarks>
    public TimeSpan InitialDelay { get; init; }

    /// <summary>Gets an optional diagnostic name containing at most 128 valid Unicode scalar values; names need not be unique.</summary>
    public string? Name { get; init; }

    internal HeartbeatRegistrationOptions ValidatedCopy()
    {
        if (Interval <= TimeSpan.Zero || Interval > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(Interval), "Supply a positive interval no greater than HeartbeatRegistrationOptions.MaximumDuration.");
        }
        if (InitialDelay < TimeSpan.Zero || InitialDelay > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialDelay), "Supply an initial delay from zero through HeartbeatRegistrationOptions.MaximumDuration.");
        }
        if (Name is not null && !UnicodeScalarText.IsValid(Name, 128, allowControls: true))
        {
            throw new ArgumentException("Supply a diagnostic name containing at most 128 valid Unicode scalar values.", nameof(Name));
        }
        return new HeartbeatRegistrationOptions { Interval = Interval, InitialDelay = InitialDelay, Name = Name };
    }
}
