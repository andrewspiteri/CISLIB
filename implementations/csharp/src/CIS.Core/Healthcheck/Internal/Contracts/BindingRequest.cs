namespace CIS.Healthcheck.Internal;

/// <summary>An immutable detached request selecting exactly one external monitor or typed policy.</summary>
internal sealed class BindingRequest
{
    private BindingRequest(
        HealthExecutionBindingOptions options,
        Guid? monitorId = null,
        ConsecutiveFailureMonitorOptions? consecutiveFailurePolicy = null,
        AvailabilityMonitorOptions? availabilityPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        RegistrationId = options.RegistrationId;
        SuccessPublishesUsableData = options.SuccessPublishesUsableData;
        Interval = options.Interval;
        InitialDelay = options.InitialDelay;
        DisplayName = options.DisplayName;
        MonitorId = monitorId;
        ConsecutiveFailurePolicy = consecutiveFailurePolicy;
        AvailabilityPolicy = availabilityPolicy;
    }

    internal Guid RegistrationId { get; }
    internal Guid? MonitorId { get; }
    internal ConsecutiveFailureMonitorOptions? ConsecutiveFailurePolicy { get; }
    internal AvailabilityMonitorOptions? AvailabilityPolicy { get; }
    internal bool SuccessPublishesUsableData { get; }
    internal TimeSpan? Interval { get; }
    internal TimeSpan? InitialDelay { get; }
    internal string? DisplayName { get; }

    internal static BindingRequest ForMonitor(Guid monitorId, HealthExecutionBindingOptions options) =>
        new(options, monitorId: monitorId);

    internal static BindingRequest ForConsecutiveFailures(
        ConsecutiveFailureMonitorOptions policy,
        HealthExecutionBindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(options, consecutiveFailurePolicy: new ConsecutiveFailureMonitorOptions
        {
            IsCritical = policy.IsCritical,
            Name = policy.Name,
            FailureThreshold = policy.FailureThreshold
        });
    }

    internal static BindingRequest ForAvailability(
        AvailabilityMonitorOptions policy,
        HealthExecutionBindingOptions options)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return new(options, availabilityPolicy: new AvailabilityMonitorOptions
        {
            IsCritical = policy.IsCritical,
            Name = policy.Name
        });
    }
}
