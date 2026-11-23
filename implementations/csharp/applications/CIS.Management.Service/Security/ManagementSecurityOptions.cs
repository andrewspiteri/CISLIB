using System.Net;
using System.Text.Json.Serialization;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CIS.Management.Service.Security;

/// <summary>Explicit application bootstrap inputs, captured once before the native provider is built.</summary>
/// <remarks>Paths refer to deployment-owned PEM files. Construction and capture perform no file or credential I/O.</remarks>
public sealed class ManagementSecurityOptions
{
    public string Mode { get; init; } = "certificate";

    public string Profile { get; init; } = "management-security";

    public string AuthenticationScheme { get; init; } = "management-security";

    public string BusinessAddress { get; init; } = "127.0.0.1";

    public int BusinessPort { get; init; } = 7443;

    public int OperationalPort { get; init; } = 7080;

    public string ServerCertificateFile { get; init; } = "";

    public string ServerPrivateKeyFile { get; init; } = "";

    public string? ServerTrustBundleFile
    {
        get; init;
    }

    public string ClientTrustDomain { get; init; } = "";

    public string ClientRootBundleFile { get; init; } = "";

    public string ClientIssuingAuthorityBundleFile { get; init; } = "";

    public string? ClientIntermediateBundleFile
    {
        get; init;
    }

    public CertificateRevocationPolicy CertificateRevocation { get; init; } = CertificateRevocationPolicy.Online;

    public TimeSpan MaximumShortLivedLifetime { get; init; } = TimeSpan.FromHours(24);

    public string LocalDevelopmentEnvironment { get; init; } = "Development";

    public string PolicyVersion { get; init; } = "management-policy-v1";

    public IReadOnlyList<SecurityIdentityMapping> IdentityMappings { get; init; } = [];

    public IReadOnlyList<CapabilityGrant> Grants { get; init; } = [];

    public IReadOnlyList<string> AdditionalCapabilities { get; init; } = [];

    public string? EntraTenantId
    {
        get; init;
    }

    public string? EntraAudience
    {
        get; init;
    }

    public Uri EntraAuthorityHost { get; init; } = new("https://login.microsoftonline.com/");

    public TimeSpan EntraClockSkew { get; init; } = TimeSpan.FromSeconds(30);

    public string? NativeKeyJournalDirectory
    {
        get; init;
    }

    /// <summary>An optional borrowed fixture/application key scope, kept alive until actual host disposal.</summary>
    [JsonIgnore]
    public RestHttpNativeKeyScope? BorrowedNativeKeyScope
    {
        get; init;
    }

    /// <summary>Explicit compiled registration of the selected native handler and CIS projector.</summary>
    /// <remarks>The callback receives services, profile and scheme. It must validate real credentials;
    /// it is not a caller-context setter, token exchange or dynamically loaded provider.</remarks>
    [JsonIgnore]
    public Action<IServiceCollection, string, string>? NativeProvider
    {
        get; init;
    }

    /// <summary>An optional explicitly trusted native metadata manager; the Entra adapter retains all validation.</summary>
    [JsonIgnore]
    public IConfigurationManager<OpenIdConnectConfiguration>? EntraMetadataManager
    {
        get; init;
    }

    /// <summary>Reads the service's explicit non-secret deployment shape into detached native setup.</summary>
    /// <remarks>Native callbacks and metadata managers are code-only composition inputs, never configuration type names.</remarks>
    public static ManagementSecurityOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        string profile = configuration["Profile"] ?? "management-security";
        var mappings = new List<SecurityIdentityMapping>();
        foreach (IConfigurationSection item in configuration.GetSection("IdentityMappings").GetChildren())
        {
            if (mappings.Count == 4096)
            {
                throw new ArgumentException("Configure at most 4096 management identity mappings.");
            }
            string[] roles = ReadStrings(item.GetSection("MappedRoles"), 256);
            mappings.Add(new(new(Required(item, "Issuer"), Required(item, "Subject")),
                item["Application"], item["Environment"], roles, item["ClientApplicationId"],
                item.GetValue("ActorKind", SecurityActorKind.Application)));
        }

        var grants = new List<CapabilityGrant>();
        foreach (IConfigurationSection item in configuration.GetSection("Grants").GetChildren())
        {
            if (grants.Count == 4096)
            {
                throw new ArgumentException("Configure at most 4096 management capability grants.");
            }
            string? issuer = item["Issuer"];
            string? subject = item["Subject"];
            if ((issuer is null) != (subject is null))
            {
                throw new ArgumentException("A grant subject requires its complete issuer and subject pair.");
            }
            grants.Add(new(profile, Required(item, "Capability"), Required(item, "TargetApplication"),
                Required(item, "TargetEnvironment"), issuer is null ? null : new(issuer, subject!),
                item["MappedRole"], item.GetValue("ResourceSelection", CapabilityResourceSelection.TargetOnly),
                item["ResourceType"], item["ResourceIdentifier"], item["RequiredProviderRole"]));
        }

