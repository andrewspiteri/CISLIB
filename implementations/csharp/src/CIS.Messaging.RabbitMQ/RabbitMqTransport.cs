using System.Runtime.ExceptionServices;
using CIS.Security;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CIS.Messaging.RabbitMQ;

/// <summary>The optional AMQP 0-9-1 lifetime. Registration is passive; existing hosting owns start and actual drain.</summary>
public sealed partial class RabbitMqTransport : IMessageTransport
{
    /// <summary>The provider-owned callback alias for the one local reply endpoint.</summary>
    public const string ReplySubscriptionAlias = MessagingSystemSubscriptions.Replies;
    private readonly object gate = new();
    private readonly RabbitMqSettings settings;
    private readonly MessagingSnapshot snapshot;
    private readonly RabbitMqEndpointNamespace own;
    private readonly LocalApplicationIdentity local;
    private readonly RabbitMqEvidenceRegistry evidence;
    private readonly ISecurityProfileStatusReporter securityStatus;
    private readonly TimeProvider clock;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource dispatchReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<PublisherLane> publishers = [];
    private readonly List<ConsumerOwner> consumers = [];
    private readonly SemaphoreSlim available;
    private RabbitMqConnectionSettings? native;
    private IConnection? connection;
    private Task? start;
    private Task? stopConsumers;
    private Task? stop;
    private TaskCompletionSource? publicationDrain;
    private ExceptionDispatchInfo? callbackFault;
    private bool stopping;
    private bool ready;
    private int activePublications;
    private int nextLane;
    private long connectionGeneration;
    private string? failureCode;

    internal RabbitMqTransport(RabbitMqSettings settings, MessagingSnapshot snapshot, SecurityPolicySnapshot policy,
        RabbitMqEvidenceRegistry evidence, ISecurityProfileStatusReporter securityStatus, TimeProvider clock)
    {
        this.settings = settings;
        this.snapshot = snapshot;
        this.evidence = evidence;
        this.securityStatus = securityStatus;
        this.clock = clock;
        snapshot.Validate();
        local = LocalApplicationIdentity.Capture();
        own = settings.GetNamespace(local.Application, local.Environment);
        SecurityProfile profile = policy.GetProfile(settings.SecurityProfile);
        SecurityIdentityMapping? producer = profile.IdentityMappings.SingleOrDefault(item =>
            item.Subject == new SecuritySubject(settings.SubjectIssuer, settings.UserName)
            && item.ActorKind == SecurityActorKind.Application);
        if (local.Application == "unknown" || local.Environment == "unknown"
            || producer?.Application != local.Application || producer.Environment != local.Environment
            || producer.ClientApplicationId is not null)
        {
            throw new ArgumentException("The configured publishing principal must map to the known captured local application/environment.");
        }
        if (snapshot.Subscriptions.Any(item => item.Alias == ReplySubscriptionAlias))
        {
            throw new ArgumentException("The provider reply callback alias is reserved.");
        }
        available = new(snapshot.Settings.PublisherConcurrency, snapshot.Settings.PublisherConcurrency);
    }

