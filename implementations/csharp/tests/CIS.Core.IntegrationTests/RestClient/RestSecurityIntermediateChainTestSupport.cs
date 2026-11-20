using System.Net;
using System.Net.Security;
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

internal sealed record RestIntermediateChainObservation(bool Accepted, bool ExpectedIssuerInChain,
    bool IssuerInNativeExtraStore);

// The generic peer uses the native TLS-provided chain, including its received ExtraStore.
// A fresh, no-AIA issuer per fixture prevents another case's issuer from supplying the missing link.
internal sealed class RestIntermediateChainFixture : IAsyncDisposable
{
    private readonly TaskCompletionSource<RestIntermediateChainObservation> observation =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private X509Certificate2 clientRoot = null!;
    private X509Certificate2 clientIssuer = null!;
    private X509Certificate2 serverAuthority = null!;
    private RestHttpNativeCertificate? serverOwner;
    private WebApplication? application;
    private int requests;

    internal string Address => application!.Urls.Single();
    internal X509Certificate2 ServerAuthority => serverAuthority;
    internal Task<RestIntermediateChainObservation> Observation => observation.Task;
    internal int Requests => Volatile.Read(ref requests);

    internal static async Task<RestIntermediateChainFixture> StartAsync(bool peerHasIssuer)
    {
        var fixture = new RestIntermediateChainFixture();
        try
        {
            string unique = Guid.NewGuid().ToString("N");
            fixture.clientRoot = Authority("client root " + unique);
            fixture.clientIssuer = Authority("client intermediate " + unique, fixture.clientRoot);
            fixture.serverAuthority = Authority("server root " + unique);
            fixture.serverOwner = new(Leaf("server " + unique, fixture.serverAuthority, client: false));
            fixture.serverOwner.PrepareForTls(RestSecurityNativeKeyCustody.Shared);

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = [],
                EnvironmentName = "Production"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0, listener =>
                listener.UseHttps(options =>
                {
                    options.ServerCertificate = fixture.serverOwner.Certificate;
                    options.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    options.OnAuthenticate = (_, native) =>
                    {
                        var policy = new X509ChainPolicy
                        {
                            TrustMode = X509ChainTrustMode.CustomRootTrust,
                            RevocationMode = X509RevocationMode.NoCheck,
                            DisableCertificateDownloads = true
                        };
                        policy.CustomTrustStore.Add(fixture.clientRoot);
                        policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2"));
                        if (peerHasIssuer)
                        {
                            policy.ExtraStore.Add(fixture.clientIssuer);
                        }
                        native.CertificateChainPolicy = policy;
                    };
                    options.ClientCertificateValidation = (_, chain, errors) => fixture.ValidatePeer(chain, errors);
                })));
            fixture.application = builder.Build();
            fixture.application.Run(async context =>
            {
                Interlocked.Increment(ref fixture.requests);
                await context.Response.WriteAsync("intermediate-issued client accepted", context.RequestAborted);
            });
            await fixture.application.StartAsync().WaitAsync(ResilienceTestSupport.Deadline);
            return fixture;
        }
        catch (Exception original)
        {
            try
            {
                await fixture.DisposeAsync();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    internal RestSecurityBinding Binding() => new("intermediate-fixture",
        new SecuritySubject("fixture-issuer", "fixture-client"), "fixture-policy",
        RestSecurityCredentialKind.ClientCertificate, [Address]);

    internal RestHttpCertificateMaterial PrepareMaterial()
    {
        var owner = new RestHttpNativeCertificate(Leaf("client " + Guid.NewGuid().ToString("N"), clientIssuer, client: true));
        try
        {
            owner.PrepareForTls(RestSecurityNativeKeyCustody.Shared);
            return RestHttpCertificateMaterial.FromNativeCertificate(owner, [clientIssuer]);
        }
        catch (Exception original)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(original, cleanup);
            }
            throw;
        }
    }

    private bool ValidatePeer(X509Chain? nativeChain, SslPolicyErrors errors)
    {
        bool expectedIssuer = nativeChain is not null && nativeChain.ChainElements.Count >= 2
            && nativeChain.ChainElements[1].Certificate.RawData.AsSpan().SequenceEqual(clientIssuer.RawData);
        bool receivedIssuer = nativeChain is not null && nativeChain.ChainPolicy.ExtraStore
            .Cast<X509Certificate2>().Any(certificate => certificate.RawData.AsSpan().SequenceEqual(clientIssuer.RawData));
        bool accepted = errors == SslPolicyErrors.None && expectedIssuer
            && nativeChain is not null && nativeChain.ChainStatus.Length == 0;
        observation.TrySetResult(new(accepted, expectedIssuer, receivedIssuer));
        return accepted;
    }

    private static X509Certificate2 Authority(string name, X509Certificate2? issuer = null)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        if (issuer is null)
        {
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        }
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            issuer, includeKeyIdentifier: true, includeIssuerAndSerial: true));
        using X509Certificate2 certificate = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-3),
            DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 Leaf(string name, X509Certificate2 issuer, bool client)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            issuer, includeKeyIdentifier: true, includeIssuerAndSerial: true));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1") }, true));
        var names = new SubjectAlternativeNameBuilder();
        if (client)
        {
            names.AddUri(new Uri("https://identity.test/clients/" + Guid.NewGuid().ToString("N")));
        }
        else
        {
            names.AddIpAddress(IPAddress.Loopback);
        }
        request.CertificateExtensions.Add(names.Build());
        using X509Certificate2 certificate = request.Create(issuer, DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(2), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        if (application is not null)
        {
            try
            {
                await application.StopAsync().WaitAsync(ResilienceTestSupport.Deadline);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
            try
            {
                await application.DisposeAsync();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        try
        {
            serverOwner?.Dispose();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        finally
        {
            serverAuthority?.Dispose();
            clientIssuer?.Dispose();
            clientRoot?.Dispose();
        }
        if (failures.Count != 0)
        {
            throw new AggregateException(failures);
        }
    }
}
