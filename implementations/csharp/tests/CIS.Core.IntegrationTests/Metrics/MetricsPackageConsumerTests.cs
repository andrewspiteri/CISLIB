using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Metrics.OpenTelemetry;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
[DoNotParallelize]
public sealed class MetricsPackageConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualCoreAndOptionalPackagesBuildPublishAndRunTheSameMetricApiOnNet10WithDiagnosticSource11()
    {
        var directory = new TemporaryDirectory();
        bool cleanupConfirmed = true;
        string root = directory.Path, language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
        string app = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        string cache = Path.Combine(root, "packages"), project = Path.Combine(app, "MetricApi.csproj");
        string core = typeof(EnvironmentOptions).Assembly.Location, optional = typeof(MetricsOptions).Assembly.Location;
        string configuration = typeof(MetricsOptions).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string version = FileVersionInfo.GetVersionInfo(core).ProductVersion!.Split('+')[0];
        Assert.AreEqual(version, FileVersionInfo.GetVersionInfo(optional).ProductVersion!.Split('+')[0]);
        string identity = "Metrics." + new string('界', 100);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(identity) > 256 && identity.EnumerateRunes().Count() <= 255);
        var environment = CandidatePackageFixture.Variables(root, new Dictionary<string, string?>
        {
            ["CORE_SERVICE_NAME"] = identity,
            ["CORE_ENVIRONMENT"] = "package-acceptance",
            ["DOTNET_CLI_HOME"] = Path.Combine(root, "cli"),
            ["NUGET_PACKAGES"] = cache,
            ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "http-cache"),
            ["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(root, "plugin-cache"),
            ["NUGET_FALLBACK_PACKAGES"] = null,
            ["RestoreSources"] = feed,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["UseSharedCompilation"] = "false"
        });
        try
        {
            File.Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json"));
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                <SelfContained>false</SelfContained><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
                """);
            foreach (string name in new[] { "Directory.Build.targets", "Directory.Packages.props" })
            { File.WriteAllText(Path.Combine(root, name), "<Project />"); }
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "local-only"), new XAttribute("value", feed))),
                new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))))
                .Save(Path.Combine(root, "NuGet.Config"));
            string source = Path.Combine(language, "examples/metrics/MetricApi");
            foreach (string file in Directory.GetFiles(source, "*.cs"))
            {
                string copied = Path.Combine(app, Path.GetFileName(file));
                File.Copy(file, copied);
                Assert.AreEqual(CandidatePackageFixture.Hash(file), CandidatePackageFixture.Hash(copied));
            }
            XDocument consumer = XDocument.Load(Path.Combine(source, "MetricApi.csproj"));
            consumer.Descendants("ProjectReference").Remove();
            consumer.Descendants("Import").Remove();
            // The existing Core SDK supplies its exact matching runtime PackageReference
            // and the early inventory/RID import required by packaged Type Manager.
            consumer.Root!.AddFirst(new XElement("Sdk", new XAttribute("Name", "CIS.Core"), new XAttribute("Version", version)));
            consumer.Root!.Add(new XElement("ItemGroup",
                new XElement("PackageReference", new XAttribute("Include", "CIS.Metrics.OpenTelemetry"), new XAttribute("Version", "[" + version + "]"))));
            consumer.Save(project);
            Assert.IsFalse(XDocument.Load(project).Descendants("ProjectReference").Any());
            Assert.AreEqual("10.0.401", (await Run(["--version"], root)).StandardOutput.Trim());

            // Repack already-built artifacts only; native execution never rebuilds a shared project.
            JsonObject coreAssets = Assets("src/CIS.Core/obj/project.assets.json");
            JsonObject optionalAssets = Assets("src/CIS.Metrics.OpenTelemetry/obj/project.assets.json");
            environment["NUGET_PACKAGES"] = optionalAssets["packageFolders"]!.AsObject().First().Key;
            await Run(["pack", "src/CIS.Core/CIS.Core.csproj", "-c", configuration, "--no-build", "--no-restore", "-o", feed], language);
            await Run(["pack", "src/CIS.Metrics.OpenTelemetry/CIS.Metrics.OpenTelemetry.csproj", "-c", configuration,
                "--no-build", "--no-restore", "-o", feed], language);
            var payloads = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["CIS.Core"] = core, ["CIS.Metrics.OpenTelemetry"] = optional };
            var packageHashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string id, string current) in payloads)
            {
                string package = Path.Combine(feed, id + "." + version + ".nupkg");
                using ZipArchive zip = ZipFile.OpenRead(package);
                using Stream dll = zip.GetEntry("lib/net10.0/" + id + ".dll")!.Open();
                Assert.AreEqual(CandidatePackageFixture.Hash(current), Convert.ToHexStringLower(SHA256.HashData(dll)), "Pack the actual current " + id);
                packageHashes[id] = CandidatePackageFixture.Hash(package);
            }
            CollectionAssert.AreEquivalent(new[] { "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12" },
                Packages(coreAssets).ToArray(), "Core retains exactly its approved Microsoft package closure.");
            Assert.IsTrue(Packages(optionalAssets).Contains("OpenTelemetry/1.21.0", StringComparer.Ordinal));
            Assert.IsTrue(Packages(optionalAssets).Contains("OpenTelemetry.Exporter.Prometheus.AspNetCore/1.21.0-beta.1", StringComparer.Ordinal));
            Assert.IsTrue(Packages(optionalAssets).Contains("System.Diagnostics.DiagnosticSource/11.0.0", StringComparer.Ordinal));
            foreach (JsonObject assets in new[] { coreAssets, optionalAssets, Assets("examples/metrics/MetricApi/obj/project.assets.json") })
            {
                foreach (string selected in Packages(assets))
                {
                    string[] parts = selected.Split('/');
                    string id = parts[0].ToLowerInvariant(), selectedVersion = parts[1].ToLowerInvariant();
                    string file = id + "." + selectedVersion + ".nupkg";
                    string destination = Path.Combine(feed, file);
                    if (File.Exists(destination)) { continue; }
                    string? cached = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, id, selectedVersion, file)).FirstOrDefault(File.Exists);
                    Assert.IsNotNull(cached, "Use the actual selected cached package: " + selected);
                    File.Copy(cached, destination);
                }
            }
            Assert.IsFalse(Directory.Exists(cache), "The isolated package consumer starts with a fresh cache.");
            environment["NUGET_PACKAGES"] = cache;
            await Run(["restore", project, "--configfile", Path.Combine(root, "NuGet.Config"), "--no-cache"], root);
            JsonObject restored = JsonNode.Parse(File.ReadAllBytes(Path.Combine(app, "obj/project.assets.json")))!.AsObject();
            Assert.IsTrue(restored["libraries"]!.AsObject().All(item => item.Value!["type"]!.GetValue<string>() == "package"));
            Assert.AreEqual(1, restored["packageFolders"]!.AsObject().Count);
            Assert.AreEqual(Path.GetFullPath(cache), Path.TrimEndingDirectorySeparator(Path.GetFullPath(restored["packageFolders"]!.AsObject().Single().Key)));
            foreach ((string id, string hash) in packageHashes)
            {
                string lower = id.ToLowerInvariant();
                Assert.AreEqual(hash, CandidatePackageFixture.Hash(Path.Combine(cache, lower, version, lower + "." + version + ".nupkg")));
            }
            await Run(["build", project, "-c", "Release", "--no-restore"], root);
            await VerifyConsumer(Path.Combine(app, "bin/Release/net10.0"), "build");
            string published = Path.Combine(root, "published");
            await Run(["publish", project, "-c", "Release", "--no-restore", "-o", published], root);
            await VerifyConsumer(published, "publish");
            TestContext.WriteLine("METRICS-PACKAGE Core=" + packageHashes["CIS.Core"] + "; optional=" + packageHashes["CIS.Metrics.OpenTelemetry"]
                + "; SDK=10.0.401; runtime=10.0.12; OpenTelemetry=1.21.0; exporter=1.21.0-beta.1; DiagnosticSource=11.0.0; build/publish executed.");

            JsonObject Assets(string relative) => JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, relative)))!.AsObject();
            static IEnumerable<string> Packages(JsonObject assets) => assets["libraries"]!.AsObject()
                .Where(item => item.Value!["type"]!.GetValue<string>() == "package").Select(item => item.Key);

            async Task VerifyConsumer(string output, string lane)
            {
                foreach ((string id, string current) in payloads)
                { Assert.AreEqual(CandidatePackageFixture.Hash(current), CandidatePackageFixture.Hash(Path.Combine(output, id + ".dll"))); }
                string diagnostic = Path.Combine(output, "System.Diagnostics.DiagnosticSource.dll");
                Assert.IsTrue(File.Exists(diagnostic), lane + " must deploy the optional dependency rather than require runtime 11.");
                Assert.AreEqual(11, AssemblyName.GetAssemblyName(diagnostic).Version!.Major);
                JsonObject deps = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, "CIS.Metrics.Examples.MetricApi.deps.json")))!.AsObject();
                Assert.IsTrue(deps["libraries"]!.AsObject().ContainsKey("System.Diagnostics.DiagnosticSource/11.0.0"));
                string working = Directory.CreateDirectory(Path.Combine(root, "run-" + lane)).FullName;
                ProcessResult process = await Run(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                    Path.Combine(output, "CIS.Metrics.Examples.MetricApi.dll"), "--self-test"], working, TimeSpan.FromSeconds(45));
                Assert.AreEqual("", process.StandardError);
                JsonObject report = JsonNode.Parse(process.StandardOutput)!.AsObject();
                Assert.AreEqual("Metrics.MetricApi", report["example"]!.GetValue<string>());
                Assert.AreEqual(200, report["before"]!["metricsStatus"]!.GetValue<int>());
                Assert.AreEqual(500, report["before"]!["readinessStatus"]!.GetValue<int>());
                Assert.AreEqual(503, report["before"]!["businessStatus"]!.GetValue<int>());
                Assert.AreEqual(0, report["before"]!["businessCalls"]!.GetValue<int>());
                Assert.IsTrue(report["before"]!["startupPending"]!.GetValue<bool>());
                Assert.AreEqual(200, report["after"]!["businessStatus"]!.GetValue<int>());
                Assert.IsTrue(report["after"]!["sameTypeManager"]!.GetValue<bool>());
                Assert.AreEqual(identity, report["metrics"]!["application"]!.GetValue<string>());
                Assert.AreEqual("package-acceptance", report["metrics"]!["environment"]!.GetValue<string>());
                Assert.IsTrue(report["metrics"]!["allSamplesHaveIdentity"]!.GetValue<bool>());
                Assert.AreEqual(1d, report["metrics"]!["completed"]!.GetValue<double>());
                Assert.AreEqual(1d, report["metrics"]!["durationCount"]!.GetValue<double>());
                Assert.AreEqual(".NET 10.0.12", report["runtime"]!["framework"]!.GetValue<string>());
                Assert.AreEqual("11.0.0.0", report["runtime"]!["diagnosticSourceVersion"]!.GetValue<string>());
                foreach ((string field, string file) in new[] { ("corePath", "CIS.Core.dll"), ("metricsPath", "CIS.Metrics.OpenTelemetry.dll"),
                    ("diagnosticSourcePath", "System.Diagnostics.DiagnosticSource.dll") })
                {
                    Assert.IsTrue(string.Equals(Path.GetFullPath(Path.Combine(output, file)), Path.GetFullPath(report["runtime"]![field]!.GetValue<string>()),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Load the actual isolated " + file);
                }
                Assert.IsTrue(report["stopped"]!.GetValue<bool>());
                TestContext.WriteLine(lane + ": " + process.StandardOutput);
            }
        }
        finally
        {
            if (cleanupConfirmed) { directory.Dispose(); }
            else { TestContext.WriteLine("Retained Metrics consumer directory because child cleanup is unconfirmed: " + root); }
        }

        async Task<ProcessResult> Run(string[] arguments, string working, TimeSpan? timeout = null)
        {
            try
            {
                ProcessResult process = await ProcessRunner.RunAsync("dotnet", arguments, working, timeout ?? TimeSpan.FromSeconds(120),
                    environment, new UTF8Encoding(false, true), maximumCapturedBytesPerStream: 262_144);
                cleanupConfirmed &= process.OutputDrained;
                Assert.IsTrue(process.OutputDrained && !process.CaptureTruncated);
                Assert.AreEqual(0, process.ExitCode, string.Join(' ', arguments) + "\n" + process.StandardOutput + process.StandardError);
                return process;
            }
            catch (ProcessTimeoutException error)
            {
                cleanupConfirmed &= error.CleanupCompleted;
                TestContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError);
                throw;
            }
            catch (ProcessOutputLimitException error) { TestContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError); throw; }
        }
    }
}
