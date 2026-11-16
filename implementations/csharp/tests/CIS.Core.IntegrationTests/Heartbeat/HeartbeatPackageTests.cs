using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Heartbeat;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Heartbeat;

[TestClass]
[DoNotParallelize]
public sealed class HeartbeatPackageTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task ActualPackagedPollingExampleBuildsPublishesRecoversAndDrainsItsRealScope()
    {
        var directory = new TemporaryDirectory();
        bool cleanupConfirmed = true;
        string root = directory.Path, repository = CandidatePackageFixture.Repository();
        string language = Path.Combine(repository, "implementations/csharp");
        string app = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
        string feed = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
        string cache = Path.Combine(root, "packages"), project = Path.Combine(app, "Polling.csproj");
        string currentCore = typeof(HeartbeatRuntime).Assembly.Location;
        string version = FileVersionInfo.GetVersionInfo(currentCore).ProductVersion!.Split('+')[0];
        string configuration = typeof(HeartbeatRuntime).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var environment = new Dictionary<string, string?>
        {
            ["DOTNET_CLI_HOME"] = Path.Combine(root, "cli"),
            ["NUGET_FALLBACK_PACKAGES"] = null, ["RestoreSources"] = feed,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1", ["UseSharedCompilation"] = "false", ["CIS_HEARTBEAT_MAX_CONCURRENT"] = null
        };
        try
        {
            JsonNode assets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "src/CIS.Core/obj/project.assets.json")))!;
            environment["NUGET_PACKAGES"] = assets["packageFolders"]!.AsObject().First().Key;
            File.Copy(Path.Combine(language, "global.json"), Path.Combine(root, "global.json"));
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><SelfContained>false</SelfContained>
                <NuGetAudit>false</NuGetAudit></PropertyGroup></Project>
                """);
            foreach (string name in new[] { "Directory.Build.targets", "Directory.Packages.props" })
            { File.WriteAllText(Path.Combine(root, name), "<Project />"); }
            new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
                new XElement("add", new XAttribute("key", "local-only"), new XAttribute("value", feed))),
                new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))))
                .Save(Path.Combine(root, "NuGet.Config"));
            string source = Path.Combine(language, "examples/heartbeat/Polling");
            foreach (string name in new[] { "Program.cs", "Polling.csproj" }) { File.Copy(Path.Combine(source, name), Path.Combine(app, name)); }
            string assembly = XDocument.Load(project).Descendants("AssemblyName").Single().Value;
            Assert.AreEqual(CandidatePackageFixture.Hash(Path.Combine(source, "Program.cs")), CandidatePackageFixture.Hash(Path.Combine(app, "Program.cs")));
            Assert.AreEqual("10.0.401", (await Run(["--version"], root)).StandardOutput.Trim());

            // Repack only the already-built candidate; never rebuild the shared Core while tests execute.
            await Run(["pack", "src/CIS.Core/CIS.Core.csproj", "-c", configuration, "--no-build", "--no-restore", "-o", feed], language);
            string package = Path.Combine(feed, "CIS.Core." + version + ".nupkg");
            string coreHash;
            using (ZipArchive zip = ZipFile.OpenRead(package))
            {
                using Stream dll = zip.GetEntry("lib/net10.0/CIS.Core.dll")!.Open();
                coreHash = Convert.ToHexStringLower(SHA256.HashData(dll));
                using Stream nuspec = zip.GetEntry("CIS.Core.nuspec")!.Open();
                Assert.AreEqual(version, XDocument.Load(nuspec).Descendants().Single(item => item.Name.LocalName == "version").Value);
            }
            Assert.AreEqual(CandidatePackageFixture.Hash(currentCore), coreHash, "The package must contain the actual current test-host Core.");
            string packageHash = CandidatePackageFixture.Hash(package);
            JsonNode policy = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "eng/runtime-dependencies.json")))!;
            CollectionAssert.AreEquivalent(new[] { "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12" },
                policy["packages"]!.AsArray().Select(item => item!["id"]!.GetValue<string>() + "/" + item["version"]!.GetValue<string>()).ToArray());
            foreach (JsonNode? dependency in policy["packages"]!.AsArray())
            {
                string id = dependency!["id"]!.GetValue<string>().ToLowerInvariant(), selected = dependency["version"]!.GetValue<string>();
                string name = id + "." + selected + ".nupkg";
                string cached = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, id, selected, name)).First(File.Exists);
                File.Copy(cached, Path.Combine(feed, name));
            }
            string coreLock = Path.Combine(language, "src/CIS.Core/packages.lock.json"), coreLockHash = CandidatePackageFixture.Hash(coreLock);
            string sourceProject = Path.Combine(source, "Polling.csproj");
            await Run(["restore", sourceProject, "--configfile", Path.Combine(root, "NuGet.Config"), "-p:RestorePackagesWithLockFile=true", "-p:CisHeartbeatUsePackage=false", "-p:NuGetAudit=false"], language);
            JsonNode sourceLock = JsonNode.Parse(File.ReadAllBytes(Path.Combine(source, "packages.lock.json")))!;
            Assert.IsTrue(sourceLock["dependencies"]!["net10.0"]!.AsObject().Any(item => item.Key.Equals("CIS.Core", StringComparison.OrdinalIgnoreCase)
                && item.Value!["type"]!.GetValue<string>() == "Project"), "The real source workflow must retain its actual Core project reference.");
            JsonNode sourceAssets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(source, "obj/project.assets.json")))!;
            JsonObject sourceFramework = sourceAssets["project"]!["frameworks"]!["net10.0"]!.AsObject();
            Assert.IsTrue(sourceFramework["frameworkReferences"]!.AsObject().ContainsKey("Microsoft.AspNetCore.App"),
                "The real source consumer must reference ASP.NET's shared framework; package pruning may remove redundant abstraction packages.");
            var approved = policy["packages"]!.AsArray().ToDictionary(item => item!["id"]!.GetValue<string>(),
                item => item!["version"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
            JsonObject retained = sourceLock["dependencies"]!["net10.0"]!.AsObject();
            foreach ((string id, JsonNode? dependency) in retained)
            {
                if (dependency!["type"]!.GetValue<string>() == "Project") { continue; }
                Assert.IsTrue(approved.TryGetValue(id, out string? selected), "An unapproved package remains in the source lock: " + id);
                Assert.AreEqual(selected, dependency!["resolved"]!.GetValue<string>(), "A retained source package must use its approved version.");
            }
            foreach (string id in approved.Keys)
            {
                bool packageRetained = retained.Any(item => item.Key.Equals(id, StringComparison.OrdinalIgnoreCase) && item.Value!["type"]!.GetValue<string>() != "Project");
                bool frameworkPruned = sourceFramework["packagesToPrune"]?.AsObject().Any(item => item.Key.Equals(id, StringComparison.OrdinalIgnoreCase)) == true;
                Assert.IsTrue(packageRetained || frameworkPruned, "An approved dependency is neither retained nor explicitly pruned by the selected framework: " + id);
            }
            Assert.AreEqual(coreLockHash, CandidatePackageFixture.Hash(coreLock), "Source example restore must not rewrite Core's existing lock contract.");
            await Run(["build", sourceProject, "-c", configuration, "--no-restore", "-p:BuildProjectReferences=false", "-p:CisHeartbeatUsePackage=false"], language);
            await VerifyConsumer(Path.Combine(source, "bin", configuration, "net10.0"), "source");
            environment["NUGET_PACKAGES"] = cache;
            environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(root, "http-cache");
            environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(root, "plugin-cache");
            Assert.IsFalse(Directory.Exists(cache) && Directory.EnumerateFileSystemEntries(cache).Any(), "The isolated consumer package cache must start empty.");
            string[] packageProperties = ["-p:CisHeartbeatUsePackage=true", "-p:CisHeartbeatPackageVersion=[" + version + "]"];
            await Run(["restore", project, "--configfile", Path.Combine(root, "NuGet.Config"), "--no-cache", .. packageProperties], root);
            JsonNode restored = JsonNode.Parse(File.ReadAllBytes(Path.Combine(app, "obj/project.assets.json")))!;
            Assert.AreEqual(1, restored["packageFolders"]!.AsObject().Count);
            Assert.AreEqual(Path.GetFullPath(cache), Path.TrimEndingDirectorySeparator(Path.GetFullPath(restored["packageFolders"]!.AsObject().Single().Key)));
            Assert.IsTrue(restored["targets"]!["net10.0"]!.AsObject().All(item => item.Value!["type"]!.GetValue<string>() == "package"), "An isolated consumer cannot resolve a project reference.");
            Assert.AreEqual(packageHash, CandidatePackageFixture.Hash(Path.Combine(cache, "cis.core", version, "cis.core." + version + ".nupkg")));
            await Run(["build", project, "-c", "Release", "--no-restore", .. packageProperties], root);
            string built = Path.Combine(app, "bin/Release/net10.0"), published = Path.Combine(root, "published");
            await VerifyConsumer(built, "build");
            await Run(["publish", project, "-c", "Release", "--no-restore", "-o", published, .. packageProperties], root);
            await VerifyConsumer(published, "publish");
            Assert.AreEqual(coreHash, CandidatePackageFixture.Hash(currentCore));
            Assert.AreEqual(packageHash, CandidatePackageFixture.Hash(package));
            TestContext.WriteLine($"HEARTBEAT-PACKAGE version={version}; packageSha256={packageHash}; coreSha256={coreHash}; source/build/publish all executed.");

            async Task VerifyConsumer(string output, string lane)
            {
                Assert.AreEqual(coreHash, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.Core.dll")), lane + " must execute the exact packed Core.");
                string working = Directory.CreateDirectory(Path.Combine(root, "run-" + lane)).FullName;
                ProcessResult process = await Run([Path.Combine(output, assembly + ".dll")], working, TimeSpan.FromSeconds(30));
                string text = process.StandardOutput;
                string[] expected = ["negative result (no exception handler)", "awaited recovery: temporary polling dependency failure", "refresh succeeded",
                    "exception handler ran 1 time(s)", "heartbeat drained; scoped dependency disposed safely"];
                int previous = -1;
                foreach (string message in expected)
                {
                    int at = text.IndexOf(message, StringComparison.Ordinal);
                    Assert.IsTrue(at > previous, lane + " missing or out-of-order lifecycle evidence: " + message + "\n" + text);
                    previous = at;
                }
                Assert.AreEqual(1, text.Split("awaited recovery:", StringSplitOptions.None).Length - 1);
                Assert.IsFalse(process.StandardError.Contains("Unhandled exception", StringComparison.Ordinal));
                TestContext.WriteLine(lane + " output:\n" + text);
            }
        }
        finally
        {
            if (cleanupConfirmed) { directory.Dispose(); }
            else { TestContext.WriteLine("Retained child root because process cleanup is unconfirmed: " + root); }
        }

        async Task<ProcessResult> Run(string[] arguments, string working, TimeSpan? timeout = null)
        {
            try
            {
                ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, working, timeout ?? TimeSpan.FromSeconds(90),
                    environment, maximumCapturedBytesPerStream: 262_144);
                cleanupConfirmed &= result.OutputDrained;
                Assert.IsTrue(result.OutputDrained && !result.CaptureTruncated, "Complete bounded process capture is required.");
                Assert.AreEqual(0, result.ExitCode, string.Join(' ', arguments) + "\n" + result.StandardOutput + result.StandardError);
                return result;
            }
            catch (ProcessTimeoutException error)
            {
                cleanupConfirmed &= error.CleanupCompleted;
                TestContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError);
                Assert.IsTrue(error.CleanupCompleted, "A child timeout left process cleanup unconfirmed.");
                throw;
            }
            catch (ProcessOutputLimitException error) { TestContext.WriteLine(error.Result.StandardOutput + error.Result.StandardError); throw; }
        }
    }
}
