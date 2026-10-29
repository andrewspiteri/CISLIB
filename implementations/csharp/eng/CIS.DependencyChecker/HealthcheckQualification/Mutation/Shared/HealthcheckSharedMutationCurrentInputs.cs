using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private sealed record SharedCurrentComparison(HealthcheckSharedMutationCurrentPlan.Decision[] Decisions, JsonObject Observation);

    private static async Task<SharedCurrentComparison> CompareSharedCurrentPlanAsync(PinnedInputs files, string snapshot,
        JsonObject descriptor, string originalProfile, JsonObject profile, JsonObject receipt,
        HealthcheckSharedMutationProtocol.Input[] inputs, string dotnet, string assembly)
    {
        string targetProfile = files.Pin(Item(descriptor["currentProfile"]));
        Need(Inside(files.Repository, targetProfile) && Hash(targetProfile) == Hash(originalProfile),
            "The current target profile must be a byte-identical original profile under repo-root.");
        string[] sourceFiles = Items(profile, "sourceFiles").Select(node => node!.GetValue<string>()).ToArray();
        string group = sourceFiles.All(path => path.StartsWith(CorePrefix + "Healthcheck/Pipes/", StringComparison.Ordinal)) ? "pipes"
            : sourceFiles.All(path => path.StartsWith(CorePrefix + "Healthcheck/AspNetCore/", StringComparison.Ordinal)) ? "aspnet" : "unit-runtime";
        var originalGraph = ReadSharedComparisonGraph(files, snapshot, group);
        var targetGraph = ReadSharedComparisonGraph(files, files.Repository, group);
        Need(originalGraph.BuildId == String(receipt, "graphBuildId"), "The original graph differs from the verified receipt.");
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in inputs.Where(input => input.CompareCurrent))
        {
            string current = files.Resolve(input.Path);
            if (!File.Exists(current)) { continue; }
            PinSharedCurrentFile(files, current);
            hashes.Add(input.Path, Hash(current));
        }
        string state = Under(files.Repository, ".cis/local/testing/incremental/" + String(profile, "id") + "/state.json");
        if (File.Exists(state)) { PinSharedCurrentFile(files, state); }
        ProcessResult execution = await ProcessRunner.RunAsync(dotnet,
            [assembly, "test", "incremental", "plan", "--repo", files.Repository, "--profile", Path.GetRelativePath(files.Repository, targetProfile), "--format", "json"],
            files.Repository, TimeSpan.FromMinutes(10), outputEncoding: new UTF8Encoding(false, true), maximumCapturedBytesPerStream: 16384,
            maximumCapturedStandardOutputBytes: 1048576);
        Need(execution.OutputDrained && !execution.CaptureTruncated, "Current planning did not return complete bounded output.");
        var plan = HealthcheckSharedMutationCurrentPlan.Read(execution.StandardOutput, execution.ExitCode, sourceFiles);
        Need(plan.GraphBuildId == targetGraph.BuildId, "Current plan used a different or changed graph.");
        var decisions = HealthcheckSharedMutationCurrentPlan.Compare(receipt, inputs, plan, hashes, originalGraph.Tests, targetGraph.Tests);
        files.VerifyAgain();
        return new(decisions, new JsonObject
        {
            ["targetProfileSha256"] = Hash(targetProfile),
            ["graphBuildId"] = plan.GraphBuildId,
            ["planSha256"] = TypeManagerVerificationInputs.Digest(execution.StandardOutputBytes.Span),
            ["originalTestFileCount"] = originalGraph.Tests.Length,
            ["currentTestFileCount"] = targetGraph.Tests.Length,
            ["decisions"] = JsonSerializer.SerializeToNode(decisions),
        });
    }

    private static (string BuildId, string[] Tests) ReadSharedComparisonGraph(PinnedInputs files, string root, string group)
    {
        string manifestPath = Under(root, ".cis/local/graph/manifest.json");
        string database = Under(root, ".cis/local/graph/context.db");
        PinSharedCurrentFile(files, manifestPath);
        PinSharedCurrentFile(files, database);
        string diagnostics = Under(root, ".cis/local/graph/diagnostics.json");
        if (File.Exists(diagnostics)) { PinSharedCurrentFile(files, diagnostics); }
        JsonObject manifest = files.ReadDocument(manifestPath);
        Need(Integer(manifest, "schemaVersion") == 2, "Current comparison requires the complete graph manifest schema2.");
        var graphInputs = Items(manifest, "inputs").Select(Item).ToDictionary(item => String(item, "path"), item => String(item, "hash"), StringComparer.Ordinal);
        Need(graphInputs.Count is > 0 and <= 250000, "Graph input inventory is empty or excessive.");
        JsonObject maintained = files.ReadDocument(MaintainedConfig(files, group));
        string[] projects = Items(Item(maintained["stryker-config"]), "test-projects").Select(node => node!.GetValue<string>()).ToArray();
        Need(projects.Length is > 0 and <= 4, "Canonical test project inventory is missing or excessive.");
        var tests = new HashSet<string>(StringComparer.Ordinal);
        foreach (string project in projects)
        {
            string projectPath = Path.GetFullPath(Path.Combine(root, CorePrefix, project));
            string testRoot = Path.GetDirectoryName(projectPath)!;
            Need(Inside(root, projectPath) && Path.GetRelativePath(root, testRoot).Replace('\\', '/').StartsWith("implementations/csharp/tests/", StringComparison.Ordinal),
                "Canonical test project escaped the target test inventory.");
            bool SourceInput(string path)
            {
                string relative = Path.GetRelativePath(testRoot, path).Replace('\\', '/');
                return path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !relative.Split('/').Any(part => part is "bin" or "obj");
            }
            files.TrackInventory(testRoot, SourceInput);
            foreach (string path in Directory.EnumerateFiles(testRoot, "*", SearchOption.AllDirectories).Where(SourceInput))
            {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                Need(tests.Add(relative), "Canonical test project inventories overlap.");
                Need(tests.Count <= 16384 && graphInputs.TryGetValue(relative, out string? sha256) && sha256 == "sha256:" + Hash(path),
                    "A canonical test source is absent, stale or excluded from the complete graph inventory.");
                PinSharedCurrentFile(files, path);
            }
        }
        Need(tests.Count > 0, "Canonical test discovery inventory is empty.");
        return (String(manifest, "buildId"), tests.Order(StringComparer.Ordinal).ToArray());
    }
}
