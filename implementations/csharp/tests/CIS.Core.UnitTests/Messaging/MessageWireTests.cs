using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
[DoNotParallelize]
public sealed class MessageWireTests
{
    private const string MessageId = "11111111-1111-4111-8111-111111111111";
    private const string RequestId = "22222222-2222-4222-8222-222222222222";
    private const string AttemptId = "33333333-3333-4333-8333-333333333333";
    private const string EndpointId = "44444444-4444-4444-8444-444444444444";
    private const string ReplyId = "55555555-5555-4555-8555-555555555555";
    private const string PrecisePayload = """{"country":"MT","amount":"12345678901234567890.0100","sequence":9007199254740993}""";
    private const string MinimalEventWire = """{"envelopeVersion":1,"messageId":"11111111-1111-4111-8111-111111111111","kind":"event","messageType":"country.updated","schemaVersion":1,"producerApplication":"country-service","producerEnvironment":"test","createdAtUtc":"2026-11-22T12:34:56.1234567Z","payload":null}""";

    [TestMethod]
    [DataRow("event-precision")]
    [DataRow("command")]
    [DataRow("durable-request")]
    [DataRow("reply")]
    public void PortableContractsMatchFrozenWireAndRetainedDigests(string id)
    {
        JsonObject vector = Vector("valid", id);
        string expectedWire = vector["wireUtf8"]!.GetValue<string>();
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedWire);

        EncodedMessage encoded = MessageWire.Encode(EnvelopeFor(id));

        CollectionAssert.AreEqual(expectedBytes, encoded.CopyBytes());
        Assert.AreEqual(vector["sha256"]!.GetValue<string>(),
            Convert.ToHexStringLower(encoded.Digest.CopyBytes()));
        Assert.AreEqual(vector["sha256"]!.GetValue<string>(),
            Convert.ToHexStringLower(SHA256.HashData(expectedBytes)));
        Assert.IsTrue(MessageWire.TryDecode(expectedBytes, out EncodedMessage? decoded, out MessageWireFailure failure));
        Assert.IsNotNull(decoded);
        Assert.AreEqual(MessageWireFailure.None, failure);
        CollectionAssert.AreEqual(expectedBytes, decoded.CopyBytes());
        Assert.AreEqual(vector["payloadUtf8"]!.GetValue<string>(), Encoding.UTF8.GetString(decoded.Envelope.CopyPayload()));
        Assert.AreEqual(encoded.Digest, decoded.Digest);

