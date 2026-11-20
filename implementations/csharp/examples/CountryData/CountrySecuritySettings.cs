using CIS.Configuration;
using CIS.Security;

namespace CIS.Examples.CountryData;

/// <summary>Explicit tutorial bootstrap defaults; CIS retains and coordinates the prepared root.</summary>
public sealed class CountrySecuritySettings : ConfigurationObject
{
    public string PolicyVersion { get; set; } = "country-example-v1";
    public string Environment { get; set; } = "Development";
    public string ServiceApplication { get; set; } = "CountryData";
    public string CallerApplication { get; set; } = "CountryData.Caller";
    public string TrustDomain { get; set; } = "";
    public string CaCertificateFile { get; set; } = "";
    public string? IssuingAuthorityFile
    {
        get; set;
    }
    public string? ServerTrustCertificateFile
    {
        get; set;
    }
    public string? NativeKeyJournalDirectory
    {
        get; set;
    }
    public string ServerCertificateFile { get; set; } = "";
    public string ServerPrivateKeyFile { get; set; } = "";
    public int BusinessPort { get; set; } = 7443;
    public int OperationalPort { get; set; } = 7080;
    public string[] AllowedOrigins { get; set; } = ["https://localhost:7443"];
    public CountryCredentialReference[] Applications { get; set; } = [];
    public string? TenantId
    {
        get; set;
    }
    public string? Audience
    {
        get; set;
    }
    public string? CallerClientId
    {
        get; set;
    }
    public string? CallerObjectId
    {
        get; set;
    }
    public string? FederatedSubject
    {
        get; set;
    }
    public string? FederatedTokenFile
    {
        get; set;
    }
    public string? Scope
    {
        get; set;
    }

    internal CountrySecuritySettings Copy() => new()
    {
        PolicyVersion = PolicyVersion,
        Environment = Environment,
        ServiceApplication = ServiceApplication,
        CallerApplication = CallerApplication,
        TrustDomain = TrustDomain,
        CaCertificateFile = CaCertificateFile,
        IssuingAuthorityFile = IssuingAuthorityFile,
        ServerTrustCertificateFile = ServerTrustCertificateFile,
        NativeKeyJournalDirectory = NativeKeyJournalDirectory,
        ServerCertificateFile = ServerCertificateFile,
        ServerPrivateKeyFile = ServerPrivateKeyFile,
        BusinessPort = BusinessPort,
        OperationalPort = OperationalPort,
        AllowedOrigins = AllowedOrigins.ToArray(),
        Applications = Applications.Select(value => value.Copy()).ToArray(),
        TenantId = TenantId,
        Audience = Audience,
        CallerClientId = CallerClientId,
        CallerObjectId = CallerObjectId,
        FederatedSubject = FederatedSubject,
        FederatedTokenFile = FederatedTokenFile,
        Scope = Scope
    };

    internal CountryCredentialReference Credential(string application)
        => Applications.SingleOrDefault(value => value.Application == application)
            ?? throw new InvalidOperationException("Configure exactly one credential reference for the selected application.");

    internal SecurityOptions Policy(string mode, bool caller)
    {
        bool entra = mode == "entra";
        SecuritySubject subject = entra
            ? new("https://login.microsoftonline.com/" + TenantId + "/v2.0", CallerObjectId!)
            : new(TrustDomain, Credential(CallerApplication).Identity);
        var options = new SecurityOptions { PolicyVersion = PolicyVersion };
        options.Profiles.Add(new(CountrySecurityComposition.Profile,
            entra ? SecurityProfileKind.EntraWorkloadIdentity : mode == "local" ? SecurityProfileKind.LocalDevelopment : SecurityProfileKind.ClientCertificate,
            caller ? SecurityProfileRoles.Outbound : SecurityProfileRoles.Inbound,
            authenticationScheme: caller ? null : CountrySecurityComposition.Scheme,
            identityMappings: caller ? [] : [new(subject, CallerApplication, Environment,
                mappedRoles: entra ? ["Country.Read", "Country.Select"] : null, clientApplicationId: entra ? CallerClientId : null)],
            outboundIdentity: caller ? new(CallerApplication, Environment, subject) : null,
            allowedOrigins: caller ? AllowedOrigins : null));
        options.Capabilities.Add("country.read");
        options.Capabilities.Add("country.select");
        if (!caller)
        {
            options.Grants.Add(new(CountrySecurityComposition.Profile, "country.read", ServiceApplication, Environment,
                subject: subject, requiredProviderRole: entra ? "Country.Read" : null));
            options.Grants.Add(new(CountrySecurityComposition.Profile, "country.read", ServiceApplication, Environment,
                subject: subject, resourceSelection: CapabilityResourceSelection.AllOfType, resourceType: "country",
                requiredProviderRole: entra ? "Country.Read" : null));
            options.Grants.Add(new(CountrySecurityComposition.Profile, "country.select", ServiceApplication, Environment,
                subject: subject, resourceSelection: CapabilityResourceSelection.ExactResource, resourceType: "country",
                resourceIdentifier: "MT", requiredProviderRole: entra ? "Country.Select" : null));
        }
        return options;
    }
}

public sealed class CountryCredentialReference
{
    public string Application { get; set; } = "";
    public string Identity { get; set; } = "";
    public string CertificateFile { get; set; } = "";
    public string PrivateKeyFile { get; set; } = "";
    internal CountryCredentialReference Copy() => new()
    {
        Application = Application,
        Identity = Identity,
        CertificateFile = CertificateFile,
        PrivateKeyFile = PrivateKeyFile
    };
}

internal sealed record CountrySecuritySnapshot(CountrySecuritySettings Settings);
