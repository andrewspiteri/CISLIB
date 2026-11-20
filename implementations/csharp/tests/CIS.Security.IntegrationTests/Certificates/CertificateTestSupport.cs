using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CIS.CommonTesting;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Certificates;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Hosting;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.Security.Certificates;

internal sealed class CertificateFixture : IDisposable
{
    internal const string Domain = "fixture-client-issuer";
    internal const string Identity = "https://identity.example.test/applications/caller/environments/testing";
    internal const string DeniedIdentity = "https://identity.example.test/applications/denied/environments/testing";
    internal const string Profile = "mtls";
    internal const string Scheme = "fixture-client-certificates";
    internal static readonly CapabilityTarget Target = new("receiver", "Testing");
    private readonly List<X509Certificate2> owned = [];
    private readonly List<RestHttpNativeCertificate> nativeOwners = [];
    private X509Certificate2? server;

    internal CertificateFixture()
    {
        Now = DateTimeOffset.UtcNow;
        Root = Authority("Fixture client root");
        ServerRoot = Authority("Separate fixture server root");
    }

    internal DateTimeOffset Now
    {
        get;
    }
    internal X509Certificate2 Root
    {
        get;
    }
    internal X509Certificate2 ServerRoot
    {
        get;
    }
    internal X509Certificate2 Server
    {
        get => server ??= PrepareNative(Leaf(ServerRoot, null, server: true)).Certificate;
    }

