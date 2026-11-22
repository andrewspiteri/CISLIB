namespace CIS.Messaging;

/// <summary>One finite immutable registration snapshot; constructing or validating it performs no I/O.</summary>
public sealed class MessagingSnapshot
{
    /// <summary>The maximum number of registered aliases in each registry.</summary>
    public const int MaximumAliases = 16;

    /// <summary>Copies setup and each finite registry without constructing application handlers or native providers.</summary>
    public MessagingSnapshot(MessagingOptions options, IEnumerable<MessageRouteDefinition>? routes = null,
        IEnumerable<MessageStoreDefinition>? stores = null, IEnumerable<MessageSubscriptionDefinition>? subscriptions = null,
        IEnumerable<MessageRequestDefinition>? requests = null)
        : this(options?.Capture() ?? throw new ArgumentNullException(nameof(options)), routes, stores, subscriptions, requests)
    {
    }

    internal MessagingSnapshot(MessagingSettings settings, IEnumerable<MessageRouteDefinition>? routes,
        IEnumerable<MessageStoreDefinition>? stores, IEnumerable<MessageSubscriptionDefinition>? subscriptions,
        IEnumerable<MessageRequestDefinition>? requests)
    {
        Settings = settings;
        Routes = Copy(routes, item => item.Alias);
        Stores = Copy(stores, item => item.Alias);
        Subscriptions = Copy(subscriptions, item => item.Alias);
        Requests = Copy(requests, item => item.Alias);
    }

    /// <summary>Gets the single restart-required settings snapshot.</summary>
    public MessagingSettings Settings
    {
        get;
    }
    /// <summary>Gets at most sixteen finite route definitions.</summary>
    public IReadOnlyList<MessageRouteDefinition> Routes
    {
        get;
    }
    /// <summary>Gets at most sixteen finite store mappings.</summary>
    public IReadOnlyList<MessageStoreDefinition> Stores
    {
        get;
    }
    /// <summary>Gets at most sixteen stable logical subscriptions.</summary>
    public IReadOnlyList<MessageSubscriptionDefinition> Subscriptions
    {
        get;
    }
    /// <summary>Gets at most sixteen caller request contracts, independently of responder handlers.</summary>
    public IReadOnlyList<MessageRequestDefinition> Requests
    {
        get;
    }
    internal object RegistrationIdentity { get; } = new();

    /// <summary>Validates the complete selected graph without database, broker, service resolution or worker startup.</summary>
    public void Validate()
    {
        if (Requests.Count != 0 && (Settings.MaximumConcurrentDeliveries < 2
            || Settings.MaximumEncodedBufferBytes < 12L * Settings.Limits.MaximumEnvelopeBytes))
        {
            throw new InvalidOperationException("RPC requires reserved reply work and encoded headroom alongside request/response work.");
        }
        if (Subscriptions.Any(item => item.Alias == MessagingSystemSubscriptions.Replies))
        {
            throw new InvalidOperationException("The native requester reply callback subscription is reserved.");
        }
        MessageStoreDefinition selected = Stores.SingleOrDefault(item => item.Alias == Settings.StoreAlias)
            ?? throw new InvalidOperationException("Register the selected messaging store before starting the runtime.");
        foreach (MessageRouteDefinition route in Routes.Where(item => item.Kind == MessageKind.Command))
        {
            if (Subscriptions.Count(item => item.RouteAlias == route.Alias) > 1)
            {
                throw new InvalidOperationException("A command/request route has one logical receiving subscription; replicas compete on that identity.");
            }
        }
        foreach (MessageSubscriptionDefinition subscription in Subscriptions)
        {
            MessageRouteDefinition route = GetRoute(subscription.RouteAlias);
            if (route.Contract != subscription.Contract || route.ValueType != subscription.ValueType
                || subscription.DatabaseName != selected.DatabaseName)
            {
                throw new InvalidOperationException("Subscription route, codec type/contract and original store database must agree.");
            }
            if (subscription.RequestMetadata is { } request
                && !Requests.Any(item => ReferenceEquals(item, request)))
            {
                throw new InvalidOperationException("Register the responder's exact immutable request definition as metadata.");
            }
        }
        foreach (MessageRequestDefinition request in Requests)
        {
            MessageRouteDefinition route = GetRoute(request.RouteAlias);
            if (route.Kind != MessageKind.Command || route.Contract != request.RequestContract
                || route.ValueType != request.RequestType)
            {
                throw new InvalidOperationException("A request needs a matching typed single-service route.");
            }
        }
    }

    /// <summary>Gets one registered route or reports invalid API/configuration use.</summary>
    public MessageRouteDefinition GetRoute(string alias)
    {
        MessageText.Alias(alias, nameof(alias));
        return Routes.SingleOrDefault(item => item.Alias == alias)
            ?? throw new ArgumentException("The logical route is not registered.", nameof(alias));
    }

    /// <summary>Gets one registered request contract or reports invalid API/configuration use.</summary>
    public MessageRequestDefinition GetRequest(string alias)
    {
        MessageText.Alias(alias, nameof(alias));
        return Requests.SingleOrDefault(item => item.Alias == alias)
            ?? throw new ArgumentException("The typed request is not registered.", nameof(alias));
    }

    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? source, Func<T, string> alias) where T : class
    {
        var copied = new List<T>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (source is not null)
        {
            foreach (T item in source)
            {
                ArgumentNullException.ThrowIfNull(item);
                if (copied.Count == MaximumAliases || !names.Add(alias(item)))
                {
                    throw new ArgumentException("Register at most sixteen unique ordinal aliases in each messaging registry.", nameof(source));
                }
                copied.Add(item);
            }
        }
        return Array.AsReadOnly(copied.OrderBy(alias, StringComparer.Ordinal).ToArray());
    }
}
