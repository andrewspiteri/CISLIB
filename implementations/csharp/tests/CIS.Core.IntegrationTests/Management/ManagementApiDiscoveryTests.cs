using System.Net;
using CIS.Management;
using CIS.Management.Wire;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementApiDiscoveryTests
{
    [TestMethod]
    public async Task CompleteInstalledDiscoveryPagesAcrossReplicasAndBindsTheCurrentActorApplicationAndSize()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync();
        string readerToken = fixture.Token("reader");
        HttpClient[] readers = [fixture.Client(subject: null, replica: 0, explicitToken: readerToken),
            fixture.Client(subject: null, replica: 1, explicitToken: readerToken)];
        HttpClient reader = readers[0];
        using HttpResponseMessage infoResponse = await reader.GetAsync("service-info");
        Assert.AreEqual(HttpStatusCode.OK, infoResponse.StatusCode);
        ManagementServiceInfo info = Decode<ManagementServiceInfo>(await infoResponse.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(1, info.ApiDiscoveryVersion);
        Assert.IsTrue(info.Features.Count <= 64);
        Assert.IsFalse(info.Features.Any(feature => feature.OperationId == "listServiceFeatures"));

        var installed = new List<ManagementFeature>();
        string? cursor = null;
        int pageNumber = 0;
        do
        {
            HttpClient replica = readers[pageNumber++ % 2];
            using HttpResponseMessage response = await replica.GetAsync("service-features?pageSize=3"
                + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor)));
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            ManagementServiceFeaturePage page = Decode<ManagementServiceFeaturePage>(await response.Content.ReadAsByteArrayAsync());
            Assert.AreEqual("complete-v1", page.Profile);
            Assert.IsTrue(page.Items.Count <= 3);
            installed.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        CollectionAssert.AreEqual(installed.Select(feature => feature.OperationId).Order(StringComparer.Ordinal).ToArray(),
            installed.Select(feature => feature.OperationId).ToArray());
        Assert.AreEqual(installed.Count, installed.Select(feature => feature.OperationId).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(info.Features.Select(feature => feature.OperationId)
                .Concat(["listServiceFeatures", "listApplicationApiCapabilities"]).ToArray(),
            installed.Select(feature => feature.OperationId).ToArray());
        Assert.IsFalse(installed.Any(feature => feature.Path.Contains("/rule-engine", StringComparison.Ordinal)),
            "An unenrolled Rule Engine must not be advertised by portable declarations alone.");

        string path = "applications/" + fixture.ApplicationA + "/api-capabilities?pageSize=1";
        using HttpResponseMessage first = await reader.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        ManagementApplicationApiCapabilityPage capabilities = Decode<ManagementApplicationApiCapabilityPage>(await first.Content.ReadAsByteArrayAsync());
        Assert.AreEqual(fixture.ApplicationA, capabilities.ApplicationId);
        Assert.IsNotNull(capabilities.NextCursor);
        string suffix = "&cursor=" + Uri.EscapeDataString(capabilities.NextCursor);
        foreach ((HttpClient client, string target) in new[]
        {
            (reader, path.Replace("pageSize=1", "pageSize=2", StringComparison.Ordinal) + suffix),
            (fixture.Client("operator-a"), path + suffix),
            (reader, "service-features?pageSize=1" + suffix)
        })
        {
            using HttpResponseMessage invalid = await client.GetAsync(target);
            Assert.AreEqual(HttpStatusCode.BadRequest, invalid.StatusCode, target);
        }
        using HttpResponseMessage oversized = await reader.GetAsync("service-features?pageSize=65");
        Assert.AreEqual(HttpStatusCode.BadRequest, oversized.StatusCode);
    }

    private static T Decode<T>(byte[] bytes) where T : class
        => ManagementWire.DecodeResponse<T>(bytes) ?? throw new AssertFailedException("Expected an admitted " + typeof(T).Name + ".");
}