    internal X509Certificate2 Authority(string name, X509Certificate2? issuer = null, bool uniqueName = true)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name + (uniqueName ? "-" + Guid.NewGuid().ToString("N") : ""),
            key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 3, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        X509Certificate2 result;
        if (issuer is null)
        {
            result = request.CreateSelfSigned(Now.AddDays(-1), Now.AddDays(30));
        }
        else
        {
            request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                issuer, includeKeyIdentifier: true, includeIssuerAndSerial: true));
            using X509Certificate2 publicCertificate = request.Create(issuer, Now.AddHours(-1), Now.AddDays(20), Serial());
            result = publicCertificate.CopyWithPrivateKey(key);
        }
        owned.Add(result);
        return result;
    }

    internal X509Certificate2 Leaf(X509Certificate2 issuer, string? identity = Identity, bool server = false,
        DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, bool ca = false,
        bool clientEku = true, X509KeyUsageFlags usage = X509KeyUsageFlags.DigitalSignature, string? secondIdentity = null)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=not-an-identity-" + Guid.NewGuid().ToString("N"), key,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            issuer, includeKeyIdentifier: true, includeIssuerAndSerial: true));
        if (server || clientEku)
        {
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
                new OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, true));
        }
        if (server)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }
        else if (identity is not null)
        {
            // Encode raw IA5String directly; Uri normalization must not create a mapped identity.
            var names = new AsnWriter(AsnEncodingRules.DER);
            names.PushSequence();
            names.WriteCharacterString(UniversalTagNumber.IA5String, identity, new Asn1Tag(TagClass.ContextSpecific, 6));
            if (secondIdentity is not null)
            {
                names.WriteCharacterString(UniversalTagNumber.IA5String, secondIdentity, new Asn1Tag(TagClass.ContextSpecific, 6));
            }
            names.PopSequence();
            request.CertificateExtensions.Add(new X509Extension("2.5.29.17", names.Encode(), false));
        }
        using X509Certificate2 publicCertificate = request.Create(issuer, notBefore ?? Now.AddMinutes(-5),
            notAfter ?? Now.AddHours(12), Serial());
        X509Certificate2 result = publicCertificate.CopyWithPrivateKey(key);
        owned.Add(result);
        return result;
    }

    internal CertificateTrustPolicy Trust(CertificateRevocationPolicy revocation = CertificateRevocationPolicy.ShortLived,
        X509Certificate2? issuer = null, IReadOnlyList<X509Certificate2>? intermediates = null)
        => new(Domain, [Root], [issuer ?? Root], intermediates, revocation);

    internal static SecurityOptions CommonPolicy(SecurityProfileKind kind = SecurityProfileKind.ClientCertificate)
    {
        var options = new SecurityOptions { PolicyVersion = "certificate-receiving-fixture" };
        options.Profiles.Add(new(Profile, kind, SecurityProfileRoles.Inbound, Scheme,
            [new(new(Domain, Identity), "caller", "Testing"), new(new(Domain, DeniedIdentity), "denied", "Testing")]));
        options.Capabilities.Add("country.read");
        options.Grants.Add(new(Profile, "country.read", Target.Application, Target.Environment, subject: new(Domain, Identity)));
        return options;
    }

    internal static void Register(IServiceCollection services, CertificateInboundOptions receiver,
        ManualTimeProvider? clock = null, SecurityProfileKind kind = SecurityProfileKind.ClientCertificate)
    {
        services.AddLogging();
        if (clock is not null)
        {
            services.AddSingleton<TimeProvider>(clock);
        }
        services.AddCisSecurity(CommonPolicy(kind));
        services.AddCisCertificateAuthentication(Profile, Scheme, receiver);
    }

    internal HttpClient Client(X509Certificate2? leaf, int port)
    {
        X509Certificate2? nativeLeaf = leaf is null ? null : PrepareNative(leaf).Certificate;
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true
        };
        policy.CustomTrustStore.Add(ServerRoot);
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = policy,
                ClientCertificates = nativeLeaf is null ? null : new X509CertificateCollection { nativeLeaf }
            }
        };
        return new(handler)
        {
            BaseAddress = new Uri($"https://localhost:{port}"),
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        // Test declarations dispose clients and stop Kestrel before the fixture. Keep
        // native user-key owners until that actual TLS/pool ownership has settled.
        foreach (RestHttpNativeCertificate owner in nativeOwners)
        {
            try
            {
                owner.Dispose();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        foreach (X509Certificate2 certificate in owned)
        {
            try
            {
                certificate.Dispose();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        if (failures.Count != 0)
        {
            throw new AggregateException("Native certificate fixture cleanup failed.", failures);
        }
    }

    private RestHttpNativeCertificate PrepareNative(X509Certificate2 leaf)
    {
        // The raw fixture certificate remains available for chain-only assertions.
        // Transfer an independent memory clone to the explicit native TLS owner.
        using X509Certificate2 publicLeaf = X509CertificateLoader.LoadCertificate(leaf.RawDataMemory.Span);
        using RSA key = leaf.GetRSAPrivateKey() ?? throw new InvalidOperationException("The generated TLS fixture leaf requires its RSA key.");
        var owner = new RestHttpNativeCertificate(publicLeaf.CopyWithPrivateKey(key));
        try
        {
            owner.PrepareForTls(SecurityTestAssemblySetup.NativeKeys);
            nativeOwners.Add(owner);
            return owner;
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

    private static byte[] Serial()
    {
        byte[] result = RandomNumberGenerator.GetBytes(16);
        result[0] &= 0x7f;
        result[^1] |= 1;
        return result;
    }
}

internal sealed class CertificateTestServer : IAsyncDisposable
{
    private readonly WebApplication application;
    private int effects;

    private CertificateTestServer(WebApplication application, int businessPort, int operationalPort)
    {
        this.application = application;
        BusinessPort = businessPort;
        OperationalPort = operationalPort;
    }

    internal int BusinessPort
    {
        get;
    }
    internal int OperationalPort
    {
        get;
    }
    internal int Effects => Volatile.Read(ref effects);
    internal string? LastConnection
    {
        get; private set;
    }
    internal string? LastObservedConnection
    {
        get; private set;
    }
    internal CallerContext? LastCaller
    {
        get; private set;
    }
    internal IServiceProvider Services => application.Services;

    internal static async Task<CertificateTestServer> StartAsync(CertificateFixture certificates,
        CertificateTrustPolicy? trust = null, ManualTimeProvider? clock = null,
        CertificateDevelopmentGate? development = null, string hostEnvironment = "Production", RequestDelegate? business = null)
    {
        using var businessReservation = Reserve();
        using var operationalReservation = Reserve();
        int businessPort = ((IPEndPoint)businessReservation.LocalEndPoint!).Port;
        int operationalPort = ((IPEndPoint)operationalReservation.LocalEndPoint!).Port;
        CertificateTrustPolicy selectedTrust = trust ?? certificates.Trust();
        var listeners = new CertificateListenerPolicy([new(IPAddress.Loopback, businessPort, CertificateFixture.Profile)],
            [new(IPAddress.Loopback, operationalPort)]);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = hostEnvironment });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, businessPort, listener => listener.UseHttps(https =>
            {
                https.ServerCertificate = certificates.Server;
                https.UseCisClientCertificateTrust(selectedTrust, clock);
            }));
            options.Listen(IPAddress.Loopback, operationalPort);
        });
        CertificateFixture.Register(builder.Services, new(selectedTrust, development), clock,
            development is null ? SecurityProfileKind.ClientCertificate : SecurityProfileKind.LocalDevelopment);
        builder.Services.AddCisSecurityWeb(options => options.SelectProfile = listeners.SelectProfile);
        builder.Services.AddCisStartup();
        WebApplication application = builder.Build();
        var result = new CertificateTestServer(application, businessPort, operationalPort);
        application.UseRouting();
        application.Use((context, next) =>
        {
            if (context.Request.Path == "/business")
            {
                result.LastObservedConnection = context.Connection.Id;
            }
            return next(context);
        });
        application.UseCisCertificateListenerIsolation(listeners);
        application.UseCisSecurityAuthentication();
        application.UseCisSecurityAuthorization();
        application.MapGet("/_health", () => Results.Ok());
        application.MapGet("/business", async (HttpContext context) =>
        {
            Interlocked.Increment(ref result.effects);
            result.LastConnection = context.Connection.Id;
            result.LastCaller = context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current;
            if (business is not null)
            {
                await business(context);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
            }
        }).RequireCisCapabilities(CertificateFixture.Profile, CertificateFixture.Target, "country.read");
        application.MapGet("/_health/ready", () =>
        {
            Interlocked.Increment(ref result.effects);
            return Results.Ok();
        }).RequireCisCapabilities(CertificateFixture.Profile, CertificateFixture.Target, "country.read");
        businessReservation.Dispose();
        operationalReservation.Dispose();
        try
        {
            await application.StartAsync();
            return result;
        }
        catch
        {
            await application.StopAsync();
            await application.DisposeAsync();
            throw;
        }
    }

    internal static Socket Reserve()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return socket;
    }

    public async ValueTask DisposeAsync()
    {
        await application.StopAsync();
        await application.DisposeAsync();
    }
}
