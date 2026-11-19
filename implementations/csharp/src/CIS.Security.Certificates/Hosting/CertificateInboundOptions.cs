using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace CIS.Security.Certificates.Hosting;

/// <summary>Immutable receiver setup; no outgoing leaf, key or source is required.</summary>
public sealed class CertificateInboundOptions
{
    /// <summary>Creates receiving trust with the optional mandatory LocalDevelopment activation gate.</summary>
    public CertificateInboundOptions(CertificateTrustPolicy trust, CertificateDevelopmentGate? development = null)
    {
        ArgumentNullException.ThrowIfNull(trust);
        if ((trust.Revocation == CertificateRevocationPolicy.Development) != (development is not null))
        {
            throw new ArgumentException("Development trust and its explicit activation gate must be selected together.");
        }
        Trust = trust;
        Development = development;
    }

    /// <summary>Gets immutable client issuer trust, separate from outgoing server TLS trust.</summary>
    public CertificateTrustPolicy Trust
    {
        get;
    }
    /// <summary>Gets isolated development gates, absent for a production certificate profile.</summary>
    public CertificateDevelopmentGate? Development
    {
        get;
    }
}

/// <summary>Explicit canonical/native development environments and finite private listener exceptions.</summary>
public sealed class CertificateDevelopmentGate
{
    private readonly string[] hostEnvironments;
    private readonly IPAddress[] privateAddresses;

    /// <summary>Captures development activation; loopback is the default, private container addresses require exact opt-in.</summary>
    public CertificateDevelopmentGate(string canonicalEnvironment, IReadOnlyList<string> permittedNativeEnvironments,
        IReadOnlyList<IPAddress>? permittedPrivateListenerAddresses = null)
    {
        CanonicalEnvironment = new CapabilityTarget("development-gate", canonicalEnvironment).Environment;
        if (CanonicalEnvironment == "unknown")
        {
            throw new ArgumentException("Unknown canonical identity cannot activate local development.", nameof(canonicalEnvironment));
        }
        ArgumentNullException.ThrowIfNull(permittedNativeEnvironments);
        if (permittedNativeEnvironments.Count is < 1 or > 16)
        {
            throw new ArgumentException("Select a finite nonempty native development environment set.", nameof(permittedNativeEnvironments));
        }
        hostEnvironments = permittedNativeEnvironments.Select(value => new CapabilityTarget("native-host", value).Environment).ToArray();
        if (hostEnvironments.Any(value => value.Equals(Environments.Production, StringComparison.OrdinalIgnoreCase)
            || value == "unknown"))
        {
            throw new ArgumentException("Production and unknown native host environments cannot activate development trust.");
        }
        if (permittedPrivateListenerAddresses?.Count > 16)
        {
            throw new ArgumentException("Select at most sixteen exact private listener addresses.");
        }
        privateAddresses = (permittedPrivateListenerAddresses ?? []).Select(value =>
        {
            ArgumentNullException.ThrowIfNull(value);
            IPAddress copy = new(value.GetAddressBytes());
            if (!IsPrivate(copy))
            {
                throw new ArgumentException("A development exception must be an exact private network address.");
            }
            return copy;
        }).ToArray();
    }

    /// <summary>Gets the exact required canonical deployment environment.</summary>
    public string CanonicalEnvironment
    {
        get;
    }

    internal void Validate(IHostEnvironment? environment)
    {
        if (!string.Equals(EnvironmentOptions.EnvironmentName, CanonicalEnvironment, StringComparison.Ordinal)
            || environment is null || !hostEnvironments.Contains(environment.EnvironmentName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("LocalDevelopment requires its exact canonical environment and an explicitly permitted native host environment.");
        }
    }

    internal bool Permits(HttpContext context)
    {
        IPAddress? address = context.Connection.LocalIpAddress;
        return address is not null && (IPAddress.IsLoopback(address)
            || privateAddresses.Any(permitted => permitted.Equals(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address)));
    }

    private static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        byte[] bytes = address.GetAddressBytes();
        return bytes.Length == 4 ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 192 && bytes[1] == 168 : (bytes[0] & 0xfe) == 0xfc;
    }
}
