using Microsoft.AspNetCore.Authentication;

namespace CIS.Messaging;

/// <summary>Native broker acceptance evidence, distinct from application processing.</summary>
public enum MessagePublicationDisposition
{
    /// <summary>The selected broker confirmed acceptance and required routing checks.</summary>
    Confirmed,
    /// <summary>The broker or validated configuration rejected this publication.</summary>
    Rejected,
    /// <summary>The adapter cannot establish whether acceptance occurred.</summary>
    Unknown
}

/// <summary>Finite publication policy classifications; coordinators never parse exception text.</summary>
public enum MessagePublicationFailure
{
    /// <summary>No failure.</summary>
    None,
    /// <summary>A recoverable availability or negative-confirmation condition.</summary>
    Transient,
    /// <summary>A mandatory publication has no permitted route.</summary>
    Unroutable,
    /// <summary>A permanent malformed or incompatible configuration.</summary>
    Configuration,
    /// <summary>An obsolete reply destination has passed its explicit supported age.</summary>
    ReplyDestinationExpired
}

/// <summary>One immutable native acceptance result, without a business-success claim.</summary>
public sealed record MessagePublicationResult
{
    /// <summary>Creates consistent finite acceptance facts.</summary>
    public MessagePublicationResult(MessagePublicationDisposition disposition,
        MessagePublicationFailure failure = MessagePublicationFailure.None, string? safeCode = null)
    {
        if (!Enum.IsDefined(disposition) || !Enum.IsDefined(failure))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if (disposition == MessagePublicationDisposition.Confirmed && failure != MessagePublicationFailure.None
            || disposition != MessagePublicationDisposition.Confirmed && failure == MessagePublicationFailure.None
            || disposition == MessagePublicationDisposition.Unknown && failure != MessagePublicationFailure.Transient)
        {
            throw new ArgumentException("Only confirmed publication has no failure classification.", nameof(failure));
        }
        Disposition = disposition;
        Failure = failure;
        SafeCode = safeCode is null ? null : MessageText.Code(safeCode, nameof(safeCode));
    }

    /// <summary>Gets confirmed, rejected or uncertain native acceptance.</summary>
    public MessagePublicationDisposition Disposition
    {
        get;
    }
    /// <summary>Gets the fixed publication policy category.</summary>
    public MessagePublicationFailure Failure
    {
        get;
    }
    /// <summary>Gets optional bounded safe diagnostic context.</summary>
    public string? SafeCode
    {
        get;
    }
}

/// <summary>A borrowed immutable body and provider-owned delivery metadata for one publication attempt.</summary>
public sealed record MessagePublication
{
    /// <summary>Validates fixed metadata before any optional adapter can perform native I/O.</summary>
    public MessagePublication(EncodedMessage message, MessageDestination destination, MessageRecordPurpose purpose,
        int deliveryOrdinal, Guid outboxRecordId, int generation = 0, string? targetSubscription = null, string? safeCode = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(purpose))
        {
            throw new ArgumentOutOfRangeException(nameof(purpose));
        }
        Message = message;
        Destination = destination;
        Purpose = purpose;
        DeliveryOrdinal = MessageText.Range(deliveryOrdinal, 1, 32, nameof(deliveryOrdinal));
        OutboxRecordId = MessageText.Id(outboxRecordId, nameof(outboxRecordId));
        Generation = MessageText.Range(generation, 0, int.MaxValue, nameof(generation));
        TargetSubscription = targetSubscription is null ? null : MessageText.Alias(targetSubscription, nameof(targetSubscription));
        SafeCode = safeCode is null ? null : MessageText.Code(safeCode, nameof(safeCode));
        if (purpose is MessageRecordPurpose.Retry or MessageRecordPurpose.Terminal or MessageRecordPurpose.Redrive
            && TargetSubscription is null)
        {
            throw new ArgumentException("A targeted handoff requires its configured subscription.", nameof(targetSubscription));
        }
        if (purpose == MessageRecordPurpose.Reply && destination.ReplyRoute is null)
        {
            throw new ArgumentException("A reply publication requires a structured permitted destination.", nameof(destination));
        }
    }

    /// <summary>Gets the borrowed immutable envelope for this actual attempt.</summary>
    public EncodedMessage Message
    {
        get;
    }
    /// <summary>Gets the registered destination and optional reply namespace.</summary>
    public MessageDestination Destination
    {
        get;
    }
    /// <summary>Gets the internal responsibility.</summary>
    public MessageRecordPurpose Purpose
    {
        get;
    }
    /// <summary>Gets the scheduled ordinal, excluding crash reinvocations.</summary>
    public int DeliveryOrdinal
    {
        get;
    }
    /// <summary>Gets the unique outgoing row identity.</summary>
    public Guid OutboxRecordId
    {
        get;
    }
    /// <summary>Gets the explicit audited redrive generation.</summary>
    public int Generation
    {
        get;
    }
    /// <summary>Gets the specific failed subscription for a targeted handoff.</summary>
    public string? TargetSubscription
    {
        get;
    }
    /// <summary>Gets fixed safe context without native exception text.</summary>
    public string? SafeCode
    {
        get;
    }
}

