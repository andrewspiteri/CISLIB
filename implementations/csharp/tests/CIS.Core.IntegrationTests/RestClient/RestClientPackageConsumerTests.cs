using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.RestClient.Http;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
[DoNotParallelize]
public sealed class RestClientPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualCoreOnlyPackageBuildsPublishesAndExecutesItsPublicRestWorkflow()
    {
        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "RestClient.Package", typeof(HttpRestClient).Assembly);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "app")).FullName;
        string project = Path.Combine(app, "Consumer.csproj"), assemblyName = "CIS.RestClient.PackageCoreOnly";
        File.WriteAllText(Path.Combine(app, "Program.cs"), CoreOnlyProgram);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"),
            new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", package.Version)),
            new XElement("PropertyGroup", new XElement("AssemblyName", assemblyName),
                new XElement("CisTypeManagerEnabled", true), new XElement("AppendRuntimeIdentifierToOutputPath", false)))).Save(project);
        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        string[] expected = ["CIS.Core/" + package.Version, "Microsoft.Extensions.Logging.Abstractions/10.0.12",
            "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        Assert.IsTrue(RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json")))
            .All(dependency => expected.Contains(dependency, StringComparer.Ordinal)), "Retained dependencies must use the approved versions; framework pruning may remove abstractions.");
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        await Verify(Path.Combine(app, "bin/Release/net10.0"), "build");
        string published = Path.Combine(package.Root, "published");
        await package.RunAsync(["publish", project, "-c", "Release", "--no-restore", "-o", published], package.Root);
        await Verify(published, "publish");
        package.AssertOriginalAssembliesUnchanged();

        async Task Verify(string output, string lane)
        {
            JsonObject deps = package.AssertDeployed(output, assemblyName);
            Assert.IsTrue(File.Exists(Path.Combine(output, assemblyName + ".types.json")), "The facade uses the real packaged Type Manager inventory.");
            Assert.IsFalse(deps["libraries"]!.AsObject().Any(item => item.Key.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)
                || item.Key.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                || item.Key.StartsWith("CIS.RestClient.Resilience/", StringComparison.Ordinal)
                || item.Key.StartsWith("CIS.Metrics.OpenTelemetry/", StringComparison.Ordinal)
                || item.Key.StartsWith("System.Diagnostics.DiagnosticSource/", StringComparison.Ordinal)));
            foreach (string name in new[] { "Polly.Core", "CIS.RestClient.Resilience", "CIS.Metrics.OpenTelemetry", "System.Diagnostics.DiagnosticSource" })
            { Assert.IsFalse(File.Exists(Path.Combine(output, name + ".dll"))); }
            string working = Directory.CreateDirectory(Path.Combine(package.Root, "run-" + lane)).FullName;
            ProcessResult process = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                Path.Combine(output, assemblyName + ".dll")], working, TimeSpan.FromSeconds(45));
            Assert.AreEqual("", process.StandardError);
            JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
            Assert.AreEqual("RestClient.CoreOnly", report["example"]!.GetValue<string>());
            Assert.IsTrue(report["success"]!.GetValue<bool>());
            Assert.AreEqual(17, report["value"]!.GetValue<int>());
            Assert.AreEqual(1, report["attempts"]!.GetValue<int>());
            Assert.IsFalse(report["optionalLoaded"]!.GetValue<bool>());
            Assert.IsTrue(report["metricsFallback"]!.GetValue<bool>());
            Assert.AreEqual(404, report["metricsStatus"]!.GetValue<int>());
            Assert.AreEqual(0, report["optionalRegistrars"]!.GetValue<int>());
            Assert.IsTrue(report["stopped"]!.GetValue<bool>());
            Assert.AreEqual(".NET 10.0.12", report["runtime"]!["framework"]!.GetValue<string>());
            RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["runtime"]!["corePath"]!.GetValue<string>());
            TestContext.WriteLine("core-only/" + lane + ": " + process.StandardOutput);
        }
    }

    private const string CoreOnlyProgram = """
        using System.Diagnostics.Metrics;
        using System.Runtime.InteropServices;
        using System.Text.Json;
        using CIS.Hosting;
        using CIS.Metrics;
        using CIS.RestClient;
        using CIS.RestClient.Http;
        using CIS.TypeManagement;
        using Microsoft.AspNetCore.Hosting.Server;
        using Microsoft.AspNetCore.Hosting.Server.Features;

        int publications = 0, callbacks = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, observer) =>
        {
            if (instrument.Name.StartsWith("app.core_only.", StringComparison.Ordinal))
            { publications++; observer.EnableMeasurementEvents(instrument); }
        };
        listener.Start();
        CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            builder.LoggingEnabled = false;
            builder.Host.Logging.ClearProviders();
            builder.WebHost!.UseKestrel().UseUrls("http://127.0.0.1:0");
        }, web => web.MapGet("/value", () => new Receipt(17)));
        bool success = false, metricsFallback = false; int value = 0, attempts = 0, metricsStatus = 0, optionalRegistrars = -1;
        try
        {
            await app.StartAsync();
            string address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            IMetrics metrics = app.Services.GetRequiredService<IMetrics>();
            metrics.CreateCounter(new("app.core_only.events")).Add();
            metrics.RegisterObservableGauge(new("app.core_only.state"), () => { callbacks++; return [new(1)]; });
            listener.RecordObservableInstruments();
            metricsFallback = ReferenceEquals(metrics, app.Services.GetRequiredService<IMetrics>())
                && metrics.GetType().Assembly == typeof(IMetrics).Assembly && publications == 0 && callbacks == 0;
            optionalRegistrars = app.Services.GetRequiredService<ITypeManager>().GetImplementations<ICISApplicationIntegration>().Count;
            using var http = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            using HttpResponseMessage metricsResponse = await http.GetAsync("/_metrics");
            metricsStatus = (int)metricsResponse.StatusCode;
            await using var client = new HttpRestClient(new RestClientOptions("core-only")
            { BaseUrl = address + "/", RetryPolicy = RestRetrySelection.None });
            RestResult<Receipt> result = await client.Get<Receipt>("value");
            success = result.Success && result.Context.HasValue; value = result.Value?.Count ?? 0; attempts = result.Context.AttemptCount;
        }
        finally { try { await app.StopAsync(); } finally { await app.DisposeAsync(); } }
        bool optionalLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name is { } name && (name.StartsWith("Polly", StringComparison.Ordinal) || name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || name == "CIS.RestClient.Resilience" || name == "CIS.Metrics.OpenTelemetry"));
        Console.WriteLine(JsonSerializer.Serialize(new { example = "RestClient.CoreOnly", success, value, attempts, optionalLoaded,
            metricsFallback, metricsStatus, optionalRegistrars,
            runtime = new { framework = RuntimeInformation.FrameworkDescription, corePath = typeof(HttpRestClient).Assembly.Location }, stopped = true }));
        public sealed record Receipt(int Count);
        """;
}

