using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CIS.RestClient.Http;
using CIS.Security.Certificates;
using CIS.Startup;

namespace CIS.Management.Service.Security;

// This application owns its server leaf until native Kestrel/response disposal.
// The existing native certificate owner handles temporary Windows TLS key custody.
internal sealed class ManagementServerCertificate(ManagementSecurityOptions options, RestHttpNativeKeyScope keys,
    TimeProvider? timeProvider = null) : IStartable, IDisposable
{
    private readonly object gate = new();

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private readonly X509Certificate2Collection publicChain = new();

    private RestHttpNativeCertificate? owner;

    private bool disposed;

    // Kestrel captures this collection during setup; sequential Startup fills it
    // before native listeners start, and the application keeps it through drain.
    internal X509Certificate2Collection PublicChain => publicChain;

    internal X509Certificate2 Certificate
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return owner?.Certificate ?? throw new InvalidOperationException("Management TLS has not completed sequential Startup.");
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (owner is not null)
            {
                throw new InvalidOperationException("The management server leaf is loaded once per host.");
            }
        }

        byte[] publicPem = [];
        byte[] privatePem = [];
        byte[] publicAgain = [];
        byte[] privateAgain = [];
        char[] privateCharacters = [];
        var chainCertificates = new X509Certificate2Collection();
        var roots = new X509Certificate2Collection();
        X509Certificate2? leaf = null;
        RestHttpNativeCertificate? candidate = null;
        try
        {
            publicPem = await ReadBoundedAsync(options.ServerCertificateFile, 262144, cancellationToken).ConfigureAwait(false);
            privatePem = await ReadBoundedAsync(options.ServerPrivateKeyFile, 65536, cancellationToken).ConfigureAwait(false);
            publicAgain = await ReadBoundedAsync(options.ServerCertificateFile, 262144, cancellationToken).ConfigureAwait(false);
            privateAgain = await ReadBoundedAsync(options.ServerPrivateKeyFile, 65536, cancellationToken).ConfigureAwait(false);
            if (!publicPem.AsSpan().SequenceEqual(publicAgain) || !privatePem.AsSpan().SequenceEqual(privateAgain))
            {
                throw new InvalidDataException("The management server PEM pair changed during its bounded Startup capture.");
            }

            char[] publicCharacters = PemCharacters(publicPem);
            privateCharacters = PemCharacters(privatePem);
            chainCertificates.ImportFromPem(publicCharacters);
            if (chainCertificates.Count is < 1 or > 8)
            {
                throw new InvalidDataException("Supply one leaf and at most seven public server-chain certificates.");
            }
            leaf = X509Certificate2.CreateFromPem(publicCharacters, privateCharacters);
            candidate = new(leaf);
            leaf = null;
            if (options.ServerTrustBundleFile is { } rootFile)
            {
                byte[] rootBytes = await ReadBoundedAsync(rootFile, 262144, cancellationToken).ConfigureAwait(false);
                roots.ImportFromPem(PemCharacters(rootBytes));
                if (roots.Count is < 1 or > 16)
                {
                    throw new InvalidDataException("Supply one to sixteen explicit public server trust roots.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            Validate(candidate.Certificate, chainCertificates, roots);
            cancellationToken.ThrowIfCancellationRequested();
            candidate.PrepareForTls(keys);
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (owner is not null)
                {
                    throw new InvalidOperationException("The management server leaf has already been adopted.");
                }
                foreach (X509Certificate2 certificate in chainCertificates.Cast<X509Certificate2>().Skip(1))
                {
                    publicChain.Add(X509CertificateLoader.LoadCertificate(certificate.RawData));
                }
                owner = candidate;
                candidate = null;
            }
        }
        catch (Exception original)
        {
            try
            {
                candidate?.Dispose();
                leaf?.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
        finally
        {
            foreach (byte[] buffer in new[] { publicPem, privatePem, publicAgain, privateAgain })
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
            Array.Clear(privateCharacters);
            foreach (X509Certificate2 certificate in chainCertificates)
            {
                certificate.Dispose();
            }
            foreach (X509Certificate2 root in roots)
            {
                root.Dispose();
            }
        }
    }

    private void Validate(X509Certificate2 leaf, X509Certificate2Collection intermediates, X509Certificate2Collection roots)
    {
        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset from = new(leaf.NotBefore.ToUniversalTime());
        DateTimeOffset until = new(leaf.NotAfter.ToUniversalTime());
        bool shortLived = options.CertificateRevocation == CertificateRevocationPolicy.ShortLived;
        if (!leaf.HasPrivateKey || now < from || now >= until
            || leaf.Extensions.OfType<X509BasicConstraintsExtension>().Count() != 1
            || leaf.Extensions.OfType<X509BasicConstraintsExtension>().Single().CertificateAuthority
            || leaf.Extensions.OfType<X509KeyUsageExtension>().Count() != 1
            || (leaf.Extensions.OfType<X509KeyUsageExtension>().Single().KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0
            || !leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(extension => extension.EnhancedKeyUsages.Cast<Oid>()
                .Any(usage => usage.Value == "1.3.6.1.5.5.7.3.1"))
            || shortLived && until - from > options.MaximumShortLivedLifetime)
        {
            throw new InvalidOperationException("Management TLS requires a current non-CA signing server-authentication leaf under its selected lifetime policy.");
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.VerificationTime = now.UtcDateTime;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.RevocationMode = options.Mode == "local" || shortLived
            ? X509RevocationMode.NoCheck : X509RevocationMode.Online;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        chain.ChainPolicy.ExtraStore.AddRange(intermediates);
        if (options.ServerTrustBundleFile is not null)
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(roots);
        }
        if (!chain.Build(leaf))
        {
            throw new InvalidOperationException("Management TLS server chain validation failed under its configured trust and revocation policy.");
        }
    }

    private static char[] PemCharacters(byte[] bytes)
    {
        if (bytes.Any(value => value > 127))
        {
            throw new InvalidDataException("PEM material must use its ASCII representation.");
        }

        return Encoding.ASCII.GetChars(bytes);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximum, CancellationToken token)
    {
        byte[] buffer = new byte[maximum + 1];
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int count = 0;
            while (count < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                count += read;
            }
            if (count == 0 || count > maximum)
            {
                throw new InvalidDataException("The actual management server PEM bytes exceed their supported nonempty bound.");
            }

            return buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public void Dispose()
    {
        RestHttpNativeCertificate? owned;
        lock (gate)
        {
            disposed = true;
            owned = owner;
            owner = null;
        }

        try
        {
            owned?.Dispose();
        }
        finally
        {
            foreach (X509Certificate2 certificate in publicChain)
            {
                certificate.Dispose();
            }
            publicChain.Clear();
        }
    }
}
