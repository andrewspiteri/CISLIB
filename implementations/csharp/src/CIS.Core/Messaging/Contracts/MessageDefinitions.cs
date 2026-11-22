using CIS.Operations;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Messaging;

/// <summary>The registered semantics of a typed request.</summary>
public enum MessageRequestMode
{
    /// <summary>A read whose freshness belongs to the application contract.</summary>
    Query,
    /// <summary>A registered RM-03 operation with an original recoverable business result.</summary>
    DurableCommand
}

/// <summary>Typed trusted application resolution of the actual protected resource.</summary>
/// <param name="services">Services in the independently established delivery scope.</param>
/// <param name="value">The explicitly decoded bounded application value.</param>
/// <param name="caller">Established current caller data, never self-asserted headers.</param>
/// <param name="cancellationToken">The current cooperative token.</param>
/// <returns>The exact server-owned resource to authorize.</returns>
public delegate ValueTask<CapabilityTarget> MessageTargetResolver<T>(IServiceProvider services, T value,
    CallerContext? caller, CancellationToken cancellationToken);

/// <summary>Optional current application policy for original-producer reauthorization during a trusted retry/redrive.</summary>
/// <param name="services">Services in the current retry service's independently established scope.</param>
/// <param name="value">The decoded business input.</param>
/// <param name="caller">The current authenticated retry service, never an impersonated original caller.</param>
/// <param name="original">Retained verified original audit facts, not delegated authority.</param>
/// <param name="binding">The exact retained operation scope/resource when this delivery uses Operations.</param>
/// <param name="cancellationToken">Cooperative current execution cancellation.</param>
/// <returns>Whether additional current application rules permit execution or replay disclosure.</returns>
public delegate ValueTask<bool> MessageRetryValidator<T>(IServiceProvider services, T value, CallerContext caller,
    MessageProvenance original, MessageOperationBinding? binding, CancellationToken cancellationToken);

/// <summary>Explicit delivery services and participant context, without transaction-completion ownership.</summary>
public sealed class MessageHandlerContext
{
    /// <summary>Creates handler data after the dispatcher establishes the independent authorized scope.</summary>
    public MessageHandlerContext(ILocalTransactionContext transaction, IServiceProvider services, MessageEnvelope envelope,
        int deliveryOrdinal = 1, int generation = 0)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(envelope);
        Transaction = transaction;
        Services = services;
        Envelope = envelope;
        DeliveryOrdinal = MessageText.Range(deliveryOrdinal, 1, 32, nameof(deliveryOrdinal));
        Generation = MessageText.Range(generation, 0, int.MaxValue, nameof(generation));
    }

    /// <summary>Gets the original borrowed local participant.</summary>
    public ILocalTransactionContext Transaction
    {
        get;
    }
    /// <summary>Gets independently scoped services; join owned work before scope disposal.</summary>
    public IServiceProvider Services
    {
        get;
    }
    /// <summary>Gets immutable wire metadata; producer fields do not authenticate.</summary>
    public MessageEnvelope Envelope
    {
        get;
    }
    /// <summary>Gets scheduled ordinal, not a strict physical invocation count.</summary>
    public int DeliveryOrdinal
    {
        get;
    }
    /// <summary>Gets the audited explicit redrive generation.</summary>
    public int Generation
    {
        get;
    }
}

/// <summary>One explicitly scoped event or ordinary command handler.</summary>
public interface IMessageHandler<T>
{
    /// <summary>Performs application work outside native callbacks using the original participant.</summary>
    Task HandleAsync(MessageHandlerContext context, T value, CancellationToken cancellationToken = default);
}

/// <summary>A query or durable command business handler; exceptions are not automatically business rejection.</summary>
public interface IRequestHandler<TRequest, TResponse>
{
    /// <summary>Returns an independent typed Succeed/Reject decision without claiming commit.</summary>
    Task<OperationDecision<TResponse>> HandleAsync(MessageHandlerContext context, TRequest value,
        CancellationToken cancellationToken = default);
}