// Existing local-feed/package/cache checks shared by the two genuine consumer tests.
// This fixture never builds a shared source project or supplies another host/client implementation.
internal sealed class RestPackage : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly TestContext testContext;
    private readonly Dictionary<string, Assembly> assemblies;
    private readonly Dictionary<string, string> payloadHashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> packageHashes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> environment;
    private bool cleanupConfirmed = true;

    private RestPackage(TestContext testContext, string identity, Assembly[] assemblies)
    {
        this.testContext = testContext;
        this.assemblies = assemblies.ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);
        environment = CandidatePackageFixture.Variables(Root, new Dictionary<string, string?>
        {
            ["CORE_SERVICE_NAME"] = identity, ["CORE_ENVIRONMENT"] = "package-acceptance",
            ["DOTNET_CLI_HOME"] = Path.Combine(Root, "cli"), ["NUGET_FALLBACK_PACKAGES"] = null,
            ["RestoreSources"] = Feed, ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1", ["UseSharedCompilation"] = "false"
        });
        foreach (string key in Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(key => key.StartsWith("CIS_HEALTHCHECK_", StringComparison.OrdinalIgnoreCase)))
        { environment[key] = null; }
    }

    internal string Root => directory.Path;
    internal string Feed => Path.Combine(Root, "feed");
    internal string Config => Path.Combine(Root, "NuGet.Config");
    internal string Version { get; private set; } = "";
    private string Cache => Path.Combine(Root, "packages");

    internal static async Task<RestPackage> PrepareAsync(TestContext testContext, string identity, params Assembly[] assemblies)
    {
        var package = new RestPackage(testContext, identity, assemblies);
        try { await package.PrepareAsync(); return package; }
        catch { package.Dispose(); throw; }
    }

    private async Task PrepareAsync()
    {
        Directory.CreateDirectory(Feed);
        string language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
        Version = ProductVersion(assemblies["CIS.Core"]);
        var assets = assemblies.Keys.ToDictionary(id => id,
            id => Assets(Path.Combine(language, "src", id, "obj/project.assets.json")), StringComparer.Ordinal);
        File.Copy(Path.Combine(language, "global.json"), Path.Combine(Root, "global.json"));
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), """
            <Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><SelfContained>false</SelfContained>
            <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion><RollForward>Disable</RollForward>
            <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
            """);
        foreach (string name in new[] { "Directory.Build.targets", "Directory.Packages.props" })
        { File.WriteAllText(Path.Combine(Root, name), "<Project />"); }
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "local-only"), new XAttribute("value", Feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear")))).Save(Config);
        Assert.AreEqual("10.0.401", (await RunAsync(["--version"], Root)).StandardOutput.Trim());
        foreach ((string id, Assembly assembly) in assemblies)
        {
            Assert.AreEqual(Version, ProductVersion(assembly));
            payloadHashes[id] = CandidatePackageFixture.Hash(assembly.Location);
            environment["NUGET_PACKAGES"] = assets[id]["packageFolders"]!.AsObject().First().Key;
            string configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            await RunAsync(["pack", "src/" + id + "/" + id + ".csproj", "-c", configuration, "--no-build", "--no-restore", "-o", Feed], language);
            string path = Path.Combine(Feed, id + "." + Version + ".nupkg");
            using ZipArchive zip = ZipFile.OpenRead(path);
            using Stream dll = zip.GetEntry("lib/net10.0/" + id + ".dll")!.Open();
            Assert.AreEqual(payloadHashes[id], Convert.ToHexStringLower(SHA256.HashData(dll)), "Package the actual current " + id);
            packageHashes[id] = CandidatePackageFixture.Hash(path);
            selected.Add(id, Version);
        }
        CollectionAssert.AreEquivalent(new[] { "Microsoft.Extensions.Logging.Abstractions/10.0.12",
            "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12" }, Packages(assets["CIS.Core"]).ToArray());
        foreach (JsonObject input in assets.Values)
        {
            foreach (string package in Packages(input))
            {
                string[] parts = package.Split('/');
                if (selected.TryGetValue(parts[0], out string? previous)) { Assert.AreEqual(previous, parts[1]); }
                else { selected.Add(parts[0], parts[1]); }
                string id = parts[0].ToLowerInvariant(), version = parts[1].ToLowerInvariant(), name = id + "." + version + ".nupkg";
                string destination = Path.Combine(Feed, name);
                if (File.Exists(destination)) { continue; }
                string? source = input["packageFolders"]!.AsObject()
                    .Select(folder => Path.Combine(folder.Key, id, version, name)).FirstOrDefault(File.Exists);
                Assert.IsNotNull(source, "Use the selected cached dependency: " + package);
                File.Copy(source, destination);
            }
        }
        Assert.IsFalse(Directory.Exists(Cache));
        environment["NUGET_PACKAGES"] = Cache;
        environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "http-cache");
        environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(Root, "plugin-cache");
    }

    internal static JsonObject Assets(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    internal static IEnumerable<string> Packages(JsonObject assets) => assets["libraries"]!.AsObject()
        .Where(item => item.Value!["type"]!.GetValue<string>() == "package").Select(item => item.Key);

    internal void AssertRestored(string app, params string[] requiredIds)
    {
        JsonObject assets = Assets(Path.Combine(app, "obj/project.assets.json"));
        Assert.AreEqual(1, assets["packageFolders"]!.AsObject().Count);
        SamePath(Cache, assets["packageFolders"]!.AsObject().Single().Key);
        Assert.IsTrue(assets["libraries"]!.AsObject().All(item => item.Value!["type"]!.GetValue<string>() == "package"));
        foreach (string dependency in Packages(assets))
        {
            string[] parts = dependency.Split('/');
            Assert.IsTrue(selected.TryGetValue(parts[0], out string? version), dependency);
            Assert.AreEqual(version, parts[1]);
        }
        foreach (string id in requiredIds.Length == 0 ? assemblies.Keys.ToArray() : requiredIds)
        {
            Assert.IsTrue(Packages(assets).Contains(id + "/" + Version, StringComparer.Ordinal));
            string lower = id.ToLowerInvariant();
            Assert.AreEqual(packageHashes[id], CandidatePackageFixture.Hash(Path.Combine(Cache, lower, Version, lower + "." + Version + ".nupkg")));
        }
    }

    internal JsonObject AssertDeployed(string output, string assemblyName, params string[] requiredIds)
    {
        foreach (string id in requiredIds.Length == 0 ? payloadHashes.Keys.ToArray() : requiredIds)
        { Assert.AreEqual(payloadHashes[id], CandidatePackageFixture.Hash(Path.Combine(output, id + ".dll"))); }
        return Assets(Path.Combine(output, assemblyName + ".deps.json"));
    }

    internal string DependencyHash(string file, string assembly)
    {
        using ZipArchive zip = ZipFile.OpenRead(Path.Combine(Feed, file));
        using Stream dll = zip.GetEntry("lib/net10.0/" + assembly + ".dll")!.Open();
        return Convert.ToHexStringLower(SHA256.HashData(dll));
    }

    internal void AssertOriginalAssembliesUnchanged()
    {
        foreach ((string id, Assembly assembly) in assemblies)
        { Assert.AreEqual(payloadHashes[id], CandidatePackageFixture.Hash(assembly.Location)); }
        testContext.WriteLine("PACKAGES version=" + Version + "; SDK=10.0.401; runtime=10.0.12; "
            + string.Join("; ", packageHashes.Select(item => item.Key + "=" + item.Value)));
    }

    internal async Task<ProcessResult> RunAsync(string[] arguments, string working, TimeSpan? timeout = null)
    {
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, working, timeout ?? TimeSpan.FromSeconds(120),
                environment, new UTF8Encoding(false, true), maximumCapturedBytesPerStream: 262_144);
            cleanupConfirmed &= result.OutputDrained;
            Assert.IsTrue(result.OutputDrained && !result.CaptureTruncated);
            Assert.AreEqual(0, result.ExitCode, string.Join(' ', arguments) + "\n" + result.StandardOutput + result.StandardError);
            return result;
        }
        catch (ProcessTimeoutException error)
        { cleanupConfirmed &= error.CleanupCompleted; testContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError); throw; }
        catch (ProcessOutputLimitException error)
        { cleanupConfirmed &= error.Result.OutputDrained; testContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError); throw; }
    }

    internal static void SamePath(string expected, string actual) => Assert.IsTrue(string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    private static string ProductVersion(Assembly assembly) => FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion!.Split('+')[0];

    public void Dispose()
    {
        if (cleanupConfirmed) { directory.Dispose(); }
        else { testContext.WriteLine("Retained package consumer because child cleanup is unconfirmed: " + Root); }
    }
}
