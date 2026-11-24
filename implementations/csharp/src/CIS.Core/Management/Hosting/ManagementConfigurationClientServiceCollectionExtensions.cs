using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Management.Hosting;

/// <summary>Passive configuration client composition over a dedicated native REST alias.</summary>
public static class ManagementConfigurationClientServiceCollectionExtensions
{
    /// <summary>Captures a separate configuration codec/budget profile before ordinary or security-bound REST registration.</summary>
    /// <remarks>Preserves tighter processing limits, authentication, transport, trace and logging choices.
    /// Two observations are reserved: configuration and the native request fallback. No client is started.</remarks>
    public static RestClientOptions ConfigureCisManagementConfigurationRestClient(this RestClientOptions rest,
        ManagementConfigurationClientOptions configuration)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(rest.Limits);
        ManagementConfigurationClientSnapshot selected = ManagementConfigurationClientSnapshot.Capture(configuration);
        if (rest.ClientName != selected.RestClientName || rest.ConfigureRequest is not null || rest.ResponseMapper is not null
            || rest.Codec is not null and not ManagementConfigurationContentCodec || rest.RetryPolicy.PolicyName is not null
            || rest.Codec is ManagementConfigurationContentCodec configured && !ReferenceEquals(configured.Limits, selected.WireLimits)
            || rest.Limits.MaxErrorBodyBytes == 0 || rest.Operations.Any(operation => operation is not (ManagementConfigurationClient.OperationAlias or "request")))
        {
            throw new ArgumentException("Use one matching dedicated configuration alias without competing processing, inherited retries or disabled error-body observation.", nameof(rest));
        }
        if (rest.BaseUrl is not null && (!Uri.TryCreate(rest.BaseUrl, UriKind.Absolute, out Uri? existing)
            || existing.AbsoluteUri.TrimEnd('/') != selected.BaseUri.AbsoluteUri.TrimEnd('/')))
        {
            throw new ArgumentException("Native REST and configuration endpoints must match exactly.", nameof(rest));
        }
        rest.BaseUrl = selected.BaseUri.AbsoluteUri;
        rest.Timeout = selected.Timeout;
        rest.RetryPolicy = RestRetrySelection.None;
        rest.ResponseFormat = RestResponseFormat.Json;
        rest.RequireValue = true;
        rest.Codec = new ManagementConfigurationContentCodec(selected.WireLimits);
        rest.Limits = new()
        {
            MaxResponseBytes = Math.Min(rest.Limits.MaxResponseBytes, selected.WireLimits.MaximumBodyBytes),
            MaxErrorBodyBytes = Math.Min(rest.Limits.MaxErrorBodyBytes, ManagementWire.MaximumErrorBodyBytes),
            MaxErrorMessageScalars = Math.Min(rest.Limits.MaxErrorMessageScalars, 1024)
        };
        if (!rest.Operations.Contains(ManagementConfigurationClient.OperationAlias, StringComparer.Ordinal))
        {
            rest.Operations.Add(ManagementConfigurationClient.OperationAlias);
        }
        return rest;
    }

    /// <summary>Registers a keyed typed port and the first default alias, borrowing the already selected native owner.</summary>
    public static IServiceCollection AddCisManagementConfigurationClient(this IServiceCollection services,
        ManagementConfigurationClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ManagementConfigurationClientSnapshot selected = ManagementConfigurationClientSnapshot.Capture(options);
        RestClientRegistration[] native = services.Where(item => !item.IsKeyedService && item.ServiceType == typeof(RestClientRegistration))
            .Select(item => item.ImplementationInstance).OfType<RestClientRegistration>()
            .Where(item => item.Options.ClientName == selected.RestClientName).ToArray();
        if (native.Length != 1 || native[0].Options.Codec is not ManagementConfigurationContentCodec codec
            || !ReferenceEquals(codec.Limits, selected.WireLimits) || native[0].Options.BaseUrl != selected.BaseUri.AbsoluteUri
            || native[0].Options.ConfigureRequest is not null || native[0].Options.ResponseMapper is not null
            || native[0].Options.Limits.MaxErrorBodyBytes == 0
            || native[0].Options.Limits.MaxErrorBodyBytes > ManagementWire.MaximumErrorBodyBytes
            || native[0].Options.Limits.MaxErrorMessageScalars > 1024
            || native[0].Options.Limits.MaxResponseBytes > selected.WireLimits.MaximumBodyBytes
            || native[0].Options.Operations.Any(operation => operation is not (ManagementConfigurationClient.OperationAlias or "request")))
        {
            throw new InvalidOperationException("First configure and register the matching dedicated native configuration profile.");
        }
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(IManagementConfigurationClient)
            && Equals(item.ServiceKey, selected.RestClientName)))
        {
            throw new InvalidOperationException("Register each typed configuration alias once.");
        }
        services.AddKeyedSingleton<IManagementConfigurationClient>(selected.RestClientName, (root, _) => new ManagementConfigurationClient(
            root.GetRequiredKeyedService<IRestClient>(selected.RestClientName), selected,
            root.GetServices<RestRetryPolicy>(), root.GetService<TimeProvider>()));
        services.TryAddSingleton<IManagementConfigurationClient>(root => root.GetRequiredKeyedService<IManagementConfigurationClient>(selected.RestClientName));
        return services;
    }
}