/// <summary>One configured logical route and stable payload contract.</summary>
public abstract class MessageRouteDefinition
{
    private protected MessageRouteDefinition(string alias, MessageKind kind, MessageContract contract, bool allowNoSubscribers)
    {
        Alias = MessageText.Alias(alias, nameof(alias));
        if (kind is not MessageKind.Event and not MessageKind.Command)
        {
            throw new ArgumentException("Configure an event fan-out route or a single-service command/request route.", nameof(kind));
        }
        if (allowNoSubscribers && kind != MessageKind.Event)
        {
            throw new ArgumentException("Only an explicit event route can allow no subscribers.", nameof(allowNoSubscribers));
        }
        ArgumentNullException.ThrowIfNull(contract);
        Kind = kind;
        Contract = contract;
        AllowNoSubscribers = allowNoSubscribers;
    }

    /// <summary>Gets the registered finite route alias.</summary>
    public string Alias
    {
        get;
    }
    /// <summary>Gets Event fan-out or Command single-service routing; typed requests use the latter.</summary>
    public MessageKind Kind
    {
        get;
    }
    /// <summary>Gets the captured portable payload name/version.</summary>
    public MessageContract Contract
    {
        get;
    }
    /// <summary>Gets explicit event no-binding policy; it never means Delivered or Processed.</summary>
    public bool AllowNoSubscribers
    {
        get;
    }
    internal abstract Type ValueType
    {
        get;
    }
}

/// <summary>A typed route whose explicit codec is captured passively, without reflection or I/O.</summary>
public sealed class MessageRouteDefinition<T> : MessageRouteDefinition
{
    /// <summary>Registers one stable codec and logical routing policy.</summary>
    public MessageRouteDefinition(string alias, MessageKind kind, IMessageCodec<T> codec, bool allowNoSubscribers = false)
        : base(alias, kind, codec?.Contract ?? throw new ArgumentNullException(nameof(codec)), allowNoSubscribers)
    {
        Codec = codec;
    }

    /// <summary>Gets the application-owned thread-safe explicit DTO codec.</summary>
    public IMessageCodec<T> Codec
    {
        get;
    }
    internal override Type ValueType => typeof(T);
}

/// <summary>A finite store alias mapped to an application-owned RM-02 database registration.</summary>
public sealed record MessageStoreDefinition
{
    /// <summary>Copies metadata without opening a data source.</summary>
    public MessageStoreDefinition(string alias, string databaseName)
    {
        Alias = MessageText.Alias(alias, nameof(alias));
        DatabaseName = MessageText.Alias(databaseName, nameof(databaseName));
    }

    /// <summary>Gets the logical store alias used by observations.</summary>
    public string Alias
    {
        get;
    }
    /// <summary>Gets the exact original-participant database alias.</summary>
    public string DatabaseName
    {
        get;
    }
}

/// <summary>Caller request contract metadata, independent of registering a responder handler.</summary>
public abstract class MessageRequestDefinition
{
    private protected MessageRequestDefinition(string alias, string routeAlias, MessageContract requestContract,
        MessageContract responseContract, MessageRequestMode mode, string responderApplication, string responderEnvironment,
        string? operationName, IEnumerable<string>? responseCapabilities)
    {
        Alias = MessageText.Alias(alias, nameof(alias));
        RouteAlias = MessageText.Alias(routeAlias, nameof(routeAlias));
        ArgumentNullException.ThrowIfNull(requestContract);
        ArgumentNullException.ThrowIfNull(responseContract);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        if ((mode == MessageRequestMode.DurableCommand) != (operationName is not null))
        {
            throw new ArgumentException("Only durable command mode requires a registered operation alias.", nameof(operationName));
        }
        var responder = new CapabilityTarget(responderApplication, responderEnvironment);
        RequestContract = requestContract;
        ResponseContract = responseContract;
        Mode = mode;
        ResponderApplication = responder.Application;
        ResponderEnvironment = responder.Environment;
        OperationName = operationName is null ? null : MessageText.Alias(operationName, nameof(operationName));
        ResponseCapabilities = MessageText.Capabilities(responseCapabilities ?? ["messaging.reply"], nameof(responseCapabilities));
    }

