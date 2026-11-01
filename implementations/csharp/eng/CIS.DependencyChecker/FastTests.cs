using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using CIS.TestSupport;

namespace CIS.Verification;

internal static class FastTests
{
    internal static readonly string[] Projects = ["CIS.Core.FastTests", "CIS.Core.FastLoggingTests", "CIS.Core.FastIsolationTests", "CIS.Core.FastArchitectureTests"];

    internal static int Workers(string project) => project switch
    {
        "CIS.Core.FastTests" => 22,
        "CIS.Core.FastLoggingTests" => 4,
        "CIS.Core.FastIsolationTests" => 8,
        "CIS.Core.FastArchitectureTests" => 4,
        _ => throw new InvalidDataException("Unknown ordinary Fast owner: " + project)
    };

    internal static string WriteSettings(string language, string directory, string project)
    {
        XDocument settings = XDocument.Load(Path.Combine(language, "verification", "fast.runsettings"));
        settings.Descendants("Workers").Single().Value = Workers(project).ToString(CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, "fast.runsettings");
        settings.Save(path);
        return path;
    }

    internal static void ValidateProjects(string language)
    {
        string[] selected = XDocument.Load(Path.Combine(language, "CisLib.slnx")).Descendants("Project")
            .Select(item => item.Attribute("Path")?.Value ?? throw new InvalidDataException("Solution project path is missing."))
            .Select(path => Path.GetFullPath(Path.Combine(language, path)))
            .Where(path => string.Equals(XDocument.Load(path).Descendants("IsTestProject").LastOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase)).ToArray();
        string[] expected = Projects.Select(project => Path.GetFullPath(Path.Combine(language, "tests", project, project + ".csproj"))).ToArray();
        TypeManagerVerificationInputs.Require(selected.Length == Projects.Length && selected.ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).SetEquals(expected),
            "The ordinary solution test owners changed; update the fast native route.");
    }

    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root"], ["configuration", "no-build", "timeout-seconds"]);
        string language = input.Language, configuration = input.Optional("configuration") ?? "Debug";
        TypeManagerVerificationInputs.Require(configuration is "Debug" or "Release", "Configuration must be Debug or Release.");
        TypeManagerVerificationInputs.Require(bool.TryParse(input.Optional("no-build") ?? "false", out bool noBuild), "no-build must be true or false.");
        TypeManagerVerificationInputs.Require(int.TryParse(input.Optional("timeout-seconds") ?? "300", NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds is >= 1 and <= 3600,
            "Fast tests require a finite 1-3600 second budget.");
        ValidateProjects(language);
        var clock = Stopwatch.StartNew();
        TimeSpan Remaining() => clock.Elapsed.TotalSeconds < seconds ? TimeSpan.FromSeconds(seconds - clock.Elapsed.TotalSeconds) : throw new TimeoutException("The shared fast-test deadline expired.");
        var environment = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(name => name.StartsWith("CIS_VERIFICATION_", StringComparison.Ordinal))
            .ToDictionary(name => name, _ => (string?)null, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string name in new[] { "CisMutationTesting", "CisHealthcheckMutationCohort", "CisHealthcheckPilotCohort", "CisLinuxIdentityQualification" })
        {
            TypeManagerVerificationInputs.Require(!string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase), "The ordinary fast route cannot run a mutation/identity cohort.");
            environment[name] = null;
        }
        environment["CIS_ARCHITECTURE_INSPECTION"] = null;
        environment["CIS_TEST_HOST_IDENTITY"] = "1";
        string output = Path.Combine(input.PathOf("repo-root"), ".artifacts", "csharp", "fast", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        Console.WriteLine("Fast native summary: " + Path.Combine(output, "summary.json"));
        var results = new List<object>();
        var hosts = new List<NativeTestRunner.HostIdentity>();
        object sync = new();
        string console = "";
        bool passed = false;
        int selectedCases = 0;
        string? failure = null;
        try
        {
            ProcessResult sdk = await ProcessRunner.RunAsync(input.Dotnet, ["--version"], language, Remaining(), environment);
            TypeManagerVerificationInputs.Require(sdk.ExitCode == 0 && sdk.OutputDrained && sdk.StandardOutput.Trim() == "10.0.401", "Fast tests require the pinned SDK 10.0.401.");
            console = await NativeTestRunner.ResolveConsoleAsync(input.Dotnet, language, Remaining());
            if (!noBuild)
            {
                ProcessResult build = await ProcessRunner.RunAsync(input.Dotnet,
                    ["build", "CisLib.slnx", "-c", configuration, "-p:RestoreLockedMode=true", "-p:CisMutationTesting=false", "-p:CisHealthcheckMutationCohort=false", "-p:CisHealthcheckPilotCohort=false", "-p:CisLinuxIdentityQualification=false"],
                    language, Remaining(), environment, allowTruncatedOutput: true);
                Console.Write(build.StandardOutput); Console.Error.Write(build.StandardError);
                TypeManagerVerificationInputs.Require(build.ExitCode == 0 && build.OutputDrained, "Ordinary fast build failed.");
            }
            await Task.WhenAll(Projects.Select(RunProject));
            TypeManagerVerificationInputs.Require(hosts.Count == 4 && hosts.Select(host => (host.ProcessId, host.StartedUtc)).Distinct().Count() == 4 && hosts.Select(host => host.ProcessId).Distinct().Count() == 4,
                "Fast tests must execute in four distinct test host processes.");
            passed = true;
            Console.WriteLine($"Passed {selectedCases} fast native cases in four isolated test hosts.");
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or TimeoutException or System.Xml.XmlException or JsonException or System.ComponentModel.Win32Exception)
        { failure = error.Message; Console.Error.WriteLine(failure); return 1; }
        finally
        {
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
            {
                scope = "ordinary-fast-tests",
                passed,
                failure,
                configuration,
                noBuild,
                selectedCases,
                runnerSecondsIncludingSelectedBuild = clock.Elapsed.TotalSeconds,
                checkerBootstrapMeasuredByCallingCommand = true,
                results,
                hosts
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        async Task RunProject(string project)
        {
            string assembly = Path.Combine(language, "tests", project, "bin", configuration, "net10.0", project + ".dll");
            NativeTestRunner.ValidateAssembly(assembly);
            string directory = Path.Combine(output, project);
            Directory.CreateDirectory(directory);
            string settingsPath = WriteSettings(language, directory, project);
            string[] command = NativeTestRunner.Arguments(console, assembly, settingsPath, directory);
            DateTimeOffset started = DateTimeOffset.UtcNow;
            ProcessResult child;
            try { child = await ProcessRunner.RunAsync(input.Dotnet, command, language, Remaining(), environment, allowTruncatedOutput: true); }
            catch (ProcessTimeoutException error)
            {
                FocusedTests.WriteDiagnosticLog(Path.Combine(directory, "native.log"), error.Result.StandardOutput, error.Result.StandardError, error.Result.CaptureTruncated);
                throw;
            }
            FocusedTests.WriteDiagnosticLog(Path.Combine(directory, "native.log"), child.StandardOutput, child.StandardError, child.CaptureTruncated);
            lock (sync) { Console.Write(child.StandardOutput); Console.Error.Write(child.StandardError); }
            TypeManagerVerificationInputs.Require(child.ExitCode == 0 && child.OutputDrained, "Native fast owner failed: " + project);
            string report = Path.Combine(directory, "report.trx");
            FocusedTests.NativeCase[] cases = FocusedTests.ReadResults(report, started);
            TypeManagerVerificationInputs.Require(cases.All(item => NativeTestRunner.SamePath(item.Source, assembly)), "Fast native CodeBase differs from its selected owner.");
            NativeTestRunner.HostIdentity host = NativeTestRunner.ReadHostIdentity(report, assembly);
            lock (sync) { selectedCases += cases.Length; hosts.Add(host); results.Add(new { project, passed = cases.Length, report, processId = child.ProcessId, host, arguments = command }); }
        }
    }
}
