using System.Text.Json.Nodes;
using CIS.Tests.DiscoveryFixture;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
public sealed class ExceptionContentProcessTests
{
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Cases => ExceptionContentSources.Cases.Select(id => new object[] { id });

    [TestMethod]
    [DynamicData(nameof(Cases))]
    public async Task ActualContentExclusionAndLossCasesRunInUninitializedChildren(string id)
    {
        JsonObject actual = await ExceptionProjectionProcessTests.Capture("exception-content", id, TestContext, sentinel: ExceptionContentSources.PrivateSentinel);
        ExceptionContentExpectations.Verify(id, actual);
        ExceptionBoundaryProcessTests.CheckShape(actual);
    }
}