    /// <inheritdoc />
    public MessageReplyRoute? ReplyEndpoint { get; private set; }
    /// <inheritdoc />
    public bool IsReady
    {
        get
        {
            lock (gate)
            {
                return ready && !stopping && connection?.IsOpen == true
                    && publishers.All(item => item.Channel.IsOpen) && consumers.All(item => item.IsReady);
            }
        }
    }
    /// <summary>Gets one bounded safe readiness code, without payloads, credentials or native exception text.</summary>
    public string? FailureCode
    {
        get
        {
            lock (gate)
            {
                return failureCode;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return start ??= StartCoreAsync(dispatch, cancellationToken);
        }
    }

    private async Task StartCoreAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken)
    {
        try
        {
            native = new(settings, MessagingLimits.MaximumSupportedPayloadBytes + MessagingLimits.MaximumMetadataBytes);
            connection = await native.Factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            connection.ConnectionShutdownAsync += (_, _) =>
            {
                Interlocked.Increment(ref connectionGeneration);
                SetFailure("messaging.broker.unavailable");
                ReportSecurityStatus(false);
                return Task.CompletedTask;
            };
            connection.RecoverySucceededAsync += (_, _) =>
            {
                if (IsReady)
                {
                    lock (gate)
                    {
                        failureCode = null;
                    }
                    ReportSecurityStatus(true);
                }
                return Task.CompletedTask;
            };
            await using (IChannel validation = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                await RabbitMqTopology.ValidateAsync(validation, settings, snapshot, own, cancellationToken).ConfigureAwait(false);
            }
            for (int index = 0; index < snapshot.Settings.PublisherConcurrency; index++)
            {
                IChannel channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true),
                    cancellationToken).ConfigureAwait(false);
                publishers.Add(new(channel));
            }
            if (snapshot.Requests.Count != 0)
            {
                ReplyEndpoint = new(local.Application, local.Environment, Guid.NewGuid());
                await using IChannel setup = await connection.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                var arguments = RabbitMqTopology.QueueArguments(settings,
                    RabbitMqTopology.NativeQuarantineExchange(settings, own, "replies"));
                arguments.Add("x-expires", checked((long)snapshot.Settings.ReplyEndpointLifetime.TotalMilliseconds));
                arguments.Add("x-message-ttl", checked((long)snapshot.Settings.ReplyEndpointLifetime.TotalMilliseconds));
                await setup.QueueDeclareAsync(RabbitMqTopology.ReplyQueue(settings, own, ReplyEndpoint.EndpointId),
                    true, false, false, arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
                await setup.QueueBindAsync(RabbitMqTopology.ReplyQueue(settings, own, ReplyEndpoint.EndpointId),
                    RabbitMqTopology.ReplyExchange(settings, own), ReplyEndpoint.EndpointId.ToString("N"),
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            foreach (MessageSubscriptionDefinition subscription in snapshot.Subscriptions)
            {
                await StartConsumerAsync(subscription.Alias, subscription.RouteAlias,
                    RabbitMqTopology.SubscriptionQueue(settings, own, subscription.Alias),
                    subscription.Settings.Concurrency, subscription.Settings.Prefetch, false, dispatch, cancellationToken).ConfigureAwait(false);
            }
            if (ReplyEndpoint is { } endpoint)
            {
                await StartConsumerAsync(ReplySubscriptionAlias, ReplySubscriptionAlias,
                    RabbitMqTopology.ReplyQueue(settings, own, endpoint.EndpointId), 1, 16, true, dispatch, cancellationToken).ConfigureAwait(false);
            }
            lock (gate)
            {
                ready = true;
            }
            ReportSecurityStatus(true);
            dispatchReady.TrySetResult();
        }
        catch (Exception original)
        {
            SetFailure("messaging.broker.start-failed");
            lifetime.Cancel();
            dispatchReady.TrySetCanceled();
            try
            {
                await CleanupNativeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException("Messaging startup and cleanup failed.", original, cleanup);
            }
            throw;
        }
    }

    private async Task StartConsumerAsync(string alias, string route, string queue, int concurrency, int prefetch,
        bool reply, Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken)
    {
        IChannel channel = await connection!.CreateChannelAsync(new CreateChannelOptions(false, false,
            consumerDispatchConcurrency: checked((ushort)concurrency)), cancellationToken).ConfigureAwait(false);
        var owner = new ConsumerOwner(this, channel, alias, route, reply, dispatch);
        consumers.Add(owner);
        await channel.BasicQosAsync(0, checked((ushort)prefetch), false, cancellationToken).ConfigureAwait(false);
        var consumer = new NativeConsumer(channel, owner);
        consumer.ReceivedAsync += owner.ReceiveAsync;
        string tag = "cis-" + Guid.NewGuid().ToString("N");
        owner.SetTag(tag);
        await channel.BasicConsumeAsync(queue, false, tag, false, false, null, consumer, cancellationToken).ConfigureAwait(false);
        await owner.WaitForRegistrationAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        cancellationToken.ThrowIfCancellationRequested();
        if (!await available.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return Rejected(MessagePublicationFailure.Transient, "messaging.publish.overloaded");
        }
        lock (gate)
        {
            if (!ready || stopping || connection?.IsOpen != true || publishers.Count == 0)
            {
                available.Release();
                return Rejected(MessagePublicationFailure.Transient, "messaging.publish.unavailable");
            }
            activePublications++;
        }
        try
        {
            if (publication.Purpose == MessageRecordPurpose.Reply
                && clock.GetUtcNow() - publication.Message.Envelope.CreatedAtUtc >= snapshot.Settings.ReplyEndpointLifetime)
            {
                return Rejected(MessagePublicationFailure.ReplyDestinationExpired, "messaging.reply.destination-expired");
            }
            (string exchange, string routingKey, bool mandatory) = ResolveDestination(publication);
            PublisherLane lane = publishers[(int)((uint)Interlocked.Increment(ref nextLane) % (uint)publishers.Count)];
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            deadline.CancelAfter(snapshot.Settings.PublicationTimeout);
            await lane.Gate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                ReadOnlyMemory<byte> body = publication.Message.BytesMemory;
                var properties = new BasicProperties
                {
                    Persistent = true,
                    ContentType = "application/vnd.cis.messaging.v1+json",
                    MessageId = publication.Message.Envelope.MessageId.ToString("D"),
                    UserId = settings.UserName,
                    Headers = new Dictionary<string, object?>
                    {
                        ["cis-purpose"] = (int)publication.Purpose,
                        ["cis-ordinal"] = publication.DeliveryOrdinal,
                        ["cis-outbox"] = publication.OutboxRecordId.ToString("D"),
                        ["cis-generation"] = publication.Generation
                    }
                };
                if (publication.TargetSubscription is { } target)
                {
                    properties.Headers.Add("cis-target", target);
                }
                if (publication.SafeCode is { } code)
                {
                    properties.Headers.Add("cis-code", code);
                }
                try
                {
                    await lane.Channel.BasicPublishAsync(exchange, routingKey, mandatory, properties, body, deadline.Token).ConfigureAwait(false);
                    return new(MessagePublicationDisposition.Confirmed, safeCode: mandatory ? null : "messaging.publish.no-subscriber-policy");
                }
                catch (PublishException error)
                {
                    return Rejected(error.IsReturn ? MessagePublicationFailure.Unroutable : MessagePublicationFailure.Transient,
                        error.IsReturn ? "messaging.publish.unroutable" : "messaging.publish.nacked");
                }
                catch (OperationCanceledException)
                {
                    return Unknown("messaging.publish.cancelled");
                }
                catch (OperationInterruptedException error) when (error.ShutdownReason?.ReplyCode is 403 or 404 or 405 or 406)
                {
                    SetFailure("messaging.broker.configuration-rejected");
                    return Rejected(MessagePublicationFailure.Configuration, "messaging.publish.configuration-rejected");
                }
                catch (Exception error) when (error is AlreadyClosedException or OperationInterruptedException or IOException)
                {
                    SetFailure("messaging.broker.publication-unavailable");
                    return Unknown("messaging.publish.unknown");
                }
            }
            finally
            {
                lane.Gate.Release();
            }
        }
        finally
        {
            lock (gate)
            {
                activePublications--;
                if (activePublications == 0)
                {
                    publicationDrain?.TrySetResult();
                }
            }
            available.Release();
        }
    }

