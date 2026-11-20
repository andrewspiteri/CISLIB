using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient.Http;
using CIS.RestClient.Resilience;
using CIS.Security.Certificates;
using CIS.Security.Entra;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.RestClient;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CountryData;

[TestClass]
[DoNotParallelize]
public sealed class CountryDataPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task CurrentCountryDataPackagesBuildPublishAndUseAutomaticMetricsAgainstLocalProvider()
    {
        string identity = "CountryData." + new string('界', 100);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(identity) > 256 && identity.EnumerateRunes().Count() <= 255);
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, identity, typeof(HttpRestClient).Assembly,
            typeof(MetricsOptions).Assembly, typeof(RestClientResilienceServiceCollectionExtensions).Assembly,
            typeof(CertificateTrustPolicy).Assembly, typeof(EntraSecurityOptions).Assembly);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "app")).FullName;
        string source = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp/examples/CountryData");
        foreach (string file in Directory.GetFiles(source, "*.cs"))
        {
            string copied = Path.Combine(app, Path.GetFileName(file));
            File.Copy(file, copied);
            Assert.AreEqual(CandidatePackageFixture.Hash(file), CandidatePackageFixture.Hash(copied));
        }
        // Only the temporary entry belongs to this test. All actual catalog, mapping,
        // settings and route source remains byte-for-byte the current application.
        File.WriteAllText(Path.Combine(app, "Program.cs"), ConsumerProgram);
        XDocument consumer = XDocument.Load(Path.Combine(source, "CountryData.csproj"));
        consumer.Descendants("ProjectReference").Remove();
        consumer.Descendants("Import").Remove();
        consumer.Descendants("InternalsVisibleTo").Remove();
        consumer.Root!.AddFirst(new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", package.Version)));
        consumer.Root.Add(new XElement("ItemGroup", new[] { "CIS.Metrics.OpenTelemetry", "CIS.RestClient.Resilience",
            "CIS.Security.Certificates", "CIS.Security.Entra" }
            .Select(id => new XElement("PackageReference", new XAttribute("Include", id), new XAttribute("Version", "[" + package.Version + "]")))));
        string project = Path.Combine(app, "CountryData.csproj"), assemblyName = consumer.Descendants("AssemblyName").Single().Value;
        Assert.AreEqual("CIS.CountryData", assemblyName);
        Assert.IsFalse(consumer.Descendants("ProjectReference").Any());
        Assert.IsFalse(consumer.Descendants("PackageReference").Any(item => (string?)item.Attribute("Include") == "CIS.Core"));
        Assert.AreEqual("true", consumer.Descendants("CisTypeManagerEnabled").Single().Value);
        consumer.Save(project);
        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        JsonObject restored = RestPackage.Assets(Path.Combine(app, "obj/project.assets.json"));
        Assert.IsTrue(RestPackage.Packages(restored).Contains("Polly.Core/8.9.1", StringComparer.Ordinal));
        Assert.IsTrue(RestPackage.Packages(restored).Contains("System.Diagnostics.DiagnosticSource/11.0.0", StringComparer.Ordinal));
        string polly = package.DependencyHash("polly.core.8.9.1.nupkg", "Polly.Core");
        string diagnostic = package.DependencyHash("system.diagnostics.diagnosticsource.11.0.0.nupkg", "System.Diagnostics.DiagnosticSource");
        string probeSource = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp/tools/CisHealthProbe");
        string probeDirectory = Directory.CreateDirectory(Path.Combine(package.Root, "probe")).FullName;
        foreach (string file in Directory.GetFiles(probeSource, "*.cs"))
        {
            string copy = Path.Combine(probeDirectory, Path.GetFileName(file));
            File.Copy(file, copy);
            Assert.AreEqual(CandidatePackageFixture.Hash(file), CandidatePackageFixture.Hash(copy));
        }
        XDocument probe = XDocument.Load(Path.Combine(probeSource, "CisHealthProbe.csproj"));
        probe.Descendants("ProjectReference").Remove();
        probe.Root!.Add(new XElement("ItemGroup", new XElement("PackageReference",
            new XAttribute("Include", "CIS.Core"), new XAttribute("Version", "[" + package.Version + "]"))));
        string probeProject = Path.Combine(probeDirectory, "CisHealthProbe.csproj");
        probe.Save(probeProject);
        await package.RunAsync(["restore", probeProject, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(probeDirectory, "CIS.Core");
        string[] approvedProbe = ["CIS.Core/" + package.Version, "Microsoft.Extensions.Logging.Abstractions/10.0.12",
            "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        Assert.IsTrue(RestPackage.Packages(RestPackage.Assets(Path.Combine(probeDirectory, "obj/project.assets.json")))
            .All(dependency => approvedProbe.Contains(dependency, StringComparer.Ordinal)));
        await package.RunAsync(["build", probeProject, "-c", "Release", "--no-restore"], package.Root);
        string probeBuild = Path.Combine(probeDirectory, "bin/Release/net10.0");
        VerifyProbe(probeBuild);
        string probePublished = Path.Combine(package.Root, "probe-published");
        await package.RunAsync(["publish", probeProject, "-c", "Release", "--no-restore", "-o", probePublished], package.Root);
        VerifyProbe(probePublished);
        int requests = 0;
        await using var provider = await RestHttpLocalServer.StartAsync(async context =>
        {
            int number = Interlocked.Increment(ref requests);
            if (number % 3 != 0)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("temporarily unavailable", context.RequestAborted);
                return;
            }
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(ProviderJson, context.RequestAborted);
        });
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        await Verify(Path.Combine(app, "bin/Release/net10.0"), probeBuild, "build");
        string published = Path.Combine(package.Root, "published");
        await package.RunAsync(["publish", project, "-c", "Release", "--no-restore", "-o", published], package.Root);
        await Verify(published, probePublished, "publish");
        package.AssertOriginalAssembliesUnchanged();

        void VerifyProbe(string output)
        {
            JsonObject deps = package.AssertDeployed(output, "CisHealthProbe", "CIS.Core");
            Assert.IsTrue(deps["libraries"]!.AsObject().ContainsKey("CIS.Core/" + package.Version));
            Assert.IsFalse(deps["libraries"]!.AsObject().Any(item => item.Key.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)
                || item.Key.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)));
            JsonArray frameworks = RestPackage.Assets(Path.Combine(output, "CisHealthProbe.runtimeconfig.json"))
                ["runtimeOptions"]!["frameworks"]!.AsArray();
            foreach (string framework in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
            {
                Assert.AreEqual("10.0.12", frameworks.Single(item => item!["name"]!.GetValue<string>() == framework)!["version"]!.GetValue<string>());
            }
        }

        async Task Verify(string output, string probeOutput, string lane)
        {
            JsonObject deps = package.AssertDeployed(output, assemblyName);
            Assert.IsTrue(deps["libraries"]!.AsObject().ContainsKey("Polly.Core/8.9.1"));
            Assert.IsTrue(deps["libraries"]!.AsObject().ContainsKey("System.Diagnostics.DiagnosticSource/11.0.0"));
            Assert.AreEqual(polly, CandidatePackageFixture.Hash(Path.Combine(output, "Polly.Core.dll")));
            Assert.AreEqual(diagnostic, CandidatePackageFixture.Hash(Path.Combine(output, "System.Diagnostics.DiagnosticSource.dll")));
            JsonObject inventory = RestPackage.Assets(Path.Combine(output, assemblyName + ".types.json"));
            Assert.IsTrue(inventory["assemblies"]!.AsArray().Any(item => item!["assemblyName"]!.GetValue<string>()
                .StartsWith("CIS.Metrics.OpenTelemetry,", StringComparison.Ordinal)));
            int before = Volatile.Read(ref requests);
            string working = Directory.CreateDirectory(Path.Combine(package.Root, "run-" + lane)).FullName;
            ProcessResult result = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                Path.Combine(output, assemblyName + ".dll"), provider.Address + "/countries", Path.Combine(probeOutput, "CisHealthProbe.dll")], working, TimeSpan.FromSeconds(45));
            Assert.AreEqual("", result.StandardError);
            JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
            Assert.AreEqual("CountryData.Package", report["consumer"]!.GetValue<string>());
            Assert.IsTrue(report["aliasesEquivalent"]!.GetValue<bool>());
            Assert.IsTrue(report["lookupErrorsPreserved"]!.GetValue<bool>());
            Assert.IsTrue(report["sameCatalogAcrossScopes"]!.GetValue<bool>());
            Assert.IsTrue(report["ready"]!.GetValue<bool>());
            Assert.AreEqual(1, report["automaticMetricsRegistrars"]!.GetValue<int>());
            Assert.AreEqual(3d, report["entries"]!.GetValue<double>());
            Assert.IsTrue(report["lastSuccess"]!.GetValue<double>() > 0);
            Assert.AreEqual(5d, report["pipelineCompletions"]!.GetValue<double>());
            Assert.AreEqual(0d, report["pipelineActive"]!.GetValue<double>());
            Assert.AreEqual(12d, report["pipelineForwardPhases"]!.GetValue<double>());
            Assert.AreEqual(3d, report["pipelineErrorPhases"]!.GetValue<double>());
            Assert.AreEqual(8d, report["stateMachineCalls"]!.GetValue<double>());
            Assert.AreEqual(0d, report["stateMachineActive"]!.GetValue<double>());
            Assert.AreEqual(7d, report["stateMachineHandlerPhases"]!.GetValue<double>());
            Assert.AreEqual(1d, report["logicalCompletions"]!.GetValue<double>());
            Assert.AreEqual(0d, report["active"]!.GetValue<double>());
            Assert.AreEqual(3d, report["nativeCount"]!.GetValue<double>());
            Assert.IsTrue(report["allSamplesHaveIdentity"]!.GetValue<bool>());
            Assert.AreEqual(3, Volatile.Read(ref requests) - before, "Aliases and scraping must not fetch upstream.");
            Assert.IsTrue(report["heldHttpPipeIdentityMatch"]!.GetValue<bool>());
            Assert.IsTrue(report["readyHttpPipeIdentityMatch"]!.GetValue<bool>());
            Assert.AreEqual(1, report["heldHealthProbeExit"]!.GetValue<int>());
            Assert.AreEqual(1, report["heldReadinessProbeExit"]!.GetValue<int>());
            Assert.AreEqual(0, report["readyHealthProbeExit"]!.GetValue<int>());
            Assert.AreEqual(0, report["readyReadinessProbeExit"]!.GetValue<int>());
            Assert.AreEqual(2, report["unavailableProbeExit"]!.GetValue<int>());
            Assert.IsTrue(report["unavailableProbeHasOnlyBoundedError"]!.GetValue<bool>());
            Assert.IsTrue(report["stopped"]!.GetValue<bool>());
            JsonObject runtime = report["runtime"]!.AsObject();
            Assert.AreEqual(".NET 10.0.12", runtime["framework"]!.GetValue<string>());
            Assert.AreEqual("11.0.0.0", runtime["diagnosticSourceVersion"]!.GetValue<string>());
            foreach ((string field, string file) in new[]
            {
                ("corePath", "CIS.Core.dll"), ("metricsPath", "CIS.Metrics.OpenTelemetry.dll"),
                ("resiliencePath", "CIS.RestClient.Resilience.dll"), ("diagnosticSourcePath", "System.Diagnostics.DiagnosticSource.dll"),
                ("pollyPath", "Polly.Core.dll")
            })
            {
                RestPackage.SamePath(Path.Combine(output, file), runtime[field]!.GetValue<string>());
            }
            TestContext.WriteLine("country-data/" + lane + ": " + result.StandardOutput);
        }
    }

    private const string ProviderJson = """
        {"error":false,"msg":"countries","data":[{"name":"Malta","Iso2":"MT","Iso3":"MLT"},
        {"name":"France","Iso2":"FR","Iso3":"FRA"},{"name":"New Zealand","Iso2":"NZ","Iso3":"NZL"}]}
        """;

    private const string ConsumerProgram = """
        using System.Diagnostics;
        using System.Globalization;
        using System.Net;
        using System.Text;
        using System.Net.Http.Json;
        using System.Runtime.InteropServices;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using CIS;
        using CIS.Configuration;
        using CIS.Examples.CountryData;
        using CIS.Healthcheck;
        using CIS.Hosting;
        using CIS.Metrics.OpenTelemetry;
        using CIS.RestClient;
        using CIS.RestClient.Http;
        using CIS.RestClient.Resilience;
        using CIS.Startup;
        using CIS.TypeManagement;
        using CIS.Workflows;
        using CIS.Workflows.Hosting;
        using Microsoft.AspNetCore.Hosting.Server;
        using Microsoft.AspNetCore.Hosting.Server.Features;

        string source = args[0], probePath = args[1];
        string pipeName = "cis-country-package-" + Guid.NewGuid().ToString("N");
        var hold = new PackageReadinessHold();
        JsonObject? report = null;
        CISApplication application = CISApplication.CreateWeb([], builder =>
        {
            builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Healthcheck = new HealthcheckOptions { PipeEnabled = true, PipeName = pipeName, MaxReportBytes = 65_536 };
            builder.Configuration = new ConfigurationOptions(typeof(CountryCatalog).Assembly.GetName().Name!)
            {
                IsHealthCritical = true,
                Factories = new Dictionary<Type, Func<IConfigurationObject>>
                { [typeof(CountrySettings)] = () => new CountrySettings { SourceUrl = source } }
            };
            var countries = new RestClientOptions("countries")
            {
                ResponseMapper = new CountryProviderMapper(),
                RetryPolicy = RestRetrySelection.Named(RestRetryPolicy.TransientHttp.Name)
            };
            countries.Operations.Add("refresh");
            builder.RestClients.Add(countries);
            builder.Services.AddCisRestClientResilience(RestRetryPolicy.TransientHttp);
            builder.Services.AddCisWorkflows();
            builder.Services.AddCisStateMachines();
            builder.Services.AddSingleton<CountryCatalog>();
            builder.Services.AddSingleton<IStartable>(root => root.GetRequiredService<CountryCatalog>());
            builder.Services.AddSingleton<IStartable>(root => hold.Bind(root.GetRequiredService<HealthcheckService>()));
        }, web => web.MapGet("/country/{identifier}",
            (string identifier, IStateMachineFactory machines, HttpContext context)
                => CountryEndpoints.Get(identifier, machines, context)));
        Task startup = application.StartAsync();
        try
        {
            await hold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            string address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            using HttpResponseMessage heldHttp = await client.GetAsync("/_health/ready");
            JsonObject heldSnapshot = JsonNode.Parse(await heldHttp.Content.ReadAsStringAsync())!.AsObject();
            var heldHealth = await ProbeAsync(pipeName, "health");
            var heldReadiness = await ProbeAsync(pipeName, "readiness");
            bool heldMatch = (int)heldHttp.StatusCode == 500 && !startup.IsCompleted
                && heldHealth.ExitCode == 1 && heldReadiness.ExitCode == 1
                && heldHealth.Error.Length == 0 && heldReadiness.Error.Length == 0
                && heldSnapshot["application"]!["startupCompleted"]!.GetValue<bool>() == false
                && heldSnapshot["application"]!["name"]!.GetValue<string>() == EnvironmentOptions.ApplicationName
                && SameHealth(heldSnapshot, heldHealth.Snapshot) && SameHealth(heldSnapshot, heldReadiness.Snapshot);
            hold.Release.TrySetResult();
            await startup;
            using HttpResponseMessage readyHttp = await client.GetAsync("/_health/ready");
            JsonObject readySnapshot = JsonNode.Parse(await readyHttp.Content.ReadAsStringAsync())!.AsObject();
            var readyHealth = await ProbeAsync(pipeName, "health");
            var readyReadiness = await ProbeAsync(pipeName, "readiness");
            bool readyMatch = (int)readyHttp.StatusCode == 200 && readyHealth.ExitCode == 0 && readyReadiness.ExitCode == 0
                && readyHealth.Error.Length == 0 && readyReadiness.Error.Length == 0
                && readySnapshot["application"]!["startupCompleted"]!.GetValue<bool>()
                && readySnapshot["application"]!["name"]!.GetValue<string>() == EnvironmentOptions.ApplicationName
                && SameHealth(readySnapshot, readyHealth.Snapshot) && SameHealth(readySnapshot, readyReadiness.Snapshot);
            var unavailable = await ProbeAsync("cis-country-absent-" + Guid.NewGuid().ToString("N"), "health");
            var countries = new List<Country>();
            foreach (string alias in new[] { "MT", "MLT", "Malta" })
            {
                using HttpResponseMessage response = await client.GetAsync("/country/" + alias);
                response.EnsureSuccessStatusCode();
                countries.Add((await response.Content.ReadFromJsonAsync<Country>())!);
            }
            bool lookupErrorsPreserved = true;
            foreach (var expected in new[]
            {
                (Identifier: "not-a-country", Status: HttpStatusCode.NotFound, Code: "country.not_found"),
                (Identifier: "%20%20", Status: HttpStatusCode.BadRequest, Code: "country.invalid_identifier")
            })
            {
                using HttpResponseMessage response = await client.GetAsync("/country/" + expected.Identifier);
                JsonObject error = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
                lookupErrorsPreserved &= response.StatusCode == expected.Status
                    && error["code"]!.GetValue<string>() == expected.Code;
            }
            using HttpResponseMessage scrape = await client.GetAsync("/_metrics");
            scrape.EnsureSuccessStatusCode();
            string text = await scrape.Content.ReadAsStringAsync();
            string[] samples = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(line => !line.StartsWith('#')).ToArray();
            string identity = EnvironmentOptions.ApplicationName, environment = EnvironmentOptions.EnvironmentName;
            bool allIdentity = samples.Length > 0 && samples.All(line =>
                line.Contains("application=\"" + identity + "\"", StringComparison.Ordinal)
                && line.Contains("environment=\"" + environment + "\"", StringComparison.Ordinal)
                && line.Split("application=\"", StringSplitOptions.None).Length == 2
                && line.Split("environment=\"", StringSplitOptions.None).Length == 2);
            await using AsyncServiceScope first = application.Services.CreateAsyncScope();
            await using AsyncServiceScope second = application.Services.CreateAsyncScope();
            CountryCatalog catalog = application.Services.GetRequiredService<CountryCatalog>();
            report = JsonSerializer.SerializeToNode(new
            {
                consumer = "CountryData.Package",
                heldHttpPipeIdentityMatch = heldMatch, readyHttpPipeIdentityMatch = readyMatch,
                heldHealthProbeExit = heldHealth.ExitCode, heldReadinessProbeExit = heldReadiness.ExitCode,
                readyHealthProbeExit = readyHealth.ExitCode, readyReadinessProbeExit = readyReadiness.ExitCode,
                unavailableProbeExit = unavailable.ExitCode,
                unavailableProbeHasOnlyBoundedError = unavailable.Snapshot is null && unavailable.Error.Length is > 0 and <= 1024,
                aliasesEquivalent = countries.Count == 3 && countries.All(country => country == new Country("Malta", "MT", "MLT")),
                lookupErrorsPreserved,
                sameCatalogAcrossScopes = ReferenceEquals(catalog, first.ServiceProvider.GetRequiredService<CountryCatalog>())
                    && ReferenceEquals(catalog, second.ServiceProvider.GetRequiredService<CountryCatalog>()),
                ready = application.Services.GetRequiredService<HealthcheckService>().CaptureSnapshot().Ready,
                automaticMetricsRegistrars = application.Services.GetRequiredService<ITypeManager>()
                    .GetImplementations<ICISApplicationIntegration>().Count(type => type == typeof(MetricsApplicationIntegration)),
                entries = Sum("app_country_cache_entries"),
                lastSuccess = Sum("app_country_cache_last_success_timestamp_seconds"),
                logicalCompletions = Sum("cis_restclient_operation_duration_seconds_count"),
                active = Sum("cis_restclient_active_operations"),
                nativeCount = Sum("http_client_request_duration_seconds_count"),
                pipelineCompletions = Sum("cis_pipeline_execution_duration_seconds_count"),
                pipelineActive = Sum("cis_pipeline_active_executions"),
                pipelineForwardPhases = Sum("cis_pipeline_step_duration_seconds_count", "execute"),
                pipelineErrorPhases = Sum("cis_pipeline_step_duration_seconds_count", "error"),
                stateMachineCalls = Sum("cis_statemachine_trigger_duration_seconds_count"),
                stateMachineActive = Sum("cis_statemachine_active_attempts"),
                stateMachineHandlerPhases = Sum("cis_statemachine_handler_duration_seconds_count"),
                allSamplesHaveIdentity = allIdentity,
                runtime = new
                {
                    framework = RuntimeInformation.FrameworkDescription,
                    corePath = typeof(HttpRestClient).Assembly.Location,
                    metricsPath = typeof(MetricsOptions).Assembly.Location,
                    resiliencePath = typeof(RestClientResilienceServiceCollectionExtensions).Assembly.Location,
                    diagnosticSourcePath = typeof(System.Diagnostics.Metrics.Meter).Assembly.Location,
                    diagnosticSourceVersion = typeof(System.Diagnostics.Metrics.Meter).Assembly.GetName().Version!.ToString(),
                    pollyPath = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "Polly.Core").Location
                }
            })!.AsObject();
            double Sum(string family, string? phase = null) => samples.Where(line => line.StartsWith(family + "{", StringComparison.Ordinal)
                    && (phase is null || line.Contains("phase=\"" + phase + "\"", StringComparison.Ordinal)))
                .Sum(line => double.Parse(line[(line.LastIndexOf(' ') + 1)..], CultureInfo.InvariantCulture));
        }
        finally
        {
            hold.Release.TrySetResult();
            try { await application.StopAsync(); }
            finally { await application.DisposeAsync(); }
        }
        JsonObject completed = report ?? throw new InvalidOperationException("Country consumer did not complete.");
        completed["stopped"] = true;
        Console.WriteLine(completed.ToJsonString());

        static bool SameHealth(JsonObject http, JsonObject? pipe) => pipe is not null
            && JsonNode.DeepEquals(http["application"], pipe["application"])
            && JsonNode.DeepEquals(http["healthStatus"], pipe["healthStatus"])
            && JsonNode.DeepEquals(http["ready"], pipe["ready"]);

        async Task<(int ExitCode, JsonObject? Snapshot, string Error)> ProbeAsync(string pipe, string view)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Environment.CurrentDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            foreach (string argument in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", probePath,
                "--pipe", pipe, "--view", view, "--timeout-ms", "1000", "--max-report-bytes", "65536" })
            { start.ArgumentList.Add(argument); }
            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the matching probe.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                string stdout = await output.WaitAsync(TimeSpan.FromSeconds(5)), stderr = await error.WaitAsync(TimeSpan.FromSeconds(5));
                if (stdout.Length > 65_537 || stderr.Length > 1024) { throw new InvalidDataException("Probe output exceeded its report bound."); }
                return (process.ExitCode, stdout.Length == 0 ? null : JsonNode.Parse(stdout)!.AsObject(), stderr);
            }
            finally
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Task.WhenAll(output, error).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        sealed class PackageReadinessHold : IStartable
        {
            private HealthcheckService health = null!;
            internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal PackageReadinessHold Bind(HealthcheckService service) { health = service; return this; }
            public async Task StartAsync(CancellationToken cancellationToken)
            {
                await using IConditionMonitor monitor = health.CreateCondition(new ConditionMonitorOptions
                { Name = "Package readiness hold", IsCritical = true, InitiallyFlagged = true });
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
        }
        """;
}
