namespace CIS.Healthcheck;

/// <summary>Detached binding input captured and validated before a schedule is activated.</summary>
public sealed class HealthExecutionBindingOptions
{
    /// <summary>Gets the required nonempty scheduling registration identity.</summary>
    public required Guid RegistrationId { get; init; }

    /// <summary>Gets the explicit declaration that success publishes usable data; availability bindings require true.</summary>
    public bool SuccessPublishesUsableData { get; init; }

    /// <summary>Gets optional positive interval metadata; this value does not schedule work.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>Gets optional nonnegative initial-delay metadata; this value does not schedule work.</summary>
    public TimeSpan? InitialDelay { get; init; }

    /// <summary>Gets optional bounded display metadata; this value is not a registration identity.</summary>
    public string? DisplayName { get; init; }
}
