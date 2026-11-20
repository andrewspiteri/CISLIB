using System.Security.Cryptography.X509Certificates;
using CIS.Security.Certificates.Hosting;

namespace CIS.Security.Certificates;

/// <summary>Immutable references to public PEM client trust; file contents are captured only during Startup.</summary>
public sealed class CertificatePemTrustOptions
{
    /// <summary>Captures explicit root/direct-issuer bundles and revocation policy without reading files.</summary>
    public CertificatePemTrustOptions(string trustDomain, string rootBundleFile, string issuingAuthorityBundleFile,
        string? intermediateBundleFile = null, CertificateRevocationPolicy revocation = CertificateRevocationPolicy.Online,
        TimeSpan? maximumShortLivedLifetime = null)
    {
        TrustDomain = new SecuritySubject(trustDomain, "pem-trust-reference").Issuer;
        RootBundleFile = CertificatePemPaths.Absolute(rootBundleFile);
        IssuingAuthorityBundleFile = CertificatePemPaths.Absolute(issuingAuthorityBundleFile);
        IntermediateBundleFile = intermediateBundleFile is null ? null : CertificatePemPaths.Absolute(intermediateBundleFile);
        if (!Enum.IsDefined(revocation))
        {
            throw new ArgumentOutOfRangeException(nameof(revocation));
        }
        Revocation = revocation;
        MaximumShortLivedLifetime = maximumShortLivedLifetime ?? TimeSpan.FromHours(24);
        if (MaximumShortLivedLifetime <= TimeSpan.Zero || MaximumShortLivedLifetime > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumShortLivedLifetime));
        }
    }

    /// <summary>Gets the configured issuer-qualified mapping domain.</summary>
    public string TrustDomain
    {
        get;
    }
    /// <summary>Gets the explicit public client-root PEM bundle path.</summary>
    public string RootBundleFile
    {
        get;
    }
    /// <summary>Gets the explicit authorized direct-issuer PEM bundle path.</summary>
    public string IssuingAuthorityBundleFile
    {
        get;
    }
    /// <summary>Gets optional additional public intermediates used only for chain construction.</summary>
    public string? IntermediateBundleFile
    {
        get;
    }
    /// <summary>Gets the selected fixed client revocation policy.</summary>
    public CertificateRevocationPolicy Revocation
    {
        get;
    }
    /// <summary>Gets the actual ShortLived leaf ceiling.</summary>
    public TimeSpan MaximumShortLivedLifetime
    {
        get;
    }
}

/// <summary>Immutable paired PEM credential paths; no password/PFX input or key loading occurs here.</summary>
public sealed class CertificatePemCredentialFiles
{
    /// <summary>Captures the leaf-first certificate chain and matching unencrypted private-key paths.</summary>
    public CertificatePemCredentialFiles(string chainFile, string privateKeyFile)
    {
        ChainFile = CertificatePemPaths.Absolute(chainFile);
        PrivateKeyFile = CertificatePemPaths.Absolute(privateKeyFile);
        if (string.Equals(ChainFile, PrivateKeyFile, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The public chain and private key use separate files.");
        }
    }

    /// <summary>Gets the leaf-first PEM chain path.</summary>
    public string ChainFile
    {
        get;
    }
    /// <summary>Gets the matching unencrypted PEM private-key path.</summary>
    public string PrivateKeyFile
    {
        get;
    }
}

/// <summary>Detached immutable profile setup; only enabled roles load their required files.</summary>
public sealed class CertificatePemSecurityOptions
{
    /// <summary>Captures source/trust references and the existing Heartbeat cadence without I/O.</summary>
    public CertificatePemSecurityOptions(CertificatePemTrustOptions clientTrust, CertificatePemCredentialFiles? credential = null,
        string? serverTrustBundleFile = null, X509RevocationMode serverRevocationMode = X509RevocationMode.Online,
        CertificateDevelopmentGate? development = null, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(clientTrust);
        if (!Enum.IsDefined(serverRevocationMode)
            || (clientTrust.Revocation == CertificateRevocationPolicy.Development) != (development is not null))
        {
            throw new ArgumentException("Select explicit native server revocation and matching development trust/gates.");
        }
        ClientTrust = clientTrust;
        Credential = credential;
        ServerTrustBundleFile = serverTrustBundleFile is null ? null : CertificatePemPaths.Absolute(serverTrustBundleFile);
        ServerRevocationMode = serverRevocationMode;
        Development = development;
        PollInterval = pollInterval ?? TimeSpan.FromSeconds(30);
        if (PollInterval < TimeSpan.FromSeconds(1) || PollInterval > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }
    }

    /// <summary>Gets fixed client trust references, loaded once at startup.</summary>
    public CertificatePemTrustOptions ClientTrust
    {
        get;
    }
    /// <summary>Gets outgoing credential references, required only for an outgoing role.</summary>
    public CertificatePemCredentialFiles? Credential
    {
        get;
    }
    /// <summary>Gets separate private server trust, or null to explicitly initialize native system trust.</summary>
    public string? ServerTrustBundleFile
    {
        get;
    }
    /// <summary>Gets native server revocation behavior; hostname/server EKU remain mandatory.</summary>
    public X509RevocationMode ServerRevocationMode
    {
        get;
    }
    /// <summary>Gets isolated canonical/native/listener development gates.</summary>
    public CertificateDevelopmentGate? Development
    {
        get;
    }
    /// <summary>Gets existing Heartbeat's file-check interval, default thirty seconds.</summary>
    public TimeSpan PollInterval
    {
        get;
    }
}

internal static class CertificatePemPaths
{
    internal static string Absolute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 or > 4096 || !Path.IsPathFullyQualified(value))
        {
            throw new ArgumentException("Supply a bounded absolute PEM file path.", nameof(value));
        }
        return Path.GetFullPath(value);
    }
}
