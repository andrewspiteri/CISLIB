using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CIS.Messaging.Hosting;

/// <summary>Passive finite definition registration; runtime services are selected separately by existing hosting integration.</summary>
public static class MessageDefinitionServiceCollectionExtensions
{
    /// <summary>Captures restart-required setup without constructing runtime, handler, database or broker services.</summary>
    public static IServiceCollection ConfigureCisMessaging(this IServiceCollection services, MessagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        MessagingSnapshot previous = InspectRegistration(services);
        Replace(services, new(options.Capture(), previous.Routes, previous.Stores, previous.Subscriptions, previous.Requests));
        return services;
    }

    /// <summary>Registers one explicit typed codec and route without invoking application code.</summary>
    public static IServiceCollection AddCisMessageRoute(this IServiceCollection services, MessageRouteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        MessagingSnapshot previous = InspectRegistration(services);
        Replace(services, new(previous.Settings, previous.Routes.Append(definition), previous.Stores, previous.Subscriptions, previous.Requests));
        return services;
    }

    /// <summary>Registers a finite store-to-database mapping without opening a connection.</summary>
    public static IServiceCollection AddCisMessageStore(this IServiceCollection services, MessageStoreDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        MessagingSnapshot previous = InspectRegistration(services);
        Replace(services, new(previous.Settings, previous.Routes, previous.Stores.Append(definition), previous.Subscriptions, previous.Requests));
        return services;
    }

    /// <summary>Registers a logical consumer without resolving its handler or resource resolver.</summary>
    public static IServiceCollection AddCisMessageSubscription(this IServiceCollection services, MessageSubscriptionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        MessagingSnapshot previous = InspectRegistration(services);
        Replace(services, new(previous.Settings, previous.Routes, previous.Stores, previous.Subscriptions.Append(definition), previous.Requests));
        return services;
    }

    /// <summary>Registers caller/responder request contract metadata, independently of any business handler.</summary>
    public static IServiceCollection AddCisMessageRequest(this IServiceCollection services, MessageRequestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(definition);
        MessagingSnapshot previous = InspectRegistration(services);
        Replace(services, new(previous.Settings, previous.Routes, previous.Stores, previous.Subscriptions, previous.Requests.Append(definition)));
        return services;
    }

    internal static MessagingSnapshot InspectRegistration(IServiceCollection services)
    {
        ServiceDescriptor[] descriptors = services.Where(item => item.ServiceType == typeof(MessagingSnapshot)).ToArray();
        if (descriptors.Length == 0)
        {
            return new(new MessagingOptions());
        }
        if (descriptors.Length != 1 || descriptors[0].IsKeyedService
            || descriptors[0].ImplementationInstance is not MessagingSnapshot snapshot)
        {
            throw new InvalidOperationException("Use one passive registration-owned messaging snapshot, without keyed or opaque factories.");
        }
        return snapshot;
    }

    private static void Replace(IServiceCollection services, MessagingSnapshot snapshot)
    {
        services.RemoveAll<MessagingSnapshot>();
        services.AddSingleton(snapshot);
    }
}
