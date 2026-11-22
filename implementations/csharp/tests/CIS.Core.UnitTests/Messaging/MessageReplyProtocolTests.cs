using System.Text;
using System.Text.Json;
using CIS.Messaging;
using CIS.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
public sealed class MessageReplyProtocolTests
{
    private const string Digest = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
    private const string Correlation = "\"requestAlias\":\"Lookup\",\"routeAlias\":\"commands\",\"requestDigest\":\"" + Digest + "\",";

    [TestMethod]
    [DataRow("\"requestAlias\":\"Other\",\"routeAlias\":\"commands\",\"requestDigest\":\"" + Digest + "\",")]
    [DataRow("\"requestAlias\":\"Lookup\",\"routeAlias\":\"other\",\"requestDigest\":\"" + Digest + "\",")]
    [DataRow("\"requestAlias\":\"Lookup\",\"routeAlias\":\"commands\",")]
    [DataRow("\"requestAlias\":\"Lookup\",\"routeAlias\":\"commands\",\"requestDigest\":null,")]
    [DataRow("\"requestAlias\":\"Lookup\",\"routeAlias\":\"commands\",\"requestDigest\":\"000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F\",")]
    [DataRow("\"requestAlias\":\"Lookup\",\"routeAlias\":\"commands\",\"requestDigest\":\"00010203\",")]
    [DataRow("\"requestAlias\":\"Lookup\",\"routeAlias\":\"commands\",\"requestDigest\":\"ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff\",")]
    public void IndependentWrongOrMissingCorrelationNeverClassifiesTheLegitimateAttemptAsInvalid(string correlation)
    {
        AssertDecode("{" + correlation + "\"responseVersion\":999,\"protocol\":\"denied\"}", MessageReplyDecode.Unmatched);
    }

