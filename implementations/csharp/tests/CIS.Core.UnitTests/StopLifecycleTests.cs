using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StopLifecycleTests
{
    public static IEnumerable<object[]> Modes => StopLifecycleFixture.KernelModes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task PhysicalOwnershipWaitsForLateIo(string mode)
    {
        using var root = new TemporaryDirectory();
        var report = await StopLifecycleFixture.KernelAsync(mode, root.Path);
        StopLifecycleExpectations.Validate(mode, report);
    }
}
