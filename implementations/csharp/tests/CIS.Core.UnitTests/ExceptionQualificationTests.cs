using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging.Exceptions;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ExceptionQualificationTests
{
    public static IEnumerable<object[]> Rows => LogValueTests.Read("exception-qualification-cases.json")["cases"]!.AsArray()
        .SelectMany(row => new[] { "en-US", "fr-FR", "ar-SA" }.Select(culture => new object[] { row!["id"]!.GetValue<string>(), culture }));

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void EverySharedExceptionRowCapturesOnceAgainstCoreAcrossCultures(string id, string culture)
    {
        CultureInfo prior = CultureInfo.CurrentCulture;
        CultureInfo priorUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            JsonObject row = Row(id);
            JsonObject actual = ExceptionQualificationFixture.Execute(row, NativeBindings());
            ExceptionQualificationExpectations.Validate(row, actual, NativeBindings());
            Assert.IsFalse(actual.ToJsonString().Contains(ExceptionQualificationSources.PrivateSentinel, StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = prior;
            CultureInfo.CurrentUICulture = priorUi;
        }
    }

    [TestMethod]
    [DataRow("type")]
    [DataRow("message")]
    [DataRow("order")]
    [DataRow("marker")]
    [DataRow("count")]
    [DataRow("depth")]
    [DataRow("loss")]
    [DataRow("reads")]
    [DataRow("missing")]
    [DataRow("data")]
    [DataRow("bool-number")]
    [DataRow("immutability")]
    [DataRow("error")]
    [DataRow("parameter")]
    [DataRow("native-error")]
    [DataRow("echo")]
    public void SharedComparatorRejectsIncorrectTreeLossReadsTypesAndErrors(string mutation)
    {
        JsonObject row = Row(mutation is "error" or "parameter" or "native-error" or "echo" ? "EXCEPTION-MESSAGE-UTF16-RETAINED-HIGH" : "EXCEPTION-AGGREGATE-ORDER");
        JsonObject actual = ExceptionQualificationFixture.Execute(row, NativeBindings());
        JsonObject? root = actual["exception"]?.AsObject();
        switch (mutation)
        {
            case "type": root!["type"] = "incorrect"; break;
            case "message": root!["message"] = "incorrect"; break;
            case "order":
                JsonArray children = root!["inner_exceptions"]!.AsArray();
                JsonNode first = children[0]!.DeepClone();
                children[0] = children[1]!.DeepClone();
                children[1] = first;
                break;
            case "marker": root!["truncated"] = false; break;
            case "count": actual["cachedNodes"] = 100; break;
            case "depth": actual["cachedDepth"] = 100; break;
            case "loss": actual["hasTruncatedContent"] = true; break;
            case "reads": actual["reads"]!["root"]!["forbidden"] = 1; break;
            case "missing": actual.Remove("exception"); break;
            case "data": root!["data"] = "private"; break;
            case "bool-number": actual["nodes"] = true; break;
            case "immutability": actual["stableAfterMutation"] = false; break;
            case "error": actual["error"] = null; break;
            case "parameter": actual["parameter"] = "input"; break;
            case "native-error": actual["nativeError"] = "ArgumentOutOfRangeException"; break;
            case "echo": actual["safeError"] = false; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ExceptionQualificationExpectations.Validate(row, actual, NativeBindings()));
    }

    [TestMethod]
    [DataRow("EXCEPTION-SINGLE")]
    [DataRow("EXCEPTION-INNER")]
    [DataRow("EXCEPTION-AGGREGATE-ORDER")]
    [DataRow("EXCEPTION-MESSAGE-ASCII-4097")]
    [DataRow("EXCEPTION-STACK-ASTRAL-16385")]
    [DataRow("EXCEPTION-LAST-BRANCH-LOSS")]
    public void OwnedTextAndShapeExposeStableInputsForLaterAggregateSizing(string id)
    {
        JsonObject row = Row(id);
        ExceptionQualificationSource source = ExceptionQualificationSources.Create(row["input"]!.AsObject());
        CapturedException captured = ExceptionProjection.Capture(source.Root)!;
        long beforeBytes = OwnedTextBytes(captured);
        Assert.IsTrue(beforeBytes > 0);
        Assert.AreEqual(row["expected"]!["nodes"]!.GetValue<int>(), captured.NodeCount);
        Assert.AreEqual(row["expected"]!["depth"]!.GetValue<int>(), captured.Depth);
        Assert.AreEqual(row["expected"]!["hasTruncatedContent"]!.GetValue<bool>(), captured.HasTruncatedContent);
        foreach (CapturedException node in ExceptionBoundaryFixture.Preorder(captured))
        {
            var list = (IList<CapturedException>)node.InnerExceptions;
            Assert.ThrowsExactly<NotSupportedException>(() => list.Add(node));
        }
        foreach (ExceptionQualificationProbe probe in source.Probes)
        {
            probe.Text = "changed message";
            probe.Stack = "changed stack";
            probe.RawData.Clear();
        }
        Assert.AreEqual(beforeBytes, OwnedTextBytes(captured));
        Assert.IsTrue(source.Probes.All(probe => probe.ForbiddenReads == 0));
    }

    [TestMethod]
    [DataRow("EXCEPTION-SINGLE", "data")]
    [DataRow("EXCEPTION-SINGLE", "source")]
    [DataRow("EXCEPTION-SINGLE", "help")]
    [DataRow("EXCEPTION-SINGLE", "format")]
    [DataRow("EXCEPTION-SINGLE", "body")]
    [DataRow("EXCEPTION-SINGLE", "headers")]
    [DataRow("EXCEPTION-SINGLE", "environment")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "data")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "source")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "help")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "format")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "body")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "headers")]
    [DataRow("EXCEPTION-AGGREGATE-EMPTY", "environment")]
    public void BothNativeFixtureTypesHaveArmedExcludedGetterTraps(string id, string member)
    {
        JsonObject input = Row(id)["input"]!.AsObject();
        ExceptionQualificationSource armed = ExceptionQualificationSources.Create(input);
        Assert.ThrowsExactly<InvalidOperationException>(() => Touch(armed.Root!, member));
        Assert.AreEqual(1, armed.Probes.Single().ForbiddenReads);
        ExceptionQualificationSource fresh = ExceptionQualificationSources.Create(input);
        Assert.IsNotNull(ExceptionProjection.Capture(fresh.Root));
        Assert.AreEqual(0, fresh.Probes.Single().ForbiddenReads);
        Assert.AreEqual(1, fresh.Probes.Single().MessageReads);
        Assert.AreEqual(1, fresh.Probes.Single().StackReads);
    }

    private static void Touch(Exception source, string member)
    {
        switch (member)
        {
            case "data": _ = source.Data; break;
            case "source": _ = source.Source; break;
            case "help": _ = source.HelpLink; break;
            case "format": _ = source.ToString(); break;
            case "body": _ = ((IExceptionMetadataTrap)source).Body; break;
            case "headers": _ = ((IExceptionMetadataTrap)source).Headers; break;
            case "environment": _ = ((IExceptionMetadataTrap)source).Environment; break;
            default: throw new InvalidDataException("Unknown fixture trap.");
        }
    }

    // This measures only owned raw fields. Future rendering must separately budget escapes/envelope/LF.
    private static long OwnedTextBytes(CapturedException root) => ExceptionBoundaryFixture.Preorder(root)
        .Sum(node => (long)Encoding.UTF8.GetByteCount(node.Type) + Encoding.UTF8.GetByteCount(node.Message)
            + (node.StackTrace is null ? 0 : Encoding.UTF8.GetByteCount(node.StackTrace)));

    private static JsonObject NativeBindings() => LogValueTests.Read("exception-qualification-cases.json")["adapterObservations"]!["csharp"]!.AsObject();

    internal static JsonObject Row(string id) => LogValueTests.Read("exception-qualification-cases.json")["cases"]!.AsArray()
        .Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
}
