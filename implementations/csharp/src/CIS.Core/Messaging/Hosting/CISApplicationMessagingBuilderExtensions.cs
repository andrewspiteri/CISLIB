using CIS.Messaging;

namespace CIS.Hosting;

/// <summary>Selects existing Messaging hosting in the single CISApplication composition.</summary>
public static class CISApplicationMessagingBuilderExtensions
{
    /// <summary>Opts into sequential Messaging startup and actual pre-stop drain.</summary>
    /// <remarks>Register explicit routes, subscriptions, store and optional native transport in Services.
    /// This call performs no service resolution, broker/database I/O or worker startup.</remarks>
    public static CISApplicationBuilder UseMessaging(this CISApplicationBuilder builder, MessagingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (options is not null)
        {
            CIS.Messaging.Hosting.MessageDefinitionServiceCollectionExtensions.ConfigureCisMessaging(builder.Services, options);
        }
        builder.MessagingEnabled = true;
        return builder;
    }
}
