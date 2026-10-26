using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using Binding = CIS.Verification.HealthcheckMutationSemanticProofs.SourceBinding;
using CallFrame = CIS.Verification.HealthcheckMutationAdditionalUnitSemanticProofs.CallFrame;
using Proofs = CIS.Verification.HealthcheckMutationUnitContinuation2Proofs;
using Review = CIS.Verification.HealthcheckMutationAdditionalUnitSemanticProofs.ReviewedCase;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationUnitContinuation2Tests
{
    [TestMethod]
    [DataRow("direct")]
    [DataRow("bcl")]
    [DataRow("callee")]
    public void ExplicitSyntheticObservationRequiresItsFullSourceBoundReceivingRoute(string route)
    {
        var fixture = new Fixture(route);
        string original = fixture.Report.ToJsonString();
        Assert.IsTrue(fixture.Match(out JsonObject? witness));
        Assert.AreEqual("ReviewedUnitContinuation2SemanticException", (string?)witness!["kind"]);
        Assert.AreEqual(fixture.Definition.Identity, (string?)witness["definitionIdentity"]);
        Assert.AreEqual(Sha(fixture.Message), (string?)witness["nativeMessageSha256"]);
        Assert.AreEqual(Sha(fixture.Stack), (string?)witness["nativeStackSha256"]);
        Assert.AreEqual(false, (bool?)witness["pdbLineEqualsOriginalSourceLineClaim"]);
        Assert.AreEqual(false, (bool?)witness["timeoutReclassified"]);
        Assert.AreEqual(original, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("report-hash")]
    [DataRow("checked-id")]
    [DataRow("checked-status")]
    [DataRow("checked-source-path")]
    [DataRow("checked-source-hash")]
    [DataRow("checked-start")]
    [DataRow("checked-end")]
    [DataRow("checked-mutator")]
    [DataRow("checked-original")]
    [DataRow("checked-replacement")]
    [DataRow("checked-killedby")]
    [DataRow("checked-extra-killedby")]
    [DataRow("raw-status")]
    [DataRow("raw-id")]
    [DataRow("raw-mutator")]
    [DataRow("raw-replacement")]
    [DataRow("raw-start")]
    [DataRow("raw-end")]
    [DataRow("raw-source")]
    [DataRow("raw-killedby")]
    [DataRow("duplicate-raw-definition")]
    [DataRow("duplicate-source-alias")]
    [DataRow("test-guid")]
    [DataRow("test-method")]
    [DataRow("test-source")]
    [DataRow("test-path")]
    [DataRow("duplicate-test-guid")]
    [DataRow("native-guid")]
    [DataRow("native-method")]
    [DataRow("native-code-file")]
    public void AnExactExceptionCannotApproveChangedReportDefinitionOrReceivingIdentity(string mode)
    {
        var fixture = new Fixture();
        switch (mode)
        {
            case "report-hash": fixture.ReportHash = new string('b', 64); break;
            case "checked-id": fixture.Definition = fixture.Definition with { Id = "2" }; break;
            case "checked-status": fixture.Definition = fixture.Definition with { Status = "Timeout" }; break;
            case "checked-source-path": fixture.Definition = fixture.Definition with { SourcePath = "Healthcheck/Other.cs" }; break;
            case "checked-source-hash": fixture.Definition = fixture.Definition with { SourceSha256 = new string('b', 64) }; break;
            case "checked-start": fixture.Definition = fixture.Definition with { Start = 1 }; break;
            case "checked-end": fixture.Definition = fixture.Definition with { End = 1 }; break;
            case "checked-mutator": fixture.Definition = fixture.Definition with { Mutator = "Other mutation" }; break;
            case "checked-original": fixture.Definition = fixture.Definition with { Original = "other" }; break;
            case "checked-replacement": fixture.Definition = fixture.Definition with { Replacement = "other" }; break;
            case "checked-killedby": fixture.Definition = fixture.Definition with { KilledBy = [OtherGuid] }; break;
            case "checked-extra-killedby": fixture.Definition = fixture.Definition with { KilledBy = [Guid, OtherGuid] }; break;
            case "raw-status": fixture.Raw["status"] = "Survived"; break;
            case "raw-id": fixture.Raw["id"] = "2"; break;
            case "raw-mutator": fixture.Raw["mutatorName"] = "Other mutation"; break;
            case "raw-replacement": fixture.Raw["replacement"] = "other"; break;
            case "raw-start": fixture.Raw["location"]!["start"]!["column"] = 2; break;
            case "raw-end": fixture.Raw["location"]!["end"]!["column"] = 2; break;
            case "raw-source": fixture.Core["source"] = fixture.Source + " "; break;
            case "raw-killedby": fixture.Raw["killedBy"] = new JsonArray(OtherGuid); break;
            case "duplicate-raw-definition": fixture.Core["mutants"]!.AsArray().Add(fixture.Raw.DeepClone()); break;
            case "duplicate-source-alias": fixture.Report["files"]!["/duplicate" + CorePath] = fixture.Core.DeepClone(); break;
            case "test-guid": fixture.Test["tests"]![0]!["id"] = OtherGuid; break;
            case "test-method": fixture.Test["tests"]![0]!["name"] = Method + "Other"; break;
            case "test-source": fixture.Test["source"] = fixture.TestSource + " "; break;
            case "test-path": fixture.Report["testFiles"] = new JsonObject { [TestPath + "Other"] = fixture.Test.DeepClone() }; break;
            case "duplicate-test-guid": fixture.Test["tests"]!.AsArray().Add(fixture.Test["tests"]![0]!.DeepClone()); break;
            case "native-guid": fixture.NativeGuid = OtherGuid; break;
            case "native-method": fixture.NativeMethod = Method + "Other"; break;
            case "native-code-file": fixture.NativeCodeFile = TestPath + "Other"; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.IsFalse(fixture.Match(out JsonObject? witness));
        Assert.IsNull(witness);
    }

    [TestMethod]
    [DataRow("message-text")]
    [DataRow("message-newline")]
    [DataRow("message-trailing-space")]
    [DataRow("message-exception-type")]
    [DataRow("stack-space")]
    [DataRow("stack-line")]
    [DataRow("stack-newline")]
    [DataRow("stack-frame")]
    [DataRow("stack-source")]
    [DataRow("message-hash")]
    [DataRow("stack-hash")]
    [DataRow("upper-case-hash")]
    public void CompleteNativeMessageAndStackBytesAreRequiredEvenForTheSameExceptionType(string mode)
    {
        var fixture = new Fixture();
        switch (mode)
        {
            case "message-text": fixture.Message += " changed"; break;
            case "message-newline": fixture.Message = fixture.Message.Replace("\r\n", "\n", StringComparison.Ordinal); break;
            case "message-trailing-space": fixture.Message += " "; break;
            case "message-exception-type": fixture.Message = fixture.Message.Replace("ArgumentException", "InvalidOperationException", StringComparison.Ordinal); break;
            case "stack-space": fixture.Stack = " " + fixture.Stack; break;
            case "stack-line": fixture.Stack = fixture.Stack.Replace(":line 9000", ":line 1", StringComparison.Ordinal); break;
            case "stack-newline": fixture.Stack = fixture.Stack.Replace("\n", "\r\n", StringComparison.Ordinal); break;
            case "stack-frame": fixture.Stack = fixture.Stack.Replace("Synthetic.Receive", "Synthetic.Other", StringComparison.Ordinal); break;
            case "stack-source": fixture.Stack = fixture.Stack.Replace(CorePath, CorePath + "Other", StringComparison.Ordinal); break;
            case "message-hash": fixture.Review = fixture.Review with { NativeMessageSha256 = new string('a', 64) }; break;
            case "stack-hash": fixture.Review = fixture.Review with { NativeStackSha256 = new string('a', 64) }; break;
            case "upper-case-hash": fixture.Review = fixture.Review with { NativeStackSha256 = fixture.Review.NativeStackSha256.ToUpperInvariant() }; break;
            default: throw new InvalidOperationException("Unknown byte-binding control.");
        }
        Assert.IsFalse(fixture.Match(out _));
    }

    [TestMethod]
    [DataRow("admission")]
    [DataRow("contract")]
    [DataRow("cause")]
    [DataRow("wrong-bcl")]
    [DataRow("wrong-core")]
    [DataRow("wrong-core-source")]
    [DataRow("unsafe-source")]
    [DataRow("missing-core")]
    [DataRow("missing-receiver")]
    [DataRow("wrong-receiver")]
    [DataRow("timeout")]
    [DataRow("cancel")]
    [DataRow("aggregate")]
    [DataRow("cleanup")]
    [DataRow("initialization")]
    public void RebindingHashesCannotSupplyAnUnreviewedOrUnsafeReceivingRoute(string mode)
    {
        var fixture = new Fixture("bcl");
        CallFrame[] route = fixture.Review.ThrowRoute.ToArray();
        switch (mode)
        {
            case "admission": fixture.Review = fixture.Review with { AdmittedInputReview = " " }; break;
            case "contract": fixture.Review = fixture.Review with { ContractRouteReview = " " }; break;
            case "cause": fixture.Review = fixture.Review with { CausalReview = " " }; break;
            case "wrong-bcl": route[0] = new("at System.Other.Throw()", null); break;
            case "wrong-core": route[1] = route[1] with { Method = "CIS.Healthcheck.Synthetic.Other" }; break;
            case "wrong-core-source": route[1] = route[1] with { SourcePath = "implementations/csharp/src/CIS.Core/Healthcheck/Other.cs" }; break;
            case "unsafe-source": route[1] = route[1] with { SourcePath = "implementations/csharp/src/CIS.Core/../Test.cs" }; break;
            case "missing-core": route = [route[0], route[^1]]; break;
            case "missing-receiver": route = route[..^1]; break;
            case "wrong-receiver": route[^1] = route[^1] with { Method = Method + "Other" }; break;
            case "timeout": fixture.Message = fixture.Message.Replace("ArgumentException", "TimeoutException", StringComparison.Ordinal); fixture.Review = fixture.Review with { ExceptionType = "System.TimeoutException" }; break;
            case "cancel": fixture.Message = fixture.Message.Replace("ArgumentException", "OperationCanceledException", StringComparison.Ordinal); fixture.Review = fixture.Review with { ExceptionType = "System.OperationCanceledException" }; break;
            case "aggregate": fixture.Message = fixture.Message.Replace("ArgumentException", "AggregateException", StringComparison.Ordinal); fixture.Review = fixture.Review with { ExceptionType = "System.AggregateException" }; break;
            case "cleanup": fixture.Stack += "\n at Fixture.DisposeAsync()"; break;
            case "initialization": fixture.Stack += "\n at Fixture.TestInitialize()"; break;
            default: throw new InvalidOperationException("Unknown route control.");
        }
        fixture.Review = fixture.Review with { ThrowRoute = route, NativeMessageSha256 = Sha(fixture.Message), NativeStackSha256 = Sha(fixture.Stack) };
        Assert.IsFalse(fixture.Match(out _));
    }

    [TestMethod]
    public void FixedCatalogContainsOnlyThirtyThreeNewObservationsFromOneAdmittedPublicSnapshot()
    {
        string[] expected = ["3927", "3929", "3930", "3934", "3935", "3936", "3937", "3941", "3944", "3945", "3950", "3955", "3961", "3983", "3988", "3990", "3991", "4038", "4039", "4040", "4056", "4060", "4064", "4065", "4071", "4075", "4087", "4092", "4095", "4101", "4116", "4122", "4128"];
        CollectionAssert.AreEqual(expected, HealthcheckMutationUnitContinuation2Catalog.Cases.Select(value => value.Binding.Definition.Id).ToArray());
        Assert.AreEqual(1, HealthcheckMutationUnitContinuation2Catalog.Cases.Select(value => value.Binding.Guid).Distinct(StringComparer.Ordinal).Count());
        foreach (Review review in HealthcheckMutationUnitContinuation2Catalog.Cases)
        {
            Assert.AreEqual(HealthcheckMutationSemanticCatalog.Unit18ReportSha256, review.Binding.ReportSha256);
            Assert.AreEqual("Killed", review.Binding.Definition.Status);
            Assert.IsTrue(review.Binding.Definition.KilledBy.Contains(review.Binding.Guid, StringComparer.Ordinal));
            Assert.AreEqual(review.Binding.Definition.KilledBy.Length, review.Binding.Definition.KilledBy.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [TestMethod]
    [DataRow("2083")]
    [DataRow("3150")]
    [DataRow("4301")]
    [DataRow("1559")]
    [DataRow("1200")]
    [DataRow("1740")]
    [DataRow("312")]
    [DataRow("534")]
    [DataRow("548")]
    [DataRow("4291")]
    [DataRow("3956")]
    [DataRow("4066")]
    [DataRow("536")]
    [DataRow("685")]
    [DataRow("4294")]
    [DataRow("unknown")]
    public void HeldCleanupTimeoutAlreadyReviewedAndUnknownRoutesCannotEnterThisCatalog(string id)
    {
        var fixture = new Fixture();
        Assert.IsFalse(HealthcheckMutationUnitContinuation2Catalog.Cases.Any(value => value.Binding.Definition.Id == id));
        Assert.IsFalse(Proofs.TryMatch(fixture.Context(), id, Guid, Method, TestPath, fixture.Message, fixture.Stack, out _));
        Assert.IsFalse(Proofs.TryMatch(null, id, Guid, Method, TestPath, fixture.Message, fixture.Stack, out _));
    }

    [TestMethod]
    public void AnIndependentlyTestableSyntheticReviewIsNotAMaintainedCatalogEntry()
    {
        var fixture = new Fixture();
        Assert.IsTrue(fixture.Match(out _));
        Assert.IsFalse(Proofs.TryMatch(fixture.Context(), "1", Guid, Method, TestPath, fixture.Message, fixture.Stack, out _));
    }

    private const string Guid = "11111111-1111-4111-8111-111111111111";
    private const string OtherGuid = "22222222-2222-4222-8222-222222222222";
    private const string Method = "CIS.Tests.UnitTests.Healthcheck.SyntheticAdditionalSemanticFixture.ReceivesValidInput";
    private const string CoreRelative = "implementations/csharp/src/CIS.Core/Healthcheck/Test.cs";
    private const string TestRelative = "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Fixture.cs";
    private const string CorePath = "/synthetic/" + CoreRelative;
    private const string TestPath = "/synthetic/" + TestRelative;

    // Explicit synthetic parser input. It is neither subject execution nor a
    // fabricated production/native receipt; real original-data replay is separate.
    private sealed class Fixture
    {
        internal string Source { get; } = "value > maximum\n";
        internal string TestSource { get; } = "// Synthetic receiving source only.\n";
        internal string ReportHash { get; set; } = new('a', 64);
        internal Definition Definition { get; set; }
        internal Review Review { get; set; }
        internal JsonObject Report { get; }
        internal JsonObject Core => Report["files"]![CorePath]!.AsObject();
        internal JsonObject Raw => Core["mutants"]![0]!.AsObject();
        internal JsonObject Test => Report["testFiles"]![TestPath]!.AsObject();
        internal string NativeGuid { get; set; } = Guid;
        internal string NativeMethod { get; set; } = Method;
        internal string NativeCodeFile { get; set; } = TestPath;
        internal string Message { get; set; } = "Test method " + Method + " threw exception:\r\nSystem.ArgumentException: Synthetic admitted-input rejection.";
        internal string Stack { get; set; }

        internal Fixture(string route = "direct")
        {
            Definition = new("1", "Healthcheck/Test.cs", Sha(Source), 0, 15, "Equality mutation", "value > maximum", "value >= maximum", "Killed", [Guid]);
            var frames = new List<CallFrame>();
            if (route == "bcl") { frames.Add(new("at System.Nullable`1.get_Value()", null)); }
            if (route == "callee") { frames.Add(new("CIS.Healthcheck.Synthetic.Callee", "implementations/csharp/src/CIS.Core/Healthcheck/Callee.cs")); }
            frames.Add(new("CIS.Healthcheck.Synthetic.Receive", CoreRelative));
            frames.Add(new(Method, TestRelative));
            Stack = string.Join('\n', frames.Select(frame => frame.SourcePath is null ? "   " + frame.Method
                : "   at " + frame.Method + "() in /synthetic/" + frame.SourcePath + ":line 9000"))
                + "\n   at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)\n";
            Review = new(new Binding(ReportHash, Definition, Guid, Method, TestRelative, Sha(TestSource)),
                "System.ArgumentException", Sha(Message), Sha(Stack), frames.ToArray(),
                "Synthetic admitted receiving input.", "Synthetic explicit adopted receiving invariant.", "Synthetic selected causal route.");
            Report = new JsonObject
            {
                ["files"] = new JsonObject
                {
                    [CorePath] = new JsonObject
                    {
                        ["source"] = Source,
                        ["mutants"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "1",
                            ["status"] = "Killed",
                            ["mutatorName"] = Definition.Mutator,
                            ["replacement"] = Definition.Replacement,
                            ["killedBy"] = new JsonArray(Guid),
                            ["location"] = new JsonObject
                            {
                                ["start"] = new JsonObject { ["line"] = 1, ["column"] = 1 },
                                ["end"] = new JsonObject { ["line"] = 1, ["column"] = 16 }
                            }
                        })
                    }
                },
                ["testFiles"] = new JsonObject
                {
                    [TestPath] = new JsonObject { ["source"] = TestSource, ["tests"] = new JsonArray(new JsonObject { ["id"] = Guid, ["name"] = Method }) }
                }
            };
        }

        internal HealthcheckMutationSemanticProofs.Context Context() => HealthcheckMutationSemanticProofs.CreateContext(Report, ReportHash, [Definition])!;
        internal bool Match(out JsonObject? witness) => Proofs.MatchReviewedCase(Review, Context(), "1", NativeGuid, NativeMethod,
            NativeCodeFile, Message, Stack, out witness);
    }

    private static string Sha(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));
}
