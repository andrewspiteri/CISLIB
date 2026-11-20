using System.Buffers;
using System.Text;
using Azure.Core.Pipeline;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CIS.Security.Entra;

/// <summary>Trusted setup copied once after the application's initial Configuration preparation.</summary>
public sealed class EntraSecurityOptions
{
    /// <summary>Gets or sets the one explicitly accepted workload tenant's directory identifier.</summary>
    public string? TenantId
    {
        get; set;
    }

    /// <summary>Gets or sets the trusted Entra authority origin; defaults to the public Azure cloud.</summary>
    public Uri AuthorityHost { get; set; } = new("https://login.microsoftonline.com/");

    /// <summary>Gets or sets receiving inputs, required only for an enabled inbound role.</summary>
    public EntraInboundOptions? Inbound
    {
        get; set;
    }

    /// <summary>Gets or sets workload inputs, required only for an enabled outbound role.</summary>
    public EntraWorkloadOptions? Outbound
    {
        get; set;
    }
}

/// <summary>The one v2 app-only API receiving policy. It does not configure an outgoing workload credential.</summary>
public sealed class EntraInboundOptions
{
    /// <summary>Gets or sets the API's exact token audience, independently of an outgoing /.default scope.</summary>
    public string? Audience
    {
        get; set;
    }

    /// <summary>Gets or sets explicit native lifetime tolerance; default thirty seconds, maximum two minutes.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets an explicitly trusted native metadata manager, or null for native authority discovery.</summary>
    /// <remarks>The application owns a supplied manager. This platform seam does not permit disabling signature, issuer, audience or lifetime validation.</remarks>
    public IConfigurationManager<OpenIdConnectConfiguration>? MetadataManager
    {
        get; set;
    }
}

/// <summary>Explicit AKS workload identity acquisition. No field selects an operator credential fallback.</summary>
public sealed class EntraWorkloadOptions
{
    /// <summary>Gets or sets the workload client/application identifier, not its service-principal object identifier.</summary>
    public string? ClientId
    {
        get; set;
    }

    /// <summary>Gets or sets the intended service-principal object identifier in the trusted local identity binding.</summary>
    public string? ServicePrincipalObjectId
    {
        get; set;
    }

    /// <summary>Gets or sets the configured federation subject, such as system:serviceaccount:namespace:service-account.</summary>
    /// <remarks>Entra validates the projected assertion and federation. This setting records the expected deployment relationship; CIS does not issue assertions.</remarks>
    public string? FederatedSubject
    {
        get; set;
    }

    /// <summary>Gets or sets the explicit absolute projected assertion file path used by the SDK.</summary>
    public string? FederatedTokenFile
    {
        get; set;
    }

    /// <summary>Gets or sets the one trusted API resource /.default scope, never supplied by a request.</summary>
    public string? Scope
    {
        get; set;
    }

    /// <summary>Gets or sets a borrowed native SDK transport, or null for the supported SDK default.</summary>
    /// <remarks>The application owns a supplied transport. Token caching, assertion reads and token refresh remain SDK-owned.</remarks>
    public HttpPipelineTransport? Transport
    {
        get; set;
    }

    /// <summary>Gets or sets the existing Heartbeat cadence for required credential availability; default one second.</summary>
    /// <remarks>Known token expiry schedules a normal SDK availability check, not forced renewal. Recovery checks are coalesced
    /// and spaced by at least one second, even when this observation cadence is shorter. The SDK retains cache ownership.</remarks>
    public TimeSpan StatusCheckInterval { get; set; } = TimeSpan.FromSeconds(1);
}

