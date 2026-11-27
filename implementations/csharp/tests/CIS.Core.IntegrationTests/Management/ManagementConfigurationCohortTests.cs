using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Http;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationCatalogTests;
using static CIS.Tests.IntegrationTests.Management.ManagementConfigurationDistributionTests;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("ManagementService")]
[DoNotParallelize]
public sealed class ManagementConfigurationCohortTests
{
    [TestMethod]
    public async Task SelectedLegacyBackendOmitsTheOptionalFeatureAndReturnsUnsupportedOnlyAfterCurrentBindingRead()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "legacy-cohorts-catalog");
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new ManagementConfigurationCohortLegacyBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>())));
        HttpClient legacy = fixture.Client("operator-a", 1);
        using (HttpResponseMessage info = await legacy.GetAsync("service-info"))
        {
            Assert.AreEqual(HttpStatusCode.OK, info.StatusCode);
            ManagementServiceInfo service = ManagementWire.DecodeResponse<ManagementServiceInfo>(await info.Content.ReadAsByteArrayAsync())!;
            Assert.IsFalse(service.Features.Any(feature => feature.OperationId == "getConfigurationCohorts"));
        }
        using (HttpResponseMessage unsupported = await legacy.GetAsync(Cohorts(fixture.ApplicationA)))
        {
            Assert.AreEqual(HttpStatusCode.NotImplemented, unsupported.StatusCode);
            Assert.AreEqual("management.configuration.cohorts.unsupported", (await Decode<ManagementConfigurationProblem>(unsupported)).Code);
        }
        using HttpResponseMessage hidden = await fixture.Client("reader", 1).GetAsync(Cohorts(fixture.ApplicationA));
        Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.AreEqual("management.configuration.not_found", (await Decode<ManagementConfigurationProblem>(hidden)).Code);
    }

    [TestMethod]
    public async Task MalformedCompanionPagesFailClosedWithBoundedDetachedEnumeration()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        await Apply(fixture.Client("operator-a"), await Catalog(fixture.Client("operator-a")), "malformed-cohorts-catalog");
        var scenario = new MalformedCohortScenario();
        await fixture.StartReplicaAsync(configure: builder => builder.Services.AddScoped<IManagementConfigurationBackend>(services =>
            new ManagementConfigurationCohortMalformedBackend((IManagementConfigurationBackend)services.GetRequiredService<IManagementBackend>(), scenario)));
        HttpClient actor = fixture.Client("operator-a", 1);
        ManagementPage<ManagementConfigurationCohortChoice> valid = await Page(actor, Cohorts(fixture.ApplicationA, 1));
        Assert.AreEqual("a", valid.Items.Single().CohortId);
        Assert.IsNotNull(valid.NextCursor);
        foreach (string fault in new[]
        {
            "code", "not-committed", "too-many", "invalid-id", "invalid-label", "duplicate", "descending", "null-row",
            "under-reported-count", "over-reported-count", "negative-count", "not-after"
        })
        {
            scenario.Fault = fault;
            using HttpResponseMessage refused = await actor.GetAsync(Cohorts(fixture.ApplicationA, 1, fault == "not-after" ? valid.NextCursor : null));
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, refused.StatusCode, fault);
            Assert.IsNotNull(ManagementConfigurationWire.DecodeResponse<ManagementConfigurationProblem>(await refused.Content.ReadAsByteArrayAsync()), fault);
            Assert.IsTrue(scenario.EnumeratedRows <= 3, "A malformed collection cannot make the endpoint walk arbitrary provider rows: " + fault);
        }
    }

    [TestMethod]
    public async Task TrustedConfiguredFrozenAndExactBindingPinnedCohortsMatchRealAssignmentAdmissionWithoutMembershipDisclosure()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment(cohort: "frozen"));
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "cohorts-catalog");
        string registration = await Register(fixture.Client("workload-a"));
        await ChangeMapping(fixture, "retained-pin");
        ManagementConfigurationAssignmentMap map = await Map(actor, fixture.ApplicationA);
        string release = (await CreateRelease(actor, fixture.ApplicationA, map.ETag, "cohorts-release",
            new(1, new("absent"), new("absent"), "Keep reviewed defaults"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationA, map.ETag, "cohorts-pin",
            new(1, "replace", release, [new("retained-pin", release)], "Retain the configured cohort"));
        await ChangeMapping(fixture, "configured");
        await SeedMapping(fixture, fixture.ApplicationA, "private-duplicate-member", "configured");
        await SeedMapping(fixture, fixture.ApplicationB, "private-foreign-member", "foreign-only");
        ManagementConfigurationAssignmentMap otherMap = await Map(actor, fixture.ApplicationB);
        string otherRelease = (await CreateRelease(actor, fixture.ApplicationB, otherMap.ETag, "other-release",
            new(1, new("absent"), new("absent"), "Other binding defaults"))).Result!.ReleaseId!;
        await Assign(actor, fixture.ApplicationB, otherMap.ETag, "other-pin",
            new(1, "replace", otherRelease, [new("foreign-only", otherRelease)], "Pin only the other binding"));
        using (HttpResponseMessage closed = await fixture.Client("workload-a").DeleteAsync("registrations/" + registration))
        {
            Assert.AreEqual(HttpStatusCode.OK, closed.StatusCode);
        }

        ManagementPage<ManagementConfigurationCohortChoice> page = await Page(actor, Cohorts(fixture.ApplicationA));
        CollectionAssert.AreEqual(new[] { "configured", "frozen", "retained-pin" }, page.Items.Select(item => item.CohortId).ToArray());
        Assert.IsTrue(page.Items.All(item => item.DisplayName == item.CohortId));
        Assert.IsNull(page.NextCursor);
        Assert.IsFalse(page.Items.Any(item => item.CohortId == "foreign-only"));
        map = await Map(actor, fixture.ApplicationA);
        ManagementConfigurationOperationResult accepted = await Assign(actor, fixture.ApplicationA, map.ETag, "all-discovered-cohorts",
            new(1, "replace", release, page.Items.Select(item => new ManagementConfigurationOverride(item.CohortId, release)).ToArray(),
                "Use only cohorts admitted by this binding"));
        CollectionAssert.AreEquivalent(page.Items.Select(item => item.CohortId).ToArray(),
            accepted.Result!.Assignment!.Overrides.Select(item => item.CohortId).ToArray());
        await RefuseUnknownCohort(actor, fixture.ApplicationA, release, "browser-invented-cohort", "cannot-create-cohort");
        map = await Map(actor, fixture.ApplicationA);
        await Assign(actor, fixture.ApplicationA, map.ETag, "remove-retained-pins",
            new(1, "replace", release, [], "Remove every retained override"));
        CollectionAssert.AreEqual(new[] { "configured", "frozen" },
            (await Page(actor, Cohorts(fixture.ApplicationA))).Items.Select(item => item.CohortId).ToArray());
        await RefuseUnknownCohort(actor, fixture.ApplicationA, release, "retained-pin", "cannot-restore-originless-cohort");
    }

    [TestMethod]
    public async Task NativePagesBindExactBindingActorClaimsAndPageSizeBeforeDisclosingOnlyFiniteChoices()
    {
        await using ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(replicas: 1, configuration: Deployment());
        HttpClient actor = fixture.Client("operator-a");
        await Apply(actor, await Catalog(actor), "paged-cohorts-catalog");
        string[] expected = ["01-canary", "A-cohort", "a-cohort", "canary", "z-cohort"];
        for (int index = 0; index < expected.Length; index++)
        {
            await SeedMapping(fixture, fixture.ApplicationA, "private-page-member-" + index, expected[index]);
        }
        var actual = new List<string>();
        string? cursor = null;
        string? firstCursor = null;
        for (int pageNumber = 0; pageNumber < expected.Length; pageNumber++)
        {
            ManagementPage<ManagementConfigurationCohortChoice> page = await Page(actor, Cohorts(fixture.ApplicationA, 1, cursor));
            Assert.HasCount(1, page.Items);
            actual.Add(page.Items[0].CohortId);
            cursor = page.NextCursor;
            firstCursor ??= cursor;
            Assert.AreEqual(pageNumber + 1 < expected.Length, cursor is not null);
        }
        CollectionAssert.AreEqual(expected, actual.ToArray());
        Assert.IsNotNull(firstCursor);
        foreach ((HttpClient client, string path) in new[]
        {
            (actor, Cohorts(fixture.ApplicationB, 1, firstCursor)),
            (actor, Cohorts(fixture.ApplicationA, 2, firstCursor)),
            (fixture.Client("operator-b"), Cohorts(fixture.ApplicationA, 1, firstCursor)),
            (fixture.Client("operator-a", explicitToken: fixture.Token("operator-a", expiration: DateTimeOffset.UtcNow.AddMinutes(20))),
                Cohorts(fixture.ApplicationA, 1, firstCursor))
        })
        {
            using HttpResponseMessage refused = await client.GetAsync(path);
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        using (HttpResponseMessage hidden = await fixture.Client("reader").GetAsync(Cohorts(fixture.ApplicationA, 1, firstCursor)))
        {
            Assert.AreEqual(HttpStatusCode.NotFound, hidden.StatusCode);
            Assert.AreEqual("management.configuration.not_found", (await Decode<ManagementConfigurationProblem>(hidden)).Code);
        }
        Assert.IsEmpty((await Page(actor, Cohorts(fixture.ApplicationB))).Items, "No configured/frozen/pinned cohort means an empty choice list.");
        Assert.HasCount(expected.Length, (await Page(fixture.Client("operator-b"), Cohorts(fixture.ApplicationA))).Items);
        foreach (string query in new[]
        {
            "schemaVersion=1&schemaVersion=1", "schemaVersion=1&%73chemaVersion=1", "schemaVersion=1&issuer=forged",
            "schemaVersion=1&cohortId=invented", "schemaVersion=1&pageSize=501", "schemaVersion=1&cursor=forged",
            "schemaVersion=%C0%AF", "schemaVersion=1&cursor=%GG", "schemaVersion=1&cursor=%ED%A0%80"
        })
        {
            using HttpResponseMessage refused = await actor.GetAsync(CohortPath(fixture.ApplicationA) + "?" + query);
            Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode, query);
        }
        using var bodyRequest = new HttpRequestMessage(HttpMethod.Get, Cohorts(fixture.ApplicationA))
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new UnknownLengthCohortContent()
        };
        Assert.IsNull(bodyRequest.Content.Headers.ContentLength);
        Assert.AreEqual(0, bodyRequest.Headers.TransferEncoding.Count);
        using HttpResponseMessage bodyRefused = await actor.SendAsync(bodyRequest);
        Assert.AreEqual(HttpVersion.Version20, bodyRefused.Version);
        Assert.AreEqual(HttpStatusCode.BadRequest, bodyRefused.StatusCode);
        Assert.IsNull(bodyRequest.Content.Headers.ContentLength);
        Assert.AreEqual(0, bodyRequest.Headers.TransferEncoding.Count);
    }

    private static string CohortPath(string application) => "applications/" + application + "/configuration/shared.settings/cohorts";
    private static string Cohorts(string application, int size = 100, string? cursor = null)
        => CohortPath(application) + "?schemaVersion=1&pageSize=" + size
            + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));

    private static async Task<ManagementPage<ManagementConfigurationCohortChoice>> Page(HttpClient actor, string path)
    {
        using HttpResponseMessage response = await actor.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        ManagementPage<ManagementConfigurationCohortChoice>? page = ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementConfigurationCohortChoice>>(bytes);
        Assert.IsNotNull(page);
        using JsonDocument document = JsonDocument.Parse(bytes);
        Assert.IsTrue(document.RootElement.EnumerateObject().All(property => property.Name is "items" or "nextCursor"));
        foreach (JsonElement row in document.RootElement.GetProperty("items").EnumerateArray())
        {
            CollectionAssert.AreEquivalent(new[] { "cohortId", "displayName" }, row.EnumerateObject().Select(property => property.Name).ToArray());
        }
        string text = Encoding.UTF8.GetString(bytes);
        Assert.IsFalse(text.Contains(ManagementHostFixture.Issuer, StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("private-", StringComparison.Ordinal));
        return page;
    }

    private static async Task<string> Register(HttpClient workload)
    {
        string registration = Guid.NewGuid().ToString("D");
        using var request = new HttpRequestMessage(HttpMethod.Put, "registrations/" + registration)
        {
            Content = new ByteArrayContent(ManagementWire.EncodeRequest(ManagementHostFixture.Request("frozen-cohort-session")))
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await workload.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return registration;
    }

    private static async Task ChangeMapping(ManagementHostFixture fixture, string cohort)
    {
        await using var command = fixture.Source.CreateCommand($"""
            UPDATE "{fixture.Options.Persistence.Schema}".configuration_cohort SET cohort_id=@cohort
            WHERE application_id=@application AND issuer=@issuer AND subject='workload-a'
            """);
        command.Parameters.AddWithValue("cohort", cohort);
        command.Parameters.AddWithValue("application", Guid.Parse(fixture.ApplicationA));
        command.Parameters.AddWithValue("issuer", ManagementHostFixture.Issuer);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task SeedMapping(ManagementHostFixture fixture, string application, string subject, string cohort)
    {
        // Only this newly owned native fixture's trusted mapping is seeded, never through a caller-defined cohort API.
        await using var command = fixture.Source.CreateCommand($"""
            INSERT INTO "{fixture.Options.Persistence.Schema}".configuration_cohort(application_id,issuer,subject,cohort_id,logical_bytes)
            VALUES(@application,@issuer,@subject,@cohort,256)
            """);
        command.Parameters.AddWithValue("application", Guid.Parse(application));
        command.Parameters.AddWithValue("issuer", ManagementHostFixture.Issuer);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("cohort", cohort);
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task RefuseUnknownCohort(HttpClient actor, string application, string release, string cohort, string key)
    {
        using var forged = new HttpRequestMessage(HttpMethod.Put, BindingPath(application, "assignments"));
        forged.Headers.Add("Idempotency-Key", key);
        forged.Headers.TryAddWithoutValidation("If-Match", (await Map(actor, application)).ETag);
        forged.Content = new ByteArrayContent(ManagementConfigurationWire.EncodeRequest(new ManagementConfigurationAssignmentCommand(
            1, "replace", release, [new(cohort, release)], "Refuse a cohort with no trusted origin")));
        forged.Content.Headers.ContentType = new("application/json");
        using HttpResponseMessage rejected = await actor.SendAsync(forged);
        Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.AreEqual("management.configuration.cohort_unknown", (await Decode<ManagementConfigurationProblem>(rejected)).Code);
    }

    private sealed class UnknownLengthCohortContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            => stream.WriteAsync("{}"u8.ToArray(), cancellationToken).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}

[TestClass]
public sealed class ManagementConfigurationCohortBoundaryTests
{
    [TestMethod]
    [DataRow("?schemaVersion=1&schemaVersion=1")]
    [DataRow("?schemaVersion=1&%73chemaVersion=1")]
    [DataRow("?schemaVersion=1&cursor=%C0%AF")]
    [DataRow("?schemaVersion=1&cursor=%ED%A0%80")]
    [DataRow("?schemaVersion=1&cursor=%GG")]
    [DataRow("?schemaVersion=1&cursor=%")]
    [DataRow("?schemaVersion=1&pageSize=501")]
    [DataRow("?schemaVersion=1&issuer=forged")]
    [DataRow("?schemaVersion=01")]
    [DataRow("?schemaVersion=1&cohortId=invented")]
    public void CohortQueryUsesStrictRawDecodingAndAcceptsOnlyItsFiniteFields(string raw)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new(raw);
        Assert.IsFalse(ManagementDiscoveryQuery.TryReadConfigurationCohorts(context, out _, out _, out _));
    }

    [TestMethod]
    public void CohortCursorUsesExactPortablePositionAndCannotCrossOrderBindingOrAuthority()
    {
        var options = new ManagementServiceOptions("Host=localhost;Database=unused", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var authority = new ManagementAuthoritySnapshot("management", "33333333-3333-4333-8333-333333333333", "v1", 1, false);
        using var cursors = new ManagementCursor(options, () => authority);
        string cursor = cursors.EncodeConfigurationCohort("cohorts.binding-a.1", "current-principal-scope", 1, "A-canary");
        Assert.IsTrue(cursors.TryDecodeConfigurationCohort(cursor, "cohorts.binding-a.1", "current-principal-scope", 1, out string? after));
        Assert.AreEqual("A-canary", after);
        Assert.IsFalse(cursors.TryDecodeConfigurationCohort(cursor, "cohorts.binding-b.1", "current-principal-scope", 1, out _));
        Assert.IsFalse(cursors.TryDecodeConfigurationCohort(cursor, "cohorts.binding-a.2", "current-principal-scope", 1, out _));
        Assert.IsFalse(cursors.TryDecodeConfigurationCohort(cursor, "cohorts.binding-a.1", "other-principal-scope", 1, out _));
        Assert.IsFalse(cursors.TryDecodeConfigurationCohort(cursor, "cohorts.binding-a.1", "current-principal-scope", 2, out _));
        Assert.IsFalse(cursors.TryDecode(cursor, "cohorts.binding-a.1", "current-principal-scope", 1, out _));
        Assert.ThrowsExactly<ArgumentException>(() => cursors.EncodeConfigurationCohort("cohorts", "actor", 1, "../invented"));
        authority = new("management", authority.AuthorityId, "v1", 2, false);
        Assert.IsFalse(cursors.TryDecodeConfigurationCohort(cursor, "cohorts.binding-a.1", "current-principal-scope", 1, out _));
    }
}
