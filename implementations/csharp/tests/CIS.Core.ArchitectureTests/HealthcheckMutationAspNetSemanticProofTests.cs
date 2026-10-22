using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.HealthcheckMutationUnionModel;
using P = CIS.Verification.HealthcheckMutationAspNetSemanticProofs;
using S = CIS.Verification.HealthcheckMutationSemanticProofs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckMutationAspNetSemanticProofTests
{
    [TestMethod]
    [DataRow("BusinessResponse404", 0)]
    [DataRow("AdapterInvalidErrorCode", 2)]
    [DataRow("AdapterInvalidErrorCode", 3)]
    public void ReviewedReceivingFailuresBindTheirActualResponseOrFormatterOrigin(string kind, int calls)
    {
        var fixture = new Fixture(kind, calls);
        string before = fixture.Report.ToJsonString();
        Assert.IsTrue(fixture.Match(out JsonObject? witness));
        Assert.AreEqual(kind, (string?)witness!["failureCategory"]);
        Assert.AreEqual(kind == "AdapterInvalidErrorCode", (bool?)witness["coreOriginException"]);
        Assert.AreEqual(false, (bool?)witness["timeoutReclassified"]);
        Assert.AreEqual(false, (bool?)witness["childPropagationQualifiedByThisWitness"]);
        Assert.AreEqual(true, (bool?)witness["originalFullChildAuditStillRequired"]);
        Assert.AreEqual(before, fixture.Report.ToJsonString());
    }

    [TestMethod]
    [DataRow("wrong-report")]
    [DataRow("unknown-definition")]
    [DataRow("source-sha")]
    [DataRow("span")]
    [DataRow("replacement")]
    [DataRow("operator")]
    [DataRow("timeout")]
    [DataRow("raw-status")]
    [DataRow("raw-replacement")]
    [DataRow("raw-span")]
    [DataRow("raw-killed-by")]
    [DataRow("extra-killed-by")]
    [DataRow("killed-by-pin")]
    [DataRow("wrong-guid")]
    [DataRow("wrong-method")]
    [DataRow("wrong-code-file")]
    [DataRow("wrong-declared-method")]
    [DataRow("changed-test-source")]
    [DataRow("changed-module-source")]
    [DataRow("message-pin")]
    [DataRow("stack-pin")]
    [DataRow("input-review")]
    [DataRow("cause-review")]
    [DataRow("unknown-kind")]
    public void UnboundOriginalDefinitionOrReceivingFailureCannotSupplySemanticCredit(string mode)
    {
        var f = new Fixture();
        switch (mode)
        {
            case "wrong-report": f.ReportHash = new string('b', 64); break;
            case "unknown-definition": f.Definition = f.Definition with { Id = "8" }; break;
            case "source-sha": f.Definition = f.Definition with { SourceSha256 = new string('b', 64) }; break;
            case "span": f.Definition = f.Definition with { Start = 1 }; break;
            case "replacement": f.Definition = f.Definition with { Replacement = "other" }; break;
            case "operator": f.Definition = f.Definition with { Mutator = "other" }; break;
            case "timeout": f.Definition = f.Definition with { Status = "Timeout" }; break;
            case "raw-status": f.Raw["status"] = "Timeout"; break;
            case "raw-replacement": f.Raw["replacement"] = "other"; break;
            case "raw-span": f.Raw["location"]!["end"]!["column"] = 1; break;
            case "raw-killed-by": f.Raw["killedBy"] = new JsonArray(Fixture.OtherGuid); break;
            case "extra-killed-by": f.Definition = f.Definition with { KilledBy = [Fixture.OriginalGuid, Fixture.OtherGuid] }; break;
            case "killed-by-pin": f.Review = f.Review with { KilledBySetSha256 = new string('b', 64) }; break;
            case "wrong-guid": f.Guid = Fixture.OtherGuid; break;
            case "wrong-method": f.Method += "Other"; break;
            case "wrong-code-file": f.CodeFile += ".other"; break;
            case "wrong-declared-method": f.Report["testFiles"]![Fixture.TestOrigin]!["tests"]![0]!["name"] = "Other"; break;
            case "changed-test-source": f.Report["testFiles"]![Fixture.TestOrigin]!["source"] = "other"; break;
            case "changed-module-source": f.Report["files"]![Fixture.CoreOrigin]!["source"] = "other"; break;
            case "message-pin": f.Message += "other"; break;
            case "stack-pin": f.Stack += "\n"; break;
            case "input-review": f.Review = f.Review with { AdmittedInputReview = "" }; break;
            case "cause-review": f.Review = f.Review with { CausalReview = "" }; break;
            case "unknown-kind": f.Review = f.Review with { Kind = "Other" }; break;
            default: throw new InvalidOperationException("Unknown receiving control.");
        }
        Assert.IsFalse(f.Match(out JsonObject? witness));
        Assert.IsNull(witness);
    }

    [TestMethod]
    [DataRow("BusinessResponse404", "wrong-status")]
    [DataRow("BusinessResponse404", "wrong-origin")]
    [DataRow("BusinessResponse404", "wrong-helper")]
    [DataRow("BusinessResponse404", "wrong-receiving")]
    [DataRow("BusinessResponse404", "wrong-terminal")]
    [DataRow("BusinessResponse404", "core-claim")]
    [DataRow("AdapterInvalidErrorCode", "wrong-parameter")]
    [DataRow("AdapterInvalidErrorCode", "wrong-origin")]
    [DataRow("AdapterInvalidErrorCode", "wrong-collateral-path")]
    [DataRow("AdapterInvalidErrorCode", "wrong-helper")]
    [DataRow("AdapterInvalidErrorCode", "wrong-receiving")]
    [DataRow("AdapterInvalidErrorCode", "wrong-terminal")]
    [DataRow("AdapterInvalidErrorCode", "cleanup")]
    [DataRow("AdapterInvalidErrorCode", "initializer")]
    [DataRow("AdapterInvalidErrorCode", "deadline")]
    [DataRow("AdapterInvalidErrorCode", "cancellation")]
    public void EvenRepinnedSyntheticMessagesMustHaveTheReviewedCauseAndReceivingStack(string kind, string mode)
    {
        var f = new Fixture(kind, kind == "BusinessResponse404" ? 0 : 2);
        switch (mode)
        {
            case "wrong-status": f.Message = f.Message.Replace("404 (Not Found)", "500 (Server Error)", StringComparison.Ordinal); break;
            case "wrong-parameter": f.Message = f.Message.Replace("'code'", "'other'", StringComparison.Ordinal); break;
            case "wrong-origin": f.Stack = f.Stack.Replace(kind == "BusinessResponse404" ? "EnsureSuccessStatusCode" : "FormatError", "Other", StringComparison.Ordinal); break;
            case "wrong-helper": f.Stack = f.Stack.Replace(kind == "BusinessResponse404" ? "Host.GetStringAsync" : "HandleAsync", "Other", StringComparison.Ordinal); break;
            case "wrong-receiving": f.Stack = f.Stack.Replace(Fixture.Receiving + "(", "Other.Receiving(", StringComparison.Ordinal); break;
            case "wrong-terminal": f.Stack = f.Stack.Replace("TestMethodInfo.ExecuteInternalAsync", "Other.Execute", StringComparison.Ordinal); break;
            case "core-claim": f.Review = f.Review with { CoreMethods = ["Invented.Core.Origin"] }; break;
            case "wrong-collateral-path": f.Stack = f.Stack.Replace(P.FormatterPath, P.ProtocolPath, StringComparison.Ordinal); break;
            case "cleanup": f.Stack = f.Stack.Replace("HandleAsync(", "DisposeAsync(", StringComparison.Ordinal); break;
            case "initializer": f.Stack = f.Stack.Replace("HandleAsync(", "TestInitialize(", StringComparison.Ordinal); break;
            case "deadline": f.Stack = f.Stack.Replace("HandleAsync(", "TimeoutException(", StringComparison.Ordinal); break;
            case "cancellation": f.Stack = f.Stack.Replace("HandleAsync(", "OperationCanceledException(", StringComparison.Ordinal); break;
            default: throw new InvalidOperationException("Unknown cause control.");
        }
        f.Review = f.Review with { MessageSha256 = P.HashText(f.Message), StackSha256 = P.HashText(f.Stack) };
        Assert.IsFalse(f.Match(out _));
    }

    [TestMethod]
    [DataRow("absent")]
    [DataRow("missing-formatter")]
    [DataRow("missing-protocol")]
    [DataRow("duplicate-module")]
    [DataRow("duplicate-formatter")]
    [DataRow("duplicate-protocol")]
    [DataRow("formatter-text")]
    [DataRow("protocol-text")]
    [DataRow("module-text")]
    [DataRow("formatter-group")]
    [DataRow("protocol-sha")]
    public void MissingChangedOrAmbiguousCollateralCannotBeAccepted(string mode)
    {
        var f = new Fixture();
        f.Sources = mode switch
        {
            "absent" => [],
            "missing-formatter" => [f.Sources[0], f.Sources[2]],
            "missing-protocol" => [f.Sources[0], f.Sources[1]],
            "duplicate-module" => [.. f.Sources, f.Sources[0]],
            "duplicate-formatter" => [.. f.Sources, f.Sources[1]],
            "duplicate-protocol" => [.. f.Sources, f.Sources[2]],
            "formatter-text" => [f.Sources[0], f.Sources[1] with { Text = "changed" }, f.Sources[2]],
            "protocol-text" => [f.Sources[0], f.Sources[1], f.Sources[2] with { Text = "changed" }],
            "module-text" => [f.Sources[0] with { Text = "changed" }, f.Sources[1], f.Sources[2]],
            "formatter-group" => [f.Sources[0], f.Sources[1] with { Group = "aspnet" }, f.Sources[2]],
            "protocol-sha" => [f.Sources[0], f.Sources[1], f.Sources[2] with { Sha256 = new string('b', 64) }],
            _ => throw new InvalidOperationException("Unknown collateral control.")
        };
        Assert.IsFalse(f.Match(out _));
    }

    [TestMethod]
    public void ContextDetachesTheDefinitionArraysReportAndCollateralFromLaterCallerMutation()
    {
        var f = new Fixture();
        Definition[] definitions = [f.Definition];
        Source[] sources = f.Sources;
        S.Context context = S.CreateContext(f.Report, f.ReportHash, definitions, sources)!;
        definitions[0].KilledBy[0] = Fixture.OtherGuid;
        definitions[0] = definitions[0] with { Replacement = "changed" };
        sources[0] = sources[0] with { Text = "changed" };
        sources[1] = sources[1] with { Sha256 = "changed" };
        f.Raw["status"] = "Timeout";
        Assert.IsTrue(f.Match(context, out _));
        Assert.AreEqual(Fixture.OriginalGuid, context.Definitions["7"].KilledBy[0]);
        Assert.AreNotEqual("changed", context.ReviewedSources[0].Text);
    }

    [TestMethod]
    public void ShippingCatalogContainsOnlyTheThirteenReviewedRowsAndCannotAcceptASyntheticReview()
    {
        Assert.HasCount(13, HealthcheckMutationAspNetSemanticCatalog.Cases);
        CollectionAssert.AreEquivalent(new[] { "202", "204", "228", "229", "272", "280" },
            HealthcheckMutationAspNetSemanticCatalog.Cases.Select(value => value.Tuple.Id).Distinct().ToArray());
        var f = new Fixture();
        Assert.IsTrue(f.Match(out _));
        Assert.IsFalse(P.TryMatch(f.Context(), "7", f.Guid, f.Method, f.CodeFile, f.Message, f.Stack, out _));
    }

    // Explicit synthetic receiving input; no native product qualification claim.
    private sealed class Fixture
    {
        internal const string OriginalGuid = "11111111-1111-4111-8111-111111111111";
        internal const string OtherGuid = "22222222-2222-4222-8222-222222222222";
        internal const string Receiving = "CIS.Tests.IntegrationTests.SyntheticHttp.Receiving";
        internal const string Module = "Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs";
        internal const string CoreOrigin = "/receiving/implementations/csharp/src/CIS.Core/" + Module;
        internal const string TestOrigin = "/receiving/" + P.TestPath;
        internal string Guid { get; set; } = OriginalGuid;
        internal string Method { get; set; } = Receiving;
        internal string CodeFile { get; set; } = TestOrigin;
        internal string ReportHash { get; set; } = P.ReportSha256;
        internal Definition Definition { get; set; }
        internal P.ReviewedCase Review { get; set; }
        internal JsonObject Report { get; }
        internal JsonObject Raw => Report["files"]![CoreOrigin]!["mutants"]![0]!.AsObject();
        internal Source[] Sources { get; set; }
        internal string Message { get; set; }
        internal string Stack { get; set; }

        internal Fixture(string kind = "BusinessResponse404", int calls = 0)
        {
            const string source = "reserved";
            const string test = "// synthetic receiving input\n";
            const string formatter = "// synthetic formatter\n";
            const string protocol = "// synthetic protocol\n";
            Definition = new("7", Module, P.HashText(source), 0, source.Length, "Conditional mutation", source, "true", "Killed", [OriginalGuid]);
            Sources = [new(Module, P.HashText(source), "aspnet", source), new(P.FormatterPath, P.HashText(formatter), "unit-runtime", formatter), new(P.ProtocolPath, P.HashText(protocol), "unit-runtime", protocol)];
            string[] methods = kind == "BusinessResponse404" ? [] : calls == 2
                ? ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"]
                : ["CIS.Healthcheck.HealthReportFormatter.FormatError", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.AuthorizeDetailsAsync", "CIS.Healthcheck.AspNetCore.HealthcheckApplicationBuilderExtensions.HandleAsync"];
            Message = "Test method " + Receiving + " threw exception:\r\n" + (kind == "BusinessResponse404"
                ? "System.Net.Http.HttpRequestException: Response status code does not indicate success: 404 (Not Found)."
                : "System.ArgumentOutOfRangeException: Specified argument was out of the range of valid values. (Parameter 'code')");
            string Frame(string method, string path) => "   at " + method + "() in " + path + ":line 18\n";
            Stack = kind == "BusinessResponse404" ? "   at System.Net.Http.HttpResponseMessage.EnsureSuccessStatusCode()\n"
                + Frame("CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.Host.GetStringAsync", TestOrigin) + Frame(Receiving, TestOrigin) + Frame(Receiving, TestOrigin)
                : string.Concat(methods.Select((method, index) => Frame(method, index == 0 ? "/receiving/implementations/csharp/src/CIS.Core/" + P.FormatterPath : CoreOrigin))) + Frame(Receiving, TestOrigin);
            Stack += "   at Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Execution.TestMethodInfo.ExecuteInternalAsync(Object[] arguments, CancellationTokenSource timeoutTokenSource, TestInvocationState invocationState)\n";
            Review = new(Definition with { KilledBy = [] }, P.KilledByHash([OriginalGuid]), OriginalGuid, Receiving, kind, P.HashText(Message), P.HashText(Stack), methods,
                "Explicit synthetic admitted input.", "Explicit synthetic reviewed cause.", P.HashText(test), P.HashText(formatter), P.HashText(protocol));
            Report = new JsonObject
            {
                ["files"] = new JsonObject
                {
                    [CoreOrigin] = new JsonObject
                    {
                        ["source"] = source,
                        ["mutants"] = new JsonArray(new JsonObject
                        {
                            ["id"] = "7",
                            ["status"] = "Killed",
                            ["mutatorName"] = Definition.Mutator,
                            ["replacement"] = "true",
                            ["killedBy"] = new JsonArray(OriginalGuid),
                            ["location"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 1, ["column"] = 1 }, ["end"] = new JsonObject { ["line"] = 1, ["column"] = source.Length + 1 } }
                        })
                    }
                },
                ["testFiles"] = new JsonObject { [TestOrigin] = new JsonObject { ["source"] = test, ["tests"] = new JsonArray(new JsonObject { ["id"] = OriginalGuid, ["name"] = Receiving }) } }
            };
        }

        internal S.Context Context() => new(ReportHash, [Definition], Report, Sources);
        internal bool Match(out JsonObject? witness) => Match(Context(), out witness);
        internal bool Match(S.Context context, out JsonObject? witness) => P.Match(Review, context, "7", Guid, Method, CodeFile, Message, Stack, out witness);
    }
}
