using System.Collections.ObjectModel;
using CIS.Configuration;
using CIS.Heartbeat;
using CIS.Hosting;
using CIS.RestClient;

namespace CIS.Examples.CountryData;

/// <summary>Explicitly enrolls only the existing catalog settings using the supported native facade bootstrap.</summary>
public static class CountryCentralConfiguration
{
    /// <summary>The example's explicit portable definition identity.</summary>
    public const string ConfigurationId = "country.settings";
    /// <summary>The example's immutable initial schema identity.</summary>
    public const string SchemaVersion = "1";
    private static readonly RestRetryPolicy[] TutorialPolicies = [RestRetryPolicy.TransientHttp];

    /// <summary>Preserves ordinary defaults/store/factories/validators while selecting restart-required central settings.</summary>
    /// <remarks>Call in CountryApplication.CreateService's configure callback. Credentials and endpoints remain independent
    /// caller-supplied native preparation inputs. The helper performs no I/O or provider construction.</remarks>
    public static void Configure(CISApplicationBuilder builder, CISApplicationCentralConfigurationOptions central,
        TimeSpan pollingInterval)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(central);
        if (pollingInterval <= TimeSpan.Zero || pollingInterval > HeartbeatRegistrationOptions.MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(pollingInterval), "Select an explicit positive supported polling interval.");
        }
        string host = typeof(CountryApplication).Assembly.GetName().Name!;
        ConfigurationOptions selected = builder.Configuration ?? new(host);
        if (builder.CentralConfiguration is not null || selected.HostApplicationName != host || selected.SnapshotTarget is not null
            || selected.Enrollments.Any(item => item.ConfigurationType != typeof(CountrySettings)
                || item.ConfigurationId != ConfigurationId || item.SchemaVersion != SchemaVersion
                || item.UpdateMode != ConfigurationUpdateMode.RestartRequired)
            || selected.Enrollments.Count > 1)
        {
            throw new ArgumentException("Select CountryData's actual host, only CountrySettings and let one native bootstrap establish the target.");
        }

        var factories = new Dictionary<Type, Func<IConfigurationObject>>(selected.Factories);
        var validators = new Dictionary<Type, Action<IConfigurationObject>>(selected.Validators);
        validators.TryGetValue(typeof(CountrySettings), out Action<IConfigurationObject>? previous);
        validators[typeof(CountrySettings)] = candidate =>
        {
            previous?.Invoke(candidate);
            var settings = (CountrySettings)candidate;
            _ = CountrySettingsValidation.Validate(settings.SourceUrl, settings.RefreshInterval, settings.RequestTimeout,
                settings.RetryPolicyName, TutorialPolicies);
        };
        builder.Configuration = new(host, selected.Store)
        {
            IsHealthCritical = selected.IsHealthCritical ?? true,
            PollingInterval = pollingInterval,
            Factories = new ReadOnlyDictionary<Type, Func<IConfigurationObject>>(factories),
            Validators = new ReadOnlyDictionary<Type, Action<IConfigurationObject>>(validators),
            Enrollments = [new(typeof(CountrySettings), ConfigurationId, SchemaVersion, ConfigurationUpdateMode.RestartRequired)]
        };
        builder.CentralConfiguration = central;
    }
}
