using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using A = CIS.Verification.HealthcheckMutationAdditionalUnitSemanticProofs;
using P = CIS.Verification.HealthcheckMutationPipeOutputProofs;
using S = CIS.Verification.HealthcheckMutationSemanticProofs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationPipeOutputProofTests
{
    private const string OriginalReportSha256 = "847327cc1bb2465664ad095ed06b3b1bd26707b5a10cc15c50cc2cee81c67836";
    private const string OriginalSourceSha256 = "ad5afc9bf1f19fddcbd478efd88616607c87262061c75da6cf97407a2994a512";
    private const string OriginalTestSha256 = "115c483734dc6600331daa5d4d4cff43a75dcf40ae6af69c41c7c66e89ab7691";
    private const string OtherGuid = "22222222-2222-4222-8222-222222222222";

    [TestMethod]
    [DataRow("3023", 23)]
    [DataRow("3025", 23)]
    [DataRow("3026", 23)]
    [DataRow("3027", 1)]
    public void ExactOriginalStdoutFailureBindsItsFrozenReceivingAndMutantWithoutChangingHistory(string id, int killedBy)
    {
        var f = new Fixture(id);
        string before = f.Report.ToJsonString();
        Assert.AreEqual(Observation(id).Identity, f.Definition.Identity);
        Assert.HasCount(killedBy, f.Definition.KilledBy);
        Assert.IsTrue(f.ShippingMatch(out JsonObject? witness));
        Assert.AreEqual("ReviewedPipeOutputFailure", (string?)witness!["kind"]);
        Assert.AreEqual("Killed", (string?)witness["rawStatus"]);
        Assert.AreEqual(false, (bool?)witness["timeoutReclassified"]);
        Assert.AreEqual(false, (bool?)witness["childFailuresDischarged"]);
        Assert.AreEqual(false, (bool?)witness["wholeCampaignApproval"]);
        Assert.AreEqual(false, (bool?)witness["pdbLineEqualsOriginalSourceLineClaim"]);
        Assert.AreEqual(before, f.Report.ToJsonString());
    }

    [TestMethod]
    public void CatalogContainsOnlyFourReviewedStdoutRowsAndPreservesTheRawReplacementRendering()
    {
        var cases = HealthcheckMutationPipeOutputCatalog.Cases;
        Assert.HasCount(4, cases);
        CollectionAssert.AreEquivalent(new[] { "3023", "3025", "3026", "3027" }, cases.Select(x => x.Binding.Definition.Id).ToArray());
        Assert.IsTrue(cases.All(x => x.Binding.ReportSha256 == OriginalReportSha256 && x.Binding.Definition.SourceSha256 == OriginalSourceSha256
            && x.Binding.TestSourceSha256 == OriginalTestSha256 && x.ExceptionType == "System.IO.IOException" && x.Binding.Definition.Status == "Killed"));
        // Raw native replacement text is identity evidence. These assertions do
        // not claim that reparsing the text recreates Stryker's mutated AST.
        Assert.AreEqual("IOException or SocketException and UnauthorizedAccessException", cases.Single(x => x.Binding.Definition.Id == "3027").Binding.Definition.Replacement);
    }

    [TestMethod]
    [DataRow("report")]
    [DataRow("checked-replacement")]
    [DataRow("checked-source-sha")]
    [DataRow("checked-span")]
    [DataRow("checked-operator")]
    [DataRow("checked-killedby-order")]
    [DataRow("raw-killedby-order")]
    [DataRow("raw-killedby-missing")]
    [DataRow("raw-killedby-extra")]
    [DataRow("raw-replacement")]
    [DataRow("raw-status")]
    [DataRow("checked-timeout")]
    [DataRow("source")]
    [DataRow("test-source")]
    [DataRow("test-guid")]
    [DataRow("test-method")]
    [DataRow("duplicate-test-guid")]
    [DataRow("native-id")]
    [DataRow("native-guid")]
    [DataRow("native-method")]
    [DataRow("native-code-file")]
    public void AnotherDefinitionOrUnboundReceivingInputCannotSupplyAStdoutWitness(string mode)
    {
        var f = new Fixture("3023");
        switch (mode)
        {
            case "report": f.ReportHash = new string('b', 64); break;
            case "checked-replacement": f.Definition = f.Definition with { Replacement = "other" }; break;
            case "checked-source-sha": f.Definition = f.Definition with { SourceSha256 = new string('b', 64) }; break;
            case "checked-span": f.Definition = f.Definition with { Start = f.Definition.Start + 1 }; break;
            case "checked-operator": f.Definition = f.Definition with { Mutator = "other" }; break;
            case "checked-killedby-order": f.Definition = f.Definition with { KilledBy = f.Definition.KilledBy.Reverse().ToArray() }; break;
            case "raw-killedby-order": f.Raw["killedBy"] = new JsonArray(f.Definition.KilledBy.Reverse().Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()); break;
            case "raw-killedby-missing": f.Raw["killedBy"]!.AsArray().RemoveAt(0); break;
            case "raw-killedby-extra": f.Raw["killedBy"]!.AsArray().Add(OtherGuid); break;
            case "raw-replacement": f.Raw["replacement"] = "other"; break;
            case "raw-status": f.Raw["status"] = "Survived"; break;
            case "checked-timeout": f.Definition = f.Definition with { Status = "Timeout" }; break;
            case "source": f.CoreFile["source"] = OriginalSource + " "; break;
            case "test-source": f.TestFile["source"] = OriginalTestSource + " "; break;
            case "test-guid": f.TestFile["tests"]![0]!["id"] = OtherGuid; break;
            case "test-method": f.TestFile["tests"]![0]!["name"] = Fixture.Method + "Other"; break;
            case "duplicate-test-guid": f.TestFile["tests"]!.AsArray().Add(f.TestFile["tests"]![0]!.DeepClone()); break;
            case "native-id": f.Id = "3024"; break;
            case "native-guid": f.Guid = OtherGuid; break;
            case "native-method": f.MethodName += "Other"; break;
            case "native-code-file": f.CodeFile += ".other"; break;
            default: throw new InvalidOperationException("Unknown binding control.");
        }
        Assert.IsFalse(f.ShippingMatch(out JsonObject? witness));
        Assert.IsNull(witness);
    }

    [TestMethod]
    [DataRow("message-byte")]
    [DataRow("message-line-ending")]
    [DataRow("stack-byte")]
    [DataRow("stack-line-ending")]
    public void UnreviewedNativeStringBytesRemainUnqualified(string mode)
    {
        var f = new Fixture("3027");
        switch (mode)
        {
            case "message-byte": f.Message += " "; break;
            case "message-line-ending": f.Message = f.Message.Replace("\r\n", "\n", StringComparison.Ordinal); break;
            case "stack-byte": f.Stack += "\n"; break;
            case "stack-line-ending": f.Stack = f.Stack.Replace("\n", "\r\n", StringComparison.Ordinal); break;
            default: throw new InvalidOperationException("Unknown byte control.");
        }
        Assert.IsFalse(f.ShippingMatch(out _));
    }

    [TestMethod]
    [DataRow("wrong-message")]
    [DataRow("wrong-exception")]
    [DataRow("wrong-core-method")]
    [DataRow("wrong-core-file")]
    [DataRow("wrong-core-line")]
    [DataRow("wrong-receiving-line")]
    [DataRow("wrong-unwind-line")]
    [DataRow("missing-unwind")]
    [DataRow("extra-frame")]
    [DataRow("wrong-receiving-method")]
    [DataRow("wrong-adapter-terminal")]
    [DataRow("cleanup")]
    [DataRow("initialize")]
    [DataRow("deadline")]
    [DataRow("cancellation")]
    [DataRow("aggregate")]
    [DataRow("missing-input")]
    [DataRow("missing-contract")]
    [DataRow("missing-cause")]
    [DataRow("bad-message-pin")]
    [DataRow("bad-stack-pin")]
    public void RepinningSyntheticParserInputCannotApproveADifferentFailureOrRoute(string mode)
    {
        var f = new Fixture("3027");
        switch (mode)
        {
            case "wrong-message": f.Message = f.Message.Replace("private output path", "unrelated failure", StringComparison.Ordinal); break;
            case "wrong-exception": f.Message = f.Message.Replace("System.IO.IOException", "System.InvalidOperationException", StringComparison.Ordinal); f.Review = f.Review with { ExceptionType = "System.InvalidOperationException" }; break;
            case "wrong-core-method": f.Stack = f.Stack.Replace("HealthProbeClient.RunAsync(", "HealthProbeClient.Other(", StringComparison.Ordinal); break;
            case "wrong-core-file": f.Stack = f.Stack.Replace(Fixture.CoreOrigin, Fixture.CoreOrigin + ".other", StringComparison.Ordinal); break;
            case "wrong-core-line": f.Stack = f.Stack.Replace(":line 89", ":line 90", StringComparison.Ordinal); break;
            case "wrong-receiving-line": f.Stack = f.Stack.Replace(":line 77", ":line 78", StringComparison.Ordinal); break;
            case "wrong-unwind-line": f.Stack = f.Stack.Replace(":line 81", ":line 82", StringComparison.Ordinal); break;
            case "missing-unwind": f.Stack = string.Join('\n', f.Stack.Split('\n').Where((_, index) => index != 2)); break;
            case "extra-frame": f.InsertFrame("Unrelated.Caller()"); break;
            case "wrong-receiving-method": f.Stack = f.Stack.Replace(Fixture.Method + "(", Fixture.Method + "Other(", StringComparison.Ordinal); break;
            case "wrong-adapter-terminal": f.Stack = f.Stack.Replace("TestMethodInfo.ExecuteInternalAsync", "Other.Execute", StringComparison.Ordinal); break;
            case "cleanup": f.InsertFrame("Fixture.DisposeAsync()"); break;
            case "initialize": f.InsertFrame("Fixture.TestInitialize()"); break;
            case "deadline": f.InsertFrame("System.TimeoutException.Guard()"); break;
            case "cancellation": f.InsertFrame("Fixture.OperationCanceledException()"); break;
            case "aggregate": f.InsertFrame("Fixture.AggregateException()"); break;
            case "missing-input": f.Review = f.Review with { AdmittedInputReview = "" }; break;
            case "missing-contract": f.Review = f.Review with { ContractRouteReview = "" }; break;
            case "missing-cause": f.Review = f.Review with { CausalReview = "" }; break;
            case "bad-message-pin": break;
            case "bad-stack-pin": break;
            default: throw new InvalidOperationException("Unknown route control.");
        }
        f.Review = f.Review with { NativeMessageSha256 = Hash(f.Message), NativeStackSha256 = Hash(f.Stack) };
        if (mode == "bad-message-pin") { f.Review = f.Review with { NativeMessageSha256 = "unbound" }; }
        if (mode == "bad-stack-pin") { f.Review = f.Review with { NativeStackSha256 = "unbound" }; }
        Assert.IsFalse(f.TypedMatch(out JsonObject? witness));
        Assert.IsNull(witness);
    }

    private sealed class Fixture
    {
        internal const string Root = "/data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source";
        internal const string CoreOrigin = Root + "/implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs";
        internal const string TestPath = "implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs";
        internal const string Method = "CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException";
        private const string OriginalGuid = "12195f68-1cc1-8ccc-9c48-0672e31d1faa";
        internal string Id { get; set; }
        internal string Guid { get; set; } = OriginalGuid;
        internal string MethodName { get; set; } = Method;
        internal string CodeFile { get; set; } = Root + "/" + TestPath;
        internal string ReportHash { get; set; } = OriginalReportSha256;
        internal Definition Definition { get; set; }
        internal A.ReviewedCase Review { get; set; }
        internal string Message { get; set; }
        internal string Stack { get; set; }
        internal JsonObject Report { get; }
        internal JsonObject CoreFile => Report["files"]![CoreOrigin]!.AsObject();
        internal JsonObject Raw => CoreFile["mutants"]![0]!.AsObject();
        internal JsonObject TestFile => Report["testFiles"]![Root + "/" + TestPath]!.AsObject();

        internal Fixture(string id)
        {
            Literal row = Observation(id);
            Assert.AreEqual(OriginalSourceSha256, Hash(OriginalSource));
            Assert.AreEqual(OriginalTestSha256, Hash(OriginalTestSource));
            JsonObject raw = JsonNode.Parse(row.RawDefinition)!.AsObject();
            int start = id == "3023" ? 2661 : 2674, end = id switch { "3023" => 2804, "3025" => 2783, "3026" => 2754, "3027" => 2735, _ => throw new InvalidOperationException("Unknown original definition.") };
            Definition = new(id, "Healthcheck/Pipes/HealthProbeClient.cs", OriginalSourceSha256, start, end, (string)raw["mutatorName"]!, OriginalSource[start..end], (string)raw["replacement"]!, "Killed", raw["killedBy"]!.AsArray().Select(x => (string)x!).ToArray());
            Id = id; Message = row.Message; Stack = row.Stack;
            Review = HealthcheckMutationPipeOutputCatalog.Cases.Single(x => x.Binding.Definition.Id == id);
            Report = new JsonObject
            {
                ["files"] = new JsonObject { [CoreOrigin] = new JsonObject { ["source"] = OriginalSource, ["mutants"] = new JsonArray(raw) } },
                ["testFiles"] = new JsonObject { [Root + "/" + TestPath] = new JsonObject { ["source"] = OriginalTestSource, ["tests"] = new JsonArray(new JsonObject { ["id"] = OriginalGuid, ["name"] = Method }) } }
            };
        }

        internal S.Context Context() => new(ReportHash, [Definition], Report);
        internal bool ShippingMatch(out JsonObject? witness) => P.TryMatch(Context(), Id, Guid, MethodName, CodeFile, Message, Stack, out witness);
        internal bool TypedMatch(out JsonObject? witness) => P.MatchReviewedCase(Review, Context(), Id, Guid, MethodName, CodeFile, Message, Stack, out witness);
        internal void InsertFrame(string frame) => Stack = Stack.Replace("   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync", "   at " + frame + "\n   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync", StringComparison.Ordinal);
    }

    private sealed record Literal(string Identity, string RawDefinition, string Message, string Stack);
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(value)));

    // Literal original definitions and native observations: parser inputs, not a new subject run.
    private static Literal Observation(string id) => id switch
    {
        "3023" => new("af098aad909cef8a4de54f6c41c824bb6a608d8c70ea2dee7ab9a37878bca746",
            "{\"id\":\"3023\",\"mutatorName\":\"Equality mutation\",\"replacement\":\"exception is not IOException or SocketException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException\",\"location\":{\"start\":{\"line\":51,\"column\":43},\"end\":{\"line\":51,\"column\":186}},\"status\":\"Killed\",\"static\":false,\"coveredBy\":[],\"killedBy\":[\"12195f68-1cc1-8ccc-9c48-0672e31d1faa\",\"18266aea-89b4-8b1f-bf6d-cc410a77f9f6\",\"112bbcbc-2fa1-8eb5-a729-ff9fa1ed29ff\",\"1743c149-c815-8010-9ddc-cd27331a1d67\",\"16562823-8a84-8b02-9d0e-4a8dab176c81\",\"1d70c0b1-5293-8759-9285-f4ba0724d7a5\",\"1882b6f0-18e7-8729-b054-e9ebc7835c73\",\"110e0adf-b816-8b24-937e-99b2aabb95b4\",\"16095166-f89a-872f-b06f-9690a67ca158\",\"1d5cc7e9-7198-888a-b6d7-19cb7a884fd9\",\"1612580c-f11d-8609-842c-8b9ea83f8299\",\"1841270b-7476-8e7d-af03-b7c6cd5ee3a4\",\"14c680d1-bb46-8c9c-ad82-cadba24488b6\",\"18e8a6dc-2e66-8124-aab3-17cc26173d13\",\"1684524e-7466-84a8-94cf-b1599f7313de\",\"13c068e6-fc5c-82ee-a4c9-a089534e5757\",\"15e948ee-aeaa-8722-8861-e8cee6081032\",\"15bb871e-6380-802d-8d32-da44629f30fa\",\"1aa18643-40b4-8611-80d1-333924489ae6\",\"12397c7c-c04b-8d89-9e89-1461f2335a8d\",\"1ae5dbcb-34fa-802f-8aef-b14726e19265\",\"1039b74e-43f3-80be-b841-9b2f11f8721d\",\"136fd481-ac9d-8944-bace-4dc18a9ae6ec\"]}",
            "Test method CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException threw exception:\r\nSystem.IO.IOException: private output path",
            "   at CIS.Healthcheck.Pipes.HealthProbeClient.RunAsync(String[] arguments, Stream standardOutput, TextWriter standardError, CancellationToken cancellationToken) in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs:line 89\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 77\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n"),
        "3025" => new("a7cedb5f93d1668def8e283ee02317dbf0848e616691c1608f5f517663e1b788",
            "{\"id\":\"3025\",\"mutatorName\":\"Logical mutation\",\"replacement\":\"IOException or SocketException or UnauthorizedAccessException or FormatException and InvalidOperationException\",\"location\":{\"start\":{\"line\":51,\"column\":56},\"end\":{\"line\":51,\"column\":165}},\"status\":\"Killed\",\"static\":false,\"coveredBy\":[],\"killedBy\":[\"12195f68-1cc1-8ccc-9c48-0672e31d1faa\",\"18266aea-89b4-8b1f-bf6d-cc410a77f9f6\",\"112bbcbc-2fa1-8eb5-a729-ff9fa1ed29ff\",\"1743c149-c815-8010-9ddc-cd27331a1d67\",\"16562823-8a84-8b02-9d0e-4a8dab176c81\",\"1d70c0b1-5293-8759-9285-f4ba0724d7a5\",\"1882b6f0-18e7-8729-b054-e9ebc7835c73\",\"110e0adf-b816-8b24-937e-99b2aabb95b4\",\"16095166-f89a-872f-b06f-9690a67ca158\",\"1d5cc7e9-7198-888a-b6d7-19cb7a884fd9\",\"1612580c-f11d-8609-842c-8b9ea83f8299\",\"1841270b-7476-8e7d-af03-b7c6cd5ee3a4\",\"14c680d1-bb46-8c9c-ad82-cadba24488b6\",\"18e8a6dc-2e66-8124-aab3-17cc26173d13\",\"1684524e-7466-84a8-94cf-b1599f7313de\",\"13c068e6-fc5c-82ee-a4c9-a089534e5757\",\"15e948ee-aeaa-8722-8861-e8cee6081032\",\"15bb871e-6380-802d-8d32-da44629f30fa\",\"1aa18643-40b4-8611-80d1-333924489ae6\",\"12397c7c-c04b-8d89-9e89-1461f2335a8d\",\"1ae5dbcb-34fa-802f-8aef-b14726e19265\",\"1039b74e-43f3-80be-b841-9b2f11f8721d\",\"136fd481-ac9d-8944-bace-4dc18a9ae6ec\"]}",
            "Test method CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException threw exception:\r\nSystem.IO.IOException: private output path",
            "   at CIS.Healthcheck.Pipes.HealthProbeClient.RunAsync(String[] arguments, Stream standardOutput, TextWriter standardError, CancellationToken cancellationToken) in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs:line 89\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 77\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n"),
        "3026" => new("b123a38dddc45b1f2d8884f8ae87658f5ea886b7a4d9d6ec206bb280286e5ef8",
            "{\"id\":\"3026\",\"mutatorName\":\"Logical mutation\",\"replacement\":\"IOException or SocketException or UnauthorizedAccessException and FormatException\",\"location\":{\"start\":{\"line\":51,\"column\":56},\"end\":{\"line\":51,\"column\":136}},\"status\":\"Killed\",\"static\":false,\"coveredBy\":[],\"killedBy\":[\"12195f68-1cc1-8ccc-9c48-0672e31d1faa\",\"18266aea-89b4-8b1f-bf6d-cc410a77f9f6\",\"112bbcbc-2fa1-8eb5-a729-ff9fa1ed29ff\",\"1743c149-c815-8010-9ddc-cd27331a1d67\",\"16562823-8a84-8b02-9d0e-4a8dab176c81\",\"1d70c0b1-5293-8759-9285-f4ba0724d7a5\",\"1882b6f0-18e7-8729-b054-e9ebc7835c73\",\"110e0adf-b816-8b24-937e-99b2aabb95b4\",\"16095166-f89a-872f-b06f-9690a67ca158\",\"1d5cc7e9-7198-888a-b6d7-19cb7a884fd9\",\"1612580c-f11d-8609-842c-8b9ea83f8299\",\"1841270b-7476-8e7d-af03-b7c6cd5ee3a4\",\"14c680d1-bb46-8c9c-ad82-cadba24488b6\",\"18e8a6dc-2e66-8124-aab3-17cc26173d13\",\"1684524e-7466-84a8-94cf-b1599f7313de\",\"13c068e6-fc5c-82ee-a4c9-a089534e5757\",\"15e948ee-aeaa-8722-8861-e8cee6081032\",\"15bb871e-6380-802d-8d32-da44629f30fa\",\"1aa18643-40b4-8611-80d1-333924489ae6\",\"12397c7c-c04b-8d89-9e89-1461f2335a8d\",\"1ae5dbcb-34fa-802f-8aef-b14726e19265\",\"1039b74e-43f3-80be-b841-9b2f11f8721d\",\"136fd481-ac9d-8944-bace-4dc18a9ae6ec\"]}",
            "Test method CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException threw exception:\r\nSystem.IO.IOException: private output path",
            "   at CIS.Healthcheck.Pipes.HealthProbeClient.RunAsync(String[] arguments, Stream standardOutput, TextWriter standardError, CancellationToken cancellationToken) in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs:line 89\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 77\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n"),
        "3027" => new("00ca470b837ab8a834177a0f5b2d117aea1ffb14b87a2aa19cf73b294a724f5b",
            "{\"id\":\"3027\",\"mutatorName\":\"Logical mutation\",\"replacement\":\"IOException or SocketException and UnauthorizedAccessException\",\"location\":{\"start\":{\"line\":51,\"column\":56},\"end\":{\"line\":51,\"column\":117}},\"status\":\"Killed\",\"static\":false,\"coveredBy\":[],\"killedBy\":[\"12195f68-1cc1-8ccc-9c48-0672e31d1faa\"]}",
            "Test method CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException threw exception:\r\nSystem.IO.IOException: private output path",
            "   at CIS.Healthcheck.Pipes.HealthProbeClient.RunAsync(String[] arguments, Stream standardOutput, TextWriter standardError, CancellationToken cancellationToken) in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs:line 89\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 77\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at CIS.Tests.UnitTests.Healthcheck.HealthPipeInternalMutationQualificationTests.FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException() in /data/cis-healthcheck-20261021-01a15a72/pipes-full-20/source/implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/Reporting/HealthPipeInternalMutationQualificationTests.cs:line 81\n   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n"),
        _ => throw new InvalidOperationException("Unknown original observation.")
    };

    private const string OriginalSource = "using System.Globalization;\nusing System.IO.Pipes;\nusing System.Net.Sockets;\nusing System.Text;\nusing CIS.Healthcheck.Reporting;\n\nnamespace CIS.Healthcheck.Pipes;\n\n/// <summary>The matching executable's internal transport; response validation stays in the shared Core codec.</summary>\ninternal static class HealthProbeClient\n{\n    internal static async Task<int> RunAsync(string[] arguments, Stream standardOutput, TextWriter standardError,\n        CancellationToken cancellationToken = default)\n    {\n        ArgumentNullException.ThrowIfNull(arguments);\n        ArgumentNullException.ThrowIfNull(standardOutput);\n        ArgumentNullException.ThrowIfNull(standardError);\n        if (!TryParseArguments(arguments, out var settings))\n        {\n            await WriteErrorAsync(standardError, \"InvalidArguments\").ConfigureAwait(false);\n            return 2;\n        }\n        try\n        {\n            using var budget = new HealthPipeBudget(TimeProvider.System, settings.TimeoutMilliseconds, cancellationToken);\n            using var client = new NamedPipeClientStream(\".\", settings.PipeName, PipeDirection.InOut,\n                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);\n            await client.ConnectAsync(budget.Token).ConfigureAwait(false);\n            budget.ThrowIfExpired();\n            byte[] request = Encoding.UTF8.GetBytes(\"{\\\"protocolVersion\\\":1,\\\"view\\\":\\\"\" + HealthReportProtocol.ViewName(settings.View) + \"\\\"}\");\n            await HealthReportProtocol.WriteFrameAsync(client, request, 512, budget.Token).ConfigureAwait(false);\n            budget.ThrowIfExpired();\n            byte[] response = await HealthReportProtocol.ReadFrameAsync(client, settings.MaxReportBytes, budget.Token).ConfigureAwait(false);\n            budget.ThrowIfExpired();\n            var parsed = HealthReportProtocol.ParseResponse(response, settings.View, settings.MaxReportBytes);\n            budget.ThrowIfExpired();\n            if (parsed.IsError)\n            {\n                await WriteErrorAsync(standardError, parsed.ErrorCode!).ConfigureAwait(false);\n                return 2;\n            }\n            await standardOutput.WriteAsync(parsed.SnapshotUtf8, cancellationToken).ConfigureAwait(false);\n            await standardOutput.WriteAsync(\"\\n\"u8.ToArray(), cancellationToken).ConfigureAwait(false);\n            await standardOutput.FlushAsync(cancellationToken).ConfigureAwait(false);\n            return parsed.SelectedHealthy ? 0 : 1;\n        }\n        catch (OperationCanceledException)\n        {\n            await WriteErrorAsync(standardError, \"TimeoutOrCanceled\").ConfigureAwait(false);\n        }\n        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or FormatException or InvalidOperationException or ArgumentException)\n        {\n            await WriteErrorAsync(standardError, \"TransportOrProtocolFailure\").ConfigureAwait(false);\n        }\n        return 2;\n    }\n\n    private static bool TryParseArguments(string[] arguments, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProbeSettings? settings)\n    {\n        settings = null;\n        var seen = new HashSet<string>(StringComparer.Ordinal);\n        string? pipeName = null;\n        HealthReportView view = HealthReportView.Health;\n        int timeout = 1000, maximum = 4194304;\n        for (int index = 0; index < arguments.Length; index += 2)\n        {\n            string option = arguments[index];\n            if (index + 1 >= arguments.Length || !seen.Add(option)) { return false; }\n            string value = arguments[index + 1];\n            switch (option)\n            {\n                case \"--pipe\":\n                    if (!HealthcheckOptions.IsValidPipeName(value)) { return false; }\n                    pipeName = value;\n                    break;\n                case \"--view\":\n                    switch (value)\n                    {\n                        case \"health\": view = HealthReportView.Health; break;\n                        case \"readiness\": view = HealthReportView.Readiness; break;\n                        case \"details\": view = HealthReportView.Details; break;\n                        default: return false;\n                    }\n                    break;\n                case \"--timeout-ms\":\n                    if (!Integer(value, 50, 60000, out timeout)) { return false; }\n                    break;\n                case \"--max-report-bytes\":\n                    if (!Integer(value, 1024, 16777216, out maximum)) { return false; }\n                    break;\n                default: return false;\n            }\n        }\n        if (pipeName is null) { return false; }\n        settings = new ProbeSettings(pipeName, view, timeout, maximum);\n        return true;\n\n        static bool Integer(string value, int minimum, int maximum, out int number)\n            => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number) && number >= minimum && number <= maximum;\n    }\n\n    private static async Task WriteErrorAsync(TextWriter output, string code)\n    {\n        // Only known bounded codes reach stderr, never server text, paths or exception messages.\n        // An unavailable stderr must not replace the documented error exit with an unhandled exception.\n        try { await output.WriteLineAsync(\"cis-health-probe: \" + code).ConfigureAwait(false); }\n        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }\n    }\n\n    private sealed record ProbeSettings(string PipeName, HealthReportView View, int TimeoutMilliseconds, int MaxReportBytes);\n}\n";
    private const string OriginalTestSource = "using System.ComponentModel;\nusing CIS.Healthcheck;\nusing CIS.Healthcheck.Pipes;\nusing Microsoft.VisualStudio.TestTools.UnitTesting;\n\nnamespace CIS.Tests.UnitTests.Healthcheck;\n\n[TestClass]\npublic sealed class HealthPipeInternalMutationQualificationTests\n{\n    [TestMethod]\n    public void FirstInstanceProtectionRejectsAnExistingEndpointEvenWhenItHasSpareCapacity()\n    {\n        string pipe = \"hc-\" + Guid.NewGuid().ToString(\"N\");\n        using var first = HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: true);\n        using (HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: false)) { }\n        void OpenCollision() { using var collision = HealthPipeEndpoint.Create(pipe, maximumClients: 2, first: true); }\n        if (OperatingSystem.IsWindows()) { Assert.ThrowsExactly<Win32Exception>(OpenCollision); }\n        else { Assert.ThrowsExactly<UnauthorizedAccessException>(OpenCollision); }\n        Assert.IsFalse(first.IsConnected);\n    }\n\n    [TestMethod]\n    [DataRow(\"arguments\")]\n    [DataRow(\"standardOutput\")]\n    [DataRow(\"standardError\")]\n    public async Task ProbeRejectsMissingCallerResourcesBeforeConnecting(string missing)\n    {\n        using var output = new MemoryStream();\n        using var error = new StringWriter();\n        ArgumentNullException failure = await Assert.ThrowsExactlyAsync<ArgumentNullException>(() =>\n            HealthProbeClient.RunAsync(missing == \"arguments\" ? null! : [],\n                missing == \"standardOutput\" ? null! : output, missing == \"standardError\" ? null! : error));\n        Assert.AreEqual(missing, failure.ParamName);\n        Assert.AreEqual(0L, output.Length);\n        Assert.AreEqual(string.Empty, error.ToString());\n    }\n\n    [TestMethod]\n    public async Task ProbeCancellationProducesOnlyTheBoundedErrorAndKeepsCallerStreamsOpen()\n    {\n        using var cancellation = new CancellationTokenSource();\n        cancellation.Cancel();\n        using var output = new MemoryStream();\n        using var error = new StringWriter();\n        int exit = await HealthProbeClient.RunAsync([\"--pipe\", \"hc-\" + Guid.NewGuid().ToString(\"N\")], output, error, cancellation.Token);\n        Assert.AreEqual(2, exit);\n        StringAssert.Contains(error.ToString(), \"TimeoutOrCanceled\");\n        Assert.AreEqual(0L, output.Length);\n        output.WriteByte(1);\n        error.Write(\"still-owned-by-caller\");\n    }\n\n    [TestMethod]\n    [DataRow(false)]\n    [DataRow(true)]\n    public async Task BrokenErrorDestinationCannotReplaceTheDocumentedErrorExit(bool disposed)\n    {\n        using var output = new MemoryStream();\n        using var error = new UnavailableWriter(disposed);\n        Assert.AreEqual(2, await HealthProbeClient.RunAsync([], output, error));\n        Assert.AreEqual(1, error.Attempts);\n        Assert.AreEqual(0L, output.Length);\n    }\n\n    [TestMethod]\n    public async Task FailedStandardOutputIsReportedAsTransportFailureWithoutLeakingTheException()\n    {\n        await using var service = new HealthcheckService(new HealthcheckOptions(),\n            new HealthApplicationIdentity { Name = \"probe-output-owner\", InstanceId = Guid.NewGuid() });\n        service.MarkStartupCompleted();\n        string pipe = \"hc-\" + Guid.NewGuid().ToString(\"N\");\n        await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });\n        await server.StartAsync();\n        using var output = new UnavailableStream();\n        using var error = new StringWriter();\n        int exit = await HealthProbeClient.RunAsync([\"--pipe\", pipe], output, error);\n        Assert.AreEqual(2, exit);\n        StringAssert.Contains(error.ToString(), \"TransportOrProtocolFailure\");\n        Assert.IsFalse(error.ToString().Contains(\"private output path\", StringComparison.Ordinal));\n        Assert.AreEqual(1, output.Attempts);\n    }\n\n    [TestMethod]\n    public async Task SuccessfulProbeDeliversBufferedOutputBeforeReturningItsHealthExit()\n    {\n        await using var service = new HealthcheckService(new HealthcheckOptions(),\n            new HealthApplicationIdentity { Name = \"buffered-probe-owner\", InstanceId = Guid.NewGuid() });\n        service.MarkStartupCompleted();\n        string pipe = \"hc-\" + Guid.NewGuid().ToString(\"N\");\n        await using var server = new HealthPipeServer(service, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe });\n        await server.StartAsync();\n        using var delivered = new MemoryStream();\n        using var buffered = new BufferedStream(delivered, 65536);\n        using var error = new StringWriter();\n        Assert.AreEqual(0, await HealthProbeClient.RunAsync([\"--pipe\", pipe], buffered, error));\n        byte[] actual = delivered.ToArray();\n        Assert.IsTrue(actual.Length > 1);\n        Assert.AreEqual((byte)'\\n', actual[^1]);\n        using var json = System.Text.Json.JsonDocument.Parse(actual.AsMemory(0, actual.Length - 1));\n        Assert.AreEqual(\"buffered-probe-owner\", json.RootElement.GetProperty(\"application\").GetProperty(\"name\").GetString());\n        Assert.AreEqual(string.Empty, error.ToString());\n    }\n\n    [TestMethod]\n    [DataRow(49, false)]\n    [DataRow(50, true)]\n    [DataRow(51, true)]\n    public void MonotonicDeadlineRejectsTheExactBoundaryEvenWhenTimerCallbacksAreDelayed(int elapsed, bool expired)\n    {\n        var clock = new DelayedTimerClock();\n        using var budget = new HealthPipeBudget(clock, 50, CancellationToken.None);\n        clock.Timestamp = elapsed;\n        Assert.IsFalse(budget.Token.IsCancellationRequested);\n        if (expired)\n        {\n            OperationCanceledException failure = Assert.ThrowsExactly<OperationCanceledException>(budget.ThrowIfExpired);\n            Assert.AreEqual(budget.Token, failure.CancellationToken);\n        }\n        else { budget.ThrowIfExpired(); }\n        Assert.AreEqual(TimeSpan.FromMilliseconds(50), clock.DueTime);\n        Assert.AreEqual(Timeout.InfiniteTimeSpan, clock.Period);\n    }\n\n    [TestMethod]\n    public void ExternalCancellationIsLinkedAndBudgetDisposalReleasesOnlyItsOwnedResources()\n    {\n        var clock = new DelayedTimerClock();\n        using var caller = new CancellationTokenSource();\n        using WaitHandle callerWait = caller.Token.WaitHandle;\n        var budget = new HealthPipeBudget(clock, 50, caller.Token);\n        CancellationToken token = budget.Token;\n        WaitHandle ownedWait = token.WaitHandle;\n        caller.Cancel();\n        Assert.IsTrue(token.IsCancellationRequested);\n        Assert.ThrowsExactly<OperationCanceledException>(budget.ThrowIfExpired);\n        budget.Dispose();\n        Assert.IsTrue(ownedWait.SafeWaitHandle.IsClosed);\n        Assert.IsTrue(clock.TimerDisposed);\n        Assert.IsFalse(callerWait.SafeWaitHandle.IsClosed);\n    }\n\n    private sealed class UnavailableWriter(bool disposed) : StringWriter\n    {\n        internal int Attempts { get; private set; }\n        public override Task WriteLineAsync(string? value)\n        {\n            Attempts++;\n            return Task.FromException(disposed ? new ObjectDisposedException(\"caller-stderr\") : new IOException(\"private destination detail\"));\n        }\n    }\n\n    private sealed class UnavailableStream : MemoryStream\n    {\n        internal int Attempts { get; private set; }\n        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)\n        {\n            Attempts++;\n            return ValueTask.FromException(new IOException(\"private output path\"));\n        }\n    }\n\n    private sealed class DelayedTimerClock : TimeProvider\n    {\n        internal long Timestamp { get; set; }\n        internal TimeSpan DueTime { get; private set; }\n        internal TimeSpan Period { get; private set; }\n        internal bool TimerDisposed { get; private set; }\n        public override long TimestampFrequency => 1000;\n        public override long GetTimestamp() => Timestamp;\n        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)\n        {\n            DueTime = dueTime;\n            Period = period;\n            return new DelayedTimer(this);\n        }\n        private sealed class DelayedTimer(DelayedTimerClock owner) : ITimer\n        {\n            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();\n            public void Dispose() => owner.TimerDisposed = true;\n            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }\n        }\n    }\n}\n";

}
