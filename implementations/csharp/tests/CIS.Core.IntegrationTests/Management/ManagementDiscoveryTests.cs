using System.Net;
using System.Diagnostics;
using System.Text.Json;
using CIS.Management.Service.Security;
using CIS.Management;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using CIS.Security;
using Microsoft.Extensions.DependencyInjection;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementDiscoveryTests
{
    [TestMethod]
    public async Task EnvironmentContinuationSkipsHiddenGroupsAndUsesDisclosedUtf8OrderedPositions()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, start: false);
        string[] visibleIds = ["10000000-0000-4000-8000-000000000001", "20000000-0000-4000-8000-000000000001", "30000000-0000-4000-8000-000000000001"];
        await using (var insert = fixture.Source.CreateCommand($"""
            INSERT INTO "{fixture.Options.Persistence.Schema}".management_application(application_id,application,environment,display_name)
                SELECT ('00000000-0000-4000-8000-' || lpad(n::text,12,'0'))::uuid,'hidden-a-' || n,'A visible','Hidden in a visible group'
                FROM generate_series(1,24) n;
            INSERT INTO "{fixture.Options.Persistence.Schema}".management_application(application_id,application,environment,display_name)
                SELECT ('00000000-0000-4000-9000-' || lpad(n::text,12,'0'))::uuid,'hidden-b-' || n,'B hidden','Entirely hidden group'
                FROM generate_series(1,20) n;
            INSERT INTO "{fixture.Options.Persistence.Schema}".management_application(application_id,application,environment,display_name)
                VALUES(@a,'visible-a','A visible','Visible A'),(@b,'visible-b',@bmp,'Visible B'),(@c,'visible-c',@supplementary,'Visible C');
            """))
        {
            insert.Parameters.AddWithValue("a", Guid.Parse(visibleIds[0]));
            insert.Parameters.AddWithValue("b", Guid.Parse(visibleIds[1]));
            insert.Parameters.AddWithValue("c", Guid.Parse(visibleIds[2]));
            insert.Parameters.AddWithValue("bmp", "Z-\uE000");
            insert.Parameters.AddWithValue("supplementary", "Z-\U00010000");
            await insert.ExecuteNonQueryAsync();
        }
        await fixture.StartReplicaAsync(selectSecurity: original => WithAdditionalReadGrants(original, visibleIds));
        HttpClient reader = fixture.Client("reader");
        string[] expected = ["A visible", "Development", "Z-\uE000", "Z-\U00010000"];
        string? cursor = null;
        foreach (string environment in expected)
        {
            string path = "environments?pageSize=1" + (cursor is null ? string.Empty : "&cursor=" + Uri.EscapeDataString(cursor));
            using HttpResponseMessage response = await reader.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            ManagementPage<ManagementEnvironment> page = Decode<ManagementPage<ManagementEnvironment>>(await response.Content.ReadAsByteArrayAsync());
            Assert.AreEqual(environment, page.Items.Single().Environment);
            cursor = page.NextCursor;
            if (environment != expected[^1])
            {
                Assert.IsNotNull(cursor);
                string encoded = cursor.Split('.')[0].Replace('-', '+').Replace('_', '/');
                using JsonDocument payload = JsonDocument.Parse(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
                Assert.AreEqual(environment, payload.RootElement.GetProperty("Last").GetString(),
                    "The server's unsigned cursor body must disclose only the already visible group, never a hidden candidate position.");
            }
        }
        Assert.IsNull(cursor, "The final visible environment has no continuation merely because hidden groups exist.");
    }

    [TestMethod]
    public async Task ExhaustingTheCandidateBudgetReturnsUnavailableWithoutLeakingPartialItemsOrContinuation()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        Guid[] hidden = ApplicationIdsAfter(fixture.ApplicationA, fixture.ApplicationB, 4096);
        await using (var insert = fixture.Source.CreateCommand($"""
            UPDATE "{fixture.Options.Persistence.Schema}".management_application SET display_name='Budget visible' WHERE application_id=@application;
            INSERT INTO "{fixture.Options.Persistence.Schema}".management_application(application_id,application,environment,display_name)
                SELECT id,'budget-hidden-' || n,'Z hidden','Budget hidden'
                FROM unnest(@ids) WITH ORDINALITY AS candidate(id,n);
            """))
        {
            insert.Parameters.AddWithValue("application", Guid.Parse(fixture.ApplicationA));
            insert.Parameters.AddWithValue("ids", hidden);
            await insert.ExecuteNonQueryAsync();
        }
        HttpClient reader = fixture.Client("reader");
        foreach (string path in new[] { "applications?query=Budget&pageSize=500", "environments?pageSize=500" })
        {
            using HttpResponseMessage response = await reader.GetAsync(path);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode, path);
            Assert.AreEqual("management.discovery.scan_limit", Decode<ManagementProblem>(bytes).Code);
            using JsonDocument body = JsonDocument.Parse(bytes);
            Assert.IsFalse(body.RootElement.TryGetProperty("items", out _), "A visible prefix must not escape as a complete or partially successful list.");
            Assert.IsFalse(body.RootElement.TryGetProperty("nextCursor", out _));
        }
    }

    [TestMethod]
    [DataRow("applications?query=managed")]
    [DataRow("environments")]
    public async Task CallerCancellationDrainsTheActualBlockedNativeDiscoveryQuery(string path)
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1);
        HttpClient reader = fixture.Client("reader");
        await using var connection = await fixture.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var hold = connection.CreateCommand())
        {
            hold.Transaction = transaction;
            hold.CommandText = $"LOCK TABLE \"{fixture.Options.Persistence.Schema}\".management_application IN ACCESS EXCLUSIVE MODE";
            await hold.ExecuteNonQueryAsync();
        }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<HttpResponseMessage> pending = reader.GetAsync(path, cancellation.Token);
        try
        {
            Assert.IsTrue(await ObserveBlockedQuery(fixture, expected: true), "The real product SQL must reach the owned table lock before cancellation.");
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                using HttpResponseMessage response = await pending;
            });
            Assert.IsTrue(await ObserveBlockedQuery(fixture, expected: false),
                "Request cancellation must stop the native SQL while the test still holds the lock; releasing it cannot manufacture drainage.");
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                using HttpResponseMessage response = await pending;
            }
            catch (OperationCanceledException)
            {
            }
            await transaction.RollbackAsync();
        }
        using HttpResponseMessage next = await reader.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, next.StatusCode, "The same owner can perform a fresh authorized read after cancellation settles.");
    }

    [TestMethod]
    public async Task QueriesFilterBeforeSqlPagingAndNeverDiscloseHiddenEnvironmentsOrSourceDefaults()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(
            configuration: ManagementConfigurationCatalogTests.Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await ManagementConfigurationCatalogTests.Apply(actor, await ManagementConfigurationCatalogTests.Catalog(actor), "discovery-catalog");
        await using (var update = fixture.Source.CreateCommand($"""
            UPDATE "{fixture.Options.Persistence.Schema}".management_application SET display_name='Target%_ Café' WHERE application_id=@application;
            INSERT INTO "{fixture.Options.Persistence.Schema}".management_application(application_id,application,environment,display_name)
                SELECT ('00000000-0000-4000-8000-' || lpad(n::text,12,'0'))::uuid,'hidden-' || n,'Hidden environment','Other application'
                FROM generate_series(1,32) n;
            """))
        {
            update.Parameters.AddWithValue("application", Guid.Parse(fixture.ApplicationA));
            await update.ExecuteNonQueryAsync();
        }
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("management", _ => fixture.Source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        await using ServiceProvider setup = services.BuildServiceProvider();
        var store = new PostgreSqlManagementStore(setup.GetRequiredService<ILocalTransactionFactory>(), fixture.Options.Persistence);
        ManagementStoreResult<IReadOnlyList<ManagementApplication>> selected = await store.QueryApplicationsAsync(new("Target%_", "Development"), null, 1);
        Assert.IsTrue(selected.IsKnownCommitted, selected.Code);
        Assert.AreEqual(fixture.ApplicationA, selected.Value!.Single().ApplicationId,
            "Literal predicates must run before the SQL LIMIT, not over the first source page.");
        HttpClient reader = fixture.Client("reader");
        using (HttpResponseMessage filtered = await reader.GetAsync("applications?query=Target%25_&environment=Development&pageSize=1"))
        {
            Assert.AreEqual(HttpStatusCode.OK, filtered.StatusCode);
            ManagementPage<ManagementApplication> page = Decode<ManagementPage<ManagementApplication>>(await filtered.Content.ReadAsByteArrayAsync());
            Assert.AreEqual(fixture.ApplicationA, page.Items.Single().ApplicationId);
            Assert.IsNull(page.NextCursor);
        }
        using (HttpResponseMessage differentCase = await reader.GetAsync("applications?query=target"))
        {
            Assert.IsEmpty(Decode<ManagementPage<ManagementApplication>>(await differentCase.Content.ReadAsByteArrayAsync()).Items);
        }
        using (HttpResponseMessage environments = await reader.GetAsync("environments?pageSize=1"))
        {
            Assert.AreEqual(HttpStatusCode.OK, environments.StatusCode);
            ManagementPage<ManagementEnvironment> page = Decode<ManagementPage<ManagementEnvironment>>(await environments.Content.ReadAsByteArrayAsync());
            Assert.AreEqual("Development", page.Items.Single().Environment);
            Assert.IsNull(page.NextCursor, "Hidden environment groups must create neither items nor continuation evidence.");
        }
        using (HttpResponseMessage definitions = await actor.GetAsync("applications/" + fixture.ApplicationA + "/configuration-definitions"))
        {
            Assert.AreEqual(HttpStatusCode.OK, definitions.StatusCode);
            ManagementPage<ManagementConfigurationDefinition> page = await ManagementConfigurationCatalogTests.Decode<ManagementPage<ManagementConfigurationDefinition>>(definitions);
            ManagementConfigurationDefinition definition = page.Items.Single();
            Assert.AreEqual(fixture.ApplicationA, definition.ApplicationId);
            Assert.AreEqual("Development", definition.Environment);
            Assert.AreEqual("application", definition.Kind);
            Assert.IsNull(definition.DefaultsJson, "This metadata-only route omits defaults even for an actor with ContentRead.");
        }
        using (HttpResponseMessage deniedSources = await reader.GetAsync("applications/" + fixture.ApplicationA + "/configuration-definitions"))
        {
            Assert.AreEqual(HttpStatusCode.OK, deniedSources.StatusCode);
            Assert.IsEmpty((await ManagementConfigurationCatalogTests.Decode<ManagementPage<ManagementConfigurationDefinition>>(deniedSources)).Items);
        }
        using (HttpResponseMessage hiddenApplication = await reader.GetAsync("applications/" + fixture.ApplicationB + "/configuration-definitions"))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hiddenApplication.StatusCode);
        }
        foreach (string invalid in new[] { "applications?query=%C0%AF", "applications?query=first&%71uery=second", "applications?environment=", "environments?query=Target" })
        {
            using HttpResponseMessage response = await reader.GetAsync(invalid);
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, invalid);
        }
    }

    private static T Decode<T>(byte[] bytes) where T : class
        => ManagementWire.DecodeResponse<T>(bytes) ?? throw new AssertFailedException("Expected a bounded Management response.");

    private static ManagementSecurityOptions WithAdditionalReadGrants(ManagementSecurityOptions original, IReadOnlyList<string> applicationIds)
        => new()
        {
            Mode = original.Mode,
            Profile = original.Profile,
            AuthenticationScheme = original.AuthenticationScheme,
            BusinessAddress = original.BusinessAddress,
            BusinessPort = original.BusinessPort,
            OperationalPort = original.OperationalPort,
            ServerCertificateFile = original.ServerCertificateFile,
            ServerPrivateKeyFile = original.ServerPrivateKeyFile,
            ServerTrustBundleFile = original.ServerTrustBundleFile,
            CertificateRevocation = original.CertificateRevocation,
            BorrowedNativeKeyScope = original.BorrowedNativeKeyScope,
            PolicyVersion = original.PolicyVersion,
            IdentityMappings = original.IdentityMappings,
            NativeProvider = original.NativeProvider,
            Grants = original.Grants.Concat(applicationIds.Select(id => new CapabilityGrant(original.Profile,
                "management.applications.read", EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                subject: new(ManagementHostFixture.Issuer, "reader"), resourceSelection: CapabilityResourceSelection.ExactResource,
                resourceType: "management.application", resourceIdentifier: id))).ToArray()
        };

    private static Guid[] ApplicationIdsAfter(string application, string excluded, int count)
    {
        byte[] position = Convert.FromHexString(application.Replace("-", string.Empty, StringComparison.Ordinal));
        var ids = new List<Guid>(count);
        while (ids.Count < count)
        {
            int index = position.Length - 1;
            while (index >= 0 && position[index] == byte.MaxValue)
            {
                position[index] = 0;
                index--;
            }
            Assert.IsTrue(index >= 0, "This fixture needs finite UUID space after its admitted application.");
            position[index]++;
            var next = new Guid(position, bigEndian: true);
            if (next.ToString("D") != excluded)
            {
                ids.Add(next);
            }
        }
        return ids.ToArray();
    }

    private static async Task<bool> ObserveBlockedQuery(ManagementHostFixture fixture, bool expected)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await using var query = fixture.Source.CreateCommand("""
                SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid<>pg_backend_pid()
                    AND state='active' AND wait_event_type='Lock' AND strpos(query,@table)>0
                    AND strpos(query,'SELECT application_id,application,environment,display_name,description,metadata_revision')>0)
                """);
            query.Parameters.AddWithValue("table", "\"" + fixture.Options.Persistence.Schema + "\".\"management_application\"");
            bool blocked = (bool)(await query.ExecuteScalarAsync())!;
            if (blocked == expected)
            {
                return true;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        return false;
    }
}
