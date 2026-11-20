using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Examples.CountryData;
using CIS.Hosting;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient.Http;
using CIS.RestClient.Resilience;
using CIS.Security.Certificates.Hosting;
using CIS.Security;
using CIS.Security.Entra;
using CIS.Tests.Security.Certificates;
using CIS.Tests.Security.Development;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.CountryData;

[TestClass]
[DoNotParallelize]
public sealed class CountrySecurityConsumerTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ActualStartupRejectsOversizedServerPemBytesBeforeNativeKeyPreparation(bool privateKey)
    {
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup", "--service", EnvironmentOptions.ApplicationName,
            "--caller", "CountryData.NativeCaller"), workspace.Output);
        string oversizedName = privateKey ? "oversized-server.key.pem" : "oversized-server.cert.pem";
        workspace.WriteOwnedExtra(oversizedName, new string(' ', privateKey ? 65537 : 262145));
        JsonObject configuration = workspace.Configuration();
        configuration[privateKey ? "serverPrivateKeyFile" : "serverCertificateFile"] = Path.Combine(workspace.Credentials, oversizedName);
        string custody = Path.Combine(Path.GetTempPath(), "bounded-server-custody-" + Guid.NewGuid().ToString("N"));
        configuration["nativeKeyJournalDirectory"] = custody;
        workspace.WriteOwnedExtra("bounded-server.configuration.json", configuration.ToJsonString());
        using var inventory = new CountryInventory(workspace.Root);
        await using CISApplication application = CountryApplication.CreateService(
            ["--environment=Development", "--security=local", "--security-config=" + Path.Combine(workspace.Credentials, "bounded-server.configuration.json")], builder =>
            {
                builder.TypeManager = inventory.Options;
                builder.LoggingEnabled = false;
                builder.Host.Logging.ClearProviders();
            });
        Exception? failure = null;
        try
        {
            await application.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (Exception error) { failure = error; }
        Assert.IsNotNull(failure);
        Assert.IsTrue(HasBoundFailure(failure), "Startup must reject actual captured byte length before native PEM parsing or key import.");
        Assert.IsFalse(Directory.Exists(custody), "No Created key custody callback may run for rejected server bytes.");

        static bool HasBoundFailure(Exception error) => error is InvalidDataException
            || error is AggregateException aggregate && aggregate.InnerExceptions.Any(HasBoundFailure)
            || error.InnerException is { } inner && HasBoundFailure(inner);
    }

    [TestMethod]
    public async Task ActualFacadeAndSeparatelyIdentifiedCallerEnforceResourceDenialAndExposeNativeSecurityMetrics()
    {
        Assert.AreEqual("CIS.Security.IntegrationTests", EnvironmentOptions.ApplicationName);
        Assert.AreEqual("Development", EnvironmentOptions.EnvironmentName);
        using var workspace = new ToolWorkspace();
        Assert.AreEqual(0, workspace.Run("setup", "--service", EnvironmentOptions.ApplicationName,
            "--caller", "CountryData.NativeCaller"), workspace.Output);
        using var businessReservation = CertificateTestServer.Reserve();
        using var probeReservation = CertificateTestServer.Reserve();
        int businessPort = ((IPEndPoint)businessReservation.LocalEndPoint!).Port;
        int probePort = ((IPEndPoint)probeReservation.LocalEndPoint!).Port;
        JsonObject configuration = workspace.Configuration();
        configuration["businessPort"] = businessPort;
        configuration["operationalPort"] = probePort;
        configuration["allowedOrigins"] = new JsonArray($"https://localhost:{businessPort}");
        string journal = Path.Combine(Path.GetTempPath(), "country-caller-native-custody-" + Guid.NewGuid().ToString("N"));
        configuration["nativeKeyJournalDirectory"] = journal;
        string json = configuration.ToJsonString();
        workspace.WriteOwnedExtra("consumer.configuration.json", json);
        string publicConfiguration = Path.Combine(workspace.Credentials, "consumer.configuration.json");
        using var inventory = new CountryInventory(workspace.Root);
        var audit = new CountryAudit();
        WebApplicationBuilder upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.Logging.ClearProviders();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication upstream = upstreamBuilder.Build();
        upstream.MapGet("/countries", () => Results.Json(new
        {
            error = false,
            data = new[]
            {
                new { name = "Malta", Iso2 = "MT", Iso3 = "MLT" },
                new { name = "Trinidad and Tobago", Iso2 = "TT", Iso3 = "TTO" }
            }
        }));
        await upstream.StartAsync();
        string source = upstream.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Single() + "/countries";
        await using CISApplication application = CountryApplication.CreateService(
            ["--environment=Development", "--security=local", "--security-config=" + publicConfiguration], builder =>
            {
                builder.TypeManager = inventory.Options;
                builder.LoggingEnabled = false;
                builder.Host.Logging.ClearProviders();
                builder.Services.AddSingleton(SecurityTestAssemblySetup.NativeKeys);
                builder.Services.AddSingleton<ISecurityAuditSink>(audit);
                builder.Configuration = new ConfigurationOptions(typeof(CountryCatalog).Assembly.GetName().Name!)
                {
                    IsHealthCritical = true,
                    Factories = new Dictionary<Type, Func<IConfigurationObject>>
                    {
                        [typeof(CountrySettings)] = () => new CountrySettings { SourceUrl = source, RetryPolicyName = null },
                        [typeof(CountrySecuritySettings)] = () => JsonSerializer.Deserialize<CountrySecuritySettings>(json,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
                    }
                };
                builder.ConfigureCisMetrics(options => { options.Enabled = true; options.ScrapeCacheDuration = TimeSpan.Zero; });
            });
        businessReservation.Dispose();
        probeReservation.Dispose();
        await application.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            using var operational = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{probePort}"),
                Timeout = TimeSpan.FromSeconds(15)
            };
            using HttpResponseMessage ready = await operational.GetAsync("/_health/ready");
            Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
            CountrySelection selection = application.Services.GetRequiredService<CountrySelection>();
            Assert.AreEqual(new CountrySelectionSnapshot(null, 0), selection.Capture());

            string executable = inventory.ProductOutput();
            async Task<CountryCallerResult> RunCaller(string? operation, string country, bool success)
            {
                // This product child uses its normal DI-owned scope factory, not the parent's borrowed
                // assembly scope. Its exit and custody journal prove reverse-disposal ownership too.
                var arguments = new List<string>
                {
                    "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", executable,
                    "--role=caller", "--security=local", "--security-config=" + publicConfiguration,
                    $"--target=https://localhost:{businessPort}", "--country=" + country
                };
                if (operation is not null)
                {
                    arguments.Add("--operation=" + operation);
                }
                ProcessResult caller = await ProcessRunner.RunAsync("dotnet", arguments, workspace.Root, TimeSpan.FromSeconds(45),
                    new Dictionary<string, string?>
                    {
                        ["CORE_SERVICE_NAME"] = "CountryData.NativeCaller",
                        ["CORE_ENVIRONMENT"] = "Development",
                        ["DOTNET_ENVIRONMENT"] = "Development",
                        ["ASPNETCORE_ENVIRONMENT"] = "Development"
                    });
                Assert.AreEqual(success ? 0 : 1, caller.ExitCode, caller.StandardOutput + caller.StandardError);
                string resultLine = caller.StandardOutput.Split('\n').Single(line => line.StartsWith("{\"Success\"", StringComparison.Ordinal));
                return JsonSerializer.Deserialize<CountryCallerResult>(resultLine)!;
            }
            CountryCallerResult read = await RunCaller(null, "MT", success: true);
            Assert.IsTrue(read.Success);
            Assert.AreEqual("MT", read.Country?.Iso2);
            Assert.AreEqual(new CountrySelectionSnapshot(null, 0), selection.Capture(), "The default caller operation performs no mutation.");
            CountryCallerResult permitted = await RunCaller("select", "MT", success: true);
            Assert.IsTrue(permitted.Success);
            CountrySelectionSnapshot beforeDenial = selection.Capture();
            CountryCallerResult denied = await RunCaller("select", "TTO", success: false);
            Assert.IsFalse(denied.Success);
            Assert.AreEqual(403, denied.HttpStatusCode);
            CountryCallerResult inspected = await RunCaller("inspect", "MT", success: true);
            Assert.IsTrue(inspected.Success);
            Assert.AreEqual(beforeDenial, inspected.Selection);
            CountrySelectionSnapshot selected = selection.Capture();
            Assert.AreSame(beforeDenial, selected);
            Assert.AreEqual("MT", selected.Country?.Iso2);
            Assert.AreEqual(1L, selected.Revision, "The denied actual TTO resource must leave both effect and revision unchanged.");
            Assert.HasCount(1, audit.Operations);
            Assert.AreEqual(SecurityOperationOutcome.Completed, audit.Operations[0].Outcome);
            Assert.AreEqual("MT", audit.Operations[0].Target.ResourceIdentifier);
            Assert.AreEqual("CountryData.NativeCaller", audit.Operations[0].Caller.Application);
            Assert.IsTrue(audit.Decisions.Any(record => !record.Decision.IsAllowed && record.Target?.ResourceIdentifier == "TT"));

            using var spoofed = new HttpRequestMessage(HttpMethod.Put, "/selection/TTO");
            spoofed.Headers.Host = $"localhost:{businessPort}";
            using HttpResponseMessage forbidden = await operational.SendAsync(spoofed);
            Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
            Assert.AreSame(selected, selection.Capture());

            using var scrapeRequest = new HttpRequestMessage(HttpMethod.Get, "/_metrics");
            scrapeRequest.Headers.Accept.ParseAdd("text/plain; version=0.0.4");
            using HttpResponseMessage scraped = await operational.SendAsync(scrapeRequest);
            string scrape = await scraped.Content.ReadAsStringAsync();
            Assert.IsTrue(scraped.IsSuccessStatusCode, scrape);
            Assert.IsTrue(scrape.Contains("cis_security_authorization_decisions_total", StringComparison.Ordinal));
            Assert.IsTrue(scrape.Contains("profile=\"country-security\"", StringComparison.Ordinal));
            Assert.IsTrue(scrape.Contains("outcome=\"deny\"", StringComparison.Ordinal));
            Assert.IsTrue(scrape.Contains("application=\"CIS.Security.IntegrationTests\"", StringComparison.Ordinal));
            Assert.IsTrue(scrape.Contains("environment=\"Development\"", StringComparison.Ordinal));
            Assert.IsFalse(scrape.Contains("CountryData.NativeCaller", StringComparison.Ordinal), "Caller names are not unbounded metric dimensions.");
            VerifyChildCustody(journal);
        }
        finally
        {
            await application.StopAsync();
            await upstream.StopAsync();
        }
    }

    private static void VerifyChildCustody(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.IsFalse(Directory.Exists(directory));
            return;
        }
        string[] journals = Directory.GetFiles(directory, "keys.jsonl", SearchOption.AllDirectories);
        Assert.HasCount(4, journals);
        JsonObject[] rows = journals.SelectMany(File.ReadAllLines).Select(line => JsonNode.Parse(line)!.AsObject()).ToArray();
        JsonObject[] created = rows.Where(row => row["transition"]!.GetValue<int>() == (int)RestHttpNativeKeyTransition.Created).ToArray();
        JsonObject[] removed = rows.Where(row => row["transition"]!.GetValue<int>() == (int)RestHttpNativeKeyTransition.Removed).ToArray();
        Assert.HasCount(4, created);
        CollectionAssert.AreEquivalent(created.Select(row => row["UniqueName"]!.GetValue<string>()).ToArray(),
            removed.Select(row => row["UniqueName"]!.GetValue<string>()).ToArray());
        foreach (JsonObject row in created)
        {
            Assert.IsFalse(File.Exists(row["FilePath"]!.GetValue<string>()));
        }
        // Retain this test's restricted non-secret journal for the controller's independent exact-key audit.
    }

    private sealed class CountryAudit : ISecurityAuditSink
    {
        internal List<SecurityAuthorizationAudit> Decisions { get; } = [];
        internal List<SecurityOperationAudit> Operations { get; } = [];

        public ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (Decisions)
            {
                Assert.IsTrue(Decisions.Count < 16, "The focused consumer audit remains bounded.");
                Decisions.Add(audit);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (Operations)
            {
                Assert.IsTrue(Operations.Count < 4, "Only actual committed selection work is reported.");
                Operations.Add(audit);
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountryInventory : IDisposable
    {
        private readonly string directory;
        private readonly Assembly[] assemblies = [typeof(CountryApplication).Assembly, typeof(TypeManager).Assembly,
            typeof(MetricsApplicationIntegration).Assembly, typeof(RestClientResilienceServiceCollectionExtensions).Assembly,
            typeof(CertificateAuthenticationExtensions).Assembly, typeof(EntraServiceCollectionExtensions).Assembly];

        internal CountryInventory(string workspace)
        {
            directory = Path.Combine(workspace, "country-inventory");
            Directory.CreateDirectory(directory);
            foreach (Assembly assembly in assemblies)
            {
                File.Copy(assembly.Location, Path.Combine(directory, Path.GetFileName(assembly.Location)));
            }
            string manifest = Path.Combine(directory, "country.types.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                hostAssembly = assemblies[0].FullName,
                assemblies = assemblies.Select(assembly => new { assemblyName = assembly.FullName, relativePath = Path.GetFileName(assembly.Location) })
            }));
            Options = new(assemblies[0])
            {
                AssemblyManifestPath = manifest
            };
        }

        internal TypeManagerOptions Options
        {
            get;
        }

        internal string ProductOutput()
        {
            DirectoryInfo? repository = new(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                repository = repository.Parent;
            }
            Assert.IsNotNull(repository);
            string configuration = assemblies[0].GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            string output = Path.Combine(repository.FullName, "implementations", "csharp", "examples", "CountryData", "bin", configuration, "net10.0");
            foreach (Assembly assembly in assemblies)
            {
                string candidate = Path.Combine(output, Path.GetFileName(assembly.Location));
                Assert.AreEqual(Hash(assembly.Location), Hash(candidate), "The product child must execute the current exact candidate: " + assembly.GetName().Name);
            }
            return Path.Combine(output, "CIS.CountryData.dll");
        }

        private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

        public void Dispose()
        {
            foreach (string path in Directory.GetFiles(directory))
            {
                File.Delete(path);
            }
            Directory.Delete(directory);
        }
    }
}
