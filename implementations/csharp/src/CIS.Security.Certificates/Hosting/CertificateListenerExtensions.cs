using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;

namespace CIS.Security.Certificates.Hosting;

/// <summary>One exact actual local listener binding; request Host/forwarded headers are not selection inputs.</summary>
public sealed class CertificateListenerBinding
{
    private readonly byte[] address;

    /// <summary>Creates a business binding with a profile, or an operational binding with no profile.</summary>
    public CertificateListenerBinding(IPAddress address, int port, string? profile = null)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is < 1 or > 65535 || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            throw new ArgumentException("Select an exact actual local address and nonzero port.");
        }
        this.address = address.IsIPv4MappedToIPv6 ? address.MapToIPv4().GetAddressBytes() : address.GetAddressBytes();
        Port = port;
        Profile = profile is null ? null : new SecurityProfile(profile, SecurityProfileKind.ClientCertificate,
            SecurityProfileRoles.Inbound, "listener-validation").Alias;
    }

    /// <summary>Gets the fixed native listener port.</summary>
    public int Port
    {
        get;
    }
    /// <summary>Gets its explicit business profile, or null for an operational binding.</summary>
    public string? Profile
    {
        get;
    }

    internal bool Matches(HttpContext context)
    {
        IPAddress? actual = context.Connection.LocalIpAddress;
        return actual is not null && context.Connection.LocalPort == Port
            && (actual.IsIPv4MappedToIPv6 ? actual.MapToIPv4() : actual).GetAddressBytes().AsSpan().SequenceEqual(address);
    }

    internal bool SameListener(CertificateListenerBinding other) => Port == other.Port && address.AsSpan().SequenceEqual(other.address);
}

/// <summary>A finite actual-listener selection and isolation policy for business TLS and separate probes.</summary>
public sealed class CertificateListenerPolicy
{
    private readonly CertificateListenerBinding[] business;
    private readonly CertificateListenerBinding[] operational;
    private readonly string[] operationalPaths;

    /// <summary>Copies explicit listeners and reserved operational paths. No operational binding permits native-authorized endpoints.</summary>
    public CertificateListenerPolicy(IReadOnlyList<CertificateListenerBinding> businessBindings,
        IReadOnlyList<CertificateListenerBinding> operationalBindings, IReadOnlyList<string>? operationalPaths = null)
    {
        ArgumentNullException.ThrowIfNull(businessBindings);
        ArgumentNullException.ThrowIfNull(operationalBindings);
        if (businessBindings.Count is < 1 or > 16 || operationalBindings.Count > 16)
        {
            throw new ArgumentException("Select finite business and operational listener sets.");
        }
        business = businessBindings.ToArray();
        operational = operationalBindings.ToArray();
        if (business.Any(binding => binding is null || binding.Profile is null)
            || operational.Any(binding => binding is null || binding.Profile is not null))
        {
            throw new ArgumentException("Business listeners select a profile; operational listeners select none.");
        }
        CertificateListenerBinding[] all = business.Concat(operational).ToArray();
        for (int index = 0; index < all.Length; index++)
        {
            if (all.Skip(index + 1).Any(candidate => candidate.SameListener(all[index])))
            {
                throw new ArgumentException("An actual listener has one unambiguous role.");
            }
        }
        IReadOnlyList<string> paths = operationalPaths ?? ["/_health", "/_health/ready", "/_metrics"];
        if (paths.Count > 16 || paths.Any(path => path is null || path.Length is < 1 or > 256
            || !path.StartsWith('/') || path.Contains('?') || path.Contains('#') || path.Contains('%')))
        {
            throw new ArgumentException("Select at most sixteen exact bounded reserved operational paths.");
        }
        this.operationalPaths = paths.ToArray();
    }

    /// <summary>Selects a business profile from actual native local connection data only.</summary>
    public string? SelectProfile(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return business.FirstOrDefault(binding => binding.Matches(context))?.Profile;
    }

    internal bool Permits(HttpContext context)
    {
        if (business.Any(binding => binding.Matches(context)))
        {
            return context.Request.IsHttps;
        }
        return operational.Any(binding => binding.Matches(context))
            && context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count is not > 0
            && operationalPaths.Contains(context.Request.Path.Value, StringComparer.Ordinal);
    }
}

/// <summary>Constrained native TLS and actual-listener middleware without an accepting validation callback surface.</summary>
public static class CertificateListenerExtensions
{
    /// <summary>Requires a client certificate and validates explicit native client trust during the real TLS handshake.</summary>
    /// <remarks>The server certificate remains application/deployment-owned. This does not configure outgoing server trust.</remarks>
    public static HttpsConnectionAdapterOptions UseCisClientCertificateTrust(this HttpsConnectionAdapterOptions options,
        CertificateTrustPolicy trust, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(trust);
        TimeProvider effectiveClock = clock ?? TimeProvider.System;
        options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
        options.ClientCertificateValidation = (certificate, _, _) => certificate is not null
            && CertificateTrustValidator.Validate(certificate, trust, effectiveClock.GetUtcNow()).Succeeded;
        return options;
    }

    /// <summary>Installs after routing and before security/operational branches; rejects business access on probe listeners.</summary>
    public static IApplicationBuilder UseCisCertificateListenerIsolation(this IApplicationBuilder application,
        CertificateListenerPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(policy);
        return application.Use(async (context, next) =>
        {
            if (!policy.Permits(context))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next(context).ConfigureAwait(false);
        });
    }
}
