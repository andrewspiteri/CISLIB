using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileTimerCoordinationTests
{
    public static IEnumerable<object[]> Modes => FileTimerCoordinationExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ActualFileTimerSharesBoundedSerializedFlushControls(string mode) =>
        FileTimerCoordinationExpectations.Validate(mode, await FileTimerCoordinationFixture.ExecuteAsync(mode));
}
