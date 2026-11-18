using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Metrics.OpenTelemetry;
using CIS.RestClient.Http;
using CIS.RestClient.Resilience;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
[DoNotParallelize]
public sealed class RestClientPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualCoreOnlyAndOptionalPackagesBuildPublishAndExecuteTheirPublicRestWorkflows()
    {
        var directory = new TemporaryDirectory();
        bool cleanupConfirmed = true;
        string root = directory.Path, language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
        string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal)
        {
            ["CIS.Core"] = typeof(HttpRestClient).Assembly,
            ["CIS.RestClient.Resilience"] = typeof(RestClientResilienceServiceCollectionExtensions).Assembly,
            ["CIS.Metrics.OpenTelemetry"] = typeof(MetricsOptions).Assembly
        };
        var environment = CandidatePackageFixture.Variables(root, new Dictionary<string, string?>
        {
            ["CORE_SERVICE_NAME"] = "RestClient.Package", ["CORE_ENVIRONMENT"] = "package-acceptance",
            ["DOTNET_CLI_HOME"] = Path.Combine(root, "cli"), ["NUGET_FALLBACK_PACKAGES"] = null,
            ["RestoreSources"] = feed, ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1", ["UseSharedCompilation"] = "false"
        });
        try
        {
            string version = Version(assemblies["CIS.Core"]);
            var assets = assemblies.Keys.ToDictionary(id => id, id => JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "src", id, "obj/project.assets.json")))!.AsObject(), StringComparer.Ordinal);
            var payloadHashes = assemblies.ToDictionary(pair => pair.Key, pair => CandidatePackageFixture.Hash(pair.Value.Location), StringComparer.Ordinal);
            var packageHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var selectedPackages = SelectedPackages(assets.Values.SelectMany(Packages));
            foreach (string id in assemblies.Keys) { selectedPackages.Add(id, version); }
            File.Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json"));
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><SelfContained>false</SelfContained>
                <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion><RollForward>Disable</RollForward>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
                """);
            foreach (string name in new[] { "Directory.Build.targets", "Directory.Packages.props" }) { File.WriteAllText(Path.Combine(root, name), "<Project />"); }
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "local-only"), new XAttribute("value", feed))),
                new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))))
                .Save(Path.Combine(root, "NuGet.Config"));
            Assert.AreEqual("10.0.401", (await Run(["--version"], root)).StandardOutput.Trim());

            foreach ((string id, Assembly assembly) in assemblies)
            {
                Assert.AreEqual(version, Version(assembly));
                // Packing reads the already-built assets and their original cache; consumer isolation starts afterward.
                environment["NUGET_PACKAGES"] = assets[id]["packageFolders"]!.AsObject().First().Key;
                string configuration = assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
                await Run(["pack", "src/" + id + "/" + id + ".csproj", "-c", configuration, "--no-build", "--no-restore", "-o", feed], language);
                string package = Path.Combine(feed, id + "." + version + ".nupkg");
                using ZipArchive zip = ZipFile.OpenRead(package);
                using Stream dll = zip.GetEntry("lib/net10.0/" + id + ".dll")!.Open();
                Assert.AreEqual(payloadHashes[id], Convert.ToHexStringLower(SHA256.HashData(dll)), "Pack the actual current " + id);
                packageHashes[id] = CandidatePackageFixture.Hash(package);
            }
            CollectionAssert.AreEquivalent(new[] { "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12" },
                Packages(assets["CIS.Core"]).ToArray(), "Core's approved package closure is unchanged.");
            CollectionAssert.AreEqual(new[] { "Polly.Core/8.9.1" }, Packages(assets["CIS.RestClient.Resilience"]).ToArray());
            Assert.IsTrue(Packages(assets["CIS.Metrics.OpenTelemetry"]).Contains("System.Diagnostics.DiagnosticSource/11.0.0", StringComparer.Ordinal));
            foreach (JsonObject input in assets.Values)
            {
                foreach (string package in Packages(input))
                {
                    string[] parts = package.Split('/'); string id = parts[0].ToLowerInvariant(), selected = parts[1].ToLowerInvariant();
                    string name = id + "." + selected + ".nupkg", destination = Path.Combine(feed, name);
                    if (File.Exists(destination)) { continue; }
                    string? cached = input["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, id, selected, name)).FirstOrDefault(File.Exists);
                    Assert.IsNotNull(cached, "Use the selected cached dependency: " + package); File.Copy(cached, destination);
                }
            }
            string pollyHash = DependencyHash("polly.core.8.9.1.nupkg", "Polly.Core");
            string diagnosticHash = DependencyHash("system.diagnostics.diagnosticsource.11.0.0.nupkg", "System.Diagnostics.DiagnosticSource");

            await Consumer(optional: false);
            await Consumer(optional: true);
            foreach ((string id, Assembly assembly) in assemblies) { Assert.AreEqual(payloadHashes[id], CandidatePackageFixture.Hash(assembly.Location)); }
            TestContext.WriteLine("REST-PACKAGE version=" + version + "; Core=" + packageHashes["CIS.Core"]
                + "; Resilience=" + packageHashes["CIS.RestClient.Resilience"] + "; Metrics=" + packageHashes["CIS.Metrics.OpenTelemetry"]
                + "; SDK=10.0.401; runtime=10.0.12; Polly.Core=8.9.1; DiagnosticSource=11.0.0; two consumers build/publish executed.");

            async Task Consumer(bool optional)
            {
                string profile = optional ? "optional" : "core-only";
                string app = Directory.CreateDirectory(Path.Combine(root, profile)).FullName;
                string cache = Path.Combine(root, profile + "-packages"), project = Path.Combine(app, "Consumer.csproj");
                XDocument consumer;
                string assemblyName;
                if (optional)
                {
                    string source = Path.Combine(language, "examples/rest-client/RestApi");
                    foreach (string file in Directory.GetFiles(source, "*.cs"))
                    {
                        string copied = Path.Combine(app, Path.GetFileName(file)); File.Copy(file, copied);
                        Assert.AreEqual(CandidatePackageFixture.Hash(file), CandidatePackageFixture.Hash(copied));
                    }
                    consumer = XDocument.Load(Path.Combine(source, "RestApi.csproj"));
                    consumer.Descendants("ProjectReference").Remove(); consumer.Descendants("Import").Remove();
                    assemblyName = consumer.Descendants("AssemblyName").Single().Value;
                    Assert.AreEqual("CIS.RestClient.Examples.RestApi", assemblyName);
                    consumer.Root!.Add(new XElement("ItemGroup", new[] { "CIS.RestClient.Resilience", "CIS.Metrics.OpenTelemetry" }
                        .Select(id => new XElement("PackageReference", new XAttribute("Include", id), new XAttribute("Version", "[" + version + "]")))));
                }
                else
                {
                    assemblyName = "CIS.RestClient.PackageCoreOnly";
                    File.WriteAllText(Path.Combine(app, "Program.cs"), CoreOnlyProgram);
                    consumer = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk.Web"),
                        new XElement("PropertyGroup", new XElement("AssemblyName", assemblyName))));
                }
                consumer.Root!.AddFirst(new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", version)));
                consumer.Save(project);
                Assert.IsFalse(consumer.Descendants("ProjectReference").Any());
                Assert.IsFalse(consumer.Descendants("PackageReference").Any(item => (string?)item.Attribute("Include") == "CIS.Core"), "The versioned Core SDK owns its exact runtime reference.");
                Assert.IsFalse(Directory.Exists(cache), "Each consumer starts with its own empty NuGet cache.");
                environment["NUGET_PACKAGES"] = cache; environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, profile + "-http-cache");
                environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(root, profile + "-plugin-cache");
                await Run(["restore", project, "--configfile", Path.Combine(root, "NuGet.Config"), "--no-cache"], root);
                JsonObject restored = JsonNode.Parse(File.ReadAllBytes(Path.Combine(app, "obj/project.assets.json")))!.AsObject();
                Assert.AreEqual(1, restored["packageFolders"]!.AsObject().Count);
                SamePath(cache, restored["packageFolders"]!.AsObject().Single().Key);
                Assert.IsTrue(restored["libraries"]!.AsObject().All(item => item.Value!["type"]!.GetValue<string>() == "package"));
                foreach (string selected in Packages(restored))
                {
                    string[] parts = selected.Split('/'); Assert.IsTrue(selectedPackages.TryGetValue(parts[0], out string? expected), selected);
                    Assert.AreEqual(expected, parts[1]);
                    if (!optional) { Assert.IsTrue(parts[0] is "CIS.Core" or "Microsoft.Extensions.Logging.Abstractions" or "Microsoft.Extensions.DependencyInjection.Abstractions"); }
                }
                string[] required = optional ? assemblies.Keys.ToArray() : ["CIS.Core"];
                foreach (string id in required)
                {
                    Assert.IsTrue(Packages(restored).Contains(id + "/" + version, StringComparer.Ordinal));
                    string lower = id.ToLowerInvariant();
                    Assert.AreEqual(packageHashes[id], CandidatePackageFixture.Hash(Path.Combine(cache, lower, version, lower + "." + version + ".nupkg")));
                }
                if (optional) { Assert.IsTrue(Packages(restored).Contains("Polly.Core/8.9.1", StringComparer.Ordinal)); }
                await Run(["build", project, "-c", "Release", "--no-restore"], root);
                await Verify(Path.Combine(app, "bin/Release/net10.0"), "build");
                string published = Path.Combine(root, profile + "-published");
                await Run(["publish", project, "-c", "Release", "--no-restore", "-o", published], root);
                await Verify(published, "publish");

                async Task Verify(string output, string lane)
                {
                    foreach (string id in required) { Assert.AreEqual(payloadHashes[id], CandidatePackageFixture.Hash(Path.Combine(output, id + ".dll"))); }
                    JsonObject deps = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, assemblyName + ".deps.json")))!.AsObject();
                    string[] names = deps["libraries"]!.AsObject().Select(pair => pair.Key).ToArray();
                    if (optional)
                    {
                        Assert.IsTrue(names.Contains("Polly.Core/8.9.1", StringComparer.Ordinal));
                        Assert.IsTrue(names.Contains("System.Diagnostics.DiagnosticSource/11.0.0", StringComparer.Ordinal));
                        Assert.AreEqual(pollyHash, CandidatePackageFixture.Hash(Path.Combine(output, "Polly.Core.dll")));
                        Assert.AreEqual(diagnosticHash, CandidatePackageFixture.Hash(Path.Combine(output, "System.Diagnostics.DiagnosticSource.dll")));
                    }
                    else
                    {
                        Assert.IsFalse(names.Any(name => name.StartsWith("Polly", StringComparison.OrdinalIgnoreCase) || name.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("CIS.RestClient.Resilience/", StringComparison.Ordinal) || name.StartsWith("CIS.Metrics.OpenTelemetry/", StringComparison.Ordinal)
                            || name.StartsWith("System.Diagnostics.DiagnosticSource/", StringComparison.Ordinal)));
                        foreach (string file in new[] { "Polly.Core.dll", "CIS.RestClient.Resilience.dll", "CIS.Metrics.OpenTelemetry.dll", "System.Diagnostics.DiagnosticSource.dll" })
                        { Assert.IsFalse(File.Exists(Path.Combine(output, file)), "Core-only output cannot privately deploy " + file); }
                    }
                    string working = Directory.CreateDirectory(Path.Combine(root, "run-" + profile + "-" + lane)).FullName;
                    ProcessResult process = await Run(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(output, assemblyName + ".dll"), "--self-test"], working, TimeSpan.FromSeconds(45));
                    Assert.AreEqual("", process.StandardError);
                    JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
                    Assert.AreEqual(optional ? "RestClient.RestApi" : "RestClient.CoreOnly", report["example"]!.GetValue<string>());
                    JsonObject runtime = report["runtime"]!.AsObject();
                    Assert.AreEqual(".NET 10.0.12", runtime["framework"]!.GetValue<string>());
                    SamePath(Path.Combine(output, "CIS.Core.dll"), runtime["corePath"]!.GetValue<string>());
                    Assert.IsTrue(report["stopped"]!.GetValue<bool>());
                    if (optional)
                    {
                        Assert.IsTrue(report["postSuccess"]!.GetValue<bool>()); Assert.AreEqual(3, report["retryAttempts"]!.GetValue<int>());
                        Assert.AreEqual("orders.declined", report["businessCode"]!.GetValue<string>()); Assert.IsFalse(report["emptyHasValue"]!.GetValue<bool>());
                        Assert.IsTrue(report["cancellationPreserved"]!.GetValue<bool>()); Assert.AreEqual(5d, report["logicalCompletions"]!.GetValue<double>());
                        Assert.AreEqual(0d, report["active"]!.GetValue<double>()); Assert.IsTrue(report["nativeCount"]!.GetValue<double>() >= 6);
                        Assert.IsTrue(report["allSamplesHaveIdentity"]!.GetValue<bool>());
                        SamePath(Path.Combine(output, "CIS.Metrics.OpenTelemetry.dll"), runtime["metricsPath"]!.GetValue<string>());
                        SamePath(Path.Combine(output, "CIS.RestClient.Resilience.dll"), runtime["resiliencePath"]!.GetValue<string>());
                        SamePath(Path.Combine(output, "System.Diagnostics.DiagnosticSource.dll"), runtime["diagnosticSourcePath"]!.GetValue<string>());
                        Assert.AreEqual("11.0.0.0", runtime["diagnosticSourceVersion"]!.GetValue<string>());
                        SamePath(Path.Combine(output, "Polly.Core.dll"), runtime["pollyPath"]!.GetValue<string>());
                    }
                    else
                    {
                        Assert.IsTrue(report["success"]!.GetValue<bool>()); Assert.AreEqual(17, report["value"]!.GetValue<int>());
                        Assert.AreEqual(1, report["attempts"]!.GetValue<int>()); Assert.IsFalse(report["optionalLoaded"]!.GetValue<bool>());
                    }
                    TestContext.WriteLine(profile + "/" + lane + ": " + process.StandardOutput);
                }
            }

            string DependencyHash(string file, string id)
            { using ZipArchive zip = ZipFile.OpenRead(Path.Combine(feed, file)); using Stream dll = zip.GetEntry("lib/net10.0/" + id + ".dll")!.Open(); return Convert.ToHexStringLower(SHA256.HashData(dll)); }
        }
        finally
        {
            if (cleanupConfirmed) { directory.Dispose(); }
            else { TestContext.WriteLine("Retained REST consumers because child cleanup is unconfirmed: " + root); }
        }

        async Task<ProcessResult> Run(string[] arguments, string working, TimeSpan? timeout = null)
        {
            try
            {
                ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, working, timeout ?? TimeSpan.FromSeconds(120), environment,
                    new UTF8Encoding(false, true), maximumCapturedBytesPerStream: 262_144);
                cleanupConfirmed &= result.OutputDrained;
                Assert.IsTrue(result.OutputDrained && !result.CaptureTruncated);
                Assert.AreEqual(0, result.ExitCode, string.Join(' ', arguments) + "\n" + result.StandardOutput + result.StandardError);
                return result;
            }
            catch (ProcessTimeoutException error) { cleanupConfirmed &= error.CleanupCompleted; TestContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError); throw; }
            catch (ProcessOutputLimitException error) { cleanupConfirmed &= error.Result.OutputDrained; TestContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError); throw; }
        }
    }

    private static string Version(Assembly assembly) => FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion!.Split('+')[0];
    private static IEnumerable<string> Packages(JsonObject assets) => assets["libraries"]!.AsObject()
        .Where(item => item.Value!["type"]!.GetValue<string>() == "package").Select(item => item.Key);
    private static void SamePath(string expected, string actual) => Assert.IsTrue(string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual)), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    private static Dictionary<string, string> SelectedPackages(IEnumerable<string> packages)
    {
        var selected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string package in packages)
        {
            string[] parts = package.Split('/');
            if (selected.TryGetValue(parts[0], out string? previous)) { Assert.AreEqual(previous, parts[1]); }
            else { selected.Add(parts[0], parts[1]); }
        }
        return selected;
    }

    private const string CoreOnlyProgram = """
        using System.Runtime.InteropServices;
        using System.Text.Json;
        using CIS.RestClient;
        using CIS.RestClient.Http;
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build(); app.MapGet("/value", () => new Receipt(17));
        bool success = false; int value = 0, attempts = 0;
        try
        {
            await app.StartAsync();
            await using var client = new HttpRestClient(new RestClientOptions("core-only") { BaseUrl = app.Urls.Single() + "/", RetryPolicy = RestRetrySelection.None });
            RestResult<Receipt> result = await client.Get<Receipt>("value");
            success = result.Success && result.Context.HasValue; value = result.Value?.Count ?? 0; attempts = result.Context.AttemptCount;
        }
        finally { try { await app.StopAsync(CancellationToken.None); } finally { await app.DisposeAsync(); } }
        bool optionalLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
            assembly.GetName().Name is { } name && (name.StartsWith("Polly", StringComparison.Ordinal) || name.StartsWith("OpenTelemetry", StringComparison.Ordinal)
                || name == "CIS.RestClient.Resilience" || name == "CIS.Metrics.OpenTelemetry"));
        Console.WriteLine(JsonSerializer.Serialize(new { example = "RestClient.CoreOnly", success, value, attempts, optionalLoaded,
            runtime = new { framework = RuntimeInformation.FrameworkDescription, corePath = typeof(HttpRestClient).Assembly.Location }, stopped = true }));
        public sealed record Receipt(int Count);
        """;
}
