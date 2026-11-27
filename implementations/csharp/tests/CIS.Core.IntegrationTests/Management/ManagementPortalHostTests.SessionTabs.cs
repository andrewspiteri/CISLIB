using System.Text.Json;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    [TestMethod]
    public async Task ActualSharedCookieSignoutAndOperatorSwitchRetireOtherTabsProtectedValuesAndDrafts()
    {
        await RunFixtureAsync(browser: true, sessionTabs: true);
    }

    private static async Task VerifySessionTabsReadOnlyAsync(ManagementHostFixture fixture, JsonElement proof,
        IReadOnlyList<InstanceStatusFingerprint> before)
    {
        Assert.AreEqual(0, proof.GetProperty("mutations").GetInt32());
        foreach (string name in new[]
        {
            "sameSessionDraftPreserved", "pendingProtectedSurfacesHidden", "unavailableStaysMaskedWithDraft",
            "signoutClearedPeer", "operatorSwitchClearedPeer", "heldContextReadJoined"
        })
        {
            Assert.IsTrue(proof.GetProperty(name).GetBoolean(), "The actual shared-cookie browser boundary must prove " + name + ".");
        }

        CollectionAssert.AreEqual(before.ToArray(), (await ReadInstanceStatusFingerprintsAsync(fixture)).ToArray(),
            "Cross-tab identity verification must leave Configuration state, originals and Audit unchanged.");
    }
}
