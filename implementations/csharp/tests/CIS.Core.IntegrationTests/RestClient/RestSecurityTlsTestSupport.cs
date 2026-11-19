using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.IntegrationTests.RestClient;

// Native loopback TLS for the existing REST ownership tests. No machine trust store,
// provider identity mapping, test server abstraction or alternate HTTP sender is installed.
internal sealed class RestSecurityTlsFixture : IAsyncDisposable
{
    private WebApplication? application;
    private X509Certificate2 clientAuthority = null!;
    private X509Certificate2 serverAuthority = null!;
    private X509Certificate2 serverCertificate = null!;
    private RestHttpNativeCertificate? serverOwner;
    private RestHttpNativeKeyScope keyScope = null!;
    private bool disposed;

    internal string Address => application!.Urls.Single();
    internal X509Certificate2 ServerAuthority => serverAuthority;
    internal X509Certificate2 ClientAuthority => clientAuthority;

    internal static async Task<RestSecurityTlsFixture> StartAsync(RequestDelegate handler,
        bool requireClientCertificate = true, bool wrongServerName = false,
        RestHttpNativeKeyScope? keyScope = null)
    {
        var fixture = new RestSecurityTlsFixture();
        try
        {
            string authoritySuffix = Guid.NewGuid().ToString("N");
            fixture.clientAuthority = Authority("REST client fixture CA " + authoritySuffix);
            fixture.serverAuthority = Authority("REST server fixture CA " + authoritySuffix);
            fixture.keyScope = keyScope ?? RestSecurityNativeKeyCustody.Shared;
            fixture.serverOwner = new(fixture.ServerLeaf(wrongServerName));
            fixture.serverOwner.PrepareForTls(fixture.keyScope);
            fixture.serverCertificate = fixture.serverOwner.Certificate;
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                EnvironmentName = "Production"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listener =>
                listener.UseHttps(options =>
                {
                    options.ServerCertificate = fixture.serverCertificate;
                    options.ClientCertificateMode = requireClientCertificate ? ClientCertificateMode.RequireCertificate
                        : ClientCertificateMode.NoCertificate;
                    if (requireClientCertificate)
                    {
                        options.ClientCertificateValidation = (certificate, _, _) => fixture.ValidateClient(certificate);
                    }
                })));
            fixture.application = builder.Build();
            fixture.application.Run(handler);
            await fixture.application.StartAsync().WaitAsync(ResilienceTestSupport.Deadline);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal RestSecurityBinding Binding() => new("certificate-fixture",
        new SecuritySubject("fixture-issuer", "fixture-client"), "fixture-policy",
        RestSecurityCredentialKind.ClientCertificate, [Address]);

    internal X509Certificate2 ClientLeaf(string identifier, DateTimeOffset? expiresAtUtc = null)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + identifier, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            clientAuthority, includeKeyIdentifier: true, includeIssuerAndSerial: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddUri(new Uri("https://identity.test/clients/" + identifier));
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 issued = request.Create(clientAuthority, DateTimeOffset.UtcNow.AddMinutes(-1),
            expiresAtUtc ?? DateTimeOffset.UtcNow.AddHours(2), RandomNumberGenerator.GetBytes(16));
        return issued.CopyWithPrivateKey(key);
    }

    internal RestHttpCertificateMaterial PrepareMaterial(X509Certificate2 leaf)
    {
        var owner = new RestHttpNativeCertificate(leaf);
        try
        {
            owner.PrepareForTls(keyScope);
            return RestHttpCertificateMaterial.FromNativeCertificate(owner);
        }
        catch (Exception original)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
            throw;
        }
    }

    private X509Certificate2 ServerLeaf(bool wrongName)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=REST server fixture", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            serverAuthority, includeKeyIdentifier: true, includeIssuerAndSerial: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        if (wrongName)
        {
            names.AddDnsName("wrong.invalid");
        }
        else
        {
            names.AddIpAddress(IPAddress.Loopback);
        }
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 issued = request.Create(serverAuthority, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(2), RandomNumberGenerator.GetBytes(16));
        return issued.CopyWithPrivateKey(key);
    }

    private bool ValidateClient(X509Certificate2 certificate)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(clientAuthority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
        return chain.Build(certificate);
    }

    private static X509Certificate2 Authority(string name)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        try
        {
            if (application is not null)
            {
                try
                {
                    await application.StopAsync().WaitAsync(ResilienceTestSupport.Deadline);
                }
                finally { await application.DisposeAsync(); }
            }
        }
        finally
        {
            try
            {
                serverOwner?.Dispose();
            }
            finally
            {
                serverAuthority?.Dispose();
                clientAuthority?.Dispose();
            }
        }
    }
}
