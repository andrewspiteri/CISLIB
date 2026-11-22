namespace CIS.Coordination;

/// <summary>Finite setup copied and validated once by the managed ownership engine.</summary>
public sealed class CoordinationOptions
{
    /// <summary>Gets or sets requested authority validity, initially thirty seconds.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the renewal interval, initially ten seconds.</summary>
    public TimeSpan RenewalInterval { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets or sets a physical authority request timeout, initially two seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Gets or sets the conservative local margin, initially five seconds.</summary>
    public TimeSpan SafetyMargin { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the maximum graceful renewal/drain interval, initially thirty seconds.</summary>
    public TimeSpan MaximumGracefulDrain { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the exact-acquisition observation window, initially five seconds.</summary>
    public TimeSpan AcquisitionRecoveryTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Gets or sets the positive delay between exact observations, initially 250 milliseconds.</summary>
    public TimeSpan RecoveryObservationDelay { get; set; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Gets or sets bounded renewal retry spacing, initially 500 milliseconds.</summary>
    public TimeSpan RenewalRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Gets or sets the shared live/unresolved ownership capacity, initially sixty-four.</summary>
    public int MaximumManagedLeases { get; set; } = 64;
    /// <summary>Gets or sets physical acquisition capacity, initially eight, separate from renewal capacity.</summary>
    public int MaximumConcurrentAcquisitions { get; set; } = 8;
    /// <summary>Gets or sets independent physical renewal capacity, initially eight.</summary>
    public int MaximumConcurrentRenewals { get; set; } = 8;
    /// <summary>Gets or sets the maximum diagnostic page size, initially one hundred.</summary>
    public int MaximumSnapshotPageSize { get; set; } = 100;

    /// <summary>Validates complete setup without starting timers, creating handlers or contacting the authority.</summary>
    public void Validate()
    {
        CoordinationText.Duration(LeaseDuration, nameof(LeaseDuration));
        CoordinationText.Duration(RenewalInterval, nameof(RenewalInterval));
        CoordinationText.Duration(RequestTimeout, nameof(RequestTimeout));
        CoordinationText.Duration(SafetyMargin, nameof(SafetyMargin));
        CoordinationText.Duration(MaximumGracefulDrain, nameof(MaximumGracefulDrain));
        CoordinationText.Duration(AcquisitionRecoveryTimeout, nameof(AcquisitionRecoveryTimeout));
        CoordinationText.Duration(RecoveryObservationDelay, nameof(RecoveryObservationDelay));
        CoordinationText.Duration(RenewalRetryDelay, nameof(RenewalRetryDelay));
        if (RenewalInterval + RequestTimeout + SafetyMargin >= LeaseDuration
            || AcquisitionRecoveryTimeout > LeaseDuration || RecoveryObservationDelay >= AcquisitionRecoveryTimeout
            || RenewalRetryDelay >= LeaseDuration)
        {
            throw new ArgumentException("Renewal interval + request timeout + safety margin must be less than lease duration; recovery/retry intervals must fit their finite windows.");
        }
        Range(MaximumManagedLeases, 1, 1024, nameof(MaximumManagedLeases));
        Range(MaximumConcurrentAcquisitions, 1, MaximumManagedLeases, nameof(MaximumConcurrentAcquisitions));
        Range(MaximumConcurrentRenewals, 1, MaximumManagedLeases, nameof(MaximumConcurrentRenewals));
        Range(MaximumSnapshotPageSize, 1, 1000, nameof(MaximumSnapshotPageSize));
    }

    internal CoordinationOptions CopyValidated()
    {
        var copy = new CoordinationOptions
        {
            LeaseDuration = LeaseDuration,
            RenewalInterval = RenewalInterval,
            RequestTimeout = RequestTimeout,
            SafetyMargin = SafetyMargin,
            MaximumGracefulDrain = MaximumGracefulDrain,
            AcquisitionRecoveryTimeout = AcquisitionRecoveryTimeout,
            RecoveryObservationDelay = RecoveryObservationDelay,
            RenewalRetryDelay = RenewalRetryDelay,
            MaximumManagedLeases = MaximumManagedLeases,
            MaximumConcurrentAcquisitions = MaximumConcurrentAcquisitions,
            MaximumConcurrentRenewals = MaximumConcurrentRenewals,
            MaximumSnapshotPageSize = MaximumSnapshotPageSize
        };
        copy.Validate();
        return copy;
    }

    private static void Range(int value, int minimum, int maximum, string name)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name, $"Supply a value from {minimum} to {maximum}.");
        }
    }
}
