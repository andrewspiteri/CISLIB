using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.CommonTesting;
using CIS.Examples.CountryData;
using CIS.RestClient.Http;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CountryData;

[TestClass]
[DoNotParallelize]
public sealed class CountryDataPublicConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OutsideFriendAssemblyUsesActualPackagesAndCompiledCatalogThroughPublicContracts()
    {
        const string assemblyName = "CIS.PublicSubstitution.Consumer";
        Assembly application = typeof(CountryCatalog).Assembly;
        foreach (Assembly input in new[] { typeof(HttpRestClient).Assembly, typeof(ManualHeartbeat).Assembly, application })
        {
            Assert.IsFalse(input.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Any(attribute => attribute.AssemblyName.Split(',')[0] == assemblyName));
        }

        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "CountryData.PublicSubstitution",
            typeof(HttpRestClient).Assembly, typeof(ManualHeartbeat).Assembly);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "app")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        string catalogHash = CandidatePackageFixture.Hash(application.Location);
        string references = Directory.CreateDirectory(Path.Combine(app, "references")).FullName;
        string catalogReference = Path.Combine(references, "CIS.CountryData.dll");
        File.Copy(application.Location, catalogReference);
        Assert.AreEqual(catalogHash, CandidatePackageFixture.Hash(catalogReference));
        File.WriteAllText(Path.Combine(app, "Program.cs"), ConsumerProgram);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup",
                new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.CommonTesting" }.Select(id => new XElement("PackageReference",
                    new XAttribute("Include", id), new XAttribute("Version", "[" + package.Version + "]"))),
                new XElement("Reference", new XAttribute("Include", "CIS.CountryData"),
                    new XElement("HintPath", catalogReference), new XElement("Private", true))))).Save(project);

        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        string[] approved = ["CIS.Core/" + package.Version, "CIS.CommonTesting/" + package.Version,
            "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        Assert.IsTrue(RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json")))
            .All(dependency => approved.Contains(dependency, StringComparer.Ordinal)));
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        JsonObject deps = package.AssertDeployed(output, assemblyName);
        Assert.AreEqual(catalogHash, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.CountryData.dll")));
        Assert.IsFalse(deps["libraries"]!.AsObject().Any(item => item.Key.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)
            || item.Key.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
            || item.Key.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase)));
        string working = Directory.CreateDirectory(Path.Combine(package.Root, "run")).FullName;
        ProcessResult result = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(output, assemblyName + ".dll")], working, TimeSpan.FromSeconds(45));
        Assert.AreEqual("", result.StandardError);
        JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
        Assert.IsTrue(report["aliasesEquivalent"]!.GetValue<bool>());
        Assert.IsTrue(report["lastGoodRetained"]!.GetValue<bool>());
        Assert.IsTrue(report["closeJoinedActualWork"]!.GetValue<bool>());
        Assert.AreEqual(4, report["calls"]!.GetValue<int>());
        Assert.AreEqual(1, report["refreshFailures"]!.GetValue<int>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CountryData.dll"), report["catalogPath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CommonTesting.dll"), report["testingPath"]!.GetValue<string>());
        Assert.AreEqual(catalogHash, CandidatePackageFixture.Hash(application.Location));
        package.AssertOriginalAssembliesUnchanged();
        TestContext.WriteLine(result.StandardOutput);
    }

    private const string ConsumerProgram = """"
        using System.Text.Json;
        using CIS.CommonTesting;
        using CIS.Configuration;
        using CIS.Examples.CountryData;
        using CIS.Metrics;
        using CIS.RestClient;
        using Microsoft.Extensions.Hosting;

        const string json = """{"error":false,"data":[{"name":"Malta","Iso2":"MT","Iso3":"MLT"}]}""";
        var rest = new ScriptedRestClient();
        var health = new RecordingHealthMonitorFactory();
        await using var heartbeat = new ManualHeartbeat();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logs = new RecordingLogger<CountryCatalog>();
        var catalog = new CountryCatalog(rest, new CountrySettings { RetryPolicyName = null }, new Reader(),
            heartbeat, health, DisabledMetrics.Instance, new Lifetime(), logs, clock, []);
        PendingRestCall? pending = null;
        try
        {
            rest.EnqueueJsonValue(json);
            await catalog.StartAsync(CancellationToken.None);
            var registration = heartbeat.Registrations.Single();
            Check(ReferenceEquals(catalog, registration.Subscriber));
            Check(registration.Options.Interval == TimeSpan.FromHours(1));
            Check(registration.Options.InitialDelay == TimeSpan.FromHours(1));
            Check(ReferenceEquals(registration.Monitor, health.Monitors.Single()));
            bool aliasesEquivalent = catalog.TryGet("MT", out var first) && catalog.TryGet("MLT", out var alpha3)
                && catalog.TryGet("Malta", out var name) && ReferenceEquals(first, alpha3) && ReferenceEquals(first, name);
            Check(aliasesEquivalent);

            rest.EnqueueFailure(RestFailureKind.Http, "provider.unavailable", "temporary", 503);
            var failed = await heartbeat.TriggerAsync(catalog);
            Check(failed.CallbackResult == false && failed.Error is null);
            bool lastGoodRetained = catalog.TryGet("MT", out var retained) && ReferenceEquals(first, retained);
            Check(lastGoodRetained);
            rest.EnqueueJsonValue(json);
            Check((await heartbeat.TriggerAsync(catalog)).CallbackResult == true);

            pending = rest.EnqueuePendingJsonValue(json);
            Task<ManualHeartbeatResult> running = heartbeat.TriggerAsync(catalog);
            await pending.Entered;
            Task closing = catalog.DisposeAsync().AsTask();
            bool closeJoinedActualWork = !closing.IsCompleted && !running.IsCompleted;
            Check(closeJoinedActualWork);
            pending.Release();
            Check((await running).CallbackResult == true);
            await closing;
            Check(heartbeat.Registrations.Count == 0 && health.Monitors.Single().IsWithdrawn);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                aliasesEquivalent, lastGoodRetained, closeJoinedActualWork,
                calls = rest.TotalCalls,
                refreshFailures = health.Monitors.Single().Count(HealthReportKind.RefreshFailure),
                catalogPath = typeof(CountryCatalog).Assembly.Location,
                corePath = typeof(IRestClient).Assembly.Location,
                testingPath = typeof(ManualHeartbeat).Assembly.Location
            }));
        }
        finally
        {
            pending?.Release();
            await catalog.DisposeAsync();
        }

        static void Check(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Public consumer contract failed.");
            }
        }

        sealed class Reader : IConfigurationReader
        {
            public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> read)
                where TConfiguration : class, IConfigurationObject => read(configuration);
        }

        sealed class Lifetime : IHostApplicationLifetime
        {
            public CancellationToken ApplicationStarted => CancellationToken.None;
            public CancellationToken ApplicationStopping => CancellationToken.None;
            public CancellationToken ApplicationStopped => CancellationToken.None;
            public void StopApplication() { }
        }
        """";
}
