using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class TypeManagerPackageConsumerTests
{
    private static readonly Lazy<Task<TypeManagerPackageConsumerFixture>> Prepared = new(TypeManagerPackageConsumerFixture.CreateAsync);
    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static void ReleaseFixture(TestContext context)
    {
        if (!Prepared.IsValueCreated || !Prepared.Value.IsCompletedSuccessfully) { return; }
        var fixture = Prepared.Value.Result;
        try { context.WriteLine("CIS-TYPE-MANAGER-PACKAGE " + fixture.Summary); }
        finally { fixture.Dispose(); }
    }

    [TestMethod]
    [DataRow("Console")]
    [DataRow("Web")]
    public async Task PackagedSdkSupportsBuildAndBothPublishModes(string host)
        => await (await Prepared.Value).VerifyHostAsync(host, TestContext);

    [TestMethod]
    public async Task MissingSelectedAssemblyFailsBeforeWebBuilderOrContainerCreation()
        => await (await Prepared.Value).VerifyBrokenDeploymentAsync(TestContext);

    [TestMethod]
    public async Task OrdinaryPackageReferenceWithoutInventoryRemainsUsable()
        => await (await Prepared.Value).VerifyOrdinaryConsumerAsync(TestContext);
}
