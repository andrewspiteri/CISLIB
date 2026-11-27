using System.Globalization;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service.Configuration;
using CIS.Security;
using Npgsql;

namespace CIS.Tests.IntegrationTests.Management;

public sealed partial class ManagementPortalHostTests
{
    private const int ConfigurationMenuCount = 20;

    [TestMethod]
    public async Task ActualConfigurationListRetiresRowReadersAcrossTwentyNativeEntriesAndRepeatedMenus()
    {
        await RunFixtureAsync(browser: true, configurationMenus: true);
    }

    private sealed record ConfigurationMenuRow(string ConfigurationId, string SchemaVersion, string BindingId, string SourceId);

    private sealed record ConfigurationMenuSeed(string ApplicationId, IReadOnlyList<ConfigurationMenuRow> Rows);

    private static ManagementConfigurationManifest WithConfigurationMenuInventory(ManagementConfigurationManifest manifest)
    {
        ManagementConfigurationSchema original = manifest.Definitions.Single(row => row.ConfigurationId == "shared.settings");
        string[] additionalIds = Enumerable.Range(1, ConfigurationMenuCount - 1).Select(ConfigurationMenuId).ToArray();
        return manifest with
        {
            Definitions = [.. manifest.Definitions, .. additionalIds.Select(id => original with { ConfigurationId = id })],
            Bindings = [.. manifest.Bindings, .. additionalIds.Select(id =>
                new ManagementConfigurationBinding("managed-a", "Development", "MenuHost", id, "1"))]
        };
    }

    private static string ConfigurationMenuId(int index)
    {
        return "portal.menu." + index.ToString("D2", CultureInfo.InvariantCulture);
    }

    private static ConfigurationMenuSeed CaptureConfigurationMenuSeed(ManagementHostFixture fixture,
        IReadOnlyList<ManagementConfigurationDefinition> definitions)
    {
        ConfigurationMenuRow[] rows = definitions
            .Where(row => row.Kind == "application" && row.ApplicationId == fixture.ApplicationA)
            .OrderBy(row => row.ConfigurationId, StringComparer.Ordinal)
            .Select(row => new ConfigurationMenuRow(row.ConfigurationId, row.SchemaVersion, row.BindingId!, row.SourceId))
            .ToArray();
        Assert.HasCount(ConfigurationMenuCount, rows);
        CollectionAssert.AreEquivalent(Enumerable.Range(1, ConfigurationMenuCount - 1).Select(ConfigurationMenuId)
            .Append("shared.settings").ToArray(), rows.Select(row => row.ConfigurationId).ToArray());
        Assert.IsTrue(rows.All(row => row.SchemaVersion == "1" && Guid.TryParseExact(row.BindingId, "D", out _)
            && Guid.TryParseExact(row.SourceId, "D", out _)));
        return new(fixture.ApplicationA, rows);
    }

    private static IEnumerable<CapabilityGrant> ConfigurationMenuGrants(string issuer, ConfigurationMenuSeed seed)
    {
        foreach (ConfigurationMenuRow row in seed.Rows.Where(row => row.ConfigurationId != "shared.settings"))
        {
            yield return ResourceGrant(issuer, OperatorA, ManagementConfigurationCapabilities.DefinitionsRead,
                ManagementConfigurationCapabilities.SourceResource, row.SourceId);
            yield return ResourceGrant(issuer, OperatorA, ManagementConfigurationCapabilities.DefinitionsRead,
                ManagementConfigurationCapabilities.BindingResource, row.BindingId);
        }
    }

    private static async Task<IReadOnlyList<InstanceStatusFingerprint>> ReadConfigurationMenuFingerprintsAsync(ManagementHostFixture fixture)
    {
        var fingerprints = new List<InstanceStatusFingerprint>(await ReadInstanceStatusFingerprintsAsync(fixture));
        foreach (string table in new[] { "configuration_definition", "configuration_binding", "configuration_source", "configuration_catalog" })
        {
            await using NpgsqlCommand command = fixture.Source.CreateCommand(
                $"SELECT row_to_json(t)::text FROM \"{fixture.Options.Persistence.Schema}\".\"{table}\" t ORDER BY 1 LIMIT 257");
            fingerprints.Add(await FingerprintRowsAsync(table, command));
        }
        return fingerprints;
    }

    private static async Task VerifyConfigurationMenusAsync(ManagementHostFixture fixture, JsonElement proof,
        IReadOnlyList<InstanceStatusFingerprint> before, ConfigurationMenuSeed seed)
    {
        Assert.AreEqual(seed.ApplicationId, proof.GetProperty("applicationId").GetString());
        CollectionAssert.AreEqual(seed.Rows.Select(row => row.ConfigurationId).ToArray(),
            proof.GetProperty("configurationIds").EnumerateArray().Select(row => row.GetString()!).ToArray());
        Assert.AreEqual(0, proof.GetProperty("closedMenuReads").GetInt32());
        Assert.AreEqual(0, proof.GetProperty("mutations").GetInt32());
        Assert.AreEqual(2, proof.GetProperty("cycles").GetInt32());
        Assert.AreEqual(ConfigurationMenuCount * 2 + 1, proof.GetProperty("rowReadCount").GetInt32());
        Assert.IsTrue(proof.GetProperty("filterRestored").GetBoolean());
        Assert.IsTrue(proof.GetProperty("escapeReturnsFocus").GetBoolean());
        string[] reads = proof.GetProperty("readConfigurationIds").EnumerateArray().Select(row => row.GetString()!).ToArray();
        CollectionAssert.AreEqual(seed.Rows.Select(row => row.ConfigurationId)
            .Concat(seed.Rows.Select(row => row.ConfigurationId)).Append(seed.Rows[0].ConfigurationId).ToArray(), reads);
        CollectionAssert.AreEqual(before.ToArray(), (await ReadConfigurationMenuFingerprintsAsync(fixture)).ToArray(),
            "Read-only Configuration menus must leave native catalog, sources, assignment, originals and Audit unchanged.");
    }
}
