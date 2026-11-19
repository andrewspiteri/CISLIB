using System.Security.Cryptography.X509Certificates;

namespace CIS.RestClient.Http;

/// <summary>The result of one bounded native certificate-generation adoption request.</summary>
public enum RestHttpCertificateAdoption
{
    /// <summary>The validated material was published for subsequent exchanges.</summary>
    Adopted,
    /// <summary>Both generation slots, or the one construction reservation, are occupied; the factory was not invoked.</summary>
    Deferred
}

/// <summary>An owned native leaf/key and copied public intermediates for one TLS generation.</summary>
/// <remarks>Successful construction transfers the leaf's disposal ownership. On constructor failure the
/// caller still owns it. The adapter validates trust, stable identity, usage and revocation before returning
/// material. Material cannot change the transport's separately captured server trust.</remarks>
public sealed class RestHttpCertificateMaterial : IDisposable
{
    private readonly X509Certificate2[] intermediates;
    private RestHttpNativeCertificate? nativeOwner;
    private int disposed;

    /// <summary>Takes one leaf/private key and copies at most seven public intermediate certificates.</summary>
    public RestHttpCertificateMaterial(X509Certificate2 certificate, IReadOnlyList<X509Certificate2>? intermediates = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey || intermediates?.Count > 7)
        {
            throw new ArgumentException("Supply a leaf with its private key and at most seven intermediates.", nameof(certificate));
        }
        var copied = new List<X509Certificate2>();
        try
        {
            foreach (X509Certificate2 intermediate in intermediates ?? [])
            {
                ArgumentNullException.ThrowIfNull(intermediate);
                copied.Add(X509CertificateLoader.LoadCertificate(intermediate.RawData));
            }
            NotBeforeUtc = certificate.NotBefore.ToUniversalTime();
            ExpiresAtUtc = certificate.NotAfter.ToUniversalTime();
            if (ExpiresAtUtc <= NotBeforeUtc)
            {
                throw new ArgumentException("The leaf has no usable validity interval.", nameof(certificate));
            }
        }
        catch
        {
            foreach (X509Certificate2 item in copied)
            {
                item.Dispose();
            }
            throw;
        }
        Certificate = certificate;
        this.intermediates = copied.ToArray();
    }

    /// <summary>Takes a prepared native certificate owner and copies its public intermediate chain.</summary>
    /// <remarks>Prepare inside the transport's admitted factory first. A failure leaves the owner with
    /// the caller; success retains it through actual native generation and response-body drain.</remarks>
    public static RestHttpCertificateMaterial FromNativeCertificate(RestHttpNativeCertificate owner,
        IReadOnlyList<X509Certificate2>? intermediates = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!owner.IsPrepared)
        {
            throw new InvalidOperationException("Explicitly prepare the native certificate before transferring it.");
        }
        var material = new RestHttpCertificateMaterial(owner.Certificate, intermediates);
        material.nativeOwner = owner;
        return material;
    }

    /// <summary>Gets the leaf's actual NotBefore time.</summary>
    public DateTimeOffset NotBeforeUtc
    {
        get;
    }
    /// <summary>Gets the leaf's actual expiry; no renewal grace period is applied.</summary>
    public DateTimeOffset ExpiresAtUtc
    {
        get;
    }
    internal X509Certificate2 Certificate
    {
        get;
    }
    internal X509Certificate2Collection IntermediateCertificates => new(intermediates);
    internal bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>Disposes the owned leaf/key and copied public chain exactly once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }
        var failures = new List<Exception>();
        foreach (X509Certificate2 item in intermediates)
        {
            try
            {
                item.Dispose();
            }
            catch (Exception error) { failures.Add(error); }
        }
        try
        {
            if (nativeOwner is null)
            {
                Certificate.Dispose();
            }
            else
            {
                nativeOwner.Dispose();
            }
        }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count == 1)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException("Certificate material cleanup failed.", failures);
        }
    }
}

/// <summary>Immutable explicit server trust, separate from the roots that issue client workload identities.</summary>
public sealed class RestHttpServerTrust
{
    private readonly byte[][] roots;

    /// <summary>Copies one to eight public trust anchors. Native hostname and server-usage validation remain mandatory.</summary>
    public RestHttpServerTrust(IReadOnlyList<X509Certificate2> trustedRoots,
        X509RevocationMode revocationMode = X509RevocationMode.Online)
    {
        ArgumentNullException.ThrowIfNull(trustedRoots);
        if (trustedRoots.Count is < 1 or > 8 || !Enum.IsDefined(revocationMode))
        {
            throw new ArgumentException("Supply one to eight explicit server trust anchors and a native revocation mode.");
        }
        roots = trustedRoots.Select(root => root?.RawData.ToArray()
            ?? throw new ArgumentException("Server trust anchors cannot be null.", nameof(trustedRoots))).ToArray();
        RevocationMode = revocationMode;
    }

    /// <summary>Gets the explicitly selected native server revocation mode.</summary>
    public X509RevocationMode RevocationMode
    {
        get;
    }

    internal X509ChainPolicy CreatePolicy(List<X509Certificate2> ownedRoots)
    {
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = RevocationMode,
            RevocationFlag = X509RevocationFlag.ExcludeRoot,
            VerificationFlags = X509VerificationFlags.NoFlag
        };
        policy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
        foreach (byte[] root in roots)
        {
            X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(root);
            ownedRoots.Add(certificate);
            policy.CustomTrustStore.Add(certificate);
        }
        return policy;
    }
}
