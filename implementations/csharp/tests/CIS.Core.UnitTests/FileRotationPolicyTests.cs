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
    public async Task RotationPolicyMatcherRejectsLostFailureEvidence(string corruption)
    {
        using var root = new TemporaryDirectory(); string mode = corruption == "candidate-content" ? "metadata-jsonl-directory" : "collision-jsonl-file"; JsonObject actual = await FileRotationPolicyFixture.ExecuteAsync(mode, root.Path); FileRotationPolicyExpectations.Validate(mode, actual);
        switch (corruption)
        {
            case "filesystem": actual["after"]!.AsObject().Remove(actual["after"]!.AsObject().Select(x => x.Key).Single(x => x.EndsWith("00000000000000000001.closed", StringComparison.Ordinal) && !x.Contains("ffffffff", StringComparison.Ordinal))); break;
            case "membership": actual["namesAfter"]!.AsArray().Add("new.closed"); break;
            case "code": actual["fault"]!["outputs"]![1]!["faultCode"] = "Other"; break;
            case "console": actual["final"]!["outputs"]![0]!["written"] = 0L; break;
            case "release": actual["leaseReleased"] = false; break;
            case "candidate-content":
                string candidate = actual["after"]!.AsObject().Select(x => x.Key).Single(x => x.EndsWith(".tmp", StringComparison.Ordinal)); JsonObject next = JsonNode.Parse(Convert.FromBase64String(actual["after"]![candidate]!.GetValue<string>()))!.AsObject(); next["segments"]!.AsArray()[^1]!["id"] = 1L; actual["after"]![candidate] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(next.ToJsonString() + "\n")); break;
            default: actual["final"]!["outputs"]![1]!["abandoned"] = 0L; break;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() => FileRotationPolicyExpectations.Validate(mode, actual));
        Assert.AreEqual(corruption switch { "filesystem" => "exact filesystem transition", "membership" => "exact membership", "code" => "terminal fault", "console" => "console progress", "release" => "physical creation/release", "candidate-content" => "candidate inventory", _ => "file accounting" }, error.Message);
    }
}