    /// <summary>Gets the caller's registered request category.</summary>
    public string Alias
    {
        get;
    }
    /// <summary>Gets the registered single-service route.</summary>
    public string RouteAlias
    {
        get;
    }
    /// <summary>Gets the captured request schema.</summary>
    public MessageContract RequestContract
    {
        get;
    }
    /// <summary>Gets the allowlisted reply schema.</summary>
    public MessageContract ResponseContract
    {
        get;
    }
    /// <summary>Gets Query or DurableCommand semantics.</summary>
    public MessageRequestMode Mode
    {
        get;
    }
    /// <summary>Gets the expected authenticated responder application.</summary>
    public string ResponderApplication
    {
        get;
    }
    /// <summary>Gets the expected authenticated responder environment.</summary>
    public string ResponderEnvironment
    {
        get;
    }
    /// <summary>Gets the existing Operations alias for durable mode.</summary>
    public string? OperationName
    {
        get;
    }
    /// <summary>Gets current capabilities required of the actual responder to reply to this local request category.</summary>
    public IReadOnlyList<string> ResponseCapabilities { get; }
    internal abstract Type RequestType
    {
        get;
    }
    internal abstract Type ResponseType
    {
        get;
    }
}

/// <summary>A typed caller contract with explicit request/reply codecs; no server handler is constructed.</summary>
public sealed class MessageRequestDefinition<TRequest, TResponse> : MessageRequestDefinition
{
    /// <summary>Captures caller contracts and expected remote authority.</summary>
    public MessageRequestDefinition(string alias, string routeAlias, IMessageCodec<TRequest> requestCodec,
        IMessageCodec<TResponse> responseCodec, MessageRequestMode mode, string responderApplication,
        string responderEnvironment, string? operationName = null, IEnumerable<string>? responseCapabilities = null)
        : base(alias, routeAlias, requestCodec?.Contract ?? throw new ArgumentNullException(nameof(requestCodec)),
            responseCodec?.Contract ?? throw new ArgumentNullException(nameof(responseCodec)), mode,
            responderApplication, responderEnvironment, operationName, responseCapabilities)
    {
        RequestCodec = requestCodec;
        ResponseCodec = responseCodec;
    }

    /// <summary>Gets the explicit application-owned request codec.</summary>
    public IMessageCodec<TRequest> RequestCodec
    {
        get;
    }
    /// <summary>Gets the explicit application-owned response value codec.</summary>
    public IMessageCodec<TResponse> ResponseCodec
    {
        get;
    }
    internal override Type RequestType => typeof(TRequest);
    internal override Type ResponseType => typeof(TResponse);
}

/// <summary>Mutable per-subscription setup copied before registration.</summary>
public sealed class MessageSubscriptionOptions
{
    /// <summary>Gets or sets actual admitted work per subscription.</summary>
    public int Concurrency { get; set; } = 8;
    /// <summary>Gets or sets finite native deliveries in flight, additionally bounded by process admission.</summary>
    public int Prefetch { get; set; } = 16;
    /// <summary>Gets or sets total scheduled ordinals including the first.</summary>
    public int MaximumDeliveryOrdinals { get; set; } = 5;
    /// <summary>Gets or sets one finite delay per later ordinal.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays
    {
        get; set;
    } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];
    /// <summary>Gets or sets bounded proportional jitter, initially ten percent.</summary>
    public double RetryJitterRatio { get; set; } = 0.1;
    /// <summary>Gets or sets current service capabilities required for internal retries.</summary>
    public IReadOnlyList<string> RetryCapabilities { get; set; } = ["messaging.retry"];
    /// <summary>Gets or sets capabilities required to send a result to the recorded caller endpoint.</summary>
    public IReadOnlyList<string> ReplyCapabilities { get; set; } = ["messaging.reply"];
    /// <summary>Gets or sets explicit privileged redrive capabilities.</summary>
    public IReadOnlyList<string> RedriveCapabilities { get; set; } = ["messaging.redrive"];
    /// <summary>Gets or sets optional safe normal-call debug summaries, initially false.</summary>
    public bool LogCalls
    {
        get; set;
    }
}

