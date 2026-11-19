using System.Net;
using System.Reflection;
using System.Text.Json;
using CIS.Configuration;
using CIS.Hosting;
using CIS.Security;
using CIS.Security.Certificates.Hosting;
using CIS.Security.Hosting;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
public sealed class CertificateFacadeTests
{
    [TestMethod]
    public async Task RealFacadeReceiverNeedsNoOutgoingLeafAndKeepsOperationalListenerSeparateFromBusinessTls()
    {
        using var certificates = new CertificateFixture();
        using var inventory = new FacadeInventory();
        using var businessReservation = CertificateTestServer.Reserve();
        using var probeReservation = CertificateTestServer.Reserve();
        int businessPort = ((IPEndPoint)businessReservation.LocalEndPoint!).Port;
        int probePort = ((IPEndPoint)probeReservation.LocalEndPoint!).Port;
        var trust = certificates.Trust();
        var listeners = new CertificateListenerPolicy([new(IPAddress.Loopback, businessPort, CertificateFixture.Profile)],
            [new(IPAddress.Loopback, probePort)]);
        int effects = 0;
        CallerContext? caller = null;
        await using CISApplication application = CISApplication.CreateWeb([], builder =>
        {
            builder.TypeManager = inventory.Options;
            builder.EnableOptionalIntegrations = false;
            builder.LoggingEnabled = false;
            builder.HeartbeatEnabled = false;
            builder.Host.Logging.ClearProviders();
            builder.WebHost!.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, businessPort, listener => listener.UseHttps(https =>
                {
                    https.ServerCertificate = certificates.Server;
                    https.UseCisClientCertificateTrust(trust);
                }));
                options.Listen(IPAddress.Loopback, probePort);
            });
            CertificateFixture.Register(builder.Services, new(trust));
            builder.Services.AddCisSecurityWeb(options => options.SelectProfile = listeners.SelectProfile);
        }, web =>
        {
            // The facade already places operational health/readiness before business authorization.
            web.UseCisCertificateListenerIsolation(listeners);
            web.MapGet("/business", (HttpContext context) =>
            {
                Interlocked.Increment(ref effects);
                caller = context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current;
                return Results.Ok();
            }).RequireCisCapabilities(CertificateFixture.Profile, CertificateFixture.Target, "country.read");
        });
        businessReservation.Dispose();
        probeReservation.Dispose();
        await application.StartAsync();
        try
        {
            using var probe = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{probePort}"), Timeout = TimeSpan.FromSeconds(15) };
            using (HttpResponseMessage ready = await probe.GetAsync("/_health/ready"))
            {
                Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
            }
            using var spoofed = new HttpRequestMessage(HttpMethod.Get, "/business");
            spoofed.Headers.Host = $"localhost:{businessPort}";
            using (HttpResponseMessage forbidden = await probe.SendAsync(spoofed))
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }
            Assert.AreEqual(0, effects);
            using HttpClient business = certificates.Client(certificates.Leaf(certificates.Root), businessPort);
            using (HttpResponseMessage permitted = await business.GetAsync("/business"))
            {
                Assert.AreEqual(HttpStatusCode.OK, permitted.StatusCode);
            }
            Assert.AreEqual(1, effects);
            Assert.AreEqual(new SecuritySubject(CertificateFixture.Domain, CertificateFixture.Identity), caller?.Subject);
            Assert.AreEqual(SecurityProfileRoles.Inbound,
                application.Services.GetRequiredService<SecurityPolicySnapshot>().GetProfile(CertificateFixture.Profile).Roles);
        }
        finally
        {
            await application.StopAsync();
        }
    }

    private sealed class FacadeInventory : IDisposable
    {
        private readonly string directory;

        internal FacadeInventory()
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
            {
                root = root.Parent;
            }
            string repository = root?.FullName ?? throw new InvalidOperationException("The source fixture requires its repository root.");
            directory = Path.Combine(repository, ".artifacts", "security", "receiving-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Assembly assembly = typeof(CertificateFacadeTests).Assembly;
            File.Copy(assembly.Location, Path.Combine(directory, "Host.dll"));
            string manifest = Path.Combine(directory, "host.types.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                hostAssembly = assembly.FullName,
                assemblies = new[] { new { assemblyName = assembly.FullName, relativePath = "Host.dll" } }
            }));
            Options = new(assembly)
            {
                AssemblyManifestPath = manifest,
                PrecomputedContracts = [typeof(IConfigurationObject)]
            };
        }

        internal TypeManagerOptions Options
        {
            get;
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
