using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;

namespace CIS.Verification;

// Prepared assemblies need the test platform, not another MSBuild invocation.
internal static class NativeTestRunner
{
    internal const string HostMarker = "CIS test host identity: ";
    internal sealed record HostIdentity(int ProcessId, string StartedUtc, string Assembly);

    internal static async Task<string> ResolveConsoleAsync(string dotnet, string language, TimeSpan timeout)
    {
        ProcessResult result = await ProcessRunner.RunAsync(dotnet, ["--list-sdks"], language, timeout);
        TypeManagerVerificationInputs.Require(result.ExitCode == 0 && result.OutputDrained, "Cannot locate the pinned SDK test platform.");
        string[] locations = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim())
            .Where(line => line.StartsWith("10.0.401 [", StringComparison.Ordinal) && line.EndsWith(']')).ToArray();
        TypeManagerVerificationInputs.Require(locations.Length == 1, "Expected one SDK 10.0.401 location on the selected dotnet host.");
        string path = Path.GetFullPath(Path.Combine(locations[0][(locations[0].IndexOf('[') + 1)..^1], "10.0.401", "vstest.console.dll"));
        TypeManagerVerificationInputs.Require(File.Exists(path), "The pinned SDK test platform is missing: " + path);
        return path;
    }

    internal static string[] Arguments(string console, string assembly, string settings, string output, string? filter = null) =>
        ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", console, assembly,
            "/Framework:.NETCoreApp,Version=v10.0", "/Settings:" + settings,
            "/TestAdapterPath:" + Path.GetDirectoryName(assembly), "/Logger:trx;LogFileName=report.trx", "/ResultsDirectory:" + output,
            .. (filter is null ? Array.Empty<string>() : ["/TestCaseFilter:" + filter])];

    internal static void ValidateAssembly(string assembly)
    {
        TypeManagerVerificationInputs.Require(new[] { assembly, Path.ChangeExtension(assembly, ".deps.json"), Path.ChangeExtension(assembly, ".runtimeconfig.json") }.All(File.Exists),
            "Build the selected configuration before running its native tests: " + assembly);
        JsonObject runtime = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(assembly, ".runtimeconfig.json")))?["runtimeOptions"] as JsonObject
            ?? throw new InvalidDataException("Native runtime options are missing.");
        JsonObject[] frameworks = runtime["framework"] is JsonObject single ? [single] : runtime["frameworks"] is JsonArray many
            ? many.Select(node => node as JsonObject ?? throw new InvalidDataException("Invalid native framework entry.")).ToArray()
            : throw new InvalidDataException("Native framework identity is missing.");
        TypeManagerVerificationInputs.Require((string?)runtime["rollForward"] == "Disable" &&
            frameworks.Any(item => (string?)item["name"] == "Microsoft.NETCore.App") &&
            frameworks.All(item => ((string?)item["name"] is "Microsoft.NETCore.App" or "Microsoft.AspNetCore.App") && (string?)item["version"] == "10.0.12"),
            "Native tests must use their pinned 10.0.12 runtime without roll-forward.");
    }

    internal static HostIdentity ReadHostIdentity(string report, string assembly)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement root = XDocument.Load(report).Root!;
        XElement definition = root.Element(ns + "TestDefinitions")!.Elements(ns + "UnitTest").Single(item =>
            (string?)item.Element(ns + "TestMethod")?.Attribute("name") == "SchedulingMatchesRequestedMode" &&
            ((string?)item.Element(ns + "TestMethod")?.Attribute("className"))?.Split(',')[0] == "CIS.Tests.TestAssemblySetup");
        XElement result = root.Element(ns + "Results")!.Elements(ns + "UnitTestResult").Single(item =>
            (string?)item.Attribute("testId") == (string?)definition.Attribute("id"));
        string[] markers = (result.Element(ns + "Output")?.Element(ns + "StdOut")?.Value ?? "").Split('\n')
            .Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(HostMarker, StringComparison.Ordinal)).ToArray();
        TypeManagerVerificationInputs.Require(markers.Length == 1, "The actual native test host did not provide exactly one identity.");
        JsonObject identity = JsonNode.Parse(markers[0][HostMarker.Length..])!.AsObject();
        int pid = identity["processId"]!.GetValue<int>();
        string started = identity["startedUtc"]!.GetValue<string>(), source = identity["assembly"]!.GetValue<string>();
        TypeManagerVerificationInputs.Require(pid > 0 && DateTimeOffset.TryParse(started, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) && SamePath(source, assembly),
            "Native host identity does not match its one selected assembly.");
        return new(pid, started, source);
    }

    internal static bool SamePath(string left, string right)
    {
        string path = Uri.TryCreate(left, UriKind.Absolute, out Uri? uri) && uri.IsFile ? uri.LocalPath : left;
        return string.Equals(Path.GetFullPath(path), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
