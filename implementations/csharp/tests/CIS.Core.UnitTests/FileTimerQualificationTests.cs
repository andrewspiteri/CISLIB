using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FileTimerQualificationTests
{
    public static IEnumerable<object[]> Modes => FileTimerQualificationExpectations.Modes.Select(m => new object[] { m });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task TerminalOutputsCannotRestartPeriodicWork(string mode) =>
        FileTimerQualificationExpectations.Validate(mode, await FileTimerQualificationFixture.ExecuteAsync(mode));
}
