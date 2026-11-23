using CIS.Messaging;
using CIS.Operations;
using CIS.Security;

namespace CIS.Audit;

/// <summary>The broad purpose of an explicitly selected audit event.</summary>
public enum AuditCategory
{
    /// <summary>An application business fact.</summary>
    Business,
    /// <summary>An administrative fact.</summary>
    Administration,
    /// <summary>A security observation.</summary>
    Security
}

/// <summary>The fact known at this event's boundary, without inferred persistence.</summary>
public enum AuditOutcome
{
    /// <summary>Admission, rather than completion.</summary>
    Accepted,
    /// <summary>The source reports success.</summary>
    Succeeded,
    /// <summary>The source rejected the requested work.</summary>
    Rejected,
    /// <summary>The source reports failure.</summary>
    Failed,
    /// <summary>The source reports cancellation.</summary>
    Canceled,
    /// <summary>The outcome has not been established.</summary>
    Unknown,
    /// <summary>A final authorization decision allowed the request.</summary>
    Allowed,
    /// <summary>A final authorization decision denied the request.</summary>
    Denied
}

/// <summary>The actual available actor classification, independent of producer identity.</summary>
public enum AuditActorKind
{
    /// <summary>An authenticated human identity.</summary>
    User,
    /// <summary>An authenticated executing application identity.</summary>
    Application,
    /// <summary>An explicitly selected local application action.</summary>
    System,
    /// <summary>An actual anonymous context, never inferred from a missing caller.</summary>
    Anonymous,
    /// <summary>No actor identity was established.</summary>
    Unknown
}

/// <summary>The actual strength of instance evidence.</summary>
public enum AuditInstanceKind
{
    /// <summary>No instance identifier is available.</summary>
    NotProvided,
    /// <summary>Only reported metadata is available.</summary>
    Reported,
    /// <summary>An independently verified binding is available.</summary>
    Verified
}

/// <summary>Immutable finite limits for the complete audit payload, including its details.</summary>
public sealed class AuditLimits
{
    /// <summary>Creates a profile from one byte through one MiB and depth one through thirty-two.</summary>
    public AuditLimits(int maximumPayloadBytes = 262_144, int maximumJsonDepth = 16)
    {
        MaximumPayloadBytes = MessageText.Range(maximumPayloadBytes, 1, MessagingLimits.MaximumSupportedPayloadBytes, nameof(maximumPayloadBytes));
        MaximumJsonDepth = MessageText.Range(maximumJsonDepth, 1, MessagingLimits.MaximumSupportedJsonDepth, nameof(maximumJsonDepth));
    }

    /// <summary>Gets the complete audit payload byte limit.</summary>
    public int MaximumPayloadBytes
    {
        get;
    }

    /// <summary>Gets the complete payload's JSON nesting limit.</summary>
    public int MaximumJsonDepth
    {
        get;
    }
}

/// <summary>An exact issuer-qualified subject. It conveys provenance, never receiving authority.</summary>
public sealed record AuditSubject
{
    /// <summary>Creates the two bounded, case-sensitive identity values.</summary>
    public AuditSubject(string issuer, string identifier)
    {
        Issuer = SecurityText.Identifier(issuer, nameof(issuer));
        Identifier = SecurityText.Identifier(identifier, nameof(identifier));
    }

    /// <summary>Gets the trusted issuer or relationship identifier.</summary>
    public string Issuer
    {
        get;
    }

    /// <summary>Gets the stable subject identifier.</summary>
    public string Identifier
    {
        get;
    }
}

/// <summary>Reported and verified instance identifiers retain separate meanings.</summary>
public sealed class AuditInstance
{
    /// <summary>Creates bounded evidence without upgrading a reported value.</summary>
    public AuditInstance(string? reportedIdentifier = null, string? verifiedIdentifier = null)
    {
        ReportedIdentifier = SecurityText.OptionalIdentifier(reportedIdentifier, nameof(reportedIdentifier));
        VerifiedIdentifier = SecurityText.OptionalIdentifier(verifiedIdentifier, nameof(verifiedIdentifier));
    }

    /// <summary>Gets the strongest supplied evidence.</summary>
    public AuditInstanceKind Kind => VerifiedIdentifier is not null ? AuditInstanceKind.Verified
        : ReportedIdentifier is not null ? AuditInstanceKind.Reported : AuditInstanceKind.NotProvided;

    /// <summary>Gets reported metadata, which grants no authority.</summary>
    public string? ReportedIdentifier
    {
        get;
    }

    /// <summary>Gets the independently established instance binding.</summary>
    public string? VerifiedIdentifier
    {
        get;
    }
}

/// <summary>The local source of this fact, separate from its initiating actor.</summary>
public sealed class AuditProducer
{
    /// <summary>Creates exact bounded producer facts.</summary>
    public AuditProducer(string application, string environment, AuditInstance? instance = null)
    {
        Application = SecurityText.Application(application, nameof(application));
        Environment = SecurityText.Application(environment, nameof(environment));
        Instance = instance;
    }

    /// <summary>Gets the source application.</summary>
    public string Application
    {
        get;
    }

    /// <summary>Gets the source environment.</summary>
    public string Environment
    {
        get;
    }

    /// <summary>Gets available instance evidence, or null for not provided.</summary>
    public AuditInstance? Instance
    {
        get;
    }
}

