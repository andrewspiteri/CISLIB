using System.Security.Claims;
using CIS.Management;
using CIS.Management.Service;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Http;
using CIS.Management.Wire;
using CIS.Security;
using Microsoft.AspNetCore.Http;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
public sealed class ManagementDiscoveryBoundaryTests
{
    [TestMethod]
    public void ApiDiscoveryHasAnIndependentDefaultAndSignedOperationPosition()
    {
        var context = new DefaultHttpContext();
        Assert.IsTrue(ManagementDiscoveryQuery.TryReadApiFeatures(context, out int size, out string? cursor));
        Assert.AreEqual(32, size);
        Assert.IsNull(cursor);
        foreach (string query in new[] { "?pageSize=65", "?pageSize=100", "?pageSize=0", "?pageSize=1&pageSize=2", "?profile=complete-v1", "?cursor=%C0%AF" })
        {
            context.Request.QueryString = new(query);
            Assert.IsFalse(ManagementDiscoveryQuery.TryReadApiFeatures(context, out _, out _), query);
        }
        var options = new ManagementServiceOptions("Host=localhost;Database=unused", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var authority = new ManagementAuthoritySnapshot("management", "33333333-3333-4333-8333-333333333333", "v1", 1, false);
        using var cursors = new ManagementCursor(options, () => authority);
        string scope = ManagementDiscoveryQuery.Scope(Caller("policy-v1"), principal: Principal("reader", "one")) + ":complete-v1:inventory-a";
        string encoded = cursors.EncodeApiFeature("service-features", scope, 1, "listApplications");
        Assert.IsTrue(cursors.TryDecodeApiFeature(encoded, "service-features", scope, 1, out string? after));
        Assert.AreEqual("listApplications", after);
        Assert.IsFalse(cursors.TryDecodeApiFeature(encoded, "application-api-capabilities", scope, 1, out _));
        Assert.IsFalse(cursors.TryDecodeApiFeature(encoded, "service-features", scope + "changed-inventory", 1, out _));
        Assert.IsFalse(cursors.TryDecodeApiFeature(encoded, "service-features", scope, 2, out _));
        Assert.IsFalse(cursors.TryDecode(encoded, "service-features", scope, 1, out _));
        string applicationScope = ManagementDiscoveryQuery.Scope(Caller("policy-v1"), "11111111-1111-4111-8111-111111111111",
            principal: Principal("reader", "one")) + ":complete-v1:inventory-a";
        string anotherApplication = ManagementDiscoveryQuery.Scope(Caller("policy-v1"), "22222222-2222-4222-8222-222222222222",
            principal: Principal("reader", "one")) + ":complete-v1:inventory-a";
        string applicationCursor = cursors.EncodeApiFeature("application-api-capabilities", applicationScope, 1, "listApplications");
        Assert.IsFalse(cursors.TryDecodeApiFeature(applicationCursor, "application-api-capabilities", anotherApplication, 1, out _));
        authority = authority with
        {
            RecoveryEpoch = 2
        };
        Assert.IsFalse(cursors.TryDecodeApiFeature(encoded, "service-features", scope, 1, out _));
    }

    [TestMethod]
    [DataRow("?query=%C0%AF")]
    [DataRow("?query=%E2%82")]
    [DataRow("?query=%ED%A0%80")]
    [DataRow("?query=%GG")]
    [DataRow("?query=%")]
    [DataRow("?query=one&%71uery=two")]
    [DataRow("?environment=")]
    [DataRow("?query=%00")]
    [DataRow("?query=target&other=value")]
    [DataRow("?pageSize=0")]
    public void DiscoveryRejectsMalformedEscapesUtf8DuplicatesAndUnknownFields(string raw)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new(raw);
        Assert.IsFalse(ManagementDiscoveryQuery.TryRead(context, true, out _, out _, out _));
    }

