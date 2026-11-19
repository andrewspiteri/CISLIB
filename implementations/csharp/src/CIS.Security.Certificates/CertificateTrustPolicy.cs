using System.Security.Cryptography.X509Certificates;

namespace CIS.Security.Certificates;

/// <summary>The explicitly selected certificate revocation behavior; failure never selects another mode.</summary>
public enum CertificateRevocationPolicy
{
    /// <summary>Native online checks; unavailable or unknown required status denies authentication.</summary>
    Online,
    /// <summary>No online check, with an actual leaf validity interval of at most twenty-four hours.</summary>
    ShortLived,
    /// <summary>Isolated local development trust, subject to separate canonical and native environment gates.</summary>
    Development
}

/// <summary>An immutable public-certificate snapshot of an explicitly authorized issuing relationship.</summary>
/// <remarks>Roots establish native trust. Issuing authorities independently restrict the leaf's direct issuer.
/// Additional intermediate certificates only assist chain construction. No private keys are captured.</remarks>
public sealed class CertificateTrustPolicy
{
    private readonly byte[][] roots;
    private readonly byte[][] issuers;
    private readonly byte[][] intermediates;

    /// <summary>Copies public DER and validates finite CA inputs without credential or network I/O.</summary>
    public CertificateTrustPolicy(string trustDomain, IReadOnlyList<X509Certificate2> roots,
        IReadOnlyList<X509Certificate2> issuingAuthorities, IReadOnlyList<X509Certificate2>? intermediates = null,
        CertificateRevocationPolicy revocation = CertificateRevocationPolicy.Online,
        TimeSpan? maximumShortLivedLifetime = null)
    {
        TrustDomain = new SecuritySubject(trustDomain, "certificate-trust-validation").Issuer;
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
        this.roots = CaptureAuthorities(roots, 16, nameof(roots), required: true);
        issuers = CaptureAuthorities(issuingAuthorities, 16, nameof(issuingAuthorities), required: true);
        this.intermediates = CaptureAuthorities(intermediates ?? [], 8, nameof(intermediates), required: false);
    }

    /// <summary>Gets the trusted issuer-domain mapping key; it is never derived from a certificate DN.</summary>
    public string TrustDomain
    {
        get;
    }
    /// <summary>Gets the fixed revocation policy.</summary>
    public CertificateRevocationPolicy Revocation
    {
        get;
    }
    /// <summary>Gets the actual issued-leaf lifetime ceiling for ShortLived.</summary>
    public TimeSpan MaximumShortLivedLifetime
    {
        get;
    }
    /// <summary>Gets the number of explicitly trusted roots.</summary>
    public int RootCount => roots.Length;
    /// <summary>Gets the number of authorized direct issuing authorities.</summary>
    public int IssuingAuthorityCount => issuers.Length;

    internal X509Certificate2[] LoadRoots() => Load(roots);
    internal X509Certificate2[] LoadChainCertificates() => Load(issuers.Concat(intermediates));
    internal bool AcceptsIssuer(X509Certificate2 issuer) => issuers.Any(value => issuer.RawDataMemory.Span.SequenceEqual(value));
    internal bool SharesRoot(CertificateTrustPolicy other) => roots.Any(root => other.roots.Any(value => value.AsSpan().SequenceEqual(root)));

    internal void ValidateAuthorities(DateTimeOffset now)
    {
        X509Certificate2[] loadedRoots = LoadRoots();
        X509Certificate2[] loadedIssuers = [];
        try
        {
            loadedIssuers = LoadChainCertificates();
            if (loadedRoots.Any(root => !root.SubjectName.RawData.AsSpan().SequenceEqual(root.IssuerName.RawData))
                || loadedRoots.Concat(loadedIssuers).Any(authority => authority.NotBefore.ToUniversalTime() > now.UtcDateTime
                    || authority.NotAfter.ToUniversalTime() <= now.UtcDateTime))
            {
                throw new InvalidOperationException("Explicit certificate authorities must be currently valid and roots must be self-issued trust anchors.");
            }
            foreach (byte[] encoded in issuers)
            {
                using X509Certificate2 issuer = X509CertificateLoader.LoadCertificate(encoded);
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(loadedRoots);
                chain.ChainPolicy.ExtraStore.AddRange(loadedIssuers);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationTime = now.UtcDateTime;
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
                try
                {
                    if (!chain.Build(issuer))
                    {
                        throw new InvalidOperationException("An authorized issuing authority must chain to the explicitly selected client roots.");
                    }
                }
                finally
                {
                    foreach (X509ChainElement element in chain.ChainElements)
                    {
                        element.Certificate.Dispose();
                    }
                }
            }
        }
        finally
        {
            foreach (X509Certificate2 authority in loadedRoots.Concat(loadedIssuers))
            {
                authority.Dispose();
            }
        }
    }

    private static X509Certificate2[] Load(IEnumerable<byte[]> values)
    {
        var loaded = new List<X509Certificate2>();
        try
        {
            foreach (byte[] value in values)
            {
                loaded.Add(X509CertificateLoader.LoadCertificate(value));
            }
            return loaded.ToArray();
        }
        catch
        {
            foreach (X509Certificate2 certificate in loaded)
            {
                certificate.Dispose();
            }
            throw;
        }
    }

    private static byte[][] CaptureAuthorities(IReadOnlyList<X509Certificate2> certificates, int limit,
        string name, bool required)
    {
        ArgumentNullException.ThrowIfNull(certificates, name);
        if (certificates.Count > limit || required && certificates.Count == 0)
        {
            throw new ArgumentException("Supply a finite nonempty explicit authority set.", name);
        }
        var result = new byte[certificates.Count][];
        for (int index = 0; index < certificates.Count; index++)
        {
            X509Certificate2 certificate = certificates[index] ?? throw new ArgumentException("An authority cannot be null.", name);
            if (certificate.RawDataMemory.Length > 64 * 1024
                || certificate.Extensions.OfType<X509BasicConstraintsExtension>().Count() != 1
                || !certificate.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority
                || certificate.Extensions.OfType<X509KeyUsageExtension>().Count() != 1
                || (certificate.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages & X509KeyUsageFlags.KeyCertSign) == 0)
            {
                throw new ArgumentException("An authority requires bounded public DER, CA constraints and certificate-signing key usage.", name);
            }
            result[index] = certificate.RawDataMemory.ToArray();
        }
        return result;
    }
}
