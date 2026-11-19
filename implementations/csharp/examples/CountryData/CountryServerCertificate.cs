using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CIS.RestClient.Http;
using CIS.Startup;

namespace CIS.Examples.CountryData;

// Sequential Startup loads server material. DI owns disposal only after actual Kestrel/response drain.
internal sealed class CountryServerCertificate(CountrySecuritySnapshot snapshot, CountrySecurityMode mode,
    RestHttpNativeKeyScope keys) : IStartable, IDisposable
{
    private RestHttpNativeCertificate? owner;
    internal X509Certificate2 Certificate => owner?.Certificate
        ?? throw new InvalidOperationException("The server certificate has not completed sequential Startup.");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CountrySecuritySettings settings = snapshot.Settings;
        string? trustFile = settings.ServerTrustCertificateFile ?? (mode.Mode == "local" ? settings.CaCertificateFile : null);
        X509Certificate2 leaf = await ReadLeafAsync(settings, cancellationToken);
        var candidate = new RestHttpNativeCertificate(leaf);
        try
        {
            using var chain = new X509Chain();
            var roots = new X509Certificate2Collection();
            try
            {
                if (trustFile is not null)
                {
                    byte[] publicTrust = await ReadBoundedAsync(trustFile, 262144, cancellationToken);
                    roots.ImportFromPem(Encoding.UTF8.GetChars(publicTrust));
                    if (roots.Count is < 1 or > 8)
                    {
                        throw new InvalidOperationException("Configure one to eight explicit public server roots.");
                    }
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.AddRange(roots);
                }
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.RevocationMode = mode.Mode == "local" ? X509RevocationMode.NoCheck : X509RevocationMode.Online;
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
                if (!leaf.HasPrivateKey || !chain.Build(leaf)
                    || leaf.Extensions.OfType<X509BasicConstraintsExtension>().Any(value => value.CertificateAuthority)
                    || !leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Any(value => value.EnhancedKeyUsages.Cast<Oid>()
                        .Any(usage => usage.Value == "1.3.6.1.5.5.7.3.1")))
                {
                    throw new InvalidOperationException("The server PEM must be a current non-CA server-authentication leaf under its explicit roots.");
                }
            }
            finally
            {
                foreach (X509Certificate2 root in roots)
                {
                    root.Dispose();
                }
            }
            candidate.PrepareForTls(keys);
            cancellationToken.ThrowIfCancellationRequested();
            owner = candidate;
        }
        catch (Exception original)
        {
            try
            {
                candidate.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }

    private static async Task<X509Certificate2> ReadLeafAsync(CountrySecuritySettings settings, CancellationToken token)
    {
        byte[] certificate = await ReadBoundedAsync(settings.ServerCertificateFile, 262144, token);
        byte[]? key = null;
        char[]? privatePem = null;
        try
        {
            key = await ReadBoundedAsync(settings.ServerPrivateKeyFile, 65536, token);
            privatePem = Encoding.UTF8.GetChars(key);
            token.ThrowIfCancellationRequested();
            return X509Certificate2.CreateFromPem(Encoding.UTF8.GetChars(certificate), privatePem);
        }
        finally
        {
            if (key is not null)
            {
                CryptographicOperations.ZeroMemory(key);
            }
            if (privatePem is not null)
            {
                Array.Clear(privatePem);
            }
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, int maximum, CancellationToken token)
    {
        if (path.Length > 4096 || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Configure bounded absolute server PEM paths.");
        }
        byte[] buffer = new byte[maximum + 1];
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            int count = 0;
            while (count < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(count), token);
                if (read == 0)
                {
                    break;
                }
                count += read;
            }
            if (count == 0 || count > maximum)
            {
                throw new InvalidDataException("The actual server PEM bytes exceed their supported nonempty bound.");
            }
            return buffer.AsSpan(0, count).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public void Dispose() => owner?.Dispose();
}