/// <summary>Immutable finite per-subscription delivery/retry policy.</summary>
public sealed class MessageSubscriptionSettings
{
    internal MessageSubscriptionSettings(MessageSubscriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Concurrency = MessageText.Range(options.Concurrency, 1, 256, nameof(options.Concurrency));
        Prefetch = MessageText.Range(options.Prefetch, Concurrency, 256, nameof(options.Prefetch));
        MaximumDeliveryOrdinals = MessageText.Range(options.MaximumDeliveryOrdinals, 1, 32, nameof(options.MaximumDeliveryOrdinals));
        ArgumentNullException.ThrowIfNull(options.RetryDelays);
        var delays = new List<TimeSpan>();
        foreach (TimeSpan delay in options.RetryDelays)
        {
            if (delays.Count == 31)
            {
                throw new ArgumentException("Supply at most thirty-one retry delays.", nameof(options));
            }
            delays.Add(MessageText.Duration(delay, TimeSpan.FromMilliseconds(1), TimeSpan.FromDays(1), nameof(options.RetryDelays)));
        }
        if (delays.Count != MaximumDeliveryOrdinals - 1)
        {
            throw new ArgumentException("Supply exactly one delay per scheduled retry after the first ordinal.", nameof(options));
        }
        if (!double.IsFinite(options.RetryJitterRatio) || options.RetryJitterRatio is < 0 or > 0.25)
        {
            throw new ArgumentOutOfRangeException(nameof(options.RetryJitterRatio));
        }
        RetryDelays = Array.AsReadOnly(delays.ToArray());
        RetryJitterRatio = options.RetryJitterRatio;
        RetryCapabilities = MessageText.Capabilities(options.RetryCapabilities, nameof(options.RetryCapabilities));
        ReplyCapabilities = MessageText.Capabilities(options.ReplyCapabilities, nameof(options.ReplyCapabilities));
        RedriveCapabilities = MessageText.Capabilities(options.RedriveCapabilities, nameof(options.RedriveCapabilities));
        LogCalls = options.LogCalls;
    }

    /// <summary>Gets subscription work admission.</summary>
    public int Concurrency
    {
        get;
    }
    /// <summary>Gets native prefetch, excluding undocumented unbounded buffering.</summary>
    public int Prefetch
    {
        get;
    }
    /// <summary>Gets scheduled ordinal budget; crashes may revisit the same ordinal.</summary>
    public int MaximumDeliveryOrdinals
    {
        get;
    }
    /// <summary>Gets immutable finite later-ordinal delays.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays
    {
        get;
    }
    /// <summary>Gets finite bounded proportional jitter.</summary>
    public double RetryJitterRatio
    {
        get;
    }
    /// <summary>Gets capabilities for the retry service's current authority.</summary>
    public IReadOnlyList<string> RetryCapabilities
    {
        get;
    }
    /// <summary>Gets reply-destination capabilities.</summary>
    public IReadOnlyList<string> ReplyCapabilities
    {
        get;
    }
    /// <summary>Gets privileged redrive capabilities.</summary>
    public IReadOnlyList<string> RedriveCapabilities
    {
        get;
    }
    /// <summary>Gets safe diagnostic normal-call selection.</summary>
    public bool LogCalls
    {
        get;
    }
}

/// <summary>A logical receiving identity and explicit business/authorization registration.</summary>
public abstract class MessageSubscriptionDefinition
{
    private protected MessageSubscriptionDefinition(string alias, string databaseName, string routeAlias,
        MessageContract contract, IEnumerable<string> capabilities, MessageSubscriptionOptions? options)
    {
        Alias = MessageText.Alias(alias, nameof(alias));
        DatabaseName = MessageText.Alias(databaseName, nameof(databaseName));
        RouteAlias = MessageText.Alias(routeAlias, nameof(routeAlias));
        ArgumentNullException.ThrowIfNull(contract);
        Contract = contract;
        Capabilities = MessageText.Capabilities(capabilities, nameof(capabilities));
        Settings = new(options ?? new());
    }

    /// <summary>Gets stable logical consumer identity, excluding instance and deployment.</summary>
    public string Alias
    {
        get;
    }
    /// <summary>Gets the same-database original participant alias.</summary>
    public string DatabaseName
    {
        get;
    }
    /// <summary>Gets the configured ingress route.</summary>
    public string RouteAlias
    {
        get;
    }
    /// <summary>Gets the exact current accepted payload schema.</summary>
    public MessageContract Contract
    {
        get;
    }
    /// <summary>Gets explicit capabilities checked against the actual resolved resource.</summary>
    public IReadOnlyList<string> Capabilities
    {
        get;
    }
    /// <summary>Gets immutable resource/retry/diagnostic settings.</summary>
    public MessageSubscriptionSettings Settings
    {
        get;
    }
    internal abstract Type ValueType
    {
        get;
    }
    internal virtual MessageRequestDefinition? RequestMetadata => null;
    internal abstract TResult Accept<TResult>(IMessageSubscriptionVisitor<TResult> visitor);
}

