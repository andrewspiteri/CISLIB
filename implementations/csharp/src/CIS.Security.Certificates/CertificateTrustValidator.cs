using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CIS.Security.Certificates;

/// <summary>Detached validation facts. Success alone does not grant capabilities or establish caller scope.</summary>
public sealed class CertificateValidationResult
{
    internal CertificateValidationResult(SecuritySubject? subject, DateTimeOffset? notBeforeUtc,
        DateTimeOffset? expiresAtUtc, string? credentialIdentifier, SecurityFailure? failure)
    {
        Subject = subject;
        NotBeforeUtc = notBeforeUtc;
        ExpiresAtUtc = expiresAtUtc;
        CredentialIdentifier = credentialIdentifier;
        Failure = failure;
    }

    /// <summary>Gets whether every selected certificate check passed.</summary>
    public bool Succeeded => Failure is null;
    /// <summary>Gets the configured trust domain and exact raw URI SAN.</summary>
    public SecuritySubject? Subject
    {
        get;
    }
    /// <summary>Gets the actual issued leaf's validity start.</summary>
    public DateTimeOffset? NotBeforeUtc
    {
        get;
    }
    /// <summary>Gets the actual issued leaf's expiry.</summary>
    public DateTimeOffset? ExpiresAtUtc
    {
        get;
    }
    /// <summary>Gets a public leaf digest for diagnostics, never a stable grant key.</summary>
    public string? CredentialIdentifier
    {
        get;
    }
    /// <summary>Gets a bounded safe failure without parser input or credential material.</summary>
    public SecurityFailure? Failure
    {
        get;
    }
}

/// <summary>Strict native custom-root validation reusable by receiving and owned credential-source integration.</summary>
public static class CertificateTrustValidator
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    /// <summary>Checks the actual leaf, direct issuer, exact URI SAN, usage, lifetime and selected revocation policy.</summary>
    /// <remarks>Native online validation can use the platform's revocation cache. No fallback or principal cache is used.</remarks>
    public static CertificateValidationResult Validate(X509Certificate2 certificate, CertificateTrustPolicy trust,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(trust);
        if (certificate.RawDataMemory.Length > 256 * 1024)
        {
            return Reject(SecurityFailureKind.CredentialInvalid, "The certificate exceeds the supported leaf size.");
        }
        try
        {
            DateTimeOffset notBefore = certificate.NotBefore.ToUniversalTime();
            DateTimeOffset expiry = certificate.NotAfter.ToUniversalTime();
            if (expiry <= utcNow)
            {
                return Reject(SecurityFailureKind.CredentialExpired, "The actual certificate leaf is expired.");
            }
            if (notBefore > utcNow || expiry <= notBefore)
            {
                return Reject(SecurityFailureKind.CredentialInvalid, "The actual certificate validity interval is not currently valid.");
            }
            if (trust.Revocation == CertificateRevocationPolicy.ShortLived && expiry - notBefore > trust.MaximumShortLivedLifetime)
            {
                return Reject(SecurityFailureKind.CredentialInvalid, "The issued leaf exceeds the selected ShortLived validity ceiling.");
            }
            if (!HasExplicitClientUsage(certificate))
            {
                return Reject(SecurityFailureKind.CredentialInvalid, "The leaf requires non-CA constraints, digital signature usage and explicit client authentication EKU.");
            }
            string? identity = ReadIdentity(certificate);
            if (identity is null)
            {
                return Reject(SecurityFailureKind.CredentialInvalid, "The leaf requires exactly one bounded absolute URI identity SAN.");
            }
            var subject = new SecuritySubject(trust.TrustDomain, identity);
            X509Certificate2[] roots = trust.LoadRoots();
            X509Certificate2[] issuers = [];
            try
            {
                issuers = trust.LoadChainCertificates();
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(roots);
                chain.ChainPolicy.ExtraStore.AddRange(issuers);
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ClientAuthenticationOid));
                chain.ChainPolicy.RevocationMode = trust.Revocation == CertificateRevocationPolicy.Online
                    ? X509RevocationMode.Online : X509RevocationMode.NoCheck;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.VerificationTime = utcNow.UtcDateTime;
                chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
                chain.ChainPolicy.DisableCertificateDownloads = true;
                try
                {
                    if (!chain.Build(certificate))
                    {
                        bool unavailable = chain.ChainStatus.Any(status => (status.Status
                            & (X509ChainStatusFlags.OfflineRevocation | X509ChainStatusFlags.RevocationStatusUnknown)) != 0);
                        return Reject(unavailable ? SecurityFailureKind.ValidationUnavailable : SecurityFailureKind.CredentialInvalid,
                            unavailable ? "Required native revocation status is unavailable." : "The certificate did not satisfy explicit native client trust.");
                    }
                    if (chain.ChainElements.Count < 2 || !trust.AcceptsIssuer(chain.ChainElements[1].Certificate))
                    {
                        return Reject(SecurityFailureKind.CredentialInvalid, "The actual direct issuing authority is not authorized.");
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
            finally
            {
                foreach (X509Certificate2 authority in roots.Concat(issuers))
                {
                    authority.Dispose();
                }
            }
            return new(subject, notBefore, expiry, Convert.ToHexStringLower(SHA256.HashData(certificate.RawDataMemory.Span)), null);
        }
        catch (Exception error) when (error is CryptographicException or AsnContentException or ArgumentException)
        {
            return Reject(SecurityFailureKind.CredentialInvalid, "The certificate contains unsupported or malformed validation data.");
        }
    }

    private static bool HasExplicitClientUsage(X509Certificate2 certificate)
    {
        X509BasicConstraintsExtension[] constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().ToArray();
        X509KeyUsageExtension[] usage = certificate.Extensions.OfType<X509KeyUsageExtension>().ToArray();
        X509EnhancedKeyUsageExtension[] enhanced = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
        return constraints.Length == 1 && !constraints[0].CertificateAuthority && usage.Length == 1
            && (usage[0].KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0 && enhanced.Length == 1
            && enhanced[0].EnhancedKeyUsages.Cast<Oid>().Any(value => value.Value == ClientAuthenticationOid);
    }

    private static string? ReadIdentity(X509Certificate2 certificate)
    {
        X509Extension[] extensions = certificate.Extensions.Cast<X509Extension>().Where(value => value.Oid?.Value == "2.5.29.17").ToArray();
        if (extensions.Length != 1)
        {
            return null;
        }
        var reader = new AsnReader(extensions[0].RawData, AsnEncodingRules.DER);
        AsnReader names = reader.ReadSequence();
        reader.ThrowIfNotEmpty();
        string? identity = null;
        int count = 0;
        while (names.HasData)
        {
            if (++count > 128)
            {
                return null;
            }
            if (names.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 6)))
            {
                if (identity is not null)
                {
                    return null;
                }
                identity = names.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 6));
                if (identity.Length > 1024 || !Uri.TryCreate(identity, UriKind.Absolute, out _))
                {
                    return null;
                }
            }
            else
            {
                names.ReadEncodedValue();
            }
        }
        return identity;
    }

    private static CertificateValidationResult Reject(SecurityFailureKind kind, string message)
        => new(null, null, null, null, new(kind, message));
}
