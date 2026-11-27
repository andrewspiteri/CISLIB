using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>Passive settings for optional Scheduler and application catalog clients over one native REST owner.</summary>
public sealed class ManagementSchedulerClientOptions
{
    /// <summary>Selects one existing dedicated REST alias without starting work.</summary>
    public ManagementSchedulerClientOptions(string restClientName)
    {
        if (!ManagementWire.IsScalarText(restClientName, 64))
        {
            throw new ArgumentException("Select one bounded native REST alias.", nameof(restClientName));
        }
        RestClientName = restClientName;
    }

    /// <summary>Gets the exact native and typed client alias.</summary>
    public string RestClientName
    {
        get;
    }
    /// <summary>Gets or sets the independent HTTPS endpoint; omission selects CIS_MANAGEMENT_URL.</summary>
    public string? BaseUrl
    {
        get; set;
    }
    /// <summary>Gets or sets the finite entry-through-cleanup budget, initially thirty seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets immutable stricter-only Scheduler wire limits.</summary>
    public ManagementSchedulerWireLimits WireLimits { get; set; } = new();
    /// <summary>Gets or sets an explicitly registered conservative read policy; omission means no retry.</summary>
    public RestRetryPolicy? ReadRetryPolicy
    {
        get; set;
    }
}

internal sealed record ManagementSchedulerClientSnapshot(string RestClientName, Uri BaseUri, TimeSpan Timeout,
    ManagementSchedulerWireLimits WireLimits, RestRetryPolicy? ReadRetryPolicy)
{
    internal static ManagementSchedulerClientSnapshot Capture(ManagementSchedulerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.WireLimits);
        ManagementClientSnapshot endpoint = ManagementClientSnapshot.Capture(new(options.RestClientName)
        {
            BaseUrl = options.BaseUrl,
            Timeout = options.Timeout,
            ReadRetryPolicy = options.ReadRetryPolicy
        });
        return new(endpoint.RestClientName, endpoint.BaseUri, endpoint.Timeout, options.WireLimits, endpoint.ReadRetryPolicy);
    }
}

/// <summary>Fixed original-operation families; distinct intents sharing a key must arbitrate and conflict before effects.</summary>
public static class ManagementSchedulerOperations
{
    /// <summary>Original create, replace or state decision for one trigger.</summary>
    public const string TriggerWrite = "management.scheduler.trigger.write";
    /// <summary>Original create, replace, state or manual-run decision for one task.</summary>
    public const string TaskWrite = "management.scheduler.task.write";
    /// <summary>Original cancellation decision for one logical run.</summary>
    public const string RunWrite = "management.scheduler.run.write";
}
