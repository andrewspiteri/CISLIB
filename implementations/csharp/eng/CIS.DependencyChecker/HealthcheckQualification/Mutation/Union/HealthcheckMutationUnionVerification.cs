using System.Text.Json;
using System.Text.Json.Nodes;
using static CIS.Verification.HealthcheckMutationUnionModel;

namespace CIS.Verification;

internal static partial class HealthcheckMutationUnionVerification
{
    private const string InputSchema = "cis.healthcheck.mutation-union-inputs/v1-development";
    private const string CorePrefix = "implementations/csharp/src/CIS.Core/";
    private static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] arguments)
    {
        var inputs = new TypeManagerVerificationInputs(arguments,
            ["repo-root", "manifest", "output-root"], []);
        var files = new PinnedInputs(inputs.PathOf("repo-root"), inputs.Output);
        string manifestPath = inputs.PathOf("manifest");
        JsonObject manifest = files.ReadDocument(manifestPath);
        Need(String(manifest, "schema") == InputSchema, "Unsupported union input schema.");
        string lane = String(manifest, "osLane");
        Need(lane is "windows" or "ubuntu", "Use one explicit Windows or Ubuntu lane; pooling is forbidden.");
        Source[] sources = ReadSources(files, manifest);
        JsonObject[] descriptors = Items(manifest, "partitions").Select(Item).ToArray();
        Need(descriptors.Length == Groups.Length && descriptors.Select(d => String(d, "group"))
            .ToHashSet(StringComparer.Ordinal).SetEquals(Groups), "Exactly three distinct configured groups are required.");
        Need(!manifest.ContainsKey("adjustments"), "Unvalidated adjustment input is forbidden. Retain proposed evidence under proposals.");
        var proposals = new JsonArray();
        foreach (JsonNode? proposal in Items(manifest, "proposals"))
        {
            JsonObject pin = Item(proposal);
            files.Pin(pin);
            proposals.Add(new JsonObject { ["evidence"] = pin.DeepClone(), ["disposition"] = "UnappliedProposal" });
        }
        var dispositionRecords = ReadDispositionReferences(files, manifest, sources, lane);
        var finiteAssertionRecords = ReadFiniteAssertionReferences(files, manifest, sources, lane);
        var dispositionObservations = new JsonArray();
        var finiteAssertionObservations = new JsonArray();
        Adjustment[] validatedAdjustments = [];
        string[] pending = descriptors.Where(d => String(d, "state") == "Pending")
            .Select(d => String(d, "group")).ToArray();
        foreach (JsonObject descriptor in descriptors)
        {
            string state = String(descriptor, "state");
            Need(state is "Closed" or "Pending", "Unknown native report state.");
            Need(String(descriptor, "osLane") == lane, "Cross-platform report pooling is forbidden.");
            ValidateConfiguration(files, descriptor);
            // Optional retained inputs still protect their physical roots while
            // the union is pending. Incomplete qualification never authorizes
            // writing a receipt into an original native log/child directory.
            if (descriptor["logsManifest"] is not null) { ReadLogs(files, descriptor); }
            foreach (string name in new[] { "restorationManifest", "childSession" })
            {
                if (descriptor[name] is not null)
                { files.GuardRoot(Path.GetDirectoryName(files.Pin(Item(descriptor[name])))!); }
            }
            if (state == "Pending")
            { Need(descriptor["report"] is null, "A pending partition cannot supply a substitute report."); }
            else
            {
                string nativeRoot = String(descriptor, "nativeSourceRoot");
                Need(lane == "ubuntu" ? nativeRoot.StartsWith('/') : nativeRoot.Length > 2 && char.IsAsciiLetter(nativeRoot[0]) && nativeRoot[1] == ':',
                    "Closed report root belongs to another OS lane.");
                JsonObject pin = Item(descriptor["report"]);
                string reportPath = files.Pin(pin);
                files.GuardRoot(Path.GetDirectoryName(reportPath)!);
                JsonObject report = files.ReadDocument(reportPath);
                ParseReport(report, String(descriptor, "group"), String(pin, "sha256"), nativeRoot, sources);
            }
        }
        JsonObject result;
        if (pending.Length > 0)
        {
            result = new JsonObject
            {
                ["schema"] = "cis.healthcheck.mutation-union/v1-development",
                ["complete"] = false,
                ["qualified"] = false,
                ["reason"] = "All three original closed reports are required before calculating a union score.",
                ["pendingGroups"] = JsonSerializer.SerializeToNode(pending)
            };
        }
        else
        {
            foreach (JsonObject descriptor in descriptors)
            {
                ReadLogs(files, descriptor);
                string restoration = files.Pin(Item(descriptor["restorationManifest"]));
                files.GuardRoot(Path.GetDirectoryName(restoration)!);
                if (String(descriptor, "group") is "pipes" or "aspnet")
                { files.GuardRoot(Path.GetDirectoryName(files.Pin(Item(descriptor["childSession"])))!); }
            }
            inputs.EmptyOutput();
            var partitions = new List<Partition>();
            var audits = new JsonArray();
            foreach (JsonObject descriptor in descriptors)
            {
                (Partition partition, JsonObject observation) = await ReadPartitionAsync(files, descriptor, sources, lane, inputs.Output);
                partitions.Add(partition); audits.Add(observation);
            }
            validatedAdjustments = CompleteDispositions(dispositionRecords, partitions.ToArray(), dispositionObservations)
                .Concat(CompleteFiniteAssertions(finiteAssertionRecords, partitions.ToArray(), finiteAssertionObservations)).ToArray();
            result = Evaluate(sources, partitions.ToArray(), validatedAdjustments);
            result["nativeAudits"] = audits;
        }
        files.VerifyAgain();
        if (pending.Length > 0) { inputs.EmptyOutput(); }
        result["osLane"] = lane;
        result["proposals"] = proposals;
        result["appliedAdjustments"] = validatedAdjustments.Length;
        result["dispositions"] = dispositionObservations;
        result["pendingDispositionCount"] = pending.Length > 0 ? dispositionRecords.Length : 0;
        result["finiteAssertionReruns"] = finiteAssertionObservations;
        result["pendingFiniteAssertionCount"] = pending.Length > 0 ? finiteAssertionRecords.Length : 0;
        result["scope"] = "HC12 Healthcheck only; no whole-Core or release approval.";
        result["verifiedInputs"] = files.Inventory();
        result["manifestPath"] = manifestPath;
        result["createdUtc"] = DateTimeOffset.UtcNow.ToString("O");
        string output = Path.Combine(inputs.Output, "results.json");
        File.WriteAllText(output, result.ToJsonString(OutputOptions));
        Console.WriteLine(output);
        return result["qualified"]!.GetValue<bool>() ? 0 : 1;
    }
}
