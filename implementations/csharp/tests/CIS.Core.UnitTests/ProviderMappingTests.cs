using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderMappingTests
{
    public static IEnumerable<object[]> Modes => ProviderMappingFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ProviderMatchesEquivalentCoreMappings(string mode)
    { ProviderMappingExpectations.Validate(mode, await ProviderMappingFixture.ExecuteAsync(mode)); }
}
