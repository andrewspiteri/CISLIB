using CIS.Heartbeat.Hosting;
using CIS.Metrics;
using CIS.RestClient.Http;
using CIS.Startup.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;

namespace CIS.RestClient.Hosting;

/// <summary>Registers finite named REST clients in the application's existing container and host.</summary>
public static class RestClientServiceCollectionExtensions
{
    /// <summary>Registers one immutable client snapshot, available as a keyed <see cref="IRestClient"/>.</summary>
    /// <remarks>Dependencies precede REST; Heartbeat and Startup follow it. Disable concurrent host
    /// start/stop and await host stop before disposing its root. A supplied transport factory returns
    /// a borrowed transport using this root's exact IMeterFactory; its owner disposes it after host stop.</remarks>
    public static IServiceCollection AddCisRestClient(this IServiceCollection services, RestClientOptions options,
        Func<IServiceProvider, RestHttpTransport>? borrowedTransport = null)
        => Register(services, options, borrowedTransport, null, null, null);

    /// <summary>Registers an explicit immutable security owner without resolving its authentication provider now.</summary>
    public static IServiceCollection AddCisSecurityBoundRestClient(this IServiceCollection services, RestClientOptions options,
        RestSecurityBinding securityBinding,
        Func<IServiceProvider, Func<RestAuthenticationContext, CancellationToken, Task>>? authenticationFactory = null,
        Func<IServiceProvider, RestSecurityBinding, RestHttpTransport>? borrowedTransport = null)
    {
        ArgumentNullException.ThrowIfNull(securityBinding);
        return Register(services, options, null, _ => securityBinding, authenticationFactory, borrowedTransport);
    }

    /// <summary>Derives security binding and authentication from the one final provider after Configuration preparation.</summary>
    /// <remarks>Factories are invoked once by ordinary runtime activation, never during registration. A borrowed
    /// transport must retain the exact binding object, immutable origins and root meter factory.</remarks>
    public static IServiceCollection AddCisSecurityBoundRestClient(this IServiceCollection services, RestClientOptions options,
        Func<IServiceProvider, RestSecurityBinding> securityBindingFactory,
        Func<IServiceProvider, Func<RestAuthenticationContext, CancellationToken, Task>>? authenticationFactory = null,
        Func<IServiceProvider, RestSecurityBinding, RestHttpTransport>? borrowedTransport = null)
    {
        ArgumentNullException.ThrowIfNull(securityBindingFactory);
        return Register(services, options, null, securityBindingFactory, authenticationFactory, borrowedTransport);
    }

