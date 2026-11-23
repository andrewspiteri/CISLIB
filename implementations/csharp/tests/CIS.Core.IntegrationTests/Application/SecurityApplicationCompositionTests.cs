using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CIS.Healthcheck;
using CIS.CommonTesting;
using CIS.Management;
using CIS.Management.Hosting;
using CIS.Hosting;
using CIS.Metrics.OpenTelemetry;
using CIS.Security;
using CIS.Security.Hosting;
using CIS.Startup;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
public sealed class SecurityApplicationCompositionTests
{
    private const string Profile = "fixture";
    private static readonly SecuritySubject Allowed = new("test-issuer", "allowed");
    private static readonly CapabilityTarget Target = new("server", "Testing");

    [TestMethod]
    public async Task ManagedReadinessClosesOnlyNewEffectsAfterNativeAuthorizationWhileStatusAndAdmittedHttpContinue()
    {
        using var directory = new TemporaryDirectory();
        var admission = new ManualManagedWorkAdmission();
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        var counters = new AuthenticationCounters();
        int effects = 0;
        await using CISApplication application = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, Inventory(directory, includeMetrics: false));
            ConfigureSecurity(builder.Services, counters);
            builder.Services.AddSingleton<RootWitness>();
            builder.Services.AddSingleton<IManagedWorkAdmission>(admission);
            builder.Services.AddSingleton<IHealthReadinessSource>(new AdmissionReadiness(admission));
        }, web =>
        {
            web.MapGet("/status", () => Results.Ok()).RequireCisCapabilities(Profile, Target, "country.read");
            web.MapPost("/effect", async (HttpContext context) =>
            {
                Interlocked.Increment(ref effects);
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
                return Results.Ok();
            }).RequireCisCapabilities(Profile, Target, "country.read").RequireCisManagedWork();
        });

        Task<HttpResponseMessage>? admitted = null;
        await application.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
        using HttpClient client = ApplicationTestSupport.Client(application);
        async Task<HttpResponseMessage> PostAsync(string? subject)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/effect");
            if (subject is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", subject);
            }
            return await client.SendAsync(request);
        }
        try
        {
            await ApplicationTestSupport.StatusAsync(client, "/_health", HttpStatusCode.OK);
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError);
            using (HttpResponseMessage status = await GetAsync(client, "/status", "allowed"))
            {
                Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
            }
            using (HttpResponseMessage anonymous = await PostAsync(null))
            {
                Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }
            using (HttpResponseMessage denied = await PostAsync("denied"))
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using (HttpResponseMessage closed = await PostAsync("allowed"))
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
                Assert.IsTrue(closed.Headers.CacheControl?.NoStore == true);
            }
            Assert.AreEqual(0, effects);
            admission.Set(new(ManagedWorkAdmissionState.Ready, true, 1));
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.OK);
            admitted = PostAsync("allowed");
            await entered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            admission.Set(new(ManagedWorkAdmissionState.ContinuingOnly, false, 2));
            Assert.IsFalse(admitted.IsCompleted);
            using (HttpResponseMessage next = await PostAsync("allowed"))
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, next.StatusCode);
            }
            await ApplicationTestSupport.StatusAsync(client, "/_health", HttpStatusCode.OK);
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError);
            using (HttpResponseMessage status = await GetAsync(client, "/status", "allowed"))
            {
                Assert.AreEqual(HttpStatusCode.OK, status.StatusCode);
            }
            release.TrySetResult();
            using HttpResponseMessage completed = await admitted.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(HttpStatusCode.OK, completed.StatusCode);
            Assert.AreEqual(1, effects);
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(admitted);
            await application.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    private sealed class AdmissionReadiness(IManagedWorkAdmission admission) : IHealthReadinessSource
    {
        public bool IsReady => admission.TryAdmit();
    }

    [TestMethod]
    public async Task NativeCallerProjectionPrecedesDetailedHealthAndReadinessPrecedesBusinessAuthorization()
    {
        using var directory = new TemporaryDirectory();
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        var counters = new AuthenticationCounters();
        CallerContext? detailsCaller = null;
        RootWitness? detailsWitness = null;
        IServiceProvider? mappedRoot = null;
        int constructions = 0;
        int businessCalls = 0;

        await using CISApplication application = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, Inventory(directory, includeMetrics: false));
            builder.Healthcheck = new HealthcheckOptions { DetailsEnabled = true };
            ConfigureSecurity(builder.Services, counters);
            builder.Services.AddSingleton(_ =>
            {
                Interlocked.Increment(ref constructions);
                return new RootWitness();
            });
            builder.Services.AddSingleton<IStartable>(new ApplicationTestSupport.Participant(async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }));
            builder.AuthorizeHealthDetails = context =>
            {
                CallerContext? caller = context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current;
                if (caller?.Subject != Allowed)
                {
                    return ValueTask.FromResult(false);
                }

                Assert.IsTrue(context.User.Identity?.IsAuthenticated == true);
                detailsCaller = caller;
                detailsWitness = context.RequestServices.GetRequiredService<RootWitness>();
                return ValueTask.FromResult(true);
            };
        }, web =>
        {
            mappedRoot = web.Services;
            web.MapGet("/business", (HttpContext context) =>
            {
                Assert.AreSame(mappedRoot!.GetRequiredService<RootWitness>(),
                    context.RequestServices.GetRequiredService<RootWitness>());
                CallerContext caller = context.RequestServices.GetRequiredService<ICallerContextAccessor>().Current
                    ?? throw new AssertFailedException("The protected body requires its trusted caller scope.");
                Interlocked.Increment(ref businessCalls);
                return Results.Text(caller.Subject.Identifier);
            }).RequireCisCapabilities(Profile, Target, "country.read");
        });

        Assert.AreEqual(0, constructions, "Collecting setup must not construct the root dependency.");
        Task startup = application.StartAsync();
        try
        {
            await AwaitParticipantOrStartupAsync(entered.Task, startup);
            Assert.AreSame(mappedRoot, application.Services);
            using HttpClient client = ApplicationTestSupport.Client(application);

            using (HttpResponseMessage pending = await GetAsync(client, "/business"))
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
            }
            using (HttpResponseMessage pending = await GetAsync(client, "/business", "denied"))
            {
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
            }
            Assert.AreEqual(0, counters.Challenges);
            Assert.AreEqual(0, counters.Forbids);
            Assert.AreEqual(0, businessCalls);

            using (HttpResponseMessage deniedDetails = await GetAsync(client, "/_health/details"))
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, deniedDetails.StatusCode);
            }
            using (HttpResponseMessage allowedDetails = await GetAsync(client, "/_health/details", "allowed"))
            {
                using JsonDocument report = JsonDocument.Parse(await allowedDetails.Content.ReadAsStringAsync());
                Assert.IsTrue(report.RootElement.TryGetProperty("application", out _),
                    "Authenticated details must reach the actual health report, even before business readiness.");
            }
            Assert.IsNotNull(detailsCaller);
            Assert.AreEqual(Allowed, detailsCaller?.Subject);
            Assert.AreSame(application.Services.GetRequiredService<RootWitness>(), detailsWitness);
            Assert.AreSame(detailsWitness, counters.Witness);
            Assert.AreEqual(1, constructions);

            release.TrySetResult();
            await startup.WaitAsync(ApplicationTestSupport.Deadline);

            using (HttpResponseMessage anonymous = await GetAsync(client, "/business"))
            {
                Assert.AreEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            }
            using (HttpResponseMessage forbidden = await GetAsync(client, "/business", "denied"))
            {
                Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }
            Assert.AreEqual(0, businessCalls, "Neither native challenge nor capability denial may execute the body.");
            using (HttpResponseMessage permitted = await GetAsync(client, "/business", "allowed"))
            {
                Assert.AreEqual(HttpStatusCode.OK, permitted.StatusCode);
                Assert.AreEqual("allowed", await permitted.Content.ReadAsStringAsync());
            }
            Assert.AreEqual(1, businessCalls);
            Assert.AreEqual(1, constructions);

            using IServiceScope independentScope = application.Services.CreateScope();
            Assert.IsNull(independentScope.ServiceProvider.GetRequiredService<ICallerContextAccessor>().Current);
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(startup);
            await application.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task OperationalMetricsAndProbesKeepTheirOwnAccessBeforeReadinessAndBusinessFallbackAuthorization()
    {
        using var directory = new TemporaryDirectory();
        var entered = ApplicationTestSupport.Signal();
        var release = ApplicationTestSupport.Signal();
        var counters = new AuthenticationCounters();
        int businessCalls = 0;

        await using CISApplication application = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, Inventory(directory, includeMetrics: true));
            builder.EnableOptionalIntegrations = true;
            builder.ConfigureCisMetrics(options =>
            {
                options.CollectCoreModules = false;
                options.CollectIncomingHttp = false;
                options.ScrapeCacheDuration = TimeSpan.Zero;
            });
            ConfigureSecurity(builder.Services, counters);
            builder.Services.AddSingleton<RootWitness>();
            builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder(Profile)
                .RequireAuthenticatedUser().Build());
            builder.Services.AddSingleton<IStartable>(new ApplicationTestSupport.Participant(async token =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
            }));
        }, web => web.MapGet("/business", () =>
        {
            Interlocked.Increment(ref businessCalls);
            return Results.Ok();
        }).RequireCisCapabilities(Profile, Target, "country.read"));

        Task startup = application.StartAsync();
        try
        {
            await AwaitParticipantOrStartupAsync(entered.Task, startup);
            using HttpClient client = ApplicationTestSupport.Client(application);
            await ApplicationTestSupport.StatusAsync(client, "/_metrics", HttpStatusCode.OK);
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.InternalServerError);
            await ApplicationTestSupport.StatusAsync(client, "/business", HttpStatusCode.ServiceUnavailable);
            Assert.AreEqual(0, counters.Challenges);
            Assert.AreEqual(0, businessCalls);

            release.TrySetResult();
            await startup.WaitAsync(ApplicationTestSupport.Deadline);
            await ApplicationTestSupport.StatusAsync(client, "/_metrics", HttpStatusCode.OK);
            await ApplicationTestSupport.StatusAsync(client, "/_health/ready", HttpStatusCode.OK);
            await ApplicationTestSupport.StatusAsync(client, "/business", HttpStatusCode.Unauthorized);
            Assert.AreEqual(0, businessCalls);
            Assert.AreEqual(1, counters.Challenges);
            Assert.AreEqual(1, application.Services.GetRequiredService<ITypeManager>()
                .GetImplementations<ICISApplicationIntegration>().Count);
        }
        finally
        {
            release.TrySetResult();
            await ApplicationTestSupport.SettleAsync(startup);
            await application.StopAsync().WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    private static void ConfigureSecurity(IServiceCollection services, AuthenticationCounters counters)
    {
        var options = new SecurityOptions { PolicyVersion = "facade-fixture" };
        options.Profiles.Add(new SecurityProfile(Profile, SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            authenticationScheme: Profile, identityMappings:
            [
                new SecurityIdentityMapping(Allowed, "caller", "Testing"),
                new SecurityIdentityMapping(new("test-issuer", "denied"), "caller", "Testing")
            ]));
        options.Capabilities.Add("country.read");
        options.Grants.Add(new(Profile, "country.read", Target.Application, Target.Environment, subject: Allowed));
        services.AddSingleton(counters);
        services.AddAuthenticationCore();
        new AuthenticationBuilder(services).AddScheme<AuthenticationSchemeOptions, FixtureAuthenticationHandler>(Profile, _ => { });
        services.AddCisSecurity(options);
        services.AddCisSecurityProjection<FixtureProjector>(Profile);
        // This single-listener fixture explicitly selects the same profile for operational and business requests.
        services.AddCisSecurityWeb(web => web.SelectProfile = _ => Profile);
    }

    private static TypeManagerOptions Inventory(TemporaryDirectory directory, bool includeMetrics)
    {
        Assembly host = typeof(CISApplication).Assembly;
        Assembly[] selected = includeMetrics ? [host, typeof(MetricsApplicationIntegration).Assembly] : [host];
        foreach (Assembly assembly in selected)
        {
            File.Copy(assembly.Location, Path.Combine(directory.Path, assembly.GetName().Name + ".dll"));
        }

        string manifest = Path.Combine(directory.Path, "security-application.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = selected.Select(assembly => new
            {
                assemblyName = assembly.FullName,
                relativePath = assembly.GetName().Name + ".dll"
            }).ToArray()
        }));
        return new(host)
        {
            AssemblyManifestPath = manifest,
            PrecomputedContracts = [typeof(ICISApplicationIntegration)]
        };
    }

    private static async Task AwaitParticipantOrStartupAsync(Task entered, Task startup)
    {
        Task completed = await Task.WhenAny(entered, startup).WaitAsync(ApplicationTestSupport.Deadline);
        if (ReferenceEquals(completed, startup))
        {
            await startup;
            Assert.Fail("Startup completed before the required held participant entered.");
        }

        await entered;
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? credential = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (credential is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        }

        return await client.SendAsync(request);
    }

    // These fixtures prove native facade ordering and scoped projection only.
    // JWT signature, certificate authentication and deployment proofs belong to adapter tests.
    private sealed class FixtureProjector : ISecurityIdentityProjector
    {
        public FixtureProjector()
        {
        }

        public ValueTask<SecurityProjectionResult> ProjectAsync(HttpContext context, AuthenticateResult authentication,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string identifier = authentication.Principal!.FindFirst(ClaimTypes.NameIdentifier)!.Value;
            return ValueTask.FromResult(SecurityProjectionResult.Authenticated(new(
                new SecuritySubject("test-issuer", identifier), SecurityActorKind.Application,
                new SecurityAuthenticationEvidence(DateTimeOffset.UtcNow), Profile)));
        }
    }

    private sealed class RootWitness
    {
        public RootWitness()
        {
        }
    }

    private sealed class AuthenticationCounters
    {
        internal int Challenges;
        internal int Forbids;
        internal RootWitness? Witness;
    }

    private sealed class FixtureAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly AuthenticationCounters counters;

        public FixtureAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder, AuthenticationCounters counters, RootWitness witness)
            : base(options, logger, encoder)
        {
            this.counters = counters;
            counters.Witness = witness;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string value = Request.Headers.Authorization.ToString();
            string? identifier = value switch
            {
                "Bearer allowed" => "allowed",
                "Bearer denied" => "denied",
                _ => null
            };
            if (identifier is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, identifier)], Profile));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Profile)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Interlocked.Increment(ref counters.Challenges);
            return base.HandleChallengeAsync(properties);
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Interlocked.Increment(ref counters.Forbids);
            return base.HandleForbiddenAsync(properties);
        }
    }
}