/// <summary>The narrow acceptance boundary implemented by the selected optional broker adapter.</summary>
public interface IMessagePublisher
{
    /// <summary>Awaits actual native settlement. Socket completion alone never means Confirmed.</summary>
    /// <remarks>The caller retains the immutable message until completion. Any provider working copy requires byte admission.</remarks>
    Task<MessagePublicationResult> PublishAsync(MessagePublication publication, CancellationToken cancellationToken = default);
}

/// <summary>Actual ingress route plus claimed internal headers, which require retained-store verification.</summary>
public sealed record MessageDeliveryMetadata(string RouteAlias, MessageRecordPurpose Purpose = MessageRecordPurpose.Application,
    int DeliveryOrdinal = 1, Guid? OutboxRecordId = null, int Generation = 0,
    string? TargetSubscription = null, string? SafeCode = null);

/// <summary>A trusted provider factory's native authentication result and immutable audit provenance.</summary>
/// <remarks>Passing serialized headers or CallerContext here does not establish authority.
/// The selected native projector must validate the provider's private ticket evidence.</remarks>
public sealed class MessageNativeEvidence
{
    /// <summary>Captures one actual native result for the existing independent security scope.</summary>
    public MessageNativeEvidence(string profile, AuthenticateResult authentication, MessageProvenance provenance)
    {
        Profile = MessageText.Alias(profile, nameof(profile));
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(provenance);
        Authentication = authentication;
        Provenance = provenance;
    }

    /// <summary>Gets the explicitly configured Native/Inbound security profile.</summary>
    public string Profile
    {
        get;
    }
    /// <summary>Gets the provider-minted native authentication result, not wire data.</summary>
    public AuthenticateResult Authentication
    {
        get;
    }
    /// <summary>Gets original established audit facts, never delegated authority.</summary>
    public MessageProvenance Provenance
    {
        get;
    }
}

/// <summary>One native delivery whose buffer remains borrowed until the awaited dispatch callback returns.</summary>
public interface IMessageDelivery : IAsyncDisposable
{
    /// <summary>Gets borrowed bounded native bytes. Core reserves owned byte capacity before copying or decoding.</summary>
    ReadOnlyMemory<byte> Body
    {
        get;
    }
    /// <summary>Gets the configured receiving subscription; it is not selected from message headers.</summary>
    string SubscriptionAlias
    {
        get;
    }
    /// <summary>Gets actual route and claimed internal metadata.</summary>
    MessageDeliveryMetadata Metadata
    {
        get;
    }
    /// <summary>Obtains the trusted adapter's current native evidence, or null for untrusted input.</summary>
    ValueTask<MessageNativeEvidence?> GetNativeEvidenceAsync(CancellationToken cancellationToken = default);
    /// <summary>Acknowledges this delivery only after known committed responsibility.</summary>
    Task AcknowledgeAsync(CancellationToken cancellationToken = default);
    /// <summary>Releases this unacknowledged attempt for broker redelivery without a success claim.</summary>
    Task RequeueAsync(CancellationToken cancellationToken = default);
    /// <summary>Uses only a verified safe native quarantine handoff; false leaves responsibility unacknowledged.</summary>
    Task<bool> QuarantineMalformedAsync(string safeCode, CancellationToken cancellationToken = default);
    /// <summary>Closes affected-consumer admission visibly without joining the current callback.</summary>
    /// <remarks>The adapter settles native cancellation after this callback unwinds. Never self-join global shutdown here.</remarks>
    void RequestConsumerStop(string safeCode);
}

/// <summary>One selected adapter lifetime with bounded native channels and one recovery owner.</summary>
public interface IMessageTransport : IMessagePublisher, IAsyncDisposable
{
    /// <summary>Gets the provider-generated local requester endpoint after successful start.</summary>
    MessageReplyRoute? ReplyEndpoint
    {
        get;
    }
    /// <summary>Gets whether required configured transport resources are currently ready.</summary>
    bool IsReady
    {
        get;
    }
    /// <summary>Starts through existing hosting. Publish the endpoint before any dispatch can occur.</summary>
    Task StartAsync(Func<IMessageDelivery, CancellationToken, Task> dispatch, CancellationToken cancellationToken = default);
    /// <summary>Closes consumer admission and awaits actual admitted callbacks; never invoke from one of those callbacks.</summary>
    Task StopConsumersAsync();
    /// <summary>Joins native publication and connection cleanup after consumers settle.</summary>
    Task StopAsync();
}
