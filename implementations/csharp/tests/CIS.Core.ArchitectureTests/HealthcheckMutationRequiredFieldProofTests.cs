using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Proofs = CIS.Verification.HealthcheckMutationRequiredFieldProofs;
using Semantics = CIS.Verification.HealthcheckMutationSemanticProofs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationRequiredFieldProofTests
{
    [TestMethod]
    public void ExactReviewedRequiredFieldObservationBindsTheOriginalDefinitionWithoutChangingItsRawStatus()
    {
        var fixture = new Fixture();
        string original = fixture.Report.ToJsonString();
        Assert.IsTrue(fixture.Match(out JsonObject? witness));
        Assert.AreEqual("ReviewedMissingRequiredJsonField", (string?)witness!["kind"]);
        Assert.AreEqual("$.monitors[0].name", (string?)witness["requiredJsonPath"]);
        Assert.AreEqual("Killed", (string?)witness["rawStatus"]);
        Assert.AreEqual(false, (bool?)witness["timeoutReclassified"]);
        Assert.AreEqual(false, (bool?)witness["pdbLineEqualsOriginalSourceLineClaim"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("report")]
    [DataRow("definition")]
    [DataRow("test-source")]
    [DataRow("code-file")]
    [DataRow("guid")]
    [DataRow("timeout-status")]
    [DataRow("unreviewed-stack")]
    [DataRow("missing-path-review")]
    [DataRow("missing-input-review")]
    [DataRow("missing-cause-review")]
    [DataRow("bad-stack-pin")]
    [DataRow("wrong-exception")]
    [DataRow("wrong-getter")]
    [DataRow("wrong-receiving-frame")]
    [DataRow("wrong-terminal")]
    [DataRow("cleanup")]
    [DataRow("initialize")]
    [DataRow("deadline")]
    [DataRow("cancellation")]
    public void MissingKeysFromUnboundSourcesOrUnexplainedFailuresCannotSupplyCredit(string mode)
    {
        var fixture = new Fixture();
        switch (mode)
        {
            case "report": fixture.ReportHash = new string('b', 64); break;
            case "definition": fixture.RawDefinition["replacement"] = "other"; break;
            case "test-source": fixture.TestFile["source"] = "// changed\n"; break;
            case "code-file": fixture.CodeFile += ".other"; break;
            case "guid": fixture.Guid = "22222222-2222-4222-8222-222222222222"; break;
            case "timeout-status": fixture.Definition = fixture.Definition with { Status = "Timeout" }; break;
            case "unreviewed-stack": fixture.Stack += "\n"; break;
            case "missing-path-review": fixture.Review = fixture.Review with { JsonPath = "" }; break;
            case "missing-input-review": fixture.Review = fixture.Review with { AdmittedInputReview = "" }; break;
            case "missing-cause-review": fixture.Review = fixture.Review with { CausalReview = "" }; break;
            case "bad-stack-pin": fixture.Review = fixture.Review with { NativeStackSha256 = "unbound" }; break;
            case "wrong-exception": fixture.Message = fixture.Message.Replace("KeyNotFoundException", "InvalidOperationException", StringComparison.Ordinal); break;
            case "wrong-getter": fixture.ReviewStack(fixture.Stack.Replace("JsonElement.GetProperty", "JsonElement.GetString", StringComparison.Ordinal)); break;
            case "wrong-receiving-frame": fixture.ReviewStack(fixture.Stack.Replace(".Receiving(", ".Other(", StringComparison.Ordinal)); break;
            case "wrong-terminal": fixture.ReviewStack(fixture.Stack.Replace("TestMethodInfo.ExecuteInternalAsync", "Unrelated.Execute", StringComparison.Ordinal)); break;
            case "cleanup": fixture.ReviewStack(fixture.Stack.Replace("\n   at " + Fixture.Method, "\n   at Fixture.DisposeAsync()\n   at " + Fixture.Method, StringComparison.Ordinal)); break;
            case "initialize": fixture.ReviewStack(fixture.Stack.Replace("\n   at " + Fixture.Method, "\n   at Fixture.TestInitialize()\n   at " + Fixture.Method, StringComparison.Ordinal)); break;
            case "deadline": fixture.ReviewStack(fixture.Stack.Replace("\n   at " + Fixture.Method, "\n   at System.TimeoutException.Guard()\n   at " + Fixture.Method, StringComparison.Ordinal)); break;
            case "cancellation": fixture.ReviewStack(fixture.Stack.Replace("\n   at " + Fixture.Method, "\n   at Fixture.OperationCanceledException()\n   at " + Fixture.Method, StringComparison.Ordinal)); break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.IsFalse(fixture.Match(out JsonObject? witness));
        Assert.IsNull(witness);
    }

    [TestMethod]
    public void ASyntheticReviewCannotEnterTheFixedShippingCatalog()
    {
        var fixture = new Fixture();
        Assert.IsTrue(fixture.Match(out _));
        Assert.IsFalse(Proofs.TryMatch(fixture.Context(), "7", fixture.Guid, Fixture.Method, fixture.CodeFile,
            fixture.Message, fixture.Stack, out _));
    }

    // Explicit parser input only; this is not a native product observation.
    private sealed class Fixture
    {
        internal const string Method = "CIS.Tests.UnitTests.SyntheticRequiredField.Receiving";
        private const string OriginalGuid = "11111111-1111-4111-8111-111111111111";
        private const string CorePath = "/receiving/implementations/csharp/src/CIS.Core/Healthcheck/Reporting/Example.cs";
        private const string TestPath = "implementations/csharp/tests/CIS.Core.UnitTests/Example.cs";
        internal string Guid { get; set; } = OriginalGuid;
        internal string CodeFile { get; set; } = "/receiving/" + TestPath;
        internal string ReportHash { get; set; } = new('a', 64);
        internal Definition Definition { get; set; }
        internal Proofs.ReviewedCase Review { get; set; }
        internal JsonObject Report { get; }
        internal JsonObject RawDefinition => Report["files"]![CorePath]!["mutants"]![0]!.AsObject();
        internal JsonObject TestFile => Report["testFiles"]!["/receiving/" + TestPath]!.AsObject();
        internal string Message { get; set; } = "Test method " + Method + " threw exception:\r\nSystem.Collections.Generic.KeyNotFoundException: The given key was not present in the dictionary.";
        internal string Stack { get; set; }

        internal Fixture()
        {
            const string source = "writer.WriteString(\"name\", monitor.Name);";
            const string testSource = "// Explicit synthetic receiving input.\n";
            Definition = new Definition("7", "Healthcheck/Reporting/Example.cs", Hash(source), 0, source.Length,
                "Statement mutation", source, ";", "Killed", [OriginalGuid]);
            Stack = "   at System.Text.Json.JsonElement.GetProperty(String propertyName)\n   at " + Method + "() in " + CodeFile + ":line 18\n"
                + "   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n";
            Review = new(new Semantics.SourceBinding(ReportHash, Definition, OriginalGuid, Method, TestPath, Hash(testSource)),
                "$.monitors[0].name", Hash(Stack), "Explicit synthetic admitted input review.", "Explicit synthetic causal review.");
            Report = new JsonObject
            {
                ["files"] = new JsonObject
                {
                    [CorePath] = new JsonObject
                    {
                        ["source"] = source,
                        ["mutants"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "7",
                            ["status"] = "Killed",
                            ["mutatorName"] = "Statement mutation",
                            ["replacement"] = ";",
                            ["killedBy"] = new JsonArray(OriginalGuid),
                            ["location"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = 1, ["column"] = 1 },
                                ["end"] = new JsonObject { ["line"] = 1, ["column"] = source.Length + 1 }
                            }
                        })
                    }
                },
                ["testFiles"] = new JsonObject
                {
                    ["/receiving/" + TestPath] = new JsonObject
                    {
                        ["source"] = testSource,
                        ["tests"] = new JsonArray(new JsonObject { ["id"] = OriginalGuid, ["name"] = Method })
                    }
                }
            };
        }

        internal Semantics.Context Context() => new(ReportHash, [Definition], Report);
        internal bool Match(out JsonObject? witness) => Proofs.MatchReviewedCase(Review, Context(), "7", Guid, Method, CodeFile, Message, Stack, out witness);
        internal void ReviewStack(string value) { Stack = value; Review = Review with { NativeStackSha256 = Hash(value) }; }
        private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));
    }
}
