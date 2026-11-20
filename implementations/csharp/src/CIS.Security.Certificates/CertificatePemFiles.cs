using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CIS.Security.Certificates;

internal static class CertificatePemFiles
{
    internal const int MaximumChainBytes = 256 * 1024;
    internal const int MaximumKeyBytes = 64 * 1024;

    internal static async Task<byte[]> ReadAsync(string path, int maximum, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximum)
        {
            throw new InvalidDataException("The PEM file exceeds its declared byte bound.");
        }
        byte[] buffer = new byte[maximum + 1];
        try
        {
            int length = 0;
            while (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                length += read;
            }
            if (length == 0 || length > maximum)
            {
                throw new InvalidDataException("The PEM file is empty or exceeds its declared byte bound.");
            }
            return buffer.AsSpan(0, length).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    internal static async Task<PemCandidate> ReadPairAsync(CertificatePemCredentialFiles files, CancellationToken token)
    {
        byte[] chain = [];
        byte[] key = [];
        byte[] secondChain = [];
        byte[] secondKey = [];
        try
        {
            chain = await ReadAsync(files.ChainFile, MaximumChainBytes, token).ConfigureAwait(false);
            key = await ReadAsync(files.PrivateKeyFile, MaximumKeyBytes, token).ConfigureAwait(false);
            secondChain = await ReadAsync(files.ChainFile, MaximumChainBytes, token).ConfigureAwait(false);
            secondKey = await ReadAsync(files.PrivateKeyFile, MaximumKeyBytes, token).ConfigureAwait(false);
            if (!chain.AsSpan().SequenceEqual(secondChain) || !key.AsSpan().SequenceEqual(secondKey))
            {
                throw new InvalidDataException("The paired PEM files changed during bounded capture.");
            }
            var result = new PemCandidate(chain, key);
            chain = [];
            key = [];
            return result;
        }
        finally
        {
            foreach (byte[] value in new[] { chain, key, secondChain, secondKey })
            {
                CryptographicOperations.ZeroMemory(value);
            }
        }
    }

    internal static char[] Characters(byte[] bytes)
    {
        if (bytes.Any(value => value > 127))
        {
            throw new InvalidDataException("PEM input must use its ASCII representation.");
        }
        return Encoding.ASCII.GetChars(bytes);
    }

    internal static void CheckBlocks(ReadOnlySpan<char> pem, bool privateKey)
    {
        int count = 0;
        while (PemEncoding.TryFind(pem, out PemFields fields))
        {
            (int start, int length) = fields.Location.GetOffsetAndLength(pem.Length);
            ReadOnlySpan<char> label = pem[fields.Label];
            count++;
            if (!pem[..start].Trim().IsEmpty || privateKey && count > 1
                || !privateKey && count > 8
                || privateKey && !(label.SequenceEqual("PRIVATE KEY") || label.SequenceEqual("RSA PRIVATE KEY") || label.SequenceEqual("EC PRIVATE KEY"))
                || !privateKey && !label.SequenceEqual("CERTIFICATE"))
            {
                throw new InvalidDataException("Supply supported PEM blocks within the fixed chain/key bounds.");
            }
            pem = pem[(start + length)..];
        }
        if (count == 0 || !pem.Trim().IsEmpty)
        {
            throw new InvalidDataException("Supply only supported complete PEM blocks and whitespace.");
        }
    }

    internal static async Task<X509Certificate2Collection> ReadCertificatesAsync(string path, CancellationToken token)
    {
        byte[] bytes = await ReadAsync(path, MaximumChainBytes, token).ConfigureAwait(false);
        char[] characters = [];
        var certificates = new X509Certificate2Collection();
        try
        {
            characters = Characters(bytes);
            CheckBlocks(characters, privateKey: false);
            certificates.ImportFromPem(characters);
            return certificates;
        }
        catch
        {
            foreach (X509Certificate2 certificate in certificates)
            {
                certificate.Dispose();
            }
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Array.Clear(characters);
        }
    }
}

internal sealed class PemCandidate(byte[] chain, byte[] key) : IDisposable
{
    internal byte[] Chain { get; } = chain;
    internal byte[] Key { get; } = key;
    internal string Fingerprint
    {
        get;
    } = Convert.ToHexStringLower(SHA256.HashData(
        SHA256.HashData(chain).Concat(SHA256.HashData(key)).ToArray()));

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(Chain);
        CryptographicOperations.ZeroMemory(Key);
    }
}
