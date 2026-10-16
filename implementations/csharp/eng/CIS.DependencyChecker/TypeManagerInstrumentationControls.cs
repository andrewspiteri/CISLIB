using System.Text.Json.Nodes;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerInstrumentationControls
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "candidate-core", "candidate-report", "output-root"], []);
        input.EmptyOutput(); string root = input.Output, candidate = input.PathOf("candidate-core"), producer = input.PathOf("candidate-report");
        string candidateHash = Hash(candidate), producerHash = Hash(producer), tool = typeof(TypeManagerInstrumentationControls).Assembly.Location, toolHash = Hash(tool);
        string changedCore = Path.Combine(root, "changed-core.dll"), changedSource = Path.Combine(root, "changed-source.json");
        using (FileStream output = File.Create(changedCore)) { using FileStream original = File.OpenRead(candidate); await original.CopyToAsync(output); output.WriteByte(0); }
        JsonObject changed = Read(producer); changed["coreSourceInputs"]!["TypeManagement/TypeManager.cs"] = new string('0', 64); WriteJson(changedSource, changed);
        var report = new JsonObject { ["complete"] = false, ["verifierSha256"] = toolHash, ["candidateCoreSha256"] = candidateHash, ["candidateReportSha256"] = producerHash, ["results"] = new JsonArray() };
        string reportPath = Path.Combine(root, "results.json"); WriteJson(reportPath, report);
        try
        {
            string runtime = (string)Read(Path.Combine(input.Language, "eng", "toolchain.json"))["targetRuntime"]!;
            foreach ((string name, string core, string record, string reason) in new[] {
                ("changed-core", changedCore, producer, "Candidate producer report does not match the frozen Core bytes"),
                ("changed-source", candidate, changedSource, "Candidate source provenance differs from the source selected for instrumentation") })
            {
                string output = Path.Combine(root, name);
                await input.Run(name, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", tool, "type-manager-instrumentation",
                    "--repo-root", input.PathOf("repo-root"), "--candidate-core", core, "--candidate-report", record, "--output-root", output, "--dotnet", input.Dotnet], expectedError: reason);
                Require(!Directory.Exists(Path.Combine(output, "binaries")) && !Directory.Exists(Path.Combine(output, "generated")), name + " was not rejected before instrumentation");
                Require((bool?)Read(Path.Combine(output, "results.json"))["complete"] == false, "Rejected instrumentation appeared complete");
                report["results"]!.AsArray().Add(new JsonObject { ["case"] = name, ["passed"] = true, ["rejectedFor"] = reason }); WriteJson(reportPath, report);
            }
            Require(Hash(candidate) == candidateHash && Hash(producer) == producerHash && Hash(tool) == toolHash, "Original provenance inputs changed");
            report["complete"] = true; WriteJson(reportPath, report); return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(reportPath, report); }
    }
}

