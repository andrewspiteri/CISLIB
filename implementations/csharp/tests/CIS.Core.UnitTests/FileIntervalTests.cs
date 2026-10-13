using CIS.Tests.DiscoveryFixture;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileIntervalTests
{
    public static IEnumerable<object[]> Modes => FileIntervalExpectations.Modes.Where(m => m != "env").Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualQueueSchedulesFileFlushDuringIdleTimeOrSafelyRollsBack(string mode)
    {
        using var root = new TemporaryDirectory();
        FileIntervalExpectations.Validate(mode, await FileIntervalFixture.ExecuteAsync(mode, root.Path));
    }
}