/// <summary>A bounded safe actor projection rather than a serialized security context.</summary>
public sealed class AuditActor
{
    /// <summary>Copies only the established safe identity and provenance fields.</summary>
    public AuditActor(AuditActorKind kind, AuditSubject? subject = null, string? application = null,
        string? environment = null, string? profile = null, string? authenticationMethod = null,
        string? policyVersion = null, AuditInstance? instance = null, AuditSubject? humanSubject = null,
        string? verifiedDelegation = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        bool identified = kind is AuditActorKind.User or AuditActorKind.Application;
        if (identified != (subject is not null))
        {
            throw new ArgumentException("Only user and application actors require a subject.", nameof(subject));
        }
        if ((application is null) != (environment is null))
        {
            throw new ArgumentException("An actor application and environment are supplied together.");
        }
        if (kind == AuditActorKind.Application && humanSubject is not null && verifiedDelegation is null)
        {
            throw new ArgumentException("An application actor's human identity requires verified delegation.", nameof(verifiedDelegation));
        }

        Kind = kind;
        Subject = subject;
        Application = application is null ? null : SecurityText.Application(application, nameof(application));
        Environment = environment is null ? null : SecurityText.Application(environment, nameof(environment));
        Profile = profile is null ? null : SecurityText.Alias(profile, nameof(profile));
        AuthenticationMethod = authenticationMethod is null ? null : SecurityText.Alias(authenticationMethod, nameof(authenticationMethod));
        PolicyVersion = SecurityText.OptionalIdentifier(policyVersion, nameof(policyVersion));
        Instance = instance;
        HumanSubject = humanSubject;
        VerifiedDelegation = SecurityText.OptionalIdentifier(verifiedDelegation, nameof(verifiedDelegation));
    }

    /// <summary>Gets the actual actor classification.</summary>
    public AuditActorKind Kind
    {
        get;
    }

    /// <summary>Gets the issuer-qualified subject where this kind requires one.</summary>
    public AuditSubject? Subject
    {
        get;
    }

    /// <summary>Gets an established caller application when available.</summary>
    public string? Application
    {
        get;
    }

    /// <summary>Gets its paired environment.</summary>
    public string? Environment
    {
        get;
    }

    /// <summary>Gets the selected native profile.</summary>
    public string? Profile
    {
        get;
    }

    /// <summary>Gets the established authentication method.</summary>
    public string? AuthenticationMethod
    {
        get;
    }

    /// <summary>Gets the trusted policy version.</summary>
    public string? PolicyVersion
    {
        get;
    }

    /// <summary>Gets instance evidence with its original strength.</summary>
    public AuditInstance? Instance
    {
        get;
    }

    /// <summary>Gets an independently established initiating human.</summary>
    public AuditSubject? HumanSubject
    {
        get;
    }

    /// <summary>Gets bounded safe delegation provenance rather than a credential.</summary>
    public string? VerifiedDelegation
    {
        get;
    }
}

/// <summary>The actual resolved resource, independent of the caller's dimensions.</summary>
public sealed class AuditTarget
{
    /// <summary>Creates a target with an optional complete resource pair.</summary>
    public AuditTarget(string application, string environment, string? resourceType = null, string? resourceIdentifier = null)
    {
        var target = new CapabilityTarget(application, environment, resourceType, resourceIdentifier);
        Application = target.Application;
        Environment = target.Environment;
        ResourceType = target.ResourceType;
        ResourceIdentifier = target.ResourceIdentifier;
    }

    /// <summary>Gets the resource-owning application.</summary>
    public string Application
    {
        get;
    }

    /// <summary>Gets the resource environment.</summary>
    public string Environment
    {
        get;
    }

    /// <summary>Gets the optional resource type.</summary>
    public string? ResourceType
    {
        get;
    }

    /// <summary>Gets its paired exact identifier.</summary>
    public string? ResourceIdentifier
    {
        get;
    }
}

/// <summary>An explicitly selected bounded reason, without native exception text.</summary>
public sealed class AuditReason
{
    /// <summary>Creates a safe code and optional selected explanation.</summary>
    public AuditReason(string code, string? text = null)
    {
        Code = SecurityText.Bounded(code, 128, nameof(code));
        Text = text is null ? null : SecurityText.Bounded(text, 512, nameof(text));
    }

    /// <summary>Gets the safe code.</summary>
    public string Code
    {
        get;
    }

    /// <summary>Gets the selected explanation, or null.</summary>
    public string? Text
    {
        get;
    }
}

/// <summary>The available RM-03 logical identity, without transport identifiers or authority.</summary>
public sealed class AuditOperationReference
{
    /// <summary>Copies the actual registered name, resolved scope and opaque key.</summary>
    public AuditOperationReference(string operationName, string scope, string key)
    {
        OperationName = OperationText.Alias(operationName, nameof(operationName));
        Scope = OperationText.Utf8(scope, 256, nameof(scope));
        Key = OperationText.Key(key, nameof(key));
    }

    /// <summary>Gets the actual registered operation name.</summary>
    public string OperationName
    {
        get;
    }

    /// <summary>Gets the resolved logical scope.</summary>
    public string Scope
    {
        get;
    }

    /// <summary>Gets the opaque original key.</summary>
    public string Key
    {
        get;
    }
}
