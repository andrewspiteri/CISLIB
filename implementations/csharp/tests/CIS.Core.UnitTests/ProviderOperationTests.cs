using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class ProviderOperationTests
{
    public static IEnumerable<object[]> Modes => ProviderOperationFixture.Modes.Select(mode => new object[] { mode });
    [TestMethod]
    [DynamicData(nameof(Modes))]
    public async Task ScopeOperationIdentityUsesCorePrecedence(string mode)
    { ProviderOperationExpectations.Validate(mode, await ProviderOperationFixture.ExecuteAsync(mode)); }
}
