using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Management.Hosting;

/// <summary>Optional passive ports over one dedicated native REST profile; no scheduler, worker or registration is started.</summary>
public static class ManagementSchedulerClientServiceCollectionExtensions
{
    /// <summary>Captures bounded content and aliases before ordinary or security-bound native REST registration.</summary>
    /// <remarks>Existing tighter body limits, native authentication, transport, trace forwarding and LogCalls choices are preserved.</remarks>
    public static RestClientOptions ConfigureCisManagementSchedulerRestClient(this RestClientOptions rest, ManagementSchedulerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(rest.Limits);
        ManagementSchedulerClientSnapshot selected = ManagementSchedulerClientSnapshot.Capture(options);
        if (rest.ClientName != selected.RestClientName || rest.ConfigureRequest is not null || rest.ResponseMapper is not null
            || rest.Codec is not null and not ManagementSchedulerContentCodec || rest.RetryPolicy.PolicyName is not null
            || rest.Codec is ManagementSchedulerContentCodec codec && !ReferenceEquals(codec.Limits, selected.WireLimits)
            || rest.Limits.MaxErrorBodyBytes == 0 || rest.Operations.Any(alias => !AllowedAlias(alias)))
        {
            throw new ArgumentException("Use one matching dedicated Scheduler profile without competing processing or inherited retries.", nameof(rest));
        }
        if (rest.BaseUrl is not null && (!Uri.TryCreate(rest.BaseUrl, UriKind.Absolute, out Uri? endpoint)
            || endpoint.AbsoluteUri.TrimEnd('/') != selected.BaseUri.AbsoluteUri.TrimEnd('/')))
        {
            throw new ArgumentException("Native and typed Scheduler endpoints must match exactly.", nameof(rest));
        }
        rest.BaseUrl = selected.BaseUri.AbsoluteUri;
        rest.Timeout = selected.Timeout;
        rest.ResponseFormat = RestResponseFormat.Json;
        rest.RequireValue = true;
        rest.RetryPolicy = RestRetrySelection.None;
        rest.Codec = new ManagementSchedulerContentCodec(selected.WireLimits);
        rest.Limits = new()
        {
            MaxResponseBytes = Math.Min(rest.Limits.MaxResponseBytes, selected.WireLimits.MaximumBodyBytes),
            MaxErrorBodyBytes = Math.Min(rest.Limits.MaxErrorBodyBytes, ManagementWire.MaximumErrorBodyBytes),
            MaxErrorMessageScalars = Math.Min(rest.Limits.MaxErrorMessageScalars, 1024)
        };
        foreach (string alias in ManagementSchedulerClient.ObservationAliases)
        {
            if (!rest.Operations.Contains(alias, StringComparer.Ordinal))
            {
                rest.Operations.Add(alias);
            }
        }
        return rest;
    }

    /// <summary>Registers optional keyed ports and first default aliases borrowing the exact existing REST owner.</summary>
    public static IServiceCollection AddCisManagementSchedulerClient(this IServiceCollection services, ManagementSchedulerClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ManagementSchedulerClientSnapshot selected = ManagementSchedulerClientSnapshot.Capture(options);
        RestClientRegistration[] native = services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(RestClientRegistration))
            .Select(item => item.ImplementationInstance).OfType<RestClientRegistration>()
            .Where(item => item.Options.ClientName == selected.RestClientName).ToArray();
        if (native.Length != 1 || native[0].Options.Codec is not ManagementSchedulerContentCodec codec
            || !ReferenceEquals(codec.Limits, selected.WireLimits) || native[0].Options.BaseUrl != selected.BaseUri.AbsoluteUri
            || native[0].Options.ConfigureRequest is not null || native[0].Options.ResponseMapper is not null
            || native[0].Options.RetryPolicy.PolicyName is not null || native[0].Options.Limits.MaxErrorBodyBytes == 0
            || native[0].Options.Limits.MaxErrorBodyBytes > ManagementWire.MaximumErrorBodyBytes
            || native[0].Options.Limits.MaxErrorMessageScalars > 1024
            || native[0].Options.Limits.MaxResponseBytes > selected.WireLimits.MaximumBodyBytes
            || native[0].Options.Operations.Any(alias => !AllowedAlias(alias))
            || ManagementSchedulerClient.ObservationAliases.Any(alias => !native[0].Options.Operations.Contains(alias, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException("First configure and register the matching dedicated native Scheduler profile.");
        }
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(ManagementSchedulerClient)
            && Equals(item.ServiceKey, selected.RestClientName)))
        {
            throw new InvalidOperationException("Register each typed Scheduler alias once.");
        }
        services.AddKeyedSingleton<ManagementSchedulerClient>(selected.RestClientName, (root, _) => new(
            root.GetRequiredKeyedService<IRestClient>(selected.RestClientName), selected,
            root.GetServices<RestRetryPolicy>(), root.GetService<TimeProvider>()));
        AddPort<IManagementSchedulerClient>(services, selected.RestClientName);
        AddPort<IManagementTriggerEventPort>(services, selected.RestClientName);
        AddPort<IManagementSchedulerWorkerClient>(services, selected.RestClientName);
        AddPort<IManagementApplicationCatalogClient>(services, selected.RestClientName);
        AddPort<IManagementConfigurationAdministrationClient>(services, selected.RestClientName);
        return services;
    }

    private static bool AllowedAlias(string alias)
        => alias == "request" || ManagementSchedulerClient.ObservationAliases.Contains(alias, StringComparer.Ordinal);

    private static void AddPort<T>(IServiceCollection services, string alias) where T : class
    {
        services.AddKeyedSingleton<T>(alias, (root, _) => (T)(object)root.GetRequiredKeyedService<ManagementSchedulerClient>(alias));
        services.TryAddSingleton<T>(root => root.GetRequiredKeyedService<T>(alias));
    }
}

