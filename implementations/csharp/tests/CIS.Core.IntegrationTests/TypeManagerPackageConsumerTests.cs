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
    public async Task PackagedSdkSupportsTheMinimalFixtureBuildAndBothPublishModes()
        => await (await Prepared.Value).VerifyHostAsync(TestContext);

    [TestMethod]
    public async Task MissingSelectedAssemblyFailsBeforeConsumerConstruction()
        => await (await Prepared.Value).VerifyBrokenDeploymentAsync(TestContext);

    [TestMethod]
    public async Task CancellationWhileLoadingSelectedAssemblyPreventsConsumerConstruction()
        => await (await Prepared.Value).VerifyCanceledPreparationAsync(TestContext);

    [TestMethod]
    public async Task OrdinaryPackageReferenceWithoutInventoryRemainsUsable()
        => await (await Prepared.Value).VerifyOrdinaryConsumerAsync(TestContext);
}
