using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Healthcheck;
using CIS.TestSupport;
using CIS.Tests.BusinessCases.Healthcheck;
using CIS.Tests.IntegrationTests.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

// The maintained application and probe sources, one exact package and a disposable feed/cache.
internal sealed class CountryListPackageFixture : IDisposable
{
    private const string ApplicationAssembly = "CIS.Healthcheck.Examples.CountryList";
    private readonly TemporaryDirectory directory = new();
    private readonly Dictionary<string, string?> environment;
    private readonly string language = Path.Combine(CandidatePackageFixture.Repository(), "implementations/csharp");
    private readonly Dictionary<string, string> sourceHashes = new(StringComparer.Ordinal);
    private string version = "", packageHash = "", coreHash = "";
    private string? published;
    private string Root => directory.Path;
    private string Cache => Path.Combine(Root, "packages");
    private string ApplicationProject => Path.Combine(Root, "application/CountryList.csproj");
    private string ProbeProject => Path.Combine(Root, "probe/CisHealthProbe.csproj");
    private string BuildOutput => Path.Combine(Root, "application/bin/Release/net10.0");
    private string ProbeOutput => Path.Combine(Root, "probe/bin/Release/net10.0");

    private CountryListPackageFixture()
    {
        environment = CandidatePackageFixture.Variables(Root);
        foreach (string key in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key =>
            key.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("CIS_HEALTHCHECK_", StringComparison.OrdinalIgnoreCase)
            || key is "DD_SERVICE" or "DD_ENV" or "DD_VERSION")) { environment[key] = null; }
        foreach (string key in new[] { "CisTypeManagerEnabled", "RuntimeIdentifier", "RuntimeIdentifiers", "UseAppHost",
            "PublishSingleFile", "PublishTrimmed", "PublishAot", "DOTNET_ADDITIONAL_DEPS", "DOTNET_STARTUP_HOOKS", "DOTNET_SHARED_STORE" })
        { environment[key] = null; }
        environment["NUGET_PACKAGES"] = Cache;
        environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(Root, "http-cache");
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        environment["UseSharedCompilation"] = "false";
        environment["Logging__LogLevel__Default"] = "None";
        environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
    }

    internal static async Task<CountryListPackageFixture> CreateAsync()
    {
        var fixture = new CountryListPackageFixture();
        try { await fixture.PrepareAsync(); return fixture; }
        catch { fixture.Dispose(); throw; }
    }

    internal string Summary => JsonSerializer.Serialize(new
    {
        packageVersion = version, packageSha256 = packageHash, coreSha256 = coreHash, sourceHashes,
        runtime = RuntimeInformation.FrameworkDescription, platform = RuntimeInformation.RuntimeIdentifier,
        scope = "Actual CountryList and matching probe; isolated PackageReference; build and folder publish; existing startup observations and same-identity HTTP/pipe consumption."
    });

    private async Task PrepareAsync()
    {
        Assert.IsTrue(OperatingSystem.IsWindows() || OperatingSystem.IsLinux());
        Assert.AreEqual(Architecture.X64, RuntimeInformation.ProcessArchitecture);
        Assert.AreEqual(".NET 10.0.12", RuntimeInformation.FrameworkDescription);
        File.Copy(Path.Combine(language, "global.json"), Path.Combine(Root, "global.json"));
        File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), """
            <Project><PropertyGroup>
              <TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
              <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
              <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion><RollForward>Disable</RollForward>
              <SelfContained>false</SelfContained><PublishTrimmed>false</PublishTrimmed>
              <PublishSingleFile>false</PublishSingleFile><PublishAot>false</PublishAot>
              <RestoreEnablePackagePruning>false</RestoreEnablePackagePruning>
              <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally><NuGetAudit>false</NuGetAudit>
            </PropertyGroup></Project>
            """);
        File.WriteAllText(Path.Combine(Root, "Directory.Build.targets"), "<Project />");
        File.WriteAllText(Path.Combine(Root, "Directory.Packages.props"), "<Project />");
        string feed = Directory.CreateDirectory(Path.Combine(Root, "feed")).FullName;
        new XDocument(new XElement("configuration", new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "candidate"), new XAttribute("value", feed))),
            new XElement("fallbackPackageFolders", new XElement("clear")), new XElement("auditSources", new XElement("clear"))))
            .Save(Path.Combine(Root, "NuGet.Config"));
        Assert.AreEqual("10.0.401", (await ToolAsync(["--version"])).StandardOutput.Trim());

        string? supplied = Environment.GetEnvironmentVariable("CIS_HEALTHCHECK_CANDIDATE");
        string? expected = Environment.GetEnvironmentVariable("CIS_HEALTHCHECK_CANDIDATE_SHA256");
        string package;
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            Assert.IsTrue(expected is { Length: 64 } && expected.All(Uri.IsHexDigit), "A supplied candidate requires its exact SHA-256.");
            package = Path.GetFullPath(supplied);
            Assert.AreEqual(expected!.ToLowerInvariant(), CandidatePackageFixture.Hash(package), "Use the same retained candidate on both platforms.");
        }
        else
        {
            Assert.IsTrue(string.IsNullOrWhiteSpace(expected), "Supply the candidate path together with its SHA-256.");
            string configuration = typeof(HealthcheckService).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            ProcessResult packed = await ProcessRunner.RunAsync("dotnet",
                ["pack", "src/CIS.Core/CIS.Core.csproj", "-c", configuration, "--no-build", "--no-restore", "-o", feed],
                language, TimeSpan.FromSeconds(90), new Dictionary<string, string?>
                { ["MSBUILDDISABLENODEREUSE"] = "1", ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["UseSharedCompilation"] = "false" });
            Assert.IsTrue(packed.OutputDrained);
            Assert.AreEqual(0, packed.ExitCode, packed.StandardOutput + packed.StandardError);
            package = Directory.GetFiles(feed, "CIS.Core.*.nupkg").Single();
        }
        packageHash = CandidatePackageFixture.Hash(package);
        using (var zip = ZipFile.OpenRead(package))
        {
            using var nuspec = zip.GetEntry("CIS.Core.nuspec")!.Open();
            version = XDocument.Load(nuspec).Descendants().Single(item => item.Name.LocalName == "version").Value;
            Assert.AreEqual(FileVersionInfo.GetVersionInfo(typeof(HealthcheckService).Assembly.Location).ProductVersion!.Split('+')[0], version);
            using var core = zip.GetEntry("lib/net10.0/CIS.Core.dll")!.Open();
            coreHash = Convert.ToHexStringLower(SHA256.HashData(core));
        }
        if (string.IsNullOrWhiteSpace(supplied))
        { Assert.AreEqual(CandidatePackageFixture.Hash(typeof(HealthcheckService).Assembly.Location), coreHash, "Pack the current built Core."); }
        string destination = Path.Combine(feed, "CIS.Core." + version + ".nupkg");
        if (!string.Equals(Path.GetFullPath(package), destination, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { File.Copy(package, destination); }
        var assets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "src/CIS.Core/obj/project.assets.json")))!;
        var policy = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "eng/runtime-dependencies.json")))!;
        foreach (var dependency in policy["packages"]!.AsArray())
        {
            string id = dependency!["id"]!.GetValue<string>().ToLowerInvariant(), selected = dependency["version"]!.GetValue<string>();
            string name = id + "." + selected + ".nupkg";
            string source = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, id, selected, name)).First(File.Exists);
            File.Copy(source, Path.Combine(feed, name));
        }
        CopyConsumer("examples/healthcheck/CountryList", ApplicationProject, packageExample: true);
        CopyConsumer("tools/CisHealthProbe", ProbeProject, packageExample: false);
        Assert.IsFalse(Directory.Exists(Cache) && Directory.EnumerateFileSystemEntries(Cache).Any(), "The first candidate cache must be empty.");
        foreach (string project in new[] { ApplicationProject, ProbeProject })
        {
            await ToolAsync(["restore", project, "--configfile", Path.Combine(Root, "NuGet.Config"), "--no-cache"]);
            string app = Path.GetDirectoryName(project)!;
            var restored = JsonNode.Parse(File.ReadAllBytes(Path.Combine(app, "obj/project.assets.json")))!;
            string[] expectedPackages = policy["packages"]!.AsArray().Select(item => item!["id"]!.GetValue<string>() + "/" + item["version"]!.GetValue<string>())
                .Append("CIS.Core/" + version).ToArray();
            CollectionAssert.AreEquivalent(expectedPackages, restored["targets"]!["net10.0"]!.AsObject().Select(item => item.Key).ToArray());
            Assert.IsTrue(restored["targets"]!["net10.0"]!.AsObject().All(item => (string?)item.Value!["type"] == "package"));
            Assert.AreEqual(1, restored["packageFolders"]!.AsObject().Count);
            Assert.AreEqual(Path.GetFullPath(Cache), Path.TrimEndingDirectorySeparator(Path.GetFullPath(restored["packageFolders"]!.AsObject().Single().Key)));
            Assert.AreEqual(packageHash, CandidatePackageFixture.Hash(Path.Combine(Cache, "cis.core", version, "cis.core." + version + ".nupkg")));
            await ToolAsync(["build", project, "-c", "Release", "--no-restore"]);
        }
        AssertRuntime(BuildOutput, ApplicationAssembly);
        AssertRuntime(ProbeOutput, "CisHealthProbe");
    }

    private void CopyConsumer(string sourcePath, string projectPath, bool packageExample)
    {
        string source = Path.Combine(language, sourcePath), app = Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!).FullName;
        foreach (string file in Directory.GetFiles(source, "*.cs"))
        {
            string copied = Path.Combine(app, Path.GetFileName(file));
            File.Copy(file, copied);
            sourceHashes[sourcePath + "/" + Path.GetFileName(file)] = CandidatePackageFixture.Hash(copied);
            Assert.AreEqual(CandidatePackageFixture.Hash(file), CandidatePackageFixture.Hash(copied));
        }
        var project = XDocument.Load(Path.Combine(source, Path.GetFileName(projectPath)));
        XElement root = project.Root ?? throw new InvalidDataException("The example project must have a root.");
        XElement reference = project.Descendants("ProjectReference").Single();
        Assert.IsTrue(((string?)reference.Attribute("Include"))?.Replace('\\', '/').EndsWith("src/CIS.Core/CIS.Core.csproj", StringComparison.Ordinal) == true);
        reference.Remove();
        Assert.IsFalse(project.Descendants("Import").Any() || root.Elements("Sdk").Any(), "Ordinary consumers must not rely on repository imports.");
        root.Add(new XElement("ItemGroup", new XElement("PackageReference", new XAttribute("Include", "CIS.Core"), new XAttribute("Version", "[" + version + "]"))));
        if (packageExample) { root.Add(new XElement("PropertyGroup", new XElement("DefineConstants", "$(DefineConstants);CIS_PACKAGE_CONSUMER"))); }
        project.Save(projectPath);
        Assert.IsFalse(Directory.Exists(Path.Combine(app, "obj")), "No repository assets are copied into the consumer.");
    }

    private void AssertRuntime(string output, string assembly)
    {
        Assert.AreEqual(coreHash, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.Core.dll")), "Execute the package payload, not repository Core or a cached replacement.");
        Assert.IsTrue(File.Exists(Path.Combine(output, assembly + (OperatingSystem.IsWindows() ? ".exe" : ""))), "Keep the ordinary SDK apphost default.");
        var config = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, assembly + ".runtimeconfig.json")))!;
        foreach (string name in new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" })
        { Assert.AreEqual("10.0.12", config["runtimeOptions"]!["frameworks"]!.AsArray().Single(item => item!["name"]!.GetValue<string>() == name)!["version"]!.GetValue<string>()); }
        var deps = JsonNode.Parse(File.ReadAllBytes(Path.Combine(output, assembly + ".deps.json")))!;
        Assert.IsTrue(deps["libraries"]!.AsObject().ContainsKey("CIS.Core/" + version));
    }

    private void AssertExecutedCore(JsonObject report, string output)
    {
        Assert.AreEqual(false, (bool?)report["sourceReferenceDevelopmentExample"]);
        string actual = report["coreAssemblyPath"]!.GetValue<string>();
        Assert.IsTrue(string.Equals(Path.GetFullPath(Path.Combine(output, "CIS.Core.dll")), Path.GetFullPath(actual),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        Assert.AreEqual(coreHash, CandidatePackageFixture.Hash(actual), "Independently hash the actual reported loaded Core file.");
    }

    private async Task<string[]> OutputsAsync()
    {
        if (published is null)
        {
            published = Path.Combine(Root, "published");
            await ToolAsync(["publish", ApplicationProject, "-c", "Release", "--no-restore", "-o", published]);
            AssertRuntime(published, ApplicationAssembly);
        }
        return [BuildOutput, published];
    }

    internal async Task VerifyStoryAsync(string option, string failureMode, TestContext context)
    {
        foreach (string output in await OutputsAsync())
        {
            string[] arguments = ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(output, ApplicationAssembly + ".dll"), "--self-test"];
            if (option.Length != 0) { arguments = [.. arguments, option]; }
            var result = await ProcessRunner.RunAsync("dotnet", arguments, Root, TimeSpan.FromSeconds(45), environment, new UTF8Encoding(false, true));
            Assert.IsTrue(result.OutputDrained);
            Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
            Assert.AreEqual("", result.StandardError);
            var report = JsonNode.Parse(result.StandardOutput)!.AsObject();
            AssertExecutedCore(report, output);
            CountryListStartupExpectations.Verify(report, failureMode, sourceReference: false);
            context.WriteLine("CIS-COUNTRY-LIST-STORY " + (output == BuildOutput ? "build " : "published ") + report.ToJsonString());
        }
    }

    internal async Task VerifyPipeAsync(bool failPrime, TestContext context)
    {
        foreach (string output in await OutputsAsync())
        {
            string pipe = TransportTestSupport.NewPipeName();
            var variables = new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)
            { ["CIS_HEALTHCHECK_PIPE_ENABLED"] = "true", ["CIS_HEALTHCHECK_PIPE_NAME"] = pipe };
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true)
            };
            foreach (string argument in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Path.Combine(output, ApplicationAssembly + ".dll") })
            { start.ArgumentList.Add(argument); }
            if (failPrime) { start.ArgumentList.Add("--fail-prime"); }
            foreach (var item in variables)
            { if (item.Value is null) { start.Environment.Remove(item.Key); } else { start.Environment[item.Key] = item.Value; } }
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the package application.");
            Task<string> stderr = child.StandardError.ReadToEndAsync();
            Task<string?> lineReading = child.StandardOutput.ReadLineAsync();
            Task<string>? stdout = null;
            try
            {
                string line = await lineReading.WaitAsync(TimeSpan.FromSeconds(20))
                    ?? throw new InvalidDataException("CountryList exited without its started observation.");
                var started = JsonNode.Parse(line)!.AsObject();
                Assert.AreEqual("countryListStarted", (string?)started["kind"]);
                Assert.AreEqual(!failPrime, (bool?)started["startupSucceeded"]);
                Assert.AreEqual(!failPrime, (bool?)started["ready"]);
                Assert.AreEqual(pipe, (string?)started["pipeName"]);
                AssertExecutedCore(started, output);
                stdout = child.StandardOutput.ReadToEndAsync();
                foreach (string view in new[] { "health", "readiness" })
                {
                    var probe = await ProbeAsync(pipe, view);
                    Assert.AreEqual(failPrime ? 1 : 0, probe.ExitCode, probe.StandardError + probe.StandardOutput);
                    Assert.AreEqual("", probe.StandardError);
                    var snapshot = JsonNode.Parse(probe.StandardOutput)!.AsObject();
                    // Normal hosting retains both startup and availability monitors on failed priming.
                    // The finite story separately withdraws availability to prove the application failure survives.
                    TransportTestSupport.ValidateSnapshot(snapshot, view, healthy: !failPrime, ready: !failPrime, active: failPrime ? 2 : 3);
                    Assert.AreEqual("CountryList", (string?)snapshot["application"]!["name"]);
                    Assert.AreEqual(!failPrime, (bool?)snapshot["application"]!["startupCompleted"]);
                    context.WriteLine("CIS-COUNTRY-LIST-PROBE " + (output == BuildOutput ? "build " : "published ") + view + " " + snapshot.ToJsonString());
                }
            }
            finally
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); }
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                if (stdout is null)
                {
                    await lineReading.WaitAsync(TimeSpan.FromSeconds(15));
                    stdout = child.StandardOutput.ReadToEndAsync();
                }
                await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(15));
            }
            // A missing endpoint is a transport error, distinct from the valid unhealthy result above.
            var missing = await ProbeAsync(TransportTestSupport.NewPipeName(), "health");
            Assert.AreEqual(2, missing.ExitCode);
            Assert.AreEqual("", missing.StandardOutput);
            Assert.IsTrue(missing.StandardError.Length is > 0 and <= 1024);
            context.WriteLine("CIS-COUNTRY-LIST-PROBE missing-endpoint exit=2 " + missing.StandardError.Trim());
        }
    }

    private async Task<ProcessResult> ProbeAsync(string pipe, string view)
    {
        AssertRuntime(ProbeOutput, "CisHealthProbe");
        var result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(ProbeOutput, "CisHealthProbe.dll"), "--pipe", pipe, "--view", view, "--timeout-ms", "1000"],
            Root, TimeSpan.FromSeconds(15), environment, new UTF8Encoding(false, true));
        Assert.IsTrue(result.OutputDrained);
        return result;
    }

    private async Task<ProcessResult> ToolAsync(string[] arguments)
    {
        if (arguments[0] is "restore" or "build" or "publish")
        { arguments = [.. arguments, "--disable-build-servers", "-m:1", "-nr:false", "-p:UseSharedCompilation=false"]; }
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, Root, TimeSpan.FromSeconds(180), environment);
        Assert.IsTrue(result.OutputDrained);
        Assert.AreEqual(0, result.ExitCode, string.Join(" ", arguments) + "\n" + result.StandardOutput + result.StandardError);
        return result;
    }

    public void Dispose() => directory.Dispose();
}
