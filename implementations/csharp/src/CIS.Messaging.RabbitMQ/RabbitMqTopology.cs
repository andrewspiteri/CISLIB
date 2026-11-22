using CIS.Security;
using RabbitMQ.Client;

namespace CIS.Messaging.RabbitMQ;

/// <summary>Explicit opt-in provisioning of the registered durable reference topology; never invoked by registration.</summary>
public static class RabbitMqTopology
{
    /// <summary>Declares exact durable exchanges, quorum queues and bindings using separately selected setup credentials.</summary>
    /// <remarks>Existing mismatches fail; no queue, exchange or accepted message is deleted or replaced.</remarks>
    public static async Task InstallAsync(RabbitMqOptions setupOptions, MessagingSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        snapshot.Validate();
        RabbitMqSettings settings = setupOptions.Capture();
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        RabbitMqEndpointNamespace own = settings.GetNamespace(local.Application, local.Environment);
        using var native = new RabbitMqConnectionSettings(settings,
            MessagingLimits.MaximumSupportedPayloadBytes + MessagingLimits.MaximumMetadataBytes);
        native.Factory.AutomaticRecoveryEnabled = false;
        await using IConnection connection = await native.Factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using IChannel channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (MessageRouteDefinition route in snapshot.Routes)
        {
            await DeclareRouteAsync(channel, settings, route, false, cancellationToken).ConfigureAwait(false);
        }
        foreach (MessageSubscriptionDefinition subscription in snapshot.Subscriptions)
        {
            await DeclareSubscriptionAsync(channel, settings, own, subscription, false, cancellationToken).ConfigureAwait(false);
            MessageRouteDefinition route = snapshot.GetRoute(subscription.RouteAlias);
            string queue = SubscriptionQueue(settings, own, subscription.Alias);
            await channel.QueueBindAsync(queue, RouteExchange(settings, route.Alias),
                route.Kind == MessageKind.Event ? "" : "message", cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(queue, RetryExchange(settings, own, subscription.Alias),
                subscription.Alias, cancellationToken: cancellationToken).ConfigureAwait(false);
            await channel.QueueBindAsync(QuarantineQueue(settings, own, subscription.Alias),
                TerminalExchange(settings, own, subscription.Alias), subscription.Alias,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        await DeclareReplyResourcesAsync(channel, settings, own, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gets the deterministic registered route exchange for deployment permission configuration.</summary>
    public static string GetRouteExchange(RabbitMqSettings settings, string routeAlias)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return RouteExchange(settings, RabbitMqText.Name(routeAlias, nameof(routeAlias), 64));
    }

    /// <summary>Gets one exact owner subscription queue for deployment and bounded diagnostics.</summary>
    public static string GetSubscriptionQueue(RabbitMqSettings settings, string application, string environment, string subscription)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return SubscriptionQueue(settings, settings.GetNamespace(application, environment),
            RabbitMqText.Name(subscription, nameof(subscription), 64));
    }

    /// <summary>Gets the current structured endpoint's physical queue, never resolving an arbitrary caller queue name.</summary>
    public static string GetReplyQueue(RabbitMqSettings settings, MessageReplyRoute route)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(route);
        return ReplyQueue(settings, settings.GetNamespace(route.Application, route.Environment), route.EndpointId);
    }

    internal static async Task ValidateAsync(IChannel channel, RabbitMqSettings settings, MessagingSnapshot snapshot,
        RabbitMqEndpointNamespace own, CancellationToken cancellationToken)
    {
        foreach (MessageRouteDefinition route in snapshot.Routes)
        {
            await DeclareRouteAsync(channel, settings, route, true, cancellationToken).ConfigureAwait(false);
        }
        foreach (MessageSubscriptionDefinition subscription in snapshot.Subscriptions)
        {
            await DeclareSubscriptionAsync(channel, settings, own, subscription, true, cancellationToken).ConfigureAwait(false);
        }
        await DeclareReplyResourcesAsync(channel, settings, own, true, cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeclareRouteAsync(IChannel channel, RabbitMqSettings settings, MessageRouteDefinition route,
        bool validate, CancellationToken cancellationToken)
    {
        string exchange = RouteExchange(settings, route.Alias);
        if (validate)
        {
            await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken).ConfigureAwait(false);
            if (!settings.ValidateTopologyEquivalence)
            {
                return;
            }
        }
        await channel.ExchangeDeclareAsync(exchange, route.Kind == MessageKind.Event ? ExchangeType.Fanout : ExchangeType.Direct,
            true, false, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeclareSubscriptionAsync(IChannel channel, RabbitMqSettings settings, RabbitMqEndpointNamespace own,
        MessageSubscriptionDefinition subscription, bool validate, CancellationToken cancellationToken)
    {
        string alias = subscription.Alias;
        string deadLetter = NativeQuarantineExchange(settings, own, alias);
        await DeclareExchangeAsync(channel, settings, deadLetter, ExchangeType.Fanout, validate, cancellationToken).ConfigureAwait(false);
        await DeclareExchangeAsync(channel, settings, RetryExchange(settings, own, alias), ExchangeType.Direct, validate, cancellationToken).ConfigureAwait(false);
        await DeclareExchangeAsync(channel, settings, TerminalExchange(settings, own, alias), ExchangeType.Direct, validate, cancellationToken).ConfigureAwait(false);
        string quarantine = QuarantineQueue(settings, own, alias);
        await DeclareQueueAsync(channel, settings, quarantine, QueueArguments(settings), validate, cancellationToken).ConfigureAwait(false);
        await DeclareQueueAsync(channel, settings, SubscriptionQueue(settings, own, alias),
            QueueArguments(settings, deadLetter), validate, cancellationToken).ConfigureAwait(false);
        if (!validate)
        {
            await channel.QueueBindAsync(quarantine, deadLetter, "", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeclareReplyResourcesAsync(IChannel channel, RabbitMqSettings settings, RabbitMqEndpointNamespace own,
        bool validate, CancellationToken cancellationToken)
    {
        await DeclareExchangeAsync(channel, settings, ReplyExchange(settings, own), ExchangeType.Direct, validate, cancellationToken).ConfigureAwait(false);
        string deadLetter = NativeQuarantineExchange(settings, own, "replies");
        await DeclareExchangeAsync(channel, settings, deadLetter, ExchangeType.Fanout, validate, cancellationToken).ConfigureAwait(false);
        string queue = QuarantineQueue(settings, own, "replies");
        await DeclareQueueAsync(channel, settings, queue, QueueArguments(settings), validate, cancellationToken).ConfigureAwait(false);
        if (!validate)
        {
            await channel.QueueBindAsync(queue, deadLetter, "", cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task DeclareExchangeAsync(IChannel channel, RabbitMqSettings settings, string name, string type,
        bool validate, CancellationToken cancellationToken)
    {
        if (validate)
        {
            await channel.ExchangeDeclarePassiveAsync(name, cancellationToken).ConfigureAwait(false);
            if (!settings.ValidateTopologyEquivalence)
            {
                return;
            }
        }
        await channel.ExchangeDeclareAsync(name, type, true, false, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeclareQueueAsync(IChannel channel, RabbitMqSettings settings, string name,
        IDictionary<string, object?> arguments, bool validate, CancellationToken cancellationToken)
    {
        if (validate)
        {
            await channel.QueueDeclarePassiveAsync(name, cancellationToken).ConfigureAwait(false);
            if (!settings.ValidateTopologyEquivalence)
            {
                return;
            }
        }
        await channel.QueueDeclareAsync(name, true, false, false, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    internal static IDictionary<string, object?> QueueArguments(RabbitMqSettings settings, string? deadLetterExchange = null)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-max-length"] = settings.MaximumQueueMessages,
            ["x-max-length-bytes"] = settings.MaximumQueueBytes,
            ["x-overflow"] = "reject-publish",
            ["x-delivery-limit"] = -1,
            ["x-delayed-retry-type"] = "disabled"
        };
        if (deadLetterExchange is not null)
        {
            arguments.Add("x-dead-letter-exchange", deadLetterExchange);
            arguments.Add("x-dead-letter-strategy", "at-least-once");
        }
        return arguments;
    }

    internal static string RouteExchange(RabbitMqSettings settings, string alias) => settings.Namespace + ".route." + alias;
    internal static string SubscriptionQueue(RabbitMqSettings settings, RabbitMqEndpointNamespace own, string alias)
        => settings.Namespace + ".sub." + own.Alias + "." + alias;
    internal static string RetryExchange(RabbitMqSettings settings, RabbitMqEndpointNamespace own, string alias)
        => settings.Namespace + ".retry." + own.Alias + "." + alias;
    internal static string TerminalExchange(RabbitMqSettings settings, RabbitMqEndpointNamespace own, string alias)
        => settings.Namespace + ".terminal." + own.Alias + "." + alias;
    internal static string NativeQuarantineExchange(RabbitMqSettings settings, RabbitMqEndpointNamespace own, string alias)
        => settings.Namespace + ".native-quarantine." + own.Alias + "." + alias;
    internal static string QuarantineQueue(RabbitMqSettings settings, RabbitMqEndpointNamespace own, string alias)
        => settings.Namespace + ".quarantine." + own.Alias + "." + alias;
    internal static string ReplyExchange(RabbitMqSettings settings, RabbitMqEndpointNamespace own)
        => settings.Namespace + ".reply." + own.Alias;
    internal static string ReplyQueue(RabbitMqSettings settings, RabbitMqEndpointNamespace own, Guid endpoint)
        => ReplyExchange(settings, own) + "." + endpoint.ToString("N");
}
