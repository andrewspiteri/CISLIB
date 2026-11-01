using CIS.Logging;
using CIS.Logging.Events;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

// Explicit qualification keeps the original method, rows and assertions.
public sealed partial class EventEncodingTests
{
    [TestMethod]
    [TestCategory("ExtendedStress")]
    public void HugeAttachmentDoesNotCreateAFullEscapedSecondaryRepresentation()
    {
        CapturedEventFacts facts = Capture(Attributes("attachment", LogValue.String(new string('x', 20_000_000))));
        _ = EventEncoder.Encode(facts, 65536);
        long before = GC.GetAllocatedBytesForCurrentThread();
        EncodedEvent encoded = EventEncoder.Encode(facts, 65536);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsTrue(allocated < 1_048_576, $"Encoding allocated {allocated} bytes for a bounded record.");
        Assert.IsTrue(encoded.IsTruncated); Assert.IsTrue(encoded.JsonLine.Length <= 65536);
    }
}
