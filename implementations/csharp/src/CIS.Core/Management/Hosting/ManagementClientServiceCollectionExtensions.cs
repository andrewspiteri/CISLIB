using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Management.Hosting;

/// <summary>Passive typed-client composition over an existing keyed native REST registration.</summary>
public static class ManagementClientServiceCollectionExtensions
{
    private static readonly string[] Operations = ["service-info", "registration", "observation", "inventory-report",
        "registration-close", "applications", "application", "instances", "inventory", "capabilities", "metadata-replace", "metadata-operation"];

    /// <summary>Prepares one dedicated profile before ordinary or security-bound native REST registration.</summary>
    /// <remarks>Credentials, selected security binding, native transport, trace policy and LogCalls remain application choices.
    /// This method sets typed codec/budget/body policy and never registers, resolves or starts a service.</remarks>
    public static RestClientOptions ConfigureCisManagementRestClient(this RestClientOptions rest, ManagementClientOptions management)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ManagementClientSnapshot snapshot = ManagementClientSnapshot.Capture(management);
        if (rest.ClientName != snapshot.RestClientName || rest.ConfigureRequest is not null || rest.ResponseMapper is not null
            || rest.Codec is not null and not ManagementContentCodec || rest.RetryPolicy.PolicyName is not null)
        {
            throw new ArgumentException("Use a dedicated matching management profile without competing codec/customization/mapper or inherited retries.", nameof(rest));
        }
        if (rest.BaseUrl is not null && (!Uri.TryCreate(rest.BaseUrl, UriKind.Absolute, out Uri? existing)
            || existing.AbsoluteUri.TrimEnd('/') != snapshot.BaseUri.AbsoluteUri.TrimEnd('/')))
        {
            throw new ArgumentException("The native REST and management endpoints must match exactly.", nameof(rest));
        }
        rest.BaseUrl = snapshot.BaseUri.AbsoluteUri;
        rest.Timeout = snapshot.Timeout;
        rest.RetryPolicy = RestRetrySelection.None;
        rest.ResponseFormat = RestResponseFormat.Json;
        rest.RequireValue = true;
        rest.Codec = new ManagementContentCodec(snapshot.WireLimits);
        rest.Limits = new RestLimits
        {
            MaxResponseBytes = Math.Max(snapshot.WireLimits.MaximumInventoryResponseBytes, snapshot.WireLimits.MaximumRegularResponseBytes),
            MaxErrorBodyBytes = ManagementWire.MaximumErrorBodyBytes,
            MaxErrorMessageScalars = 1024
        };
        foreach (string operation in Operations)
        {
            if (!rest.Operations.Contains(operation, StringComparer.Ordinal))
            {
                rest.Operations.Add(operation);
            }
        }
        return rest;
    }

    /// <summary>Registers the keyed typed port and first default alias, borrowing the already configured native client.</summary>
    /// <remarks>No registration reporter, heartbeat, provider, transport or startup participant is created.</remarks>
    public static IServiceCollection AddCisManagementClient(this IServiceCollection services, ManagementClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ManagementClientSnapshot snapshot = ManagementClientSnapshot.Capture(options);
        RestClientRegistration[] native = services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(RestClientRegistration)).Select(item => item.ImplementationInstance)
            .OfType<RestClientRegistration>().Where(item => item.Options.ClientName == snapshot.RestClientName).ToArray();
        if (native.Length != 1 || native[0].Options.Codec is not ManagementContentCodec codec
            || !ReferenceEquals(codec.Limits, snapshot.WireLimits)
            || native[0].Options.BaseUrl != snapshot.BaseUri.AbsoluteUri
            || native[0].Options.Limits.MaxErrorBodyBytes != ManagementWire.MaximumErrorBodyBytes
            || native[0].Options.Limits.MaxResponseBytes < Math.Max(snapshot.WireLimits.MaximumInventoryResponseBytes, snapshot.WireLimits.MaximumRegularResponseBytes)
            || native[0].Options.ConfigureRequest is not null)
        {
            throw new InvalidOperationException("First prepare and register the matching dedicated native REST profile.");
        }
        if (services.Any(item => item.IsKeyedService && item.ServiceType == typeof(IManagementClient)
            && Equals(item.ServiceKey, snapshot.RestClientName)))
        {
            throw new InvalidOperationException("Register each typed management alias once.");
        }
        services.AddKeyedSingleton<IManagementClient>(snapshot.RestClientName, (root, _) => new ManagementClient(
            root.GetRequiredKeyedService<IRestClient>(snapshot.RestClientName), snapshot,
            root.GetServices<RestRetryPolicy>(), root.GetService<TimeProvider>()));
        services.TryAddSingleton<IManagementClient>(root => root.GetRequiredKeyedService<IManagementClient>(snapshot.RestClientName));
        return services;
    }
}