    private (string Exchange, string Key, bool Mandatory) ResolveDestination(MessagePublication publication)
    {
        if (publication.Purpose == MessageRecordPurpose.Reply)
        {
            if (publication.Message.Envelope.Kind != MessageKind.Reply
                || !snapshot.Requests.Any(item => item.ResponseContract == publication.Message.Envelope.Contract))
            {
                throw new ArgumentException("A reply uses an explicitly registered response contract.", nameof(publication));
            }
            MessageReplyRoute reply = publication.Destination.ReplyRoute!;
            RabbitMqEndpointNamespace destination = settings.GetNamespace(reply.Application, reply.Environment);
            return (RabbitMqTopology.ReplyExchange(settings, destination), reply.EndpointId.ToString("N"), true);
        }
        if (publication.Purpose is MessageRecordPurpose.Retry or MessageRecordPurpose.Redrive)
        {
            MessageSubscriptionDefinition subscription = GetSubscription(publication.TargetSubscription);
            return (RabbitMqTopology.RetryExchange(settings, own, subscription.Alias), subscription.Alias, true);
        }
        if (publication.Purpose is MessageRecordPurpose.Terminal or MessageRecordPurpose.Rejection)
        {
            MessageSubscriptionDefinition subscription = GetSubscription(publication.TargetSubscription);
            return (RabbitMqTopology.TerminalExchange(settings, own, subscription.Alias), subscription.Alias, true);
        }
        MessageRouteDefinition route = snapshot.GetRoute(publication.Destination.RouteAlias);
        if (publication.Message.Envelope.Contract != route.Contract
            || (route.Kind == MessageKind.Event ? publication.Message.Envelope.Kind != MessageKind.Event
                : publication.Message.Envelope.Kind is not MessageKind.Command and not MessageKind.Request))
        {
            throw new ArgumentException("The publication does not match its registered route contract.", nameof(publication));
        }
        return (RabbitMqTopology.RouteExchange(settings, route.Alias), route.Kind == MessageKind.Event ? "" : "message",
            !route.AllowNoSubscribers);
    }

