using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

[TestClass]
[DoNotParallelize]
public sealed class CountryListPackageTests
{
    private static readonly Lazy<Task<CountryListPackageFixture>> Prepared = new(CountryListPackageFixture.CreateAsync);
    public TestContext TestContext { get; set; } = null!;

    [ClassCleanup]
    public static void Cleanup(TestContext context)
    {
        if (!Prepared.IsValueCreated || !Prepared.Value.IsCompletedSuccessfully) { return; }
        var fixture = Prepared.Value.Result;
        try { context.WriteLine("CIS-COUNTRY-LIST-PACKAGE " + fixture.Summary); }
        finally { fixture.Dispose(); }
    }

    [TestMethod]
    [DataRow("", "none")]
    [DataRow("--fail-prime", "priming")]
    [DataRow("--fail-refresh-registration", "registration")]
    [DataRow("--fail-later-startup", "later-startup")]
    public async Task ActualPackageExamplePreservesStartupHttpFailureRefreshAndOwnedDrain(string option, string failureMode)
        => await (await Prepared.Value).VerifyStoryAsync(option, failureMode, TestContext);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ActualPackageExampleAndMatchingProbeReportSameIdentityHealthReadinessAndTransportErrors(bool failPrime)
        => await (await Prepared.Value).VerifyPipeAsync(failPrime, TestContext);
}
