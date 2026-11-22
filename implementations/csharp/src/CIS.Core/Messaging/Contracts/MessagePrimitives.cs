using System.Security.Cryptography;
using CIS.Operations;
using CIS.Security;

namespace CIS.Messaging;

/// <summary>The four portable meanings of an immutable envelope.</summary>
public enum MessageKind
{
    /// <summary>A fact delivered independently to configured subscriptions.</summary>
    Event,
    /// <summary>An instruction to one logical service.</summary>
    Command,
    /// <summary>A typed query or durable command with a reply attempt.</summary>
    Request,
    /// <summary>A response to one exact request attempt.</summary>
    Reply
}

/// <summary>The internal responsibility of an outgoing row, outside the immutable envelope.</summary>
public enum MessageRecordPurpose
{
    /// <summary>An application publication.</summary>
    Application,
    /// <summary>A targeted scheduled delivery ordinal.</summary>
    Retry,
    /// <summary>A secured canonical terminal handoff.</summary>
    Terminal,
    /// <summary>A response secured with processing.</summary>
    Reply,
    /// <summary>A rejection independent of the canonical processing identity.</summary>
    Rejection,
    /// <summary>An explicit authorized reopening of failed work.</summary>
    Redrive
}

/// <summary>A stable portable contract name and positive version, never a CLR type identity.</summary>
public sealed record MessageContract
{
    /// <summary>Creates an exact case-sensitive contract.</summary>
    public MessageContract(string name, int version)
    {
        Name = MessageText.Contract(name, nameof(name));
        Version = MessageText.Range(version, 1, int.MaxValue, nameof(version));
    }

    /// <summary>Gets the one to 128 character ASCII contract name.</summary>
    public string Name
    {
        get;
    }
    /// <summary>Gets the positive wire schema version.</summary>
    public int Version
    {
        get;
    }
}

/// <summary>A permitted requester namespace and provider-generated endpoint, never an arbitrary queue name.</summary>
public sealed record MessageReplyRoute
{
    /// <summary>Copies namespace data; authority still comes from the selected policy and broker path.</summary>
    public MessageReplyRoute(string application, string environment, Guid endpointId)
    {
        var target = new CapabilityTarget(application, environment);
        Application = target.Application;
        Environment = target.Environment;
        EndpointId = MessageText.Id(endpointId, nameof(endpointId));
    }

    /// <summary>Gets the configured recipient application.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the configured recipient environment.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the opaque process endpoint identity.</summary>
    public Guid EndpointId
    {
        get;
    }
}

/// <summary>An untrusted operation request reference, excluding transport attempts and reply routing.</summary>
public sealed record MessageOperationReference
{
    /// <summary>Uses the existing Operations request validation without establishing scope authority.</summary>
    public MessageOperationReference(string operationName, string requestedScope, string key)
    {
        var validated = new OperationLookupRequest(operationName, requestedScope, key);
        OperationName = validated.OperationName;
        RequestedScope = validated.RequestedScope;
        Key = validated.Key;
    }

    /// <summary>Gets the registered operation alias.</summary>
    public string OperationName
    {
        get;
    }
    /// <summary>Gets scope text that the application must resolve.</summary>
    public string RequestedScope
    {
        get;
    }
    /// <summary>Gets the original opaque caller key.</summary>
    public string Key
    {
        get;
    }
}

/// <summary>A copied immutable SHA-256 transport digest; this is not a business fingerprint.</summary>
public sealed class MessageDigest : IEquatable<MessageDigest>
{
    private readonly byte[] bytes;

    /// <summary>Copies exactly 32 bytes.</summary>
    public MessageDigest(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length != 32)
        {
            throw new ArgumentException("A SHA-256 digest contains exactly 32 bytes.", nameof(bytes));
        }
        this.bytes = bytes.ToArray();
    }

    /// <summary>Gets a detached application-owned copy.</summary>
    public byte[] CopyBytes() => bytes.ToArray();
    /// <summary>Compares all immutable digest bytes.</summary>
    public bool Equals(MessageDigest? other) => other is not null && CryptographicOperations.FixedTimeEquals(bytes, other.bytes);
    /// <summary>Compares all immutable digest bytes.</summary>
    public override bool Equals(object? obj) => obj is MessageDigest other && Equals(other);
    /// <summary>Returns a stable-in-process value hash without exposing the digest as a metric label.</summary>
    public override int GetHashCode() => BitConverter.ToInt32(bytes, 0);
    internal ReadOnlySpan<byte> Span => bytes;
}

