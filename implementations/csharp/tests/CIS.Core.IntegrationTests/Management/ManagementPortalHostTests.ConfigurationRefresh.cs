using System.Text.Json;
using CIS.Management.Wire;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const string RefreshDraftPayload = """{"Refresh":"Keep this draft · 日本語 🌍 e\u0301","Count":9007199254740993,"Enabled":false}""";
    private const string RefreshConcurrentPayload = """{"Concurrent":"A different operator changed this source","Enabled":true}""";

    [TestMethod]
    public async Task ActualEditorRefreshRetriesItsReadAndPreservesDraftWithCurrentSourceAndMapPreconditions()
    {
        await RunFixtureAsync(browser: true, editorRefresh: true);
    }

    private static async Task VerifyConfigurationEditorRefreshAsync(ManagementHostFixture fixture, JsonElement proof,
        ConfigurationBoundarySeed seed, string issuer)
    {
        Assert.AreEqual(fixture.ApplicationA, proof.GetProperty("applicationId").GetString());
        Assert.AreEqual(seed.ApplicationSourceId, proof.GetProperty("sourceId").GetString());
        Assert.AreEqual(seed.BindingId, proof.GetProperty("bindingId").GetString());
        Assert.IsTrue(proof.GetProperty("transientReadRetried").GetBoolean());
        Assert.IsTrue(proof.GetProperty("draftRetained").GetBoolean());
        Assert.AreEqual(ManagementConfigurationWire.ComputePayloadSha256(RefreshDraftPayload), proof.GetProperty("sha256").GetString());
        string concurrent = proof.GetProperty("concurrentPublicationId").GetString()!;
        string publication = proof.GetProperty("publicationId").GetString()!;
        string release = proof.GetProperty("releaseId").GetString()!;
        await VerifyBoundaryPublicationAsync(fixture, seed.ApplicationSourceId!, seed.ApplicationPublicationId!, BoundaryApplicationPayload);
        await VerifyBoundaryPublicationAsync(fixture, seed.ApplicationSourceId!, concurrent, RefreshConcurrentPayload);
        await VerifyBoundaryPublicationAsync(fixture, seed.ApplicationSourceId!, publication, RefreshDraftPayload);
        await VerifyBoundaryPublicationAsync(fixture, seed.CommonSourceId!, seed.FirstCommonPublicationId!, ManualSharedPayload);
        await VerifyBoundaryPublicationAsync(fixture, seed.CommonSourceId!, seed.SecondCommonPublicationId!, ManualSharedPayload);
        await VerifySavePairAsync(fixture, seed.BindingId!, release, seed.FirstCommonPublicationId, publication);
        await AssertSaveReleaseCountAsync(fixture, seed.BindingId!, 4);
        await AssertSaveMapAsync(fixture, seed.BindingId!, revision: 4, generation: 3, release: seed.CandidateReleaseId!);
        await VerifySaveOriginalsAsync(fixture, issuer,
        [
            new(proof.GetProperty("concurrentKey").GetString()!, "management.configuration.source.publish", seed.ApplicationSourceId!, "sourcePublished", concurrent),
            new(proof.GetProperty("concurrentMapKey").GetString()!, "management.configuration.assignment.replace", seed.BindingId!, "assignmentsReplaced", seed.CandidateReleaseId)
        ], subject: RefreshOperator);
        await VerifySaveOriginalsAsync(fixture, issuer,
        [
            new(proof.GetProperty("publicationKey").GetString()!, "management.configuration.source.publish", seed.ApplicationSourceId!, "sourcePublished", publication),
            new(proof.GetProperty("releaseKey").GetString()!, "management.configuration.release.create", seed.BindingId!, "releaseCreated", release)
        ]);
        IReadOnlyList<InstanceStatusFingerprint> after = await ReadInstanceStatusFingerprintsAsync(fixture);
        foreach (string name in new[] { "configuration-originals", "configuration-audit" })
        {
            Assert.AreEqual(seed.Fingerprints.Single(row => row.Name == name).Rows + 4, after.Single(row => row.Name == name).Rows);
        }
    }
}
