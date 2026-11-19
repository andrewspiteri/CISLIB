using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Runtime.ExceptionServices;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using CIS;
using CIS.CommonTesting;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Security.Hosting;
using CIS.Startup.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CIS.Tests.Security.Entra;

// A native host and borrowed SDK transport, not a federation server or production credential provider.
internal sealed class EntraWorkloadFixture : IAsyncDisposable
{
    private readonly string assertionFile;
    private readonly IHost host;
    private RestHttpTransport? transport;

    private EntraWorkloadFixture(string origin, Func<IServiceProvider, WorkloadIdentityCredential>? borrowed,
        HttpPipelineTransport? sdkTransport, RestHttpServerTrust? serverTrust, bool required, TimeProvider? clock,
        TimeSpan? statusCheckInterval)
    {
        assertionFile = Path.Combine(Path.GetTempPath(), "cis-entra-assertion-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(assertionFile, "fixture.projected.assertion");
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Development
        });
        LocalApplicationIdentity local = LocalApplicationIdentity.Capture();
        var policy = new SecurityOptions { PolicyVersion = "entra-outbound-fixture-v1" };
        policy.Profiles.Add(new(EntraNativeAuthenticationTests.Profile, SecurityProfileKind.EntraWorkloadIdentity,
            SecurityProfileRoles.Outbound, outboundIdentity: new(local.Application, local.Environment,
                new(EntraNativeAuthenticationTests.Issuer, EntraNativeAuthenticationTests.ObjectId)),
            allowedOrigins: [origin], requiredForReadiness: required));
        Health = new HealthcheckService(new(), timeProvider: clock);
        builder.Services.AddSingleton(Health);
        builder.Services.AddSingleton<IHealthMonitorFactory>(Health);
        builder.Services.AddSingleton<IHeartbeatOwnerFactory>(Heartbeat);
        if (clock is not null)
        {
            builder.Services.AddSingleton(clock);
        }
        builder.Services.AddCisSecurity(policy);
        builder.Services.AddCisEntraSecurity(EntraNativeAuthenticationTests.Profile, _ => new()
        {
            TenantId = EntraNativeAuthenticationTests.Tenant,
            Outbound = new()
            {
                ClientId = EntraNativeAuthenticationTests.ClientId,
                ServicePrincipalObjectId = EntraNativeAuthenticationTests.ObjectId,
                FederatedSubject = "system:serviceaccount:country-demo:country-worker",
                FederatedTokenFile = assertionFile,
                Scope = "api://country-api/.default",
                Transport = sdkTransport,
                StatusCheckInterval = statusCheckInterval ?? TimeSpan.FromSeconds(1)
            }
        }, borrowedCredential: borrowed);
        var rest = new RestClientOptions("protected-api") { BaseUrl = origin, LogCalls = false };
        builder.Services.AddCisEntraRestClient(rest, EntraNativeAuthenticationTests.Profile,
            serverTrust is null ? null : (provider, binding) => transport = RestHttpTransport.CreateSecurityBound(
                binding, provider.GetRequiredService<IMeterFactory>(), serverTrust: serverTrust));
        builder.Services.AddCisStartup();
        host = builder.Build();
    }

    internal HealthcheckService Health
    {
        get;
    }
    internal ManualHeartbeat Heartbeat { get; } = new(capacity: 8);
    internal IServiceProvider Services => host.Services;
    internal IRestClient Client => host.Services.GetRequiredKeyedService<IRestClient>("protected-api");

    internal static async Task<EntraWorkloadFixture> StartAsync(string origin = "https://provider.invalid",
        Func<IServiceProvider, WorkloadIdentityCredential>? borrowed = null,
        HttpPipelineTransport? sdkTransport = null, RestHttpServerTrust? serverTrust = null, bool required = false,
        TimeProvider? clock = null, TimeSpan? statusCheckInterval = null)
    {
        var fixture = new EntraWorkloadFixture(origin, borrowed, sdkTransport, serverTrust, required, clock, statusCheckInterval);
        try
        {
            await fixture.host.StartAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal Task StopAsync() => host.StopAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        ExceptionDispatchInfo? fault = null;
        try
        {
            await host.StopAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            fault = ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            if (host is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            if (transport is not null)
            {
                await transport.DisposeAsync();
            }
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            await Heartbeat.DisposeAsync();
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
        }
        try
        {
            await Health.DisposeAsync();
        }
        catch (Exception error)
        {
            fault ??= ExceptionDispatchInfo.Capture(error);
        }
        File.Delete(assertionFile);
        fault?.Throw();
    }
}

internal sealed class ControlledWorkloadCredential : WorkloadIdentityCredential, IDisposable
{
    private readonly Func<CancellationToken, ValueTask<AccessToken>> acquire;

    internal ControlledWorkloadCredential(Func<CancellationToken, ValueTask<AccessToken>> acquire)
        : base(new WorkloadIdentityCredentialOptions
        {
            TenantId = EntraNativeAuthenticationTests.Tenant,
            ClientId = EntraNativeAuthenticationTests.ClientId,
            TokenFilePath = Path.Combine(Path.GetTempPath(), "unused-borrowed-assertion")
        })
    {
        this.acquire = acquire;
    }

    internal int Calls
    {
        get; private set;
    }
    internal int Disposals
    {
        get; private set;
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken = default)
    {
        Calls++;
        Assert.AreEqual("api://country-api/.default", requestContext.Scopes.Single());
        return acquire(cancellationToken);
    }

    public void Dispose() => Disposals++;
}

internal sealed class WorkloadSdkHandler : HttpMessageHandler
{
    internal int TokenRequests
    {
        get; private set;
    }
    internal string? LastBody
    {
        get; private set;
    }
    internal string? Error
    {
        get; set;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.AreEqual("login.microsoftonline.com", request.RequestUri!.Host);
        string json;
        HttpStatusCode status = HttpStatusCode.OK;
        if (request.RequestUri.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
        {
            TokenRequests++;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Error is { } error)
            {
                status = HttpStatusCode.BadRequest;
                json = "{\"error\":\"" + error + "\",\"error_description\":\"raw-provider-secret-that-must-not-escape\"}";
            }
            else
            {
                json = "{\"access_token\":\"fixture-access-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}";
            }
        }
        else if (request.RequestUri.AbsolutePath.EndsWith("/discovery/instance", StringComparison.Ordinal))
        {
            json = "{\"tenant_discovery_endpoint\":\"" + EntraNativeAuthenticationTests.Issuer
                + "/.well-known/openid-configuration\",\"metadata\":[{\"preferred_network\":\"login.microsoftonline.com\","
                + "\"preferred_cache\":\"login.microsoftonline.com\",\"aliases\":[\"login.microsoftonline.com\"]}]}";
        }
        else
        {
            json = "{\"issuer\":\"" + EntraNativeAuthenticationTests.Issuer + "\",\"token_endpoint\":\"https://login.microsoftonline.com/"
                + EntraNativeAuthenticationTests.Tenant + "/oauth2/v2.0/token\",\"authorization_endpoint\":\"https://login.microsoftonline.com/"
                + EntraNativeAuthenticationTests.Tenant + "/oauth2/v2.0/authorize\",\"jwks_uri\":\"https://login.microsoftonline.com/discovery/v2.0/keys\"}";
        }
        return new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class EntraTlsReceiver : IAsyncDisposable
{
    private readonly WebApplication application;
    private readonly X509Certificate2 authority;
    private readonly RestHttpNativeCertificate server;

    private EntraTlsReceiver(WebApplication application, X509Certificate2 authority, RestHttpNativeCertificate server)
    {
        this.application = application;
        this.authority = authority;
        this.server = server;
    }

    internal string Origin { get; private set; } = "";
    internal string? Authorization
    {
        get; private set;
    }
    internal int Calls
    {
        get; private set;
    }
    internal RestHttpServerTrust ServerTrust => new([authority], X509RevocationMode.NoCheck);

    internal static async Task<EntraTlsReceiver> StartAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=CIS Entra TLS Fixture Root " + Guid.NewGuid().ToString("N"),
            rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        X509Certificate2 authority = rootRequest.CreateSelfSigned(now.AddMinutes(-1), now.AddHours(2));
        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        serverRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(serverRequest.PublicKey, false));
        serverRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
            authority, includeKeyIdentifier: true, includeIssuerAndSerial: true));
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using X509Certificate2 publicServer = serverRequest.Create(authority, now.AddMinutes(-1), now.AddHours(1), RandomNumberGenerator.GetBytes(16));
        var server = new RestHttpNativeCertificate(publicServer.CopyWithPrivateKey(serverKey));
        WebApplication application;
        try
        {
            server.PrepareForTls(SecurityTestAssemblySetup.NativeKeys);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0,
                listener => listener.UseHttps(server.Certificate)));
            application = builder.Build();
        }
        catch
        {
            server.Dispose();
            authority.Dispose();
            throw;
        }
        var receiver = new EntraTlsReceiver(application, authority, server);
        application.MapGet("/value", (HttpContext context) =>
        {
            receiver.Calls++;
            receiver.Authorization = context.Request.Headers.Authorization;
            return Results.Json(new
            {
                value = 7
            });
        });
        try
        {
            await application.StartAsync();
            receiver.Origin = application.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            return receiver;
        }
        catch
        {
            await receiver.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            try
            {
                await application.StopAsync(CancellationToken.None);
            }
            finally
            {
                await application.DisposeAsync();
            }
        }
        finally
        {
            try
            {
                server.Dispose();
            }
            finally
            {
                authority.Dispose();
            }
        }
    }
}
