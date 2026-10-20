using System.Text.Json;
using CIS.Logging;
using CIS.Logging.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

public sealed partial class EventEncodingTests
{
    [TestMethod]
    public void ReservedProviderIdentityAndPayloadShareAnExactByteBoundary()
    {
        CapturedEventFacts source = Capture(new LogEventData { EventName = "provider-budget", Message = "message" });
        LogValue Attributes(int length) => LogValue.Map(new Dictionary<string, LogValue>
        {
            ["event_id"] = LogValue.Integer(7),
            ["payload"] = LogValue.String(new string('x', length))
        });
        LogValue resource = ProtectedResource(source);
        int overhead = EventEncoder.Encode(BudgetFacts(source, resource, Attributes(0), provider: true), Minimum).JsonLine.Length;
        int retainedLength = Minimum - overhead;
        Assert.IsTrue(retainedLength > 0);

        EncodedEvent encoded = EventEncoder.Encode(BudgetFacts(source, resource, Attributes(retainedLength + 1), provider: true), Minimum);
        using JsonDocument record = Record(encoded);
        Assert.AreEqual(Minimum, encoded.JsonLine.Length);
        Assert.IsTrue(encoded.IsTruncated);
        Assert.AreEqual(7, record.RootElement.GetProperty("attributes").GetProperty("event_id").GetInt32());
        Assert.AreEqual(new string('x', retainedLength), record.RootElement.GetProperty("attributes").GetProperty("payload").GetString());
    }

    [TestMethod]
    [DataRow("", false)]
    [DataRow("kept", false)]
    [DataRow("😀", false)]
    [DataRow("", true)]
    [DataRow("kept", true)]
    [DataRow("😀", true)]
    public void OptionalTemplateUsesItsExactRemainingBudgetBeforeResourceDescriptions(string template, bool provider)
    {
        CapturedEventFacts source = Capture(new LogEventData { EventName = "template-budget", Message = "message" });
        LogValue Attributes(int length)
        {
            var values = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('x', length)) };
            if (provider) { values["event_id"] = LogValue.Integer(7); }
            return LogValue.Map(values);
        }
        LogValue resource = ProtectedResource(source);
        int overhead = EventEncoder.Encode(BudgetFacts(source, resource, Attributes(0), template, provider), Minimum).JsonLine.Length;
        var overflowing = resource.MapProperties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        overflowing["os_description"] = LogValue.String(new string('d', Minimum));

        EncodedEvent encoded = EventEncoder.Encode(BudgetFacts(source, LogValue.Map(overflowing), Attributes(Minimum - overhead), template, provider), Minimum);
        using JsonDocument record = Record(encoded);
        Assert.AreEqual(Minimum, encoded.JsonLine.Length);
        Assert.AreEqual(template, record.RootElement.GetProperty("message_template").GetString());
        Assert.IsFalse(record.RootElement.GetProperty("resource").TryGetProperty("os_description", out _));
    }

    [TestMethod]
    public void FirstResourceDescriptionConsumesTheRemainingBudgetBeforeTheSecond()
    {
        CapturedEventFacts source = Capture(new LogEventData { EventName = "resource-budget", Message = "message" });
        LogValue Attributes(int length) => LogValue.Map(new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('x', length)) });
        var first = ProtectedResource(source).MapProperties.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        first["os_description"] = LogValue.String("primary");
        int overhead = EventEncoder.Encode(BudgetFacts(source, LogValue.Map(first), Attributes(0)), Minimum).JsonLine.Length;
        first["runtime_description"] = LogValue.String("secondary");

        EncodedEvent encoded = EventEncoder.Encode(BudgetFacts(source, LogValue.Map(first), Attributes(Minimum - overhead)), Minimum);
        using JsonDocument record = Record(encoded);
        Assert.AreEqual(Minimum, encoded.JsonLine.Length);
        Assert.AreEqual("primary", record.RootElement.GetProperty("resource").GetProperty("os_description").GetString());
        Assert.IsFalse(record.RootElement.GetProperty("resource").TryGetProperty("runtime_description", out _));
    }

    [TestMethod]
    public void ProviderPayloadBudgetClipsTheFollowingTemplateAtItsRemainingBytes()
    {
        CapturedEventFacts source = Capture(new LogEventData { EventName = "provider-template-budget", Message = "message" });
        LogValue Attributes(int length) => LogValue.Map(new Dictionary<string, LogValue>
        {
            ["event_id"] = LogValue.Integer(7),
            ["payload"] = LogValue.String(new string('x', length))
        });
        LogValue resource = ProtectedResource(source);
        int overhead = EventEncoder.Encode(BudgetFacts(source, resource, Attributes(0), "keep", provider: true), Minimum).JsonLine.Length;
        EncodedEvent encoded = EventEncoder.Encode(BudgetFacts(source, resource, Attributes(Minimum - overhead), "keepZZ", provider: true), Minimum);
        using JsonDocument record = Record(encoded);
        Assert.AreEqual(Minimum, encoded.JsonLine.Length);
        Assert.AreEqual("keep", record.RootElement.GetProperty("message_template").GetString());
        Assert.AreEqual(7, record.RootElement.GetProperty("attributes").GetProperty("event_id").GetInt32());
        Assert.AreEqual(new string('x', Minimum - overhead), record.RootElement.GetProperty("attributes").GetProperty("payload").GetString());
    }

    private static LogValue ProtectedResource(CapturedEventFacts source) => LogValue.Map(source.Resource.MapProperties
        .Where(pair => pair.Key is not "os_description" and not "runtime_description")
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    // Earlier capture loss is sticky; these cases allocate the remaining wire budget from owned facts.
    private static CapturedEventFacts BudgetFacts(CapturedEventFacts source, LogValue resource, LogValue attributes, string? template = null, bool provider = false) =>
        new(source.TimestampUtc, source.TraceId, source.SpanId, source.Status, source.EventName, source.Message, template,
            source.OperationId, source.Service, source.Environment, source.Version, source.Component, resource, attributes,
            null, isTruncated: true, hasProviderEventId: provider);
}