/// <summary>A typed event/ordinary-command binding that constructs its handler only inside the delivery scope.</summary>
public sealed class MessageSubscriptionDefinition<T> : MessageSubscriptionDefinition
{
    /// <summary>Captures factories without resolving them or performing network/database work.</summary>
    public MessageSubscriptionDefinition(string alias, string databaseName, string routeAlias, IMessageCodec<T> codec,
        Func<IServiceProvider, IMessageHandler<T>> handlerFactory, MessageTargetResolver<T> targetResolver,
        IEnumerable<string> capabilities, MessageSubscriptionOptions? options = null, MessageRetryValidator<T>? retryValidator = null)
        : base(alias, databaseName, routeAlias, codec?.Contract ?? throw new ArgumentNullException(nameof(codec)), capabilities, options)
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        ArgumentNullException.ThrowIfNull(targetResolver);
        Codec = codec;
        HandlerFactory = handlerFactory;
        TargetResolver = targetResolver;
        RetryValidator = retryValidator;
    }

    /// <summary>Gets the explicit typed DTO codec.</summary>
    public IMessageCodec<T> Codec
    {
        get;
    }
    /// <summary>Gets the application-owned scoped handler factory.</summary>
    public Func<IServiceProvider, IMessageHandler<T>> HandlerFactory
    {
        get;
    }
    /// <summary>Gets trusted application resource resolution, never caller-supplied authority.</summary>
    public MessageTargetResolver<T> TargetResolver
    {
        get;
    }
    /// <summary>Gets optional explicit current original-producer policy; registration never invokes it.</summary>
    public MessageRetryValidator<T>? RetryValidator { get; }
    internal override Type ValueType => typeof(T);
    internal override TResult Accept<TResult>(IMessageSubscriptionVisitor<TResult> visitor) => visitor.Visit(this);
}

/// <summary>A responder binding; durable mode also accepts commands without a reply waiter.</summary>
public sealed class MessageRequestSubscriptionDefinition<TRequest, TResponse> : MessageSubscriptionDefinition
{
    /// <summary>Captures typed response work separately from the caller's request-contract registration.</summary>
    public MessageRequestSubscriptionDefinition(string alias, string databaseName,
        MessageRequestDefinition<TRequest, TResponse> request,
        Func<IServiceProvider, IRequestHandler<TRequest, TResponse>> handlerFactory,
        MessageTargetResolver<TRequest> targetResolver, IEnumerable<string> capabilities, MessageSubscriptionOptions? options = null,
        MessageRetryValidator<TRequest>? retryValidator = null)
        : base(alias, databaseName, request?.RouteAlias ?? throw new ArgumentNullException(nameof(request)), request.RequestContract,
            capabilities, options)
    {
        ArgumentNullException.ThrowIfNull(handlerFactory);
        ArgumentNullException.ThrowIfNull(targetResolver);
        Request = request;
        HandlerFactory = handlerFactory;
        TargetResolver = targetResolver;
        RetryValidator = retryValidator;
    }

    /// <summary>Gets the exact caller/responder codec and mode contract.</summary>
    public MessageRequestDefinition<TRequest, TResponse> Request
    {
        get;
    }
    /// <summary>Gets the scoped typed business handler factory.</summary>
    public Func<IServiceProvider, IRequestHandler<TRequest, TResponse>> HandlerFactory
    {
        get;
    }
    /// <summary>Gets the actual-resource resolver, which must agree with the Operations binding in durable mode.</summary>
    public MessageTargetResolver<TRequest> TargetResolver
    {
        get;
    }
    /// <summary>Gets optional current original-producer policy, checked before execution and replay disclosure.</summary>
    public MessageRetryValidator<TRequest>? RetryValidator { get; }
    internal override Type ValueType => typeof(TRequest);
    internal override MessageRequestDefinition? RequestMetadata => Request;
    internal override TResult Accept<TResult>(IMessageSubscriptionVisitor<TResult> visitor) => visitor.Visit(this);
}

internal interface IMessageSubscriptionVisitor<TResult>
{
    TResult Visit<T>(MessageSubscriptionDefinition<T> definition);
    TResult Visit<TRequest, TResponse>(MessageRequestSubscriptionDefinition<TRequest, TResponse> definition);
}
