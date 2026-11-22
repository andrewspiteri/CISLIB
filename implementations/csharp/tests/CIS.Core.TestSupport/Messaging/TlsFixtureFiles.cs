using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CIS.TestSupport.Messaging;

/// <summary>Creates only newly owned development TLS PEM files, without importing keys or changing trust stores.</summary>
public static class TlsFixtureFiles
{
    /// <summary>Writes root.pem, server.pem and server.key into the caller's private absolute directory, refusing overwrite.</summary>
    public static void Write(string privateDirectory)
    {
        if (string.IsNullOrWhiteSpace(privateDirectory) || !Path.IsPathFullyQualified(privateDirectory))
        {
            throw new ArgumentException("Select an absolute newly owned private fixture directory.", nameof(privateDirectory));
        }
        string directory = Path.GetFullPath(privateDirectory);
        string rootPath = Path.Combine(directory, "root.pem");
        string certificatePath = Path.Combine(directory, "server.pem");
        string keyPath = Path.Combine(directory, "server.key");
        if (File.Exists(rootPath) || File.Exists(certificatePath) || File.Exists(keyPath))
        {
            throw new IOException("Fixture TLS output already exists.");
        }
        Directory.CreateDirectory(directory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using RSA rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=CISLIB isolated RM04 fixture root", rootKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using X509Certificate2 root = rootRequest.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(7));

        using RSA serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        var usages = new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") };
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(serverRequest.PublicKey, false));
        using X509Certificate2 server = serverRequest.Create(root, now.AddMinutes(-5), now.AddDays(6),
            RandomNumberGenerator.GetBytes(16));

        WriteNew(rootPath, root.ExportCertificatePem());
        WriteNew(certificatePath, server.ExportCertificatePem());
        WriteNew(keyPath, serverKey.ExportPkcs8PrivateKeyPem());
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
