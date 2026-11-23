using CIS.Management.Wire;

namespace CIS.Management.Service.Persistence;

/// <summary>Detached trusted projection supplied by the server after native authentication and self-mapping.</summary>
/// <remarks>Constructing this value does not authenticate a request or grant a capability.</remarks>
public sealed record ManagementRegistrationOwner
{
    public ManagementRegistrationOwner(string application, string environment, string issuer, string subject,
        ManagementInstanceEvidence instanceEvidence)
    {
        Application = Text(application, 255, nameof(application));
        Environment = Text(environment, 255, nameof(environment));
        Issuer = Text(issuer, 1024, nameof(issuer));
        Subject = Text(subject, 1024, nameof(subject));
        ArgumentNullException.ThrowIfNull(instanceEvidence);
        bool valid = instanceEvidence.Kind switch
        {
            "notProvided" => instanceEvidence.ReportedIdentifier is null && instanceEvidence.VerifiedIdentifier is null,
            "reported" => instanceEvidence.ReportedIdentifier is not null && instanceEvidence.VerifiedIdentifier is null,
            "verified" => instanceEvidence.VerifiedIdentifier is not null,
            _ => false
        };
        if (!valid || instanceEvidence.ReportedIdentifier is { } reported && !ManagementWire.IsScalarText(reported, 1024)
            || instanceEvidence.VerifiedIdentifier is { } verified && !ManagementWire.IsScalarText(verified, 1024))
        {
            throw new ArgumentException("Preserve the native instance evidence classification.", nameof(instanceEvidence));
        }

        InstanceEvidence = instanceEvidence;
    }

    public string Application
    {
        get;
    }
    public string Environment
    {
        get;
    }
    public string Issuer
    {
        get;
    }
    public string Subject
    {
        get;
    }
    public ManagementInstanceEvidence InstanceEvidence
    {
        get;
    }

    private static string Text(string value, int maximum, string name)
        => ManagementWire.IsScalarText(value, maximum) ? value : throw new ArgumentException("Preserve exact bounded native identity.", name);
}
