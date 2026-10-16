using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Rendering;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class PresentationTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void FormatDetailAndStreamDoNotChangeAdmissionOrCapturedMeaning(string id)
    {
        JsonObject row = Row(id); PresentationExpectations.Validate(row, PresentationFixture.Execute(row));
    }
    [TestMethod]
    [DataRow("filter")]
    [DataRow("recapture")]
    [DataRow("capture")]
    [DataRow("template")]
    [DataRow("format")]
    [DataRow("copy")]
    public void ComparatorRejectsChangedFilteringCaptureTextFormatAndByteReuse(string fault)
    {
        JsonObject row = Row("PRESENT-BOTH-C0F1D1S0"), actual = PresentationFixture.Execute(row);
        if (fault == "filter") { actual["enabled"]![1] = false; }
        else if (fault == "recapture") { actual["clockCalls"] = 6; }
        else
        {
            JsonNode frame = actual["fileFrames"]![0]!;
            if (fault == "copy") { frame["jsonMemoryReused"] = false; }
            else if (fault == "format") { actual["consoleFrames"]![0]!["line"] = actual["consoleFrames"]![0]!["source"]!.DeepClone(); }
            else
            {
                JsonObject changed = JsonNode.Parse(Convert.FromBase64String(frame["source"]!.GetValue<string>()))!.AsObject();
                changed[fault == "capture" ? "service" : "message_template"] = "CIS_CHANGED_BY_RENDERING";
                frame[fault == "capture" ? "after" : "line"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(changed.ToJsonString() + "\n"));
            }
        }
        Assert.ThrowsExactly<InvalidDataException>(() => PresentationExpectations.Validate(row, actual));
    }
    [TestMethod]
    public void ExpectedAdmissionIsIdenticalAcrossAllPresentationChoicesWithinEachProfile()
    {
        JsonArray rows = Catalog()["cases"]!.AsArray();
        var expected = (from profile in new[] { "both", "console", "file", "disabled" } from console in Enumerable.Range(0, 2) from file in Enumerable.Range(0, 2) from detail in Enumerable.Range(0, 2) from stream in Enumerable.Range(0, 2) select (profile, console, file, detail, stream)).ToArray();
        var actual = rows.Select(row => ((string)row!["profile"]!, row["consoleFormat"]!.GetValue<int>(), row["fileFormat"]!.GetValue<int>(), row["detail"]!.GetValue<int>(), row["stream"]!.GetValue<int>())).ToArray();
        CollectionAssert.AreEquivalent(expected, actual);
        foreach (var group in rows.GroupBy(row => row!["profile"]!.GetValue<string>()))
        {
            Assert.AreEqual(16, group.Count());
            JsonNode first = group.First()!["expected"]!;
            foreach (JsonNode? row in group) { Assert.IsTrue(JsonNode.DeepEquals(first, row!["expected"]), group.Key); }
        }
    }
    [TestMethod]
    public void JsonlDetailChoicesReuseOriginalOwnedBytesAndLossFlag()
    {
        JsonObject actual = HumanRenderingFixture.Execute(HumanRow("HUMAN-CAP-65536"));
        var captured = new EncodedEvent(Convert.FromBase64String(actual["json"]!.GetValue<string>()), false);
        RenderedEvent normal = EventRenderer.Render(captured, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 65536);
        RenderedEvent verbose = EventRenderer.Render(captured, LogOutputFormat.JsonLines, HumanOutputDetail.Verbose, 65536);
        Assert.IsTrue(normal.Line.Equals(captured.JsonLine) && verbose.Line.Equals(captured.JsonLine));
        Assert.IsFalse(normal.IsTruncated || verbose.IsTruncated);
        RenderedEvent human = EventRenderer.Render(captured, LogOutputFormat.Human, HumanOutputDetail.Verbose, 65536);
        Assert.IsTrue(human.IsTruncated); Assert.IsFalse(captured.IsTruncated);
        JsonObject lost = HumanRenderingFixture.Execute(HumanRow("HUMAN-CAP-16384"));
        var truncated = new EncodedEvent(Convert.FromBase64String(lost["json"]!.GetValue<string>()), true);
        Assert.IsTrue(EventRenderer.Render(truncated, LogOutputFormat.JsonLines, HumanOutputDetail.Verbose, 16384).IsTruncated);
    }
    [TestMethod]
    public void UndefinedChoicesAndWrongJsonlCapFailExplicitlyInsteadOfInferringAFormat()
    {
        JsonObject actual = HumanRenderingFixture.Execute(HumanRow("HUMAN-CAP-65536"));
        var captured = new EncodedEvent(Convert.FromBase64String(actual["json"]!.GetValue<string>()), false);
        Assert.ThrowsExactly<ArgumentNullException>(() => EventRenderer.Render(null!, LogOutputFormat.Human, HumanOutputDetail.Normal, 65536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventRenderer.Render(captured, (LogOutputFormat)99, HumanOutputDetail.Normal, 65536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventRenderer.Render(captured, LogOutputFormat.JsonLines, (HumanOutputDetail)99, 65536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventRenderer.Render(captured, LogOutputFormat.Human, HumanOutputDetail.Normal, 16383));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => EventRenderer.Render(captured, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16777217));
        Assert.ThrowsExactly<InvalidOperationException>(() => EventRenderer.Render(captured, LogOutputFormat.JsonLines, HumanOutputDetail.Normal, 16384));
    }
    private static JsonObject HumanRow(string id) => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "human-rendering-cases.json")))!["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "presentation-cases.json")))!.AsObject();
}
