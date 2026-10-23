using System.Text;
using CIS.Healthcheck;
using CIS.Healthcheck.Reporting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthReportDepthPreallocationBoundaryTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OverDepthUnknownFieldsAreRejectedBeforeTheirDescendantValuesAreMaterialized(bool objects)
    {
        const int maximumReportBytes = 1_048_576;
        const long rejectionAllocationBudget = 65_536;
        await using var service = new HealthcheckService(new HealthcheckOptions(),
            new HealthApplicationIdentity { Name = "depth preallocation boundary", InstanceId = Guid.NewGuid() });
        HealthSnapshot captured = service.CaptureSnapshot();
        HealthReportBuffer encoded = HealthReportFormatter.FormatPipeResponse(captured, HealthReportView.Health,
            new HealthcheckOptions { MaxReportBytes = maximumReportBytes });
        Assert.IsFalse(encoded.IsError);
        string envelope = Encoding.UTF8.GetString(encoded.Utf8Bytes.Span);
        // The envelope root contributes one level. All payload storage is built
        // before sampling. The short accepted control reaches exactly depth16;
        // only the rejected depth17 input has a large inaccessible descendant.
        byte[] atLimit = AddUnknown(envelope, objects, containers: 15, value: "small valid additive value");
        byte[] overLimit = AddUnknown(envelope, objects, containers: 16, value: new string('x', 262_144));
        Assert.IsTrue(overLimit.Length <= maximumReportBytes);
        long[] warmupBytes = new long[4], sampledBytes = new long[3];
        var rejections = new FormatException?[sampledBytes.Length];
        for (int index = 0; index < warmupBytes.Length; index++)
        {
            HealthParsedResponse validWarmup = HealthReportProtocol.ParseResponse(atLimit, HealthReportView.Health, maximumReportBytes);
            Assert.IsFalse(validWarmup.IsError);
            (FormatException? rejection, long allocated) = Reject(overLimit, maximumReportBytes);
            Assert.IsNotNull(rejection);
            warmupBytes[index] = allocated;
        }
        for (int index = 0; index < sampledBytes.Length; index++)
        {
            (rejections[index], sampledBytes[index]) = Reject(overLimit, maximumReportBytes);
        }

        TestContext.WriteLine("depth-preallocation objects={0} maxReportBytes={1} payloadBytes={2} allocationBudget={3} warmup=[{4}] measured=[{5}]",
            objects, maximumReportBytes, overLimit.Length, rejectionAllocationBudget, string.Join(',', warmupBytes), string.Join(',', sampledBytes));
        foreach (FormatException? rejection in rejections)
        {
            Assert.IsNotNull(rejection, "A complete otherwise valid response with a depth17 unknown field must be rejected.");
            Assert.AreEqual(typeof(FormatException), rejection.GetType());
        }
        foreach (long allocated in sampledBytes)
        {
            // The forbidden descendant alone would need over512KiB of UTF16
            // string storage. Broad fixed headroom permits the bounded depth
            // traversal and rejection, while observing materialization order.
            Assert.IsTrue(allocated <= rejectionAllocationBudget,
                $"Depth must be rejected before materializing an unknown descendant: {allocated} bytes allocated before rejection.");
        }

        HealthParsedResponse valid = HealthReportProtocol.ParseResponse(atLimit, HealthReportView.Health, maximumReportBytes);
        Assert.IsFalse(valid.IsError);
        Assert.IsNull(valid.ErrorCode);
        Assert.AreEqual(captured.HealthStatus == HealthStatus.Healthy, valid.SelectedHealthy);
        Assert.AreEqual(captured.Revision, service.CaptureSnapshot().Revision);
    }

    private static (FormatException? Rejection, long AllocatedBytes) Reject(byte[] payload, int maximum)
    {
        FormatException? rejection = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        try { _ = HealthReportProtocol.ParseResponse(payload, HealthReportView.Health, maximum); }
        catch (FormatException error) { rejection = error; }
        return (rejection, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static byte[] AddUnknown(string envelope, bool objects, int containers, string value)
    {
        var text = new StringBuilder(envelope.Length + value.Length + containers * 10);
        text.Append(envelope.AsSpan(0, envelope.Length - 1)).Append(",\"extension\":");
        for (int index = 0; index < containers; index++) { text.Append(objects ? "{\"next\":" : "["); }
        text.Append('"').Append(value).Append('"');
        for (int index = 0; index < containers; index++) { text.Append(objects ? '}' : ']'); }
        text.Append('}');
        return Encoding.UTF8.GetBytes(text.ToString());
    }
}
