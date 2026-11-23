using CIS.Management.Service;
using CIS.Management.Service.Http;
using CIS.Security;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
public sealed class ManagementBoundaryTests
{
    [TestMethod]
    public void ActorScopePreservesExactUnicodeAndUnambiguousResourceAndIssuerIdentity()
    {
        const string applicationId = "11111111-1111-4111-8111-111111111111";
        string scope = ManagementActorScope.Create(Caller(subject: "caf\u00e9"), applicationId);
        Assert.AreNotEqual(scope, ManagementActorScope.Create(Caller(subject: "cafe\u0301"), applicationId));
        Assert.AreNotEqual(scope, ManagementActorScope.Create(Caller(subject: "caf\u00e9", issuer: "other"), applicationId));
        Assert.AreNotEqual(scope, ManagementActorScope.Create(Caller(subject: "caf\u00e9"), "22222222-2222-4222-8222-222222222222"));
        Assert.AreNotEqual(ManagementActorScope.Create(Caller(subject: "ab", issuer: "c")),
            ManagementActorScope.Create(Caller(subject: "a", issuer: "bc")));
        StringAssert.StartsWith(scope, "management.actor.v1.");
    }

    [TestMethod]
    public void ActorScopeExcludesCredentialPolicyInstanceAndProfileAliasesButIncludesValidatedHumanDelegation()
    {
        CallerContext original = Caller();
        var rotated = new CallerContext("renamed-profile", "native", original.Subject, original.ActorKind,
            original.Application, original.Environment, new(DateTimeOffset.UtcNow.AddMinutes(1),
                DateTimeOffset.UtcNow.AddHours(1), "rotated-credential"), "policy-v2", new("other-instance"));
        Assert.AreEqual(ManagementActorScope.Create(original), ManagementActorScope.Create(rotated));
        var delegated = new CallerContext(original.Profile, "native", original.Subject, original.ActorKind,
            original.Application, original.Environment, original.Authentication, original.PolicyVersion,
            humanSubject: new("human-issuer", "operator"), verifiedDelegation: "validated-chain");
        Assert.AreNotEqual(ManagementActorScope.Create(original), ManagementActorScope.Create(delegated));
        var changedDelegation = new CallerContext(original.Profile, "native", original.Subject, original.ActorKind,
            original.Application, original.Environment, original.Authentication, original.PolicyVersion,
            humanSubject: new("human-issuer", "operator"), verifiedDelegation: "other-validated-chain");
        Assert.AreNotEqual(ManagementActorScope.Create(delegated), ManagementActorScope.Create(changedDelegation));
    }

    [TestMethod]
    public void CursorIsPortableBetweenReplicasAndBindsActorEndpointResourcePageSizeAndLastReturnedItem()
    {
        byte[] key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var options = new ManagementServiceOptions("Host=localhost;Database=unused", key);
        using var replicaOne = new ManagementCursor(options);
        using var replicaTwo = new ManagementCursor(options);
        const string last = "11111111-1111-4111-8111-111111111111";
        string actor = ManagementActorScope.Create(Caller(), last);
        string cursor = replicaOne.Encode("inventory", actor, 100, last);
        Assert.IsTrue(replicaTwo.TryDecode(cursor, "inventory", actor, 100, out string? after));
        Assert.AreEqual(last, after);
        Assert.IsFalse(replicaTwo.TryDecode(cursor, "instances", actor, 100, out _));
        Assert.IsFalse(replicaTwo.TryDecode(cursor, "inventory", actor, 101, out _));
        Assert.IsFalse(replicaTwo.TryDecode(cursor, "inventory", ManagementActorScope.Create(Caller(subject: "other"), last), 100, out _));
        Assert.IsFalse(replicaTwo.TryDecode(cursor, "inventory", ManagementActorScope.Create(Caller(), null), 100, out _));
        char replacement = cursor[0] == 'a' ? 'b' : 'a';
        Assert.IsFalse(replicaTwo.TryDecode(replacement + cursor[1..], "inventory", actor, 100, out _));
        Assert.IsFalse(replicaTwo.TryDecode(new string('a', 2049), "inventory", actor, 100, out _));
        using var otherDeployment = new ManagementCursor(new("Host=localhost", new byte[32]));
        Assert.IsFalse(otherDeployment.TryDecode(cursor, "inventory", actor, 100, out _));
    }

    [TestMethod]
    public void DeploymentOptionsDetachSecretsAndRejectUnboundedOrAmbiguousPrefixes()
    {
        byte[] key = new byte[32];
        var options = new ManagementServiceOptions("Host=localhost", key, basePath: "/platform/service");
        key[0] = 255;
        Assert.AreEqual(0, options.CopyCursorSigningKey()[0]);
        Assert.AreEqual("/platform/service", options.BasePath);
        foreach (string invalid in new[] { "/", "/a/", "/a/../b", "/a//b", "/a%2fb", "/a?b", "relative", "/\u00e9" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new ManagementServiceOptions("Host=localhost", key, basePath: invalid));
        }
        Assert.ThrowsExactly<ArgumentException>(() => new ManagementServiceOptions("Host=localhost", new byte[31]));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ManagementServiceOptions("Host=localhost", key, maximumConcurrentRequests: 257));
    }

    private static CallerContext Caller(string issuer = "fixture-issuer", string subject = "workload")
        => new("management-security", "native", new(issuer, subject), SecurityActorKind.Application,
            "managed-service", "Development", new(DateTimeOffset.UtcNow), "policy-v1");
}