        if (id == "event-precision")
        {
            Assert.AreEqual(vector["wireHex"]!.GetValue<string>(), Convert.ToHexStringLower(expectedBytes));
        }
    }

    [TestMethod]
    [DataRow("duplicate-envelope-key")]
    [DataRow("duplicate-payload-key")]
    [DataRow("duplicate-escaped-payload-key")]
    [DataRow("duplicate-nested-object-key")]
    [DataRow("unknown-envelope-version")]
    [DataRow("unknown-envelope-field")]
    [DataRow("missing-contract")]
    [DataRow("null-contract")]
    [DataRow("null-producer")]
    [DataRow("null-environment")]
    [DataRow("null-created-at")]
    [DataRow("date-only-created-at")]
    [DataRow("space-created-at")]
    [DataRow("no-offset-created-at")]
    [DataRow("empty-fraction-created-at")]
    [DataRow("empty-message-id")]
    [DataRow("fractional-schema-version")]
    [DataRow("request-without-attempt")]
    [DataRow("event-with-request-fields")]
    [DataRow("reply-without-original")]
    [DataRow("request-arbitrary-reply-queue")]
    [DataRow("json-comment")]
    [DataRow("trailing-json")]
    [DataRow("malformed-utf8")]
    [DataRow("whitespace-only-wire")]
    public void UntrustedFramingReturnsFiniteFailureWithoutAnEnvelope(string id)
    {
        JsonObject vector = Vector("rejected", id);
        byte[] bytes = vector["wireHex"] is { } hex
            ? Convert.FromHexString(hex.GetValue<string>())
            : Encoding.UTF8.GetBytes(vector["wireUtf8"]!.GetValue<string>());

        bool accepted = MessageWire.TryDecode(bytes, out EncodedMessage? message, out MessageWireFailure failure);

        Assert.IsFalse(accepted, id);
        Assert.IsNull(message, id);
        Assert.AreEqual(Enum.Parse<MessageWireFailure>(vector["failure"]!.GetValue<string>()), failure, id);
    }

    [TestMethod]
    [DataRow("c328")]
    [DataRow("eda080")]
    [DataRow("c0af")]
    [DataRow("80")]
    public void InvalidUtf8CannotEnterAnOtherwiseValidPayload(string invalidHex)
    {
        byte[] prefix = Encoding.UTF8.GetBytes(MinimalEventWire.Replace("\"payload\":null}", "\"payload\":\"", StringComparison.Ordinal));
        byte[] bytes = [.. prefix, .. Convert.FromHexString(invalidHex), (byte)'"', (byte)'}'];

        Assert.IsFalse(MessageWire.TryDecode(bytes, out EncodedMessage? message, out MessageWireFailure failure));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.Invalid, failure);
    }

    [TestMethod]
    [DataRow("\\ud800")]
    [DataRow("\\udc00")]
    public void EscapedUnpairedSurrogatesCannotEnterAJsonPayload(string escapedSurrogate)
    {
        string payload = "\"" + escapedSurrogate + "\"";
        Assert.ThrowsExactly<ArgumentException>(() => Event(Encoding.UTF8.GetBytes(payload)));

        byte[] wire = Encoding.UTF8.GetBytes(MinimalEventWire.Replace("\"payload\":null",
            "\"payload\":" + payload, StringComparison.Ordinal));
        Assert.IsFalse(MessageWire.TryDecode(wire, out EncodedMessage? message, out MessageWireFailure failure));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.Invalid, failure);
    }

    [TestMethod]
    public void WhitespaceWithoutAJsonValueIsRejectedAtConstructionAndDecoding()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Event(" \t\r\n"u8.ToArray()));

        byte[] missingValue = Encoding.UTF8.GetBytes(MinimalEventWire.Replace("\"payload\":null",
            "\"payload\": \t\r\n", StringComparison.Ordinal));
        Assert.IsFalse(MessageWire.TryDecode(missingValue, out EncodedMessage? message, out MessageWireFailure failure));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.Invalid, failure);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("{}")]
    public void WhitespaceAroundAScalarOrEmptyCollectionStillContainsAValidValue(string json)
    {
        string payload = " \t" + json + "\r\n";
        MessageEnvelope envelope = Event(Encoding.UTF8.GetBytes(payload));
        Assert.AreEqual(payload, Encoding.UTF8.GetString(envelope.CopyPayload()));

        EncodedMessage encoded = MessageWire.Encode(envelope);
        Assert.IsTrue(MessageWire.TryDecode(encoded.CopyBytes(), out EncodedMessage? decoded, out MessageWireFailure failure));
        Assert.IsNotNull(decoded);
        Assert.AreEqual(json, Encoding.UTF8.GetString(decoded.Envelope.CopyPayload()).Trim());
        Assert.AreEqual(MessageWireFailure.None, failure);
    }

    [TestMethod]
    public void OwnedEnvelopeAndEveryPublicCopyRemainIndependentOfCallerMutation()
    {
        byte[] payload = Encoding.UTF8.GetBytes(PrecisePayload);
        var headers = new Dictionary<string, string> { ["z-last"] = "one", ["a-first"] = "two" };
        MessageEnvelope envelope = Event(payload, headers);
        byte[] expectedPayload = payload.ToArray();
        payload[0] = (byte)'x';
        headers["z-last"] = "changed";
        headers["new"] = "injected";

        EncodedMessage encoded = MessageWire.Encode(envelope);
        byte[] firstWire = encoded.CopyBytes();
        byte[] firstDigest = encoded.Digest.CopyBytes();
        byte[] detachedPayload = envelope.CopyPayload();
        byte[] detachedWire = encoded.CopyBytes();
        byte[] detachedDigest = encoded.Digest.CopyBytes();
        detachedPayload[0] ^= 1;
        detachedWire[0] ^= 1;
        detachedDigest[0] ^= 1;

        CollectionAssert.AreEqual(expectedPayload, envelope.CopyPayload());
        CollectionAssert.AreEqual(expectedPayload, encoded.Envelope.CopyPayload());
        CollectionAssert.AreEqual(firstWire, encoded.CopyBytes());
        CollectionAssert.AreEqual(firstDigest, encoded.Digest.CopyBytes());
        Assert.AreEqual("one", envelope.Headers["z-last"]);
        Assert.IsFalse(envelope.Headers.ContainsKey("new"));

        var destination = Enumerable.Repeat((byte)0x7f, encoded.Length + 1).ToArray();
        encoded.CopyTo(destination);
        CollectionAssert.AreEqual(firstWire, destination[..encoded.Length]);
        Assert.AreEqual((byte)0x7f, destination[^1]);
        Assert.ThrowsExactly<ArgumentException>(() => encoded.CopyTo(new byte[encoded.Length - 1]));
    }

    [TestMethod]
    public void DecodingPreservesExactInputBytesRatherThanReserializingOrBorrowingThem()
    {
        string wire = MinimalEventWire.Replace("\"kind\":\"event\"", "\"kind\" : \"event\"", StringComparison.Ordinal)
            .Replace("\"payload\":null", "\"payload\" : { \"amount\" : 1.2300 }", StringComparison.Ordinal);
        byte[] input = Encoding.UTF8.GetBytes(wire);

        Assert.IsTrue(MessageWire.TryDecode(input, out EncodedMessage? message, out _));
        Assert.IsNotNull(message);
        input.AsSpan().Fill((byte)'x');

        Assert.AreEqual(wire, Encoding.UTF8.GetString(message.CopyBytes()));
        Assert.AreEqual("{ \"amount\" : 1.2300 }", Encoding.UTF8.GetString(message.Envelope.CopyPayload()));
        CollectionAssert.AreEqual(SHA256.HashData(Encoding.UTF8.GetBytes(wire)), message.Digest.CopyBytes());
    }

    [TestMethod]
    public void SameMessageIdentityWithChangedImmutableContentHasADifferentTransportDigest()
    {
        EncodedMessage first = MessageWire.Encode(Event(Encoding.UTF8.GetBytes("""{"value":1}""")));
        EncodedMessage changed = MessageWire.Encode(Event(Encoding.UTF8.GetBytes("""{"value":2}""")));

        Assert.AreEqual(first.Envelope.MessageId, changed.Envelope.MessageId);
        Assert.AreNotEqual(first.Digest, changed.Digest);
        Assert.AreEqual(PrecisePayload, Encoding.UTF8.GetString(EnvelopeFor("event-precision").CopyPayload()));
    }

    [TestMethod]
    public void PayloadAndWholeEnvelopeBoundsAreAppliedBeforeAcceptance()
    {
        var limits = new MessagingLimits(maximumPayloadBytes: 32);
        string fittingPayload = "\"" + new string('x', 30) + "\"";
        string oversizedPayload = "\"" + new string('x', 31) + "\"";

        EncodedMessage fitting = MessageWire.Encode(Event(Encoding.UTF8.GetBytes(fittingPayload)), limits);
        Assert.AreEqual(32, fitting.Envelope.PayloadLength);
        Assert.ThrowsExactly<ArgumentException>(() => MessageWire.Encode(Event(Encoding.UTF8.GetBytes(oversizedPayload)), limits));

        byte[] oversizedWire = Encoding.UTF8.GetBytes(MinimalEventWire.Replace("\"payload\":null",
            "\"payload\":" + oversizedPayload, StringComparison.Ordinal));
        Assert.IsFalse(MessageWire.TryDecode(oversizedWire, out EncodedMessage? message, out MessageWireFailure failure, limits));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.TooLarge, failure);

        Assert.IsFalse(MessageWire.TryDecode(new byte[limits.MaximumEnvelopeBytes + 1], out message, out failure, limits));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.TooLarge, failure);
    }

    [TestMethod]
    public void HeaderCountAndCombinedUtf8BytesHaveIndependentFiniteBounds()
    {
        var maximumCount = Enumerable.Range(0, 32).ToDictionary(index => "h" + index, _ => "v");
        Assert.AreEqual(32, MessageWire.Encode(Event("null"u8.ToArray(), maximumCount)).Envelope.Headers.Count);
        var tooMany = new Dictionary<string, string>(maximumCount) { ["extra"] = "v" };
        Assert.ThrowsExactly<ArgumentException>(() => Event("null"u8.ToArray(), tooMany));

        var maximumBytes = Enumerable.Range(0, 8).ToDictionary(index => ((char)('a' + index)).ToString(), _ => new string('x', 1023));
        Assert.AreEqual(8, MessageWire.Encode(Event("null"u8.ToArray(), maximumBytes)).Envelope.Headers.Count);
        var tooManyBytes = new Dictionary<string, string>(maximumBytes) { ["a"] = new string('x', 1024) };
        Assert.ThrowsExactly<ArgumentException>(() => Event("null"u8.ToArray(), tooManyBytes));
    }

    [TestMethod]
    public void EscapedMetadataHasItsOwnWireByteLimitEvenWhenDecodedHeadersFit()
    {
        var headers = new Dictionary<string, string>
        {
            ["a"] = new string('<', 1024),
            ["b"] = new string('<', 1024),
            ["c"] = new string('<', 1024)
        };
        MessageEnvelope envelope = Event("null"u8.ToArray(), headers);
        Assert.ThrowsExactly<ArgumentException>(() => MessageWire.Encode(envelope));

        string escaped = string.Concat(Enumerable.Repeat("\\u003C", 1024));
        string encodedHeaders = ",\"headers\":{\"a\":\"" + escaped + "\",\"b\":\"" + escaped + "\",\"c\":\"" + escaped + "\"}";
        byte[] wire = Encoding.UTF8.GetBytes(MinimalEventWire[..^1] + encodedHeaders + "}");
        Assert.IsFalse(MessageWire.TryDecode(wire, out EncodedMessage? message, out MessageWireFailure failure));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.TooLarge, failure);
    }

    [TestMethod]
    public void PayloadDepthIsCheckedSeparatelyFromEnvelopeNesting()
    {
        var limits = new MessagingLimits(maximumJsonDepth: 2);
        Assert.IsTrue(MessageWire.TryDecode(MessageWire.Encode(Event("[[]]"u8.ToArray()), limits).CopyBytes(),
            out _, out _, limits));

        MessageEnvelope tooDeep = Event("[[[]]]"u8.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => MessageWire.Encode(tooDeep, limits));
        byte[] wire = Encoding.UTF8.GetBytes(MinimalEventWire.Replace("\"payload\":null", "\"payload\":[[[]]]", StringComparison.Ordinal));
        Assert.IsFalse(MessageWire.TryDecode(wire, out EncodedMessage? message, out MessageWireFailure failure, limits));
        Assert.IsNull(message);
        Assert.AreEqual(MessageWireFailure.Invalid, failure);
    }

    [TestMethod]
    public void Rfc3339UtcTimestampAcceptsWholeSecondsAndFractionalSeconds()
    {
        foreach (string timestamp in new[] { "2026-11-22T12:34:56Z", "2026-11-22T12:34:56.1Z", "2026-11-22T12:34:56.1234567Z" })
        {
            byte[] wire = Encoding.UTF8.GetBytes(MinimalEventWire.Replace("2026-11-22T12:34:56.1234567Z", timestamp, StringComparison.Ordinal));
            Assert.IsTrue(MessageWire.TryDecode(wire, out EncodedMessage? message, out MessageWireFailure failure), timestamp);
            Assert.IsNotNull(message);
            Assert.AreEqual(TimeSpan.Zero, message.Envelope.CreatedAtUtc.Offset);
            Assert.AreEqual(MessageWireFailure.None, failure);
        }
    }

    private static MessageEnvelope EnvelopeFor(string id) => id switch
    {
        "event-precision" => Event(Encoding.UTF8.GetBytes(PrecisePayload),
            new Dictionary<string, string> { ["z-last"] = "one", ["a-first"] = "two" }),
        "command" => new(Guid.Parse(MessageId), MessageKind.Command, new("country.refresh", 1), "country-caller", "test", Created(),
            """{"country":"MT"}"""u8.ToArray()),
        "durable-request" => new(Guid.Parse(RequestId), MessageKind.Request, new("country.update", 1), "country-caller", "test", Created(),
            """{"country":"MT","name":"Malta"}"""u8.ToArray(), Guid.Parse(AttemptId),
            new("country-caller", "test", Guid.Parse(EndpointId)), operation: new("country.update", "country:MT", "opaque-01")),
        "reply" => new(Guid.Parse(ReplyId), MessageKind.Reply, new("country.lookup.reply", 1), "country-service", "test", Created(),
            """{"decision":"rejected","code":"not-found"}"""u8.ToArray(), Guid.Parse(AttemptId), inReplyToMessageId: Guid.Parse(RequestId)),
        _ => throw new AssertFailedException("Unknown portable wire vector.")
    };

    private static MessageEnvelope Event(byte[] payload, IReadOnlyDictionary<string, string>? headers = null)
        => new(Guid.Parse(MessageId), MessageKind.Event, new("country.updated", 1), "country-service", "test", Created(), payload, headers: headers);

    private static DateTimeOffset Created() => new DateTimeOffset(2026, 11, 22, 12, 34, 56, TimeSpan.Zero).AddTicks(1_234_567);

    private static JsonObject Vector(string group, string id)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "conformance", "messaging", "v1", "wire-vectors.json");
            if (File.Exists(path))
            {
                return JsonNode.Parse(File.ReadAllBytes(path))![group]!.AsArray()
                    .Select(item => item!.AsObject()).Single(item => item["id"]!.GetValue<string>() == id);
            }
        }
        throw new AssertFailedException("The checked-in portable messaging wire vectors were not found.");
    }
}