    [TestMethod]
    [DataRow("\"responseVersion\":2,\"protocol\":\"completed\",\"decision\":\"succeeded\",\"value\":7")]
    [DataRow("\"responseVersion\":\"1\",\"protocol\":\"denied\"")]
    [DataRow("\"protocol\":\"denied\"")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"Completed\",\"decision\":\"succeeded\",\"value\":7")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"denied\",\"unknown\":true")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"completed\"")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"completed\",\"decision\":\"succeeded\"")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"completed\",\"decision\":\"succeeded\",\"value\":\"7\"")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"completed\",\"decision\":\"succeeded\",\"value\":7,\"error\":{\"code\":\"app.no\",\"message\":\"No\"}")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"completed\",\"decision\":\"rejected\",\"value\":7")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"denied\",\"value\":7")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"denied\",\"decision\":\"rejected\"")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"denied\",\"error\":{\"code\":null,\"message\":\"No\"}")]
    [DataRow("\"responseVersion\":1,\"protocol\":\"denied\",\"error\":{\"code\":\"app.no\",\"message\":\"No\",\"extra\":1}")]
    public void OnlyExactlyCorrelatedMalformedTypedContentCanBecomeInvalidResponse(string fields)
    {
        AssertDecode("{" + Correlation + fields + "}", MessageReplyDecode.Invalid);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void IndependentlyAuthoredCompletedDecisionsKeepBusinessSuccessAndRejectionSeparate(bool rejected)
    {
        string business = rejected ? "\"decision\":\"rejected\",\"error\":{\"code\":\"app.declined\",\"message\":\"Original rejection\"}"
            : "\"decision\":\"succeeded\",\"value\":7";
        MessageResponse<int> response = AssertDecode("{" + Correlation + "\"responseVersion\":1,\"protocol\":\"completed\"," + business + "}", MessageReplyDecode.Valid)!;
        Assert.AreEqual(MessageProtocolDisposition.Completed, response.Disposition);
        Assert.AreEqual(rejected ? OperationBusinessDecision.Rejected : OperationBusinessDecision.Succeeded, response.Decision!.Decision);
        if (rejected)
        {
            Assert.AreEqual("app.declined", response.Decision.Error!.Code);
        }
        else
        {
            Assert.AreEqual(7, response.Decision.Value);
        }
    }

    [TestMethod]
    [DataRow("keyConflict", MessageProtocolDisposition.KeyConflict)]
    [DataRow("resultExpired", MessageProtocolDisposition.ResultExpired)]
    [DataRow("incompatible", MessageProtocolDisposition.Incompatible)]
    [DataRow("denied", MessageProtocolDisposition.Denied)]
    [DataRow("outcomeUnknown", MessageProtocolDisposition.OutcomeUnknown)]
    [DataRow("unavailable", MessageProtocolDisposition.Unavailable)]
    public void FiniteNonbusinessProtocolResultsNeverInventATypedDecision(string name, MessageProtocolDisposition disposition)
    {
        MessageResponse<int> response = AssertDecode("{" + Correlation + "\"responseVersion\":1,\"protocol\":\"" + name + "\"}", MessageReplyDecode.Valid)!;
        Assert.AreEqual(disposition, response.Disposition);
        Assert.IsNull(response.Decision);
    }

    [TestMethod]
    public void EncoderBindsDeniedResponseToTheActualChangedRequestDigestRatherThanAnotherAttemptsIdentity()
    {
        var actualChangedBody = new MessageDigest(Enumerable.Repeat((byte)255, 32).ToArray());
        var codec = new IntegerCodec();
        byte[] json = MessageReplyProtocol.Encode(Definition(codec), actualChangedBody, codec,
            new MessageResponse<int>(MessageProtocolDisposition.Denied), new());
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.AreEqual(new string('f', 64), document.RootElement.GetProperty("requestDigest").GetString());
        Assert.IsFalse(document.RootElement.TryGetProperty("decision", out _));
        MessageRequestReference legitimate = Reference();
        Assert.AreEqual(MessageReplyDecode.Unmatched, MessageReplyProtocol.TryDecode(Reply(json, legitimate), legitimate,
            Definition(codec), codec, new(), out MessageResponse<int>? response));
        Assert.IsNull(response);
    }

    [TestMethod]
    public void ExactCorrelationStillRequiresExpectedContractAndSelectedPayloadDepthAndByteBounds()
    {
        string fields = "{" + Correlation + "\"responseVersion\":1,\"protocol\":\"denied\",\"error\":{\"code\":\"app.no\",\"message\":\"No\"}}";
        MessageRequestReference expected = Reference();
        var codec = new IntegerCodec();
        EncodedMessage wrongContract = Reply(Encoding.UTF8.GetBytes(fields), expected, new("other.result", 1));
        Assert.AreEqual(MessageReplyDecode.Invalid, MessageReplyProtocol.TryDecode(wrongContract, expected, Definition(codec), codec, new(), out _));
        EncodedMessage valid = Reply(Encoding.UTF8.GetBytes(fields), expected);
        Assert.AreEqual(MessageReplyDecode.Unmatched, MessageReplyProtocol.TryDecode(valid, expected, Definition(codec), codec, new(32), out _));
        Assert.AreEqual(MessageReplyDecode.Unmatched, MessageReplyProtocol.TryDecode(valid, expected, Definition(codec), codec, new(maximumJsonDepth: 1), out _));
    }

    [TestMethod]
    [DataRow(" ")]
    [DataRow("{\"requestAlias\":\"Lookup\",\"requestAlias\":\"Other\"}")]
    [DataRow("{\"error\":{\"code\":\"one\",\"code\":\"two\"}}")]
    public void MalformedAndDuplicateJsonCannotEnterThePortableReplyBoundary(string payload)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Reply(Encoding.UTF8.GetBytes(payload), Reference()));
    }

    [TestMethod]
    public void UnexpectedApplicationCodecFaultRemainsOriginalRatherThanBecomingInvalidContent()
    {
        var original = new InvalidOperationException("Original application codec fault");
        var codec = new IntegerCodec(original);
        MessageRequestReference expected = Reference();
        byte[] json = Encoding.UTF8.GetBytes("{" + Correlation + "\"responseVersion\":1,\"protocol\":\"completed\",\"decision\":\"succeeded\",\"value\":7}");
        Assert.AreSame(original, Assert.ThrowsExactly<InvalidOperationException>(() =>
            MessageReplyProtocol.TryDecode(Reply(json, expected), expected, Definition(codec), codec, new(), out _)));
    }

    private static MessageResponse<int>? AssertDecode(string json, MessageReplyDecode expectedResult)
    {
        MessageRequestReference expected = Reference();
        var codec = new IntegerCodec();
        Assert.AreEqual(expectedResult, MessageReplyProtocol.TryDecode(Reply(Encoding.UTF8.GetBytes(json), expected), expected,
            Definition(codec), codec, new(), out MessageResponse<int>? response));
        if (expectedResult != MessageReplyDecode.Valid)
        {
            Assert.IsNull(response);
        }
        return response;
    }

    private static MessageRequestDefinition<int, int> Definition(IntegerCodec codec)
        => new("Lookup", "commands", codec, codec, MessageRequestMode.Query, "responder", "test");

    private static MessageRequestReference Reference()
    {
        var receipt = new MessageStageReceipt(Guid.NewGuid(), Guid.NewGuid(), "Ledger", "caller", "test",
            new MessageDigest(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()));
        return new("Lookup", Guid.NewGuid(), receipt, new("caller", "test", Guid.NewGuid()), new("integer", 1));
    }

    private static EncodedMessage Reply(byte[] json, MessageRequestReference expected, MessageContract? contract = null)
        => MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Reply, contract ?? expected.ResponseContract,
            "responder", "test", DateTimeOffset.UnixEpoch, json, requestAttemptId: expected.RequestAttemptId,
            inReplyToMessageId: expected.MessageId));

    private sealed class IntegerCodec(Exception? fault = null) : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("integer", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            if (fault is not null)
            {
                throw fault;
            }
            using JsonDocument document = JsonDocument.Parse(json);
            value = 0;
            return contract == Contract && document.RootElement.ValueKind == JsonValueKind.Number && document.RootElement.TryGetInt32(out value);
        }
    }
}
