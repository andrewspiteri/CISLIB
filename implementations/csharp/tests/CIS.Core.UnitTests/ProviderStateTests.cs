using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderStateTests
{
    public static IEnumerable<object[]> Modes => ProviderStateFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderCapturesStructuredStateThroughCore(string mode)
    { ProviderStateExpectations.Validate(mode, await ProviderStateFixture.ExecuteAsync(mode)); }
}
