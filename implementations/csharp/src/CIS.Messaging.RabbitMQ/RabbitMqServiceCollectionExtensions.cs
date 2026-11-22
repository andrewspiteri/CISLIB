using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Messaging.RabbitMQ;

/// <summary>Passive optional adapter registration using the existing native DI provider and security projector.</summary>
public static class RabbitMqServiceCollectionExtensions
{
    /// <summary>Selects one RabbitMQ transport; existing hosting invokes its start/stop methods explicitly.</summary>
    /// <remarks>No broker connection, topology mutation, credential file read or worker starts here.</remarks>
    public static IServiceCollection AddCisRabbitMqMessaging(this IServiceCollection services, RabbitMqOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(item => item.ServiceType == typeof(IMessageTransport)
            || item.ServiceType == typeof(RabbitMqSettings)))
        {
            throw new InvalidOperationException("Select one complete messaging transport per host.");
        }
        RabbitMqSettings settings = options.Capture();
        services.AddSingleton(settings);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(provider => new RabbitMqEvidenceRegistry(settings,
            provider.GetRequiredService<SecurityPolicySnapshot>(), provider.GetRequiredService<TimeProvider>()));
        services.AddCisSecurityProjection(settings.SecurityProfile, provider =>
            new RabbitMqIdentityProjector(provider.GetRequiredService<RabbitMqEvidenceRegistry>()));
        services.AddSingleton(provider => new RabbitMqTransport(settings,
            provider.GetRequiredService<MessagingSnapshot>(), provider.GetRequiredService<SecurityPolicySnapshot>(),
            provider.GetRequiredService<RabbitMqEvidenceRegistry>(),
            provider.GetRequiredService<ISecurityProfileStatusReporter>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IMessageTransport>(provider => provider.GetRequiredService<RabbitMqTransport>());
        services.TryAddSingleton<IMessagePublisher>(provider => provider.GetRequiredService<RabbitMqTransport>());
        return services;
    }
}