        return new()
        {
            Mode = configuration["Mode"] ?? "certificate",
            Profile = profile,
            AuthenticationScheme = configuration["AuthenticationScheme"] ?? "management-security",
            BusinessAddress = configuration["BusinessAddress"] ?? "127.0.0.1",
            BusinessPort = configuration.GetValue("BusinessPort", 7443),
            OperationalPort = configuration.GetValue("OperationalPort", 7080),
            ServerCertificateFile = Required(configuration, "ServerCertificateFile"),
            ServerPrivateKeyFile = Required(configuration, "ServerPrivateKeyFile"),
            ServerTrustBundleFile = configuration["ServerTrustBundleFile"],
            ClientTrustDomain = configuration["ClientTrustDomain"] ?? "",
            ClientRootBundleFile = configuration["ClientRootBundleFile"] ?? "",
            ClientIssuingAuthorityBundleFile = configuration["ClientIssuingAuthorityBundleFile"] ?? "",
            ClientIntermediateBundleFile = configuration["ClientIntermediateBundleFile"],
            CertificateRevocation = configuration.GetValue("CertificateRevocation", CertificateRevocationPolicy.Online),
            MaximumShortLivedLifetime = configuration.GetValue("MaximumShortLivedLifetime", TimeSpan.FromHours(24)),
            LocalDevelopmentEnvironment = configuration["LocalDevelopmentEnvironment"] ?? "Development",
            PolicyVersion = configuration["PolicyVersion"] ?? "management-policy-v1",
            IdentityMappings = Array.AsReadOnly(mappings.ToArray()),
            Grants = Array.AsReadOnly(grants.ToArray()),
            AdditionalCapabilities = ReadStrings(configuration.GetSection("AdditionalCapabilities"), 252),
            EntraTenantId = configuration["EntraTenantId"],
            EntraAudience = configuration["EntraAudience"],
            EntraAuthorityHost = new(configuration["EntraAuthorityHost"] ?? "https://login.microsoftonline.com/", UriKind.Absolute),
            EntraClockSkew = configuration.GetValue("EntraClockSkew", TimeSpan.FromSeconds(30)),
            NativeKeyJournalDirectory = configuration["NativeKeyJournalDirectory"]
        };
    }

    private static string[] ReadStrings(IConfigurationSection section, int maximum)
    {
        var values = new List<string>();
        foreach (IConfigurationSection value in section.GetChildren())
        {
            if (values.Count == maximum || value.Value is null)
            {
                throw new ArgumentException("Configure a finite nonnull string collection.");
            }
            values.Add(value.Value);
        }

        return values.ToArray();
    }

    private static string Required(IConfiguration configuration, string name)
        => !string.IsNullOrWhiteSpace(configuration[name]) ? configuration[name]!
            : throw new ArgumentException("Configure the required management security value: " + name + ".");

    internal SecurityProfileKind Kind => Mode switch
    {
        "local" => SecurityProfileKind.LocalDevelopment,
        "certificate" => SecurityProfileKind.ClientCertificate,
        "entra" => SecurityProfileKind.EntraWorkloadIdentity,
        "native" => SecurityProfileKind.Native,
        _ => throw new ArgumentException("Select local, certificate, entra or an explicitly integrated native security profile.")
    };

    internal ManagementSecurityOptions Capture()
    {
        SecurityProfileKind kind = Kind;
        if (!IPAddress.TryParse(BusinessAddress, out IPAddress? address)
            || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || BusinessPort is < 1 or > 65535 || OperationalPort is < 1 or > 65535 || BusinessPort == OperationalPort)
        {
            throw new ArgumentException("Select an exact business IP and distinct nonzero business and loopback operational ports.");
        }
        if (Mode == "local" && !IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("The management LocalDevelopment listener must be loopback.");
        }
        if (!Enum.IsDefined(CertificateRevocation)
            || Mode != "local" && CertificateRevocation == CertificateRevocationPolicy.Development
            || MaximumShortLivedLifetime <= TimeSpan.Zero || MaximumShortLivedLifetime > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("Select a compatible explicit certificate revocation policy and finite leaf lifetime.");
        }
        if (Mode == "native" && NativeProvider is null)
        {
            throw new ArgumentException("Native operator/workload mode requires an explicitly registered validating native provider.");
        }
        if (Mode != "native" && NativeProvider is not null)
        {
            throw new ArgumentException("A native provider cannot silently replace a workload reference profile.");
        }
        ArgumentNullException.ThrowIfNull(IdentityMappings);
        ArgumentNullException.ThrowIfNull(Grants);
        ArgumentNullException.ThrowIfNull(AdditionalCapabilities);
        if (IdentityMappings.Count is < 1 or > 4096 || Grants.Count > 4096 || AdditionalCapabilities.Count > 252
            || IdentityMappings.Any(mapping => mapping is null) || Grants.Any(grant => grant is null)
            || AdditionalCapabilities.Any(capability => capability is null))
        {
            throw new ArgumentException("Configure bounded nonnull immutable identity mappings and complete grants.");
        }
        if (kind != SecurityProfileKind.Native && IdentityMappings.Any(mapping => mapping.ActorKind != SecurityActorKind.Application))
        {
            throw new ArgumentException("Workload reference profiles cannot establish a human actor.");
        }
        _ = new SecurityProfile(Profile, kind, SecurityProfileRoles.Inbound, AuthenticationScheme, IdentityMappings);
        if (Grants.Any(grant => !string.Equals(grant.Profile, Profile, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Every management grant must select the captured profile.");
        }

        string certificate = AbsolutePath(ServerCertificateFile);
        string key = AbsolutePath(ServerPrivateKeyFile);
        if (string.Equals(certificate, key, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("Use separate server certificate and private-key files.");
        }
        string? serverTrust = OptionalPath(ServerTrustBundleFile);
        if (Mode == "local" && serverTrust is null)
        {
            throw new ArgumentException("LocalDevelopment server trust requires its explicit isolated root bundle.");
        }
        if (Mode is "local" or "certificate")
        {
            _ = new CertificatePemTrustOptions(ClientTrustDomain, AbsolutePath(ClientRootBundleFile),
                AbsolutePath(ClientIssuingAuthorityBundleFile), OptionalPath(ClientIntermediateBundleFile),
                Mode == "local" ? CertificateRevocationPolicy.Development : CertificateRevocation, MaximumShortLivedLifetime);
        }
        if (Mode == "entra")
        {
            if (!Guid.TryParseExact(EntraTenantId, "D", out _) || string.IsNullOrWhiteSpace(EntraAudience)
                || EntraAudience.Length > 1024 || !EntraAuthorityHost.IsAbsoluteUri
                || EntraAuthorityHost.Scheme != Uri.UriSchemeHttps || EntraAuthorityHost.UserInfo.Length != 0
                || EntraAuthorityHost.AbsolutePath != "/" || EntraAuthorityHost.Query.Length != 0 || EntraAuthorityHost.Fragment.Length != 0
                || EntraClockSkew < TimeSpan.Zero || EntraClockSkew > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentException("Configure one explicit Entra tenant, audience, HTTPS authority and finite native clock skew.");
            }
        }

        return new()
        {
            Mode = Mode,
            Profile = Profile,
            AuthenticationScheme = AuthenticationScheme,
            BusinessAddress = address.ToString(),
            BusinessPort = BusinessPort,
            OperationalPort = OperationalPort,
            ServerCertificateFile = certificate,
            ServerPrivateKeyFile = key,
            ServerTrustBundleFile = serverTrust,
            ClientTrustDomain = ClientTrustDomain,
            ClientRootBundleFile = Mode is "local" or "certificate" ? AbsolutePath(ClientRootBundleFile) : ClientRootBundleFile,
            ClientIssuingAuthorityBundleFile = Mode is "local" or "certificate" ? AbsolutePath(ClientIssuingAuthorityBundleFile) : ClientIssuingAuthorityBundleFile,
            ClientIntermediateBundleFile = OptionalPath(ClientIntermediateBundleFile),
            CertificateRevocation = Mode == "local" ? CertificateRevocationPolicy.Development : CertificateRevocation,
            MaximumShortLivedLifetime = MaximumShortLivedLifetime,
            LocalDevelopmentEnvironment = new CapabilityTarget("management-development", LocalDevelopmentEnvironment).Environment,
            PolicyVersion = PolicyVersion,
            IdentityMappings = Array.AsReadOnly(IdentityMappings.ToArray()),
            Grants = Array.AsReadOnly(Grants.ToArray()),
            AdditionalCapabilities = Array.AsReadOnly(AdditionalCapabilities.ToArray()),
            EntraTenantId = EntraTenantId,
            EntraAudience = EntraAudience,
            EntraAuthorityHost = new(EntraAuthorityHost.AbsoluteUri),
            EntraClockSkew = EntraClockSkew,
            NativeKeyJournalDirectory = OptionalPath(NativeKeyJournalDirectory),
            BorrowedNativeKeyScope = BorrowedNativeKeyScope,
            NativeProvider = NativeProvider,
            EntraMetadataManager = EntraMetadataManager
        };
    }

    private static string? OptionalPath(string? path) => path is null ? null : AbsolutePath(path);

    private static string AbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Configure bounded absolute PEM and custody paths.");
        }

        return Path.GetFullPath(path);
    }
}