    private MessageSubscriptionDefinition GetSubscription(string? alias)
        => snapshot.Subscriptions.SingleOrDefault(item => item.Alias == alias)
            ?? throw new ArgumentException("The targeted subscription is not registered.");

    /// <inheritdoc />
    public Task StopConsumersAsync()
    {
        lock (gate)
        {
            stopping = true;
            return stopConsumers ??= StopConsumersCoreAsync();
        }
    }

    private async Task StopConsumersCoreAsync()
    {
        Task? starting = start;
        if (starting is not null)
        {
            try
            {
                await starting.ConfigureAwait(false);
            }
            catch
            {
                return;
            }
        }
        await Task.WhenAll(consumers.Select(item => item.StopAsync())).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        lock (gate)
        {
            stopping = true;
            return stop ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        var failures = new List<Exception>();
        try
        {
            lifetime.Cancel();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        dispatchReady.TrySetCanceled();
        try
        {
            await StopConsumersAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        Task publications;
        lock (gate)
        {
            publications = activePublications == 0 ? Task.CompletedTask
                : (publicationDrain ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
        await publications.ConfigureAwait(false);
        try
        {
            await CleanupNativeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        if (callbackFault is not null)
        {
            if (failures.Count == 0)
            {
                callbackFault.Throw();
            }
            failures.Add(callbackFault.SourceException);
        }
        if (failures.Count != 0)
        {
            throw new AggregateException("Messaging native cleanup failed after actual settlement.", failures);
        }
    }

    private async Task CleanupNativeAsync()
    {
        var failures = new List<Exception>();
        foreach (ConsumerOwner consumer in consumers)
        {
            try
            {
                await consumer.StopAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        foreach (PublisherLane publisher in publishers)
        {
            try
            {
                await publisher.Channel.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        if (connection is not null)
        {
            try
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        native?.Dispose();
        native = null;
        connection = null;
        consumers.Clear();
        publishers.Clear();
        lock (gate)
        {
            ready = false;
        }
        ReportSecurityStatus(false);
        if (failures.Count != 0)
        {
            throw new AggregateException("Messaging native resources failed to close.", failures);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(StopAsync());

    private void SetFailure(string code)
    {
        lock (gate)
        {
            failureCode = code;
        }
    }

    private void RecordCallbackFault(Exception error)
    {
        lock (gate)
        {
            callbackFault ??= ExceptionDispatchInfo.Capture(error);
            failureCode = "messaging.dispatch.failed";
        }
    }

    private void ReportSecurityStatus(bool available)
    {
        securityStatus.Publish(available
            ? new(settings.SecurityProfile, SecurityProfileAvailability.Available)
            : new(settings.SecurityProfile, SecurityProfileAvailability.Unavailable,
                new(SecurityFailureKind.CredentialUnavailable, "The selected broker connection is unavailable.")));
    }

    private void ConfirmNativeRegistration()
    {
        if (connection?.IsOpen == true)
        {
            ReportSecurityStatus(true);
            if (IsReady)
            {
                lock (gate)
                {
                    failureCode = null;
                }
            }
        }
    }

    private static MessagePublicationResult Rejected(MessagePublicationFailure failure, string code)
        => new(MessagePublicationDisposition.Rejected, failure, code);
    private static MessagePublicationResult Unknown(string code)
        => new(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient, code);

    private sealed class PublisherLane(IChannel channel)
    {
        internal IChannel Channel { get; } = channel;
        internal SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
