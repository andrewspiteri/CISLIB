using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Rendering;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class HumanRenderingTests
{
    public static IEnumerable<object[]> Rows => Catalog()["cases"]!.AsArray().Select(row => new object[] { row!["id"]!.GetValue<string>() });
    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void NormalAndVerbosePreservePromisedInformationAndEscapeCallerText(string id)
    {
        JsonObject row = Row(id); HumanRenderingExpectations.Validate(row, HumanRenderingFixture.Execute(row));
    }
    [TestMethod]
    [DataRow(16384)]
    [DataRow(65536)]
    [DataRow(131072)]
    public void ExpandedPayloadRemainsBoundedAndReportsLossWhenItDoesNotFit(int cap)
    {
        JsonObject row = Row("HUMAN-CAP-" + cap), actual = HumanRenderingFixture.Execute(row);
        HumanRenderingExpectations.Validate(row, actual);
        if (cap == 16384) { Assert.IsTrue(actual["normalTruncated"]!.GetValue<bool>()); }
        if (cap == 65536) { Assert.IsTrue(actual["verboseTruncated"]!.GetValue<bool>()); }
    }
    [TestMethod]
    [DataRow("missing")]
    [DataRow("control")]
    [DataRow("invalid-utf8")]
    [DataRow("loss")]
    [DataRow("resource")]
    [DataRow("type")]
    [DataRow("exception")]
    [DataRow("split")]
    [DataRow("extra")]
    [DataRow("underfill")]
    [DataRow("unexpected-loss")]
    public void DisplayComparatorRejectsMissingInformationUnsafeBytesAndLostMarkers(string fault)
    {
        JsonObject row = Row(fault is "loss" or "underfill" ? "HUMAN-CAP-16384" : fault == "resource" ? "HUMAN-CAP-65536" : "HUMAN-INFO"), actual = HumanRenderingFixture.Execute(row);
        string detail = fault is "resource" or "type" or "exception" or "split" or "extra" ? "verbose" : "normal";
        byte[] line = Convert.FromBase64String(actual[detail]!.GetValue<string>());
        if (fault == "missing") { line = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(line).Replace("event_name=", "other=", StringComparison.Ordinal)); }
        if (fault == "control") { line[0] = 27; }
        if (fault == "invalid-utf8") { line[0] = 0xFF; }
        if (fault == "loss") { line = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(line).Replace(" truncated=true", "", StringComparison.Ordinal)); }
        if (fault is "resource" or "type" or "exception" or "split" or "extra" or "underfill" or "unexpected-loss")
        {
            var fields = HumanRenderingExpectations.Read(line, row["maxEntryBytes"]!.GetValue<int>());
            if (fault is "resource" or "extra" or "underfill" or "unexpected-loss")
            {
                string label = fault == "resource" ? "resource.os_description" : fault == "extra" ? "invented" : fault == "underfill" ? "message" : "truncated";
                using JsonDocument changed = JsonDocument.Parse(fault == "unexpected-loss" ? "true" : fault == "underfill" ? "\"\"" : "\"changed\"");
                fields[label] = changed.RootElement.Clone();
                if (fault == "unexpected-loss") { actual["normalTruncated"] = true; }
            }
            else
            {
                string label = fault == "exception" ? "exception" : "attributes";
                JsonObject changed = JsonNode.Parse(fields[label].GetRawText())!.AsObject();
                if (fault == "exception") { changed.Remove("type"); }
                else if (fault == "type") { changed["integer"] = "9007199254740991"; }
                else { changed["text"] = changed["text"]!.GetValue<string>()[..^1]; }
                using JsonDocument document = JsonDocument.Parse(changed.ToJsonString()); fields[label] = document.RootElement.Clone();
            }
            line = Encoding.UTF8.GetBytes(string.Join(" ", fields.Select(pair => pair.Key + "=" + pair.Value.GetRawText())) + "\n");
        }
        actual[detail] = Convert.ToBase64String(line);
        if (fault == "invalid-utf8") { Assert.ThrowsExactly<DecoderFallbackException>(() => HumanRenderingExpectations.Validate(row, actual)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => HumanRenderingExpectations.Validate(row, actual)); }
    }
    [TestMethod]
    public void HumanEscapingCanReportAdditionalLossWithoutChangingCaptureLossOrBytes()
    {
        JsonObject row = Row("HUMAN-CAP-65536"), actual = HumanRenderingFixture.Execute(row);
        HumanRenderingExpectations.Validate(row, actual);
        Assert.IsFalse(actual["captureTruncated"]!.GetValue<bool>());
        Assert.IsFalse(actual["normalTruncated"]!.GetValue<bool>());
        Assert.IsTrue(actual["verboseTruncated"]!.GetValue<bool>());
        Assert.IsTrue(actual["captureUnchanged"]!.GetValue<bool>());
    }
    [TestMethod]
    public void DisplayInformationComparerAcceptsFieldOrderAndExtraSpacing()
    {
        JsonObject row = Row("HUMAN-INFO"), actual = HumanRenderingFixture.Execute(row);
        var fields = HumanRenderingExpectations.Read(Convert.FromBase64String(actual["normal"]!.GetValue<string>()), 65536);
        actual["normal"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("  ", fields.Reverse().Select(pair => pair.Key + "=" + pair.Value.GetRawText())) + "\n"));
        HumanRenderingExpectations.Validate(row, actual);
    }
    [TestMethod]
    public void ClippingNeverSplitsUnicodeScalarsOrEscapeTokens()
    {
        string message = string.Concat(Enumerable.Repeat("\u200d\U0001f600", 4096));
        var data = new LogEventData { EventName = "scalar", Message = message };
        CapturedEventFacts facts = EventProjection.Capture(EventQualificationFixture.Metadata().CaptureMetadata(), new CIS.Logging.Scopes.OperationScopeStore().Capture(), "Human", CoreLogLevel.Information, data)!;
        EncodedEvent captured = EventEncoder.Encode(facts, 131072);
        Assert.IsFalse(captured.IsTruncated);
        RenderedHumanEvent rendered = HumanRenderer.Render(captured, HumanOutputDetail.Normal, 16384);
        Assert.IsTrue(rendered.IsTruncated);
        var fields = HumanRenderingExpectations.Read(rendered.Line.ToArray(), 16384);
        string retained = fields["message"].GetString()!;
        Assert.IsTrue(retained.Length > 0 && message.StartsWith(retained, StringComparison.Ordinal));
        Assert.IsFalse(char.IsHighSurrogate(retained[^1]));
    }
    [TestMethod]
    public void InvalidRendererArgumentsAndUnrepresentableProtectedEnvelopeAreExplicit()
    {
        EncodedEvent captured = EventEncoder.Encode(EventProjection.Capture(EventQualificationFixture.Metadata().CaptureMetadata(), new CIS.Logging.Scopes.OperationScopeStore().Capture(), "Human", CoreLogLevel.Information, new LogEventData { EventName = "event", Message = "hello" })!, 65536);
        Assert.ThrowsExactly<ArgumentNullException>(() => HumanRenderer.Render(null!, HumanOutputDetail.Normal, 65536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HumanRenderer.Render(captured, (HumanOutputDetail)999, 65536));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HumanRenderer.Render(captured, HumanOutputDetail.Normal, 16383));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => HumanRenderer.Render(captured, HumanOutputDetail.Normal, 16777217));
        JsonObject record = JsonNode.Parse(captured.JsonLine.Span)!.AsObject(); record["component"] = new string('\u202e', 3000);
        var impossible = new EncodedEvent(Encoding.UTF8.GetBytes(record.ToJsonString() + "\n"), false);
        Assert.ThrowsExactly<EventEnvelopeTooLargeException>(() => HumanRenderer.Render(impossible, HumanOutputDetail.Normal, 16384));
    }
    private static JsonObject Row(string id) => Catalog()["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject();
    private static JsonObject Catalog() => JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(ConfigurationFixtureAdapter.FindFixture())!, "human-rendering-cases.json")))!.AsObject();
}
