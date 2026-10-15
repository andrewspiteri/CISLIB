using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileRotationPolicyTests
{
    [TestMethod]
    [DataRow("collision-jsonl-file")]
    [DataRow("collision-human-file")]
    [DataRow("collision-jsonl-directory")]
    [DataRow("collision-human-directory")]
    [DataRow("metadata-jsonl-directory")]
    [DataRow("metadata-human-directory")]
    [DataRow("write-jsonl-failure")]
    [DataRow("write-human-failure")]
    public async Task PhysicalRotationFailuresPreserveExactTransitionsAndHealthyOutput(string mode)
    { using var root = new TemporaryDirectory(); FileRotationPolicyExpectations.Validate(mode, await FileRotationPolicyFixture.ExecuteAsync(mode, root.Path)); }

    [TestMethod]
    [DataRow("filesystem")]
    [DataRow("membership")]
    [DataRow("code")]
    [DataRow("console")]
    [DataRow("release")]
    [DataRow("accounting")]
    [DataRow("candidate-content")]
    [DataRow("transition-content")]
    public async Task RotationPolicyMatcherRejectsLostFailureEvidence(string corruption)
    {
        using var root = new TemporaryDirectory(); string mode = corruption is "candidate-content" or "transition-content" ? "metadata-jsonl-directory" : "collision-jsonl-file"; JsonObject actual = await FileRotationPolicyFixture.ExecuteAsync(mode, root.Path); FileRotationPolicyExpectations.Validate(mode, actual);
        switch (corruption)
        {
            case "filesystem": actual["after"]!.AsObject().Remove(actual["after"]!.AsObject().Select(x => x.Key).Single(x => x.EndsWith("00000000000000000001.closed", StringComparison.Ordinal) && !x.Contains("ffffffff", StringComparison.Ordinal))); break;
            case "membership": actual["namesAfter"]!.AsArray().Add("new.closed"); break;
            case "code": actual["fault"]!["outputs"]![1]!["faultCode"] = "Other"; break;
            case "console": actual["final"]!["outputs"]![0]!["written"] = 0L; break;
            case "release": actual["leaseReleased"] = false; break;
            case "transition-content":
                string pending = actual["after"]!.AsObject().Select(x => x.Key).Single(x => x.EndsWith(".cis.transition.json", StringComparison.Ordinal)); JsonObject intent = JsonNode.Parse(Convert.FromBase64String(actual["after"]![pending]!.GetValue<string>()))!.AsObject(); intent["originalSha256"] = new string('0', 64); actual["after"]![pending] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(intent.ToJsonString() + "\n")); break;
            case "candidate-content":
                actual["after"]!["logs/Ω-🙂.log.cis.inventory.json.candidate.tmp"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("invented leftover")); break;
            default: actual["final"]!["outputs"]![1]!["abandoned"] = 0L; break;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileRotationPolicyExpectations.Validate(mode, actual));
        Assert.AreEqual(corruption switch { "filesystem" => "exact filesystem transition", "membership" => "exact membership", "code" => "terminal fault", "console" => "console progress", "release" => "physical creation/release", "candidate-content" => "private inventory candidate", "transition-content" => "transition intent", _ => "file accounting" }, error.Message);
    }
}
