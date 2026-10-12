using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

// Development inputs for the real capture/encoder; no serializer substitute.
internal static class EventEncodingFixture
{
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-12T12:11:12.1234567+02:00", CultureInfo.InvariantCulture);
    }
    private sealed class StackException : Exception
    {
        public override string Message => "captured failure";
        public override string StackTrace => new string('x', 16384);
    }

    internal static JsonObject Execute(JsonObject row)
    {
        string mode = row["input"]!["mode"]!.GetValue<string>();
        int cap = row["input"]!["maxEntryBytes"]!.GetValue<int>();
        var metadata = new FixtureEnvironmentValues { ApplicationName = "Encoding.App", EnvironmentName = "tests", BuildVersion = "1.2.3", ServiceVersion = "release-4" };
        var store = new OperationScopeStore();
        var data = new LogEventData { EventName = "encoding.event", Message = "original" };
        CapturedEventFacts Capture() => EventProjection.Capture(metadata.CaptureMetadata(), store.Capture(), "component", CoreLogLevel.Information, data, new Clock())!;
        Activity? previous = Activity.Current;
        Activity.Current = null;
        try
        {
            if (mode == "complete")
            {
                using var activity = new Activity("encode").SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start();
                using (store.BeginScope(new OperationContextData { OperationId = "scope-operation" }))
                {
                    data.Message = "quote \" slash \\ newline\n café 😀"; data.MessageTemplate = "";
                    data.Attributes = new Dictionary<string, LogValue>
                    {
                        ["null"] = LogValue.Null(),
                        ["integer"] = LogValue.Integer(9007199254740991),
                        ["integerString"] = LogValue.IntegerString("90071992547409920000"),
                        ["decimal"] = LogValue.DecimalString("12.3400"),
                        ["array"] = LogValue.Array([LogValue.Boolean(true), LogValue.Number(1.25)]),
                        ["map"] = LogValue.Map(new Dictionary<string, LogValue> { ["z"] = LogValue.Integer(2), ["a"] = LogValue.Integer(1) })
                    };
                    CapturedEventFacts owned = Capture();
                    activity.Stop(); data.Message = "mutated"; data.Attributes = null;
                    return Encode(row, owned, cap);
                }
            }
            if (mode is "exact" or "over")
            {
                data.Message = ""; data.Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String("") };
                int baseline = EventEncoder.Encode(Capture(), cap).JsonLine.Length;
                data.Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(new string('x', cap - baseline + (mode == "over" ? 1 : 0))) };
            }
            else if (mode == "unicode")
            {
                data.Attributes = new Dictionary<string, LogValue> { ["payload"] = LogValue.String(string.Concat(Enumerable.Repeat("😀\u0001", 10000))) };
            }
            else if (mode == "atomic")
            {
                data.Attributes = new Dictionary<string, LogValue> { ["a"] = LogValue.DecimalString("1." + new string('0', 20000)), ["b"] = LogValue.Integer(0) };
            }
            else if (mode == "exception") { data.Exception = new StackException(); }
            else if (mode == "prior") { data.Message = new string('x', 8193); }
            else if (mode == "envelope")
            {
                string identity = string.Concat(Enumerable.Repeat("😀", 512));
                metadata.BuildVersion = identity; metadata.ServiceVersion = identity; metadata.CommitId = identity; metadata.BuildId = identity;
            }
            else if (mode is not ("exact" or "over")) { throw new ArgumentException("Unknown encoding case.", nameof(row)); }
            return Encode(row, Capture(), cap);
        }
        finally { Activity.Current = previous; }
    }

    private static JsonObject Encode(JsonObject row, CapturedEventFacts facts, int cap)
    {
        var result = new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["maxEntryBytes"] = cap };
        try
        {
            EncodedEvent encoded = EventEncoder.Encode(facts, cap);
            result["wire"] = Encoding.UTF8.GetString(encoded.JsonLine.Span);
            result["utf8Bytes"] = encoded.JsonLine.Length; result["truncated"] = encoded.IsTruncated;
        }
        catch (EventEnvelopeTooLargeException error) { result["errorType"] = error.GetType().Name; }
        return result;
    }
}