internal sealed class EntraOptionsSnapshot
{
    internal EntraOptionsSnapshot(SecurityPolicySnapshot policy, string profile,
        string? registeredScheme, EntraSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Policy = policy;
        Profile = policy.GetProfile(profile);
        if (Profile.Kind != SecurityProfileKind.EntraWorkloadIdentity)
        {
            throw new ArgumentException("The selected profile must explicitly use Entra workload identity.", nameof(profile));
        }

        TenantId = EntraText.Guid(options.TenantId, nameof(options.TenantId));
        AuthorityHost = options.AuthorityHost ?? throw new ArgumentException("An authority origin is required.", nameof(options));
        if (!AuthorityHost.IsAbsoluteUri || AuthorityHost.Scheme != Uri.UriSchemeHttps
            || AuthorityHost.UserInfo.Length != 0 || AuthorityHost.AbsolutePath != "/"
            || AuthorityHost.Query.Length != 0 || AuthorityHost.Fragment.Length != 0)
        {
            throw new ArgumentException("The trusted Entra authority must be one HTTPS origin.", nameof(options));
        }
        Issuer = new Uri(AuthorityHost, TenantId + "/v2.0").AbsoluteUri;

        bool inbound = (Profile.Roles & SecurityProfileRoles.Inbound) != 0;
        if (inbound != (registeredScheme is not null)
            || inbound && !string.Equals(registeredScheme, Profile.AuthenticationScheme, StringComparison.Ordinal))
        {
            throw new ArgumentException("Register exactly the selected inbound scheme, or no receiving scheme for an outbound-only profile.", nameof(registeredScheme));
        }
        if (inbound)
        {
            EntraInboundOptions receiving = options.Inbound
                ?? throw new ArgumentException("An enabled inbound role requires its receiving inputs.", nameof(options));
            Audience = EntraText.Identifier(receiving.Audience, nameof(receiving.Audience));
            if (receiving.ClockSkew < TimeSpan.Zero || receiving.ClockSkew > TimeSpan.FromSeconds(120))
            {
                throw new ArgumentOutOfRangeException(nameof(options), "JWT clock skew must be between zero and 120 seconds.");
            }
            ClockSkew = receiving.ClockSkew;
            MetadataManager = receiving.MetadataManager;
            foreach (SecurityIdentityMapping mapping in Profile.IdentityMappings)
            {
                if (!string.Equals(mapping.Subject.Issuer, Issuer, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Entra mappings must use the selected tenant's exact v2 issuer.", nameof(options));
                }
                _ = EntraText.Guid(mapping.Subject.Identifier, nameof(mapping.Subject.Identifier));
                _ = EntraText.Guid(mapping.ClientApplicationId, nameof(mapping.ClientApplicationId));
            }
        }

        if ((Profile.Roles & SecurityProfileRoles.Outbound) != 0)
        {
            EntraWorkloadOptions workload = options.Outbound
                ?? throw new ArgumentException("An enabled outbound role requires its workload inputs.", nameof(options));
            ClientId = EntraText.Guid(workload.ClientId, nameof(workload.ClientId));
            ObjectId = EntraText.Guid(workload.ServicePrincipalObjectId, nameof(workload.ServicePrincipalObjectId));
            FederatedSubject = EntraText.Identifier(workload.FederatedSubject, nameof(workload.FederatedSubject));
            string[] subjectParts = FederatedSubject.Split(':');
            if (subjectParts.Length != 4 || subjectParts[0] != "system" || subjectParts[1] != "serviceaccount"
                || subjectParts[2].Length == 0 || subjectParts[3].Length == 0)
            {
                throw new ArgumentException("Configure the explicit Kubernetes service-account federation subject.", nameof(options));
            }
            TokenFile = EntraText.Identifier(workload.FederatedTokenFile, nameof(workload.FederatedTokenFile));
            if (!Path.IsPathFullyQualified(TokenFile))
            {
                throw new ArgumentException("The projected assertion file path must be absolute.", nameof(options));
            }
            Scope = EntraText.Identifier(workload.Scope, nameof(workload.Scope));
            if (!Uri.TryCreate(Scope, UriKind.Absolute, out Uri? scope)
                || !Scope.EndsWith("/.default", StringComparison.Ordinal)
                || scope.UserInfo.Length != 0 || scope.Query.Length != 0 || scope.Fragment.Length != 0)
            {
                throw new ArgumentException("Configure one explicit API resource /.default scope.", nameof(options));
            }
            if (Profile.OutboundIdentity!.CredentialIdentity != new SecuritySubject(Issuer, ObjectId))
            {
                throw new ArgumentException("The local workload mapping must match its configured tenant and service-principal object identifier.", nameof(options));
            }
            Transport = workload.Transport;
            if (workload.StatusCheckInterval < TimeSpan.FromMilliseconds(10)
                || workload.StatusCheckInterval > TimeSpan.FromMinutes(1))
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Select an expiry-status cadence between ten milliseconds and one minute.");
            }
            StatusCheckInterval = workload.StatusCheckInterval;
        }
    }

    internal SecurityPolicySnapshot Policy
    {
        get;
    }
    internal SecurityProfile Profile
    {
        get;
    }
    internal string TenantId
    {
        get;
    }
    internal Uri AuthorityHost
    {
        get;
    }
    internal string Issuer
    {
        get;
    }
    internal string? Audience
    {
        get;
    }
    internal TimeSpan ClockSkew
    {
        get;
    }
    internal IConfigurationManager<OpenIdConnectConfiguration>? MetadataManager
    {
        get;
    }
    internal string? ClientId
    {
        get;
    }
    internal string? ObjectId
    {
        get;
    }
    internal string? FederatedSubject
    {
        get;
    }
    internal string? TokenFile
    {
        get;
    }
    internal string? Scope
    {
        get;
    }
    internal HttpPipelineTransport? Transport
    {
        get;
    }
    internal TimeSpan StatusCheckInterval
    {
        get;
    }
}

internal static class EntraText
{
    internal static string Guid(string? value, string parameter)
    {
        string text = Identifier(value, parameter);
        if (!System.Guid.TryParseExact(text, "D", out System.Guid identifier) || identifier == System.Guid.Empty)
        {
            throw new ArgumentException("Supply an explicit nonempty directory GUID in D format.", parameter);
        }
        return text;
    }

    internal static string Identifier(string? value, string parameter, int maximum = 1024)
    {
        ArgumentNullException.ThrowIfNull(value, parameter);
        if (value.Length > maximum * 2 || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Supply bounded nonempty configuration text.", parameter);
        }
        ReadOnlySpan<char> remaining = value.AsSpan();
        int count = 0;
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune scalar, out int consumed) != OperationStatus.Done
                || Rune.IsControl(scalar) || ++count > maximum)
            {
                throw new ArgumentException("Supply bounded valid Unicode without controls.", parameter);
            }
            remaining = remaining[consumed..];
        }
        return value;
    }
}
