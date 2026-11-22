using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
public sealed class MessageBoundedEncodingTests
{
    [TestMethod]
    [DataRow(1)]
    [DataRow(16)]
    public void TinyPayloadProfilesRejectExpandedHeadersBeforeAnUnboundedWriterCanGrow(int payloadLimit)
    {
        var headers = Enumerable.Range(0, 7).ToDictionary(index => "h" + index, _ => new string('<', 1000));
        var envelope = new MessageEnvelope(Guid.NewGuid(), MessageKind.Event, new("test.number", 1),
            "producer", "test", DateTimeOffset.UnixEpoch, "1"u8.ToArray(), headers: headers);
        Assert.AreEqual(7, envelope.Headers.Count, "The raw metadata is valid; JSON escaping exceeds the tiny framing profile.");
        Assert.Throws<ArgumentException>(() => MessageWire.Encode(envelope, new(payloadLimit)));
    }

    [TestMethod]
    public void AValidTinyProfileStillSupportsLargeMetadataAndOwnsOneConsistentWirePayload()
    {
        var headers = Enumerable.Range(0, 8).ToDictionary(index => "h" + index, _ => new string('x', 1000));
        var limits = new MessagingLimits(1);
        var envelope = new MessageEnvelope(Guid.NewGuid(), MessageKind.Event, new("test.number", 1),
            "producer", "test", DateTimeOffset.UnixEpoch, "1"u8.ToArray(), headers: headers);
        EncodedMessage encoded = MessageWire.Encode(envelope, limits);
        Assert.IsLessThanOrEqualTo(limits.MaximumEnvelopeBytes, encoded.Length);
        Assert.AreEqual(headers["h7"], encoded.Envelope.Headers["h7"]);
        CollectionAssert.AreEqual("1"u8.ToArray(), encoded.Envelope.CopyPayload());
        Assert.IsTrue(MessageWire.TryDecode(encoded.CopyBytes(), out EncodedMessage? decoded, out _, limits));
        Assert.AreEqual(encoded.Digest, decoded!.Digest);
    }
}
