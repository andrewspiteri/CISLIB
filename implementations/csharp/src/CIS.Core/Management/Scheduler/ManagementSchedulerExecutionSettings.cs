using System.Globalization;
using CIS.Jobs;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>Captured framework execution settings, distinct from business values. Durations are exact 100ns ticks.</summary>
public sealed record ManagementSchedulerExecutionSettings(string IdleDelayTicks, string? TimeoutTicks,
    string MaximumDrainTicks, string AcquisitionRetryDelayTicks)
{
    /// <summary>Gets the explicit first-release scheduled defaults, copied into a task before activation.</summary>
    public static ManagementSchedulerExecutionSettings Default { get; } = FromRuntime(new(JobExecutionMode.Scheduled));

    /// <summary>Captures the actual immutable execution values without constructing a handler or consulting a live root.</summary>
    public static ManagementSchedulerExecutionSettings FromRuntime(JobExecutionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Mode != JobExecutionMode.Scheduled)
        {
            throw new ArgumentException("A scheduled occurrence requires Scheduled execution settings.", nameof(settings));
        }

        return new(settings.IdleDelay.Ticks.ToString(CultureInfo.InvariantCulture),
            settings.Timeout?.Ticks.ToString(CultureInfo.InvariantCulture),
            settings.MaximumDrain.Ticks.ToString(CultureInfo.InvariantCulture),
            settings.AcquisitionRetryDelay.Ticks.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Recreates the captured occurrence settings. Recurrence remains owned by the separate configured trigger.</summary>
    public JobExecutionSettings ToRuntime()
        => new(JobExecutionMode.Scheduled, Duration(IdleDelayTicks, nameof(IdleDelayTicks)),
            TimeoutTicks is null ? null : Duration(TimeoutTicks, nameof(TimeoutTicks)),
            Duration(MaximumDrainTicks, nameof(MaximumDrainTicks)),
            Duration(AcquisitionRetryDelayTicks, nameof(AcquisitionRetryDelayTicks)),
            scheduledActivation: JobScheduledActivation.ExternalOccurrences);

    private static TimeSpan Duration(string value, string parameterName)
    {
        if (!ManagementWire.IsOrderedInteger(value)
            || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || ticks > TimeSpan.FromDays(49).Ticks)
        {
            throw new ArgumentException("Supply a positive canonical tick duration within the existing Jobs limit.", parameterName);
        }

        return TimeSpan.FromTicks(ticks);
    }
}
