using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CIS.Security.LocalDevelopment;

internal static class DevelopmentCertificates
{
    internal static X509Certificate2 CreateAuthority(DateTimeOffset now)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=CIS local development " + Guid.NewGuid().ToString("N"),
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        DateTimeOffset notBefore = WholeSecond(now).AddMinutes(-5);
        return request.CreateSelfSigned(notBefore, notBefore.AddDays(30));
    }

    internal static Dictionary<string, byte[]> CreateFiles(WorkspaceMarker marker, string directory,
        X509Certificate2 authority, DateTimeOffset now)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        try
        {
            PopulateFiles(result, marker, directory, authority, now);
            ValidateFiles(result, marker, now);
            return result;
        }
        catch
        {
            foreach (byte[] value in result.Values)
            {
                CryptographicOperations.ZeroMemory(value);
            }
            throw;
        }
    }

    private static void PopulateFiles(Dictionary<string, byte[]> result, WorkspaceMarker marker, string directory,
        X509Certificate2 authority, DateTimeOffset now)
    {
        result.Add("ca.cert.pem", Encode(authority.ExportCertificatePem()));
        using (RSA key = authority.GetRSAPrivateKey() ?? throw new DevelopmentToolException("The owned CA requires its matching RSA private key."))
        {
            result.Add("ca.key.pem", EncodePrivateKey(key));
        }
        AddLeaf(result, "server", authority, now, identity: null);
        AddLeaf(result, "service", authority, now, Identity(marker.ServiceApplication));
        AddLeaf(result, "caller", authority, now, Identity(marker.CallerApplication));
        var configuration = new
        {
            version = 1,
            workspaceId = marker.WorkspaceId,
            environment = "Development",
            profile = "local-development",
            trustDomain = TrustDomain(marker.WorkspaceId),
            caCertificateFile = PrivateWorkspaceFiles.Child(directory, "ca.cert.pem"),
            serverCertificateFile = PrivateWorkspaceFiles.Child(directory, "server.cert.pem"),
            serverPrivateKeyFile = PrivateWorkspaceFiles.Child(directory, "server.key.pem"),
            businessPort = 7443,
            operationalPort = 7080,
            allowedOrigins = new[] { "https://localhost:7443" },
            serviceApplication = marker.ServiceApplication,
            callerApplication = marker.CallerApplication,
            applications = new[]
            {
                Application(marker.ServiceApplication, "service", directory),
                Application(marker.CallerApplication, "caller", directory)
            },
            grants = new[]
            {
                new
                {
                    subjectApplication = marker.CallerApplication,
                    capability = "country.read",
                    targetApplication = marker.ServiceApplication,
                    targetEnvironment = "Development"
                }
            }
        };
        result.Add("configuration.json", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(configuration, WorkspaceCredentials.JsonOptions));
    }

    internal static X509Certificate2 LoadAuthority(IReadOnlyDictionary<string, byte[]> files)
    {
        X509Certificate2 authority = LoadCertificate(files, "ca");
        try
        {
            using RSA? publicKey = authority.GetRSAPublicKey();
            if (!authority.HasPrivateKey || publicKey is null)
            {
                throw new DevelopmentToolException("The owned authority must have a matching RSA key.");
            }
            return authority;
        }
        catch
        {
            authority.Dispose();
            throw;
        }
    }

    internal static string Identity(string application)
        => "https://identity.cislib.local/applications/" + application + "/environments/Development";

    internal static string TrustDomain(string workspaceId) => "cislib-local-development:" + workspaceId;

    private static object Application(string application, string prefix, string directory) => new
    {
        application,
        identity = Identity(application),
        certificateFile = PrivateWorkspaceFiles.Child(directory, prefix + ".cert.pem"),
        privateKeyFile = PrivateWorkspaceFiles.Child(directory, prefix + ".key.pem")
    };

    private static void AddLeaf(Dictionary<string, byte[]> files, string prefix, X509Certificate2 authority,
        DateTimeOffset now, string? identity)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=CIS local " + prefix + " " + Guid.NewGuid().ToString("N"),
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority,
            includeKeyIdentifier: true, includeIssuerAndSerial: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(identity is null ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        if (identity is null)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            names.AddIpAddress(IPAddress.IPv6Loopback);
            request.CertificateExtensions.Add(names.Build());
        }
        else
        {
            var names = new AsnWriter(AsnEncodingRules.DER);
            names.PushSequence();
            names.WriteCharacterString(UniversalTagNumber.IA5String, identity, new Asn1Tag(TagClass.ContextSpecific, 6));
            names.PopSequence();
            request.CertificateExtensions.Add(new X509Extension("2.5.29.17", names.Encode(), false));
        }
        DateTimeOffset notBefore = WholeSecond(now).AddMinutes(-5);
        DateTimeOffset issuerStart = authority.NotBefore.ToUniversalTime();
        if (notBefore < issuerStart)
        {
            notBefore = issuerStart;
        }
        DateTimeOffset notAfter = notBefore.AddDays(7);
        DateTimeOffset issuerExpiry = authority.NotAfter.ToUniversalTime();
        if (notAfter > issuerExpiry)
        {
            notAfter = issuerExpiry;
        }
        if (notAfter <= now.AddMinutes(1))
        {
            throw new DevelopmentToolException("The local CA has too little remaining validity. Use deliberate reset and restart the applications to adopt its new trust.");
        }
        byte[] serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7f;
        serial[^1] |= 1;
        using X509Certificate2 issued = request.Create(authority, notBefore, notAfter, serial);
        using X509Certificate2 leaf = issued.CopyWithPrivateKey(key);
        files.Add(prefix + ".cert.pem", Encode(leaf.ExportCertificatePem()));
        files.Add(prefix + ".key.pem", EncodePrivateKey(key));
    }

    internal static void ValidateFiles(IReadOnlyDictionary<string, byte[]> files, WorkspaceMarker marker, DateTimeOffset now,
        bool historical = false)
    {
        using X509Certificate2 authority = LoadAuthority(files);
        var trust = new Certificates.CertificateTrustPolicy(TrustDomain(marker.WorkspaceId), [authority], [authority],
            revocation: Certificates.CertificateRevocationPolicy.Development);
        foreach ((string prefix, string application) in new[] { ("service", marker.ServiceApplication), ("caller", marker.CallerApplication) })
        {
            using X509Certificate2 leaf = LoadCertificate(files, prefix);
            DateTimeOffset checkedAt = historical ? leaf.NotBefore.ToUniversalTime().AddSeconds(1) : now;
            Certificates.CertificateValidationResult validated = Certificates.CertificateTrustValidator.Validate(leaf, trust, checkedAt);
            if (!validated.Succeeded || validated.Subject?.Identifier != Identity(application)
                || leaf.NotAfter.ToUniversalTime() - leaf.NotBefore.ToUniversalTime() > TimeSpan.FromDays(7)
                || leaf.NotBefore.ToUniversalTime() < authority.NotBefore.ToUniversalTime()
                || leaf.NotAfter.ToUniversalTime() > authority.NotAfter.ToUniversalTime())
            {
                throw new DevelopmentToolException("Generated application material did not satisfy the actual certificate adapter.");
            }
        }
        using X509Certificate2 server = LoadCertificate(files, "server");
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        chain.ChainPolicy.VerificationTime = historical ? server.NotBefore.ToUniversalTime().AddSeconds(1) : now.UtcDateTime;
        try
        {
            if (!chain.Build(server))
            {
                throw new DevelopmentToolException("Generated server material did not satisfy native server usage and explicit trust.");
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

    private static byte[] Encode(string value) => Encoding.UTF8.GetBytes(value + "\n");

    private static byte[] EncodePrivateKey(RSA key)
    {
        byte[] exported = key.ExportPkcs8PrivateKey();
        char[]? pem = null;
        try
        {
            pem = PemEncoding.Write("PRIVATE KEY", exported);
            return Encoding.UTF8.GetBytes(pem);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exported);
            if (pem is not null)
            {
                Array.Clear(pem);
            }
        }
    }

    private static X509Certificate2 LoadCertificate(IReadOnlyDictionary<string, byte[]> files, string prefix)
    {
        char[] key = Encoding.UTF8.GetChars(files[prefix + ".key.pem"]);
        try
        {
            return X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(files[prefix + ".cert.pem"]), key);
        }
        finally
        {
            Array.Clear(key);
        }
    }

    private static DateTimeOffset WholeSecond(DateTimeOffset value) => DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());
}
