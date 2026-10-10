using CIS.Configuration;
using CIS.Tests.UnitTests.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class RuntimeMetadataFailureTests
{
    [TestMethod]
    [DataRow("start")]
    [DataRow("host")]
    [DataRow("family")]
    [DataRow("description")]
    [DataRow("osArchitecture")]
    [DataRow("processArchitecture")]
    [DataRow("runtime")]
    [DataRow("pid")]
    public void NativeFailureAffectsOnlyTheUnavailableFact(string field)
    {
        RuntimeMetadata baseline = RuntimeMetadataDiscovery.Discover(new RuntimeMetadataStub());
        var source = new RuntimeMetadataStub { FailingField = field };

        RuntimeMetadata actual = RuntimeMetadataDiscovery.Discover(source);

        RuntimeMetadata expected = field switch
        {
            "start" => baseline with { ProcessStartedAtUtc = null },
            "host" => baseline with { HostName = null },
            "family" => baseline with { OperatingSystem = "unknown" },
            "description" => baseline with { OperatingSystemDescription = null },
            "osArchitecture" => baseline with { OSArchitecture = null },
            "processArchitecture" => baseline with { ProcessArchitecture = null },
            "runtime" => baseline with { RuntimeDescription = null },
            _ => baseline with { ProcessId = null }
        };
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(8, source.Reads.Count);
    }

    [TestMethod]
    [DataRow("NotSupported")]
    [DataRow("InvalidOperation")]
    [DataRow("Access")]
    [DataRow("IO")]
    public void UnavailableProcessStartNeverUsesInitializationOrCurrentTime(string kind)
    {
        Exception failure = kind switch
        {
            "NotSupported" => new NotSupportedException(),
            "InvalidOperation" => new InvalidOperationException(),
            "Access" => new UnauthorizedAccessException(),
            _ => new IOException()
        };
        var source = new RuntimeMetadataStub { FailingField = "start", Failure = failure };

        RuntimeMetadata metadata = RuntimeMetadataDiscovery.Discover(source);

        Assert.IsNull(metadata.ProcessStartedAtUtc);
        Assert.AreEqual(123, metadata.ProcessId);
        Assert.AreEqual("ApplicationVisible.Host", metadata.HostName);
    }

    [TestMethod]
    [DataRow("start", false)]
    [DataRow("host", false)]
    [DataRow("start", true)]
    [DataRow("host", true)]
    public void UnexpectedValueOrReferenceFaultsAndCancellationPropagate(string field, bool cancellation)
    {
        Exception failure = cancellation ? new OperationCanceledException() : new ArgumentException();
        var source = new RuntimeMetadataStub { FailingField = field, Failure = failure };

        Exception actual = Assert.Throws<Exception>(() => RuntimeMetadataDiscovery.Discover(source));

        Assert.AreSame(failure, actual);
    }
}