    private static IServiceCollection Register(IServiceCollection services, RestClientOptions options,
        Func<IServiceProvider, RestHttpTransport>? borrowedTransport,
        Func<IServiceProvider, RestSecurityBinding>? securityBindingFactory,
        Func<IServiceProvider, Func<RestAuthenticationContext, CancellationToken, Task>>? authenticationFactory,
        Func<IServiceProvider, RestSecurityBinding, RestHttpTransport>? securityTransportFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (authenticationFactory is not null && options.Authenticate is not null)
        {
            throw new ArgumentException("The security provider factory cannot replace an existing authentication callback.", nameof(options));
        }
        RestClientSnapshot snapshot = RestClientSnapshot.Capture(options, deferredAuthentication: authenticationFactory is not null);
        RestClientComposition? existing = services.Where(item => !item.IsKeyedService
            && item.ServiceType == typeof(RestClientComposition)).Select(item => item.ImplementationInstance)
            .Cast<RestClientComposition>().SingleOrDefault();
        if (existing?.ClientAliases.Contains(snapshot.ClientName, StringComparer.Ordinal) == true
            || services.Any(item => item.IsKeyedService && item.ServiceType == typeof(IRestClient)
                && Equals(item.ServiceKey, snapshot.ClientName)))
        {
            throw new InvalidOperationException("Register each REST client alias once.");
        }
        var pairs = (existing?.Operations ?? []).Concat(snapshot.Operations
            .Select(operation => new RestOperationIdentity(snapshot.ClientName, operation))).ToArray();
        var composition = new RestClientComposition(existing?.RegistrationIdentity ?? new object(), pairs);
        if (existing is not null)
        {
            ServiceDescriptor previous = services.Single(item => !item.IsKeyedService
                && item.ServiceType == typeof(RestClientComposition));
            services.Remove(previous);
        }
        services.AddSingleton(composition);
        services.AddSingleton(new RestClientRegistration(snapshot, borrowedTransport, securityBindingFactory,
            authenticationFactory, securityTransportFactory));
        services.TryAddSingleton(TimeProvider.System);
        services.AddMetrics();
        services.TryAddSingleton<RestClientRuntime>(CreateRuntime);
        services.TryAddSingleton<IRestClientRuntime>(root => root.GetRequiredService<RestClientRuntime>());
        services.AddKeyedSingleton<IRestClient>(snapshot.ClientName,
            (root, _) => new HttpRestClient(root.GetRequiredService<RestClientRuntime>(), snapshot.ClientName));
        services.TryAddSingleton(new RestHostingOrder(services));
        if (!services.Any(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)
            && item.ImplementationType == typeof(RestHostedService)))
        {
            // Insert only our bridge; preserve the application's existing descriptor order.
            int before = services.ToList().FindIndex(item => !item.IsKeyedService && item.ServiceType == typeof(IHostedService)
                && (item.ImplementationType == typeof(HeartbeatHostedService) || item.ImplementationType == typeof(StartupHostedService)));
            var descriptor = ServiceDescriptor.Singleton<IHostedService, RestHostedService>();
            if (before < 0)
            {
                services.Add(descriptor);
            }
            else
            {
                services.Insert(before, descriptor);
            }
        }
        return services;
    }

    private static RestClientRuntime CreateRuntime(IServiceProvider root)
    {
        // Catalog validation must precede any application transport factory. Activation attaches
        // the module later, so resolving the Metrics facade here does not resolve this owner again.
        _ = root.GetService<IMetrics>();
        RestClientComposition composition = root.GetRequiredService<RestClientComposition>();
        RestClientRegistration[] registrations = root.GetServices<RestClientRegistration>().ToArray();
        RestRetryPolicy[] policies = root.GetServices<RestRetryPolicy>().ToArray();
        IRestRetryExecutor? executor = root.GetService<IRestRetryExecutor>();
        if (policies.Length > 32 || policies.Select(policy => policy.Name).Distinct(StringComparer.Ordinal).Count() != policies.Length)
        {
            throw new InvalidOperationException("Use at most thirty-two distinct explicit retry policies.");
        }
        foreach (var registration in registrations)
        {
            string? selected = registration.Options.RetryPolicy.PolicyName;
            if (selected is not null && (executor is null || !policies.Any(policy => policy.Name == selected)))
            {
                throw new InvalidOperationException("The selected retry policy requires its registered optional executor and exact policy.");
            }
        }
        var bindings = new List<RestClientBinding>();
        try
        {
            IMeterFactory meterFactory = root.GetRequiredService<IMeterFactory>();
            foreach (var registration in registrations)
            {
                RestClientSnapshot effective = registration.Options;
                RestSecurityBinding? security = registration.SecurityBindingFactory is { } bindingFactory
                    ? bindingFactory(root) ?? throw new InvalidOperationException("The security binding factory returned null.")
                    : effective.SecurityBinding;
                if (security is not null)
                {
                    if (security.PolicyOwner is not null)
                    {
                        security.ValidateForPolicy(root.GetRequiredService<CIS.Security.SecurityPolicySnapshot>());
                    }
                    var authenticate = registration.AuthenticationFactory?.Invoke(root);
                    if (registration.AuthenticationFactory is not null && authenticate is null)
                    {
                        throw new InvalidOperationException("The authentication factory returned null.");
                    }
                    effective = effective.WithSecurityBinding(security, authenticate);
                }
                RestHttpTransport transport = registration.SecurityTransportFactory is { } secureFactory
                    ? secureFactory(root, security!) ?? throw new InvalidOperationException("The security transport factory returned null.")
                    : registration.BorrowedTransport is { } factory
                        ? factory(root) ?? throw new InvalidOperationException("The borrowed transport factory returned null.")
                        : security is not null
                            ? RestHttpTransport.CreateSecurityBound(security, meterFactory, clock: root.GetRequiredService<TimeProvider>())
                            : RestHttpTransport.Create(meterFactory);
                bool owns = registration.BorrowedTransport is null && registration.SecurityTransportFactory is null;
                // Record ownership before validation so every created owned pool is cleaned on failure.
                bindings.Add(new(effective, transport, owns));
                transport.ValidateForHost(meterFactory);
                transport.ValidateBinding(effective.SecurityBinding, effective.AllowedOrigins);
            }
            return new(composition, bindings, root.GetRequiredService<TimeProvider>(), policies, executor,
                root.GetService<ILogger<RestClientRuntime>>());
        }
        catch (Exception original)
        {
            var failures = new List<Exception>();
            foreach (var binding in bindings.Where(binding => binding.OwnsTransport))
            {
                try
                {
                    binding.Transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception cleanup) { failures.Add(cleanup); }
            }
            if (failures.Count != 0)
            {
                throw new AggregateException("REST activation and owned transport cleanup both failed.",
                    new[] { original }.Concat(failures));
            }
            throw;
        }
    }
}

internal sealed record RestClientRegistration(RestClientSnapshot Options, Func<IServiceProvider, RestHttpTransport>? BorrowedTransport,
    Func<IServiceProvider, RestSecurityBinding>? SecurityBindingFactory = null,
    Func<IServiceProvider, Func<RestAuthenticationContext, CancellationToken, Task>>? AuthenticationFactory = null,
    Func<IServiceProvider, RestSecurityBinding, RestHttpTransport>? SecurityTransportFactory = null);