    [TestMethod]
    public void DiscoveryPreservesExactUnicodeSpacesAndLiteralWildcardCharacters()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new("?query=+Caf%C3%A9%25_+&environment=Prod%2BBlue&pageSize=1&cursor=opaque");
        Assert.IsTrue(ManagementDiscoveryQuery.TryRead(context, true, out ManagementApplicationQuery query, out int size, out string? cursor));
        Assert.AreEqual(" Café%_ ", query.Query);
        Assert.AreEqual("Prod+Blue", query.Environment);
        Assert.AreEqual(1, size);
        Assert.AreEqual("opaque", cursor);
        Assert.IsFalse(ManagementDiscoveryQuery.TryRead(context, false, out _, out _, out _));
    }

    [TestMethod]
    public void QueryCursorBindsPolicyFiltersAndValidatedClaimsWithoutChangingRecoveryActor()
    {
        var original = Caller("policy-v1");
        var changedPolicy = Caller("policy-v2");
        ClaimsPrincipal first = Principal("reader", "one");
        ClaimsPrincipal reordered = Principal("reader", "one", reverse: true);
        ClaimsPrincipal changedRole = Principal("operator", "one");
        string scope = ManagementDiscoveryQuery.Scope(original, query: new("Target", "Prod"), principal: first);
        Assert.AreEqual(scope, ManagementDiscoveryQuery.Scope(original, query: new("Target", "Prod"), principal: reordered));
        Assert.AreNotEqual(scope, ManagementDiscoveryQuery.Scope(changedPolicy, query: new("Target", "Prod"), principal: first));
        Assert.AreNotEqual(scope, ManagementDiscoveryQuery.Scope(original, query: new("target", "Prod"), principal: first));
        Assert.AreNotEqual(scope, ManagementDiscoveryQuery.Scope(original, query: new("Target", "prod"), principal: first));
        Assert.AreNotEqual(scope, ManagementDiscoveryQuery.Scope(original, query: new("Target", "Prod"), principal: changedRole));
        Assert.AreEqual(ManagementActorScope.Create(original), ManagementActorScope.Create(changedPolicy));
        var options = new ManagementServiceOptions("Host=localhost;Database=unused", Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var authority = new ManagementAuthoritySnapshot("management", "33333333-3333-4333-8333-333333333333", "v1", 1, false);
        using var cursors = new ManagementCursor(options, () => authority);
        string cursor = cursors.EncodeEnvironment("environments", scope, 1, "Prod+Blue");
        Assert.IsTrue(cursors.TryDecodeEnvironment(cursor, "environments", scope, 1, out string? after));
        Assert.AreEqual("Prod+Blue", after);
        Assert.IsFalse(cursors.TryDecode(cursor, "environments", scope, 1, out _));
        Assert.IsFalse(cursors.TryDecodeEnvironment(cursor, "environments", ManagementDiscoveryQuery.Scope(changedPolicy), 1, out _));
        Assert.IsFalse(cursors.TryDecodeEnvironment(cursor, "environments", scope, 2, out _));
    }

    [TestMethod]
    public void EnvironmentWireDetachesPagesAndRejectsInvalidScalarValues()
    {
        var mutable = new List<ManagementEnvironment> { new("Prod") };
        byte[] bytes = ManagementWire.EncodeResponse(new ManagementPage<ManagementEnvironment>(mutable));
        mutable.Clear();
        ManagementPage<ManagementEnvironment>? page = ManagementWire.DecodeResponse<ManagementPage<ManagementEnvironment>>(bytes);
        Assert.IsNotNull(page);
        Assert.AreEqual("Prod", page.Items.Single().Environment);
        Assert.IsNull(ManagementWire.DecodeResponse<ManagementEnvironment>("{\"environment\":\"\"}"u8.ToArray()));
    }

    private static CallerContext Caller(string policy)
        => new("management-security", "native", new("issuer", "person"), SecurityActorKind.User, null, null,
            new(DateTimeOffset.UtcNow), policy);

    private static ClaimsPrincipal Principal(string role, string ticket, bool reverse = false)
    {
        Claim[] claims = [new("role", role), new("jti", ticket)];
        return new(new ClaimsIdentity(reverse ? claims.Reverse() : claims, "validated-native"));
    }
}