/// <summary>Safe bounded diagnostic data without payloads, credentials, SQL or exceptions.</summary>
public sealed record MessageError
{
    /// <summary>Copies a fixed code and bounded safe message.</summary>
    public MessageError(string code, string message)
    {
        Code = MessageText.Code(code, nameof(code));
        Message = MessageText.Unicode(message, 1024, nameof(message));
    }

    /// <summary>Gets the printable safe code.</summary>
    public string Code
    {
        get;
    }
    /// <summary>Gets at most 1024 UTF-8 bytes of safe context.</summary>
    public string Message
    {
        get;
    }
}

/// <summary>Original verified broker facts retained for audit, not delegated caller authority.</summary>
public sealed record MessageProvenance
{
    /// <summary>Copies the trusted adapter's bounded original evidence.</summary>
    public MessageProvenance(string brokerAlias, string virtualHost, SecuritySubject subject, string userId,
        string application, string environment)
    {
        BrokerAlias = MessageText.Alias(brokerAlias, nameof(brokerAlias));
        VirtualHost = MessageText.Unicode(virtualHost, 256, nameof(virtualHost));
        ArgumentNullException.ThrowIfNull(subject);
        Subject = subject;
        UserId = MessageText.Unicode(userId, 256, nameof(userId));
        var target = new CapabilityTarget(application, environment);
        Application = target.Application;
        Environment = target.Environment;
    }

    /// <summary>Gets the configured broker alias.</summary>
    public string BrokerAlias
    {
        get;
    }
    /// <summary>Gets the configured virtual host.</summary>
    public string VirtualHost
    {
        get;
    }
    /// <summary>Gets the broker-qualified subject actually established.</summary>
    public SecuritySubject Subject
    {
        get;
    }
    /// <summary>Gets the broker-validated publishing user identifier.</summary>
    public string UserId
    {
        get;
    }
    /// <summary>Gets the mapped producer application.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the mapped producer environment.</summary>
    public string Environment
    {
        get;
    }
}

/// <summary>The full ordinal duplicate identity of one receiving subscription.</summary>
public sealed record MessageProcessingKey
{
    /// <summary>Creates identity data without authorizing access.</summary>
    public MessageProcessingKey(string application, string environment, string subscription, Guid messageId)
    {
        var target = new CapabilityTarget(application, environment);
        Application = target.Application;
        Environment = target.Environment;
        Subscription = MessageText.Alias(subscription, nameof(subscription));
        MessageId = MessageText.Id(messageId, nameof(messageId));
    }

    /// <summary>Gets the receiving application.</summary>
    public string Application
    {
        get;
    }
    /// <summary>Gets the receiving environment.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the stable logical subscription, excluding instance and deployment.</summary>
    public string Subscription
    {
        get;
    }
    /// <summary>Gets the original immutable logical envelope identity.</summary>
    public Guid MessageId
    {
        get;
    }
}

/// <summary>The original resolved operation identity and resource, retained for current retry-policy comparison.</summary>
/// <remarks>This is data, never a reusable authentication or capability ticket.</remarks>
public sealed record MessageOperationBinding
{
    /// <summary>Copies the complete trusted binding without creating authentication authority.</summary>
    public MessageOperationBinding(OperationIdentity identity, CapabilityTarget resource)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(resource);
        if (identity.Application != resource.Application || identity.Environment != resource.Environment
            || resource.ResourceType is null || resource.ResourceIdentifier is null)
        {
            throw new ArgumentException("The operation binding requires the same server identity and an exact resource.", nameof(resource));
        }
        Identity = identity;
        Resource = resource;
    }

    /// <summary>Gets the original full operation identity, including resolved logical scope.</summary>
    public OperationIdentity Identity
    {
        get;
    }
    /// <summary>Gets the original exact protected resource.</summary>
    public CapabilityTarget Resource
    {
        get;
    }
}

/// <summary>A configured logical route and optional structured requester destination.</summary>
public sealed record MessageDestination
{
    /// <summary>Copies a route alias; provider configuration resolves physical names.</summary>
    public MessageDestination(string routeAlias, MessageReplyRoute? replyRoute = null)
    {
        RouteAlias = MessageText.Alias(routeAlias, nameof(routeAlias));
        ReplyRoute = replyRoute;
    }

    /// <summary>Gets the registered bounded route alias.</summary>
    public string RouteAlias
    {
        get;
    }
    /// <summary>Gets a permitted endpoint namespace for a reply, if applicable.</summary>
    public MessageReplyRoute? ReplyRoute
    {
        get;
    }
}
