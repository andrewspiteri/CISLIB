using System.Globalization;
using System.Text;
using System.Text.Json;
using CIS.Coordination;
using CIS.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Coordination;

[TestClass]
public sealed class CoordinationContractTests
{
    [TestMethod]
    public void FullKeyUsesOrdinalEqualityAndUnambiguousCanonicalComponents()
    {
        var key = new CoordinationKey("env", "wallet", "compaction", "a|b");
        Assert.AreEqual(key, new CoordinationKey("env", "wallet", "compaction", "a|b"));
        Assert.AreNotEqual(key, new CoordinationKey("ENV", "wallet", "compaction", "a|b"));
        Assert.AreNotEqual(new CoordinationKey("env", "é", "job"), new CoordinationKey("env", "e\u0301", "job"));
        var first = new CoordinationKey("a", "b|c", "job", "resource");
        var second = new CoordinationKey("a|b", "c", "job", "resource");
        Assert.AreNotEqual(first.GetResourceIdentifier(), second.GetResourceIdentifier());
        Assert.AreEqual(64, first.GetResourceIdentifier().Length);
        byte[] bytes = first.GetCanonicalBytes();
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 1, (byte)'a', 0, 0, 0, 3, (byte)'b', (byte)'|', (byte)'c' }, bytes[..12]);
    }

    [TestMethod]
    public void KeyBoundsCountUtf8AndRejectMalformedOrControlTextBeforeStoreAccess()
    {
        string duty = new('é', 64);
        Assert.AreEqual(128, Encoding.UTF8.GetByteCount(new CoordinationKey("env", "scope", duty).DutyId));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationKey("env", "scope", duty + "é"));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationKey("env", "scope", "job", "x\ny"));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationKey("env", "\ud800", "job"));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationKey("env", "scope", " "));
        string environment = string.Concat(Enumerable.Repeat("🦀", 256));
        Assert.AreEqual(1024, Encoding.UTF8.GetByteCount(new CoordinationKey(environment, "scope", "job").Environment));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationKey(environment + "a", "scope", "job"));
    }

    [TestMethod]
    [DataRow(9_007_199_254_740_993L)]
    [DataRow(long.MaxValue)]
    public void GenerationJsonRoundTripsAsStrictDecimalStringBeyondFloatingPointPrecision(long generation)
    {
        CoordinationGrant grant = Grant(generation);
        string json = JsonSerializer.Serialize(grant);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.AreEqual(JsonValueKind.String, document.RootElement.GetProperty("Generation").ValueKind);
        Assert.AreEqual(generation.ToString(CultureInfo.InvariantCulture), document.RootElement.GetProperty("Generation").GetString());
        CoordinationGrant? decoded = JsonSerializer.Deserialize<CoordinationGrant>(json);
        Assert.IsNotNull(decoded);
        Assert.AreEqual(grant, decoded);
        string numeric = json.Replace("\"Generation\":\"" + generation.ToString(CultureInfo.InvariantCulture) + "\"",
            "\"Generation\":" + generation.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.ThrowsExactly<JsonException>(() => JsonSerializer.Deserialize<CoordinationGrant>(numeric));
    }

    [TestMethod]
    public void UsableGrantCannotBeConfusedWithBusyUnavailabilityOrUncertainty()
    {
        CoordinationGrant grant = Grant();
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationAcquireResult(CoordinationAcquireOutcome.Acquired));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationAcquireResult(CoordinationAcquireOutcome.Indeterminate, grant, TimeSpan.FromSeconds(30)));
        var observation = new CoordinationOwnerObservation("other", "process", 3, DateTimeOffset.UtcNow.AddSeconds(30), DateTimeOffset.UtcNow);
        var busy = new CoordinationAcquireResult(CoordinationAcquireOutcome.Busy, observedOwner: observation);
        Assert.IsNull(busy.Grant);
        Assert.AreSame(observation, busy.ObservedOwner);
        Assert.ThrowsExactly<ArgumentException>(() => new ManagedCoordinationResult(CoordinationAcquireOutcome.Unavailable, observedOwner: observation));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationObservation(CoordinationObservationOutcome.NotObserved, grant, TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    [DataRow(CoordinationRejectionReason.AuthorityUnavailable)]
    [DataRow(CoordinationRejectionReason.WrongDatabase)]
    [DataRow(CoordinationRejectionReason.InvalidTransaction)]
    public void AuthorityOrContextFailureDoesNotProveGrantNotCurrent(CoordinationRejectionReason reason)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationMutationResult(CoordinationMutationOutcome.NotCurrent,
            rejectionReason: reason));
        var uncertain = new CoordinationMutationResult(CoordinationMutationOutcome.Indeterminate);
        Assert.AreEqual(CoordinationRejectionReason.None, uncertain.RejectionReason);
    }

    [TestMethod]
    public void CapabilityTargetIsDetachedAndEnvironmentCannotChangeUnderAKey()
    {
        var key = new CoordinationKey("test", "wallet", "compaction");
        string[] capabilities = ["coordination.execute"];
        CoordinationTarget target = CoordinationTarget.ForDuty(key, "wallet-service", capabilities);
        capabilities[0] = "different.permission";
        Assert.AreEqual("coordination.execute", target.Capabilities.Single());
        Assert.AreEqual(key.GetResourceIdentifier(), target.Resource.ResourceIdentifier);
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationTarget(key,
            new CapabilityTarget("wallet-service", "other"), ["coordination.execute"]));
    }

    [TestMethod]
    public void TimingProfileIsCopiedAndRequiresAConservativeRenewalWindow()
    {
        var options = new CoordinationOptions();
        options.Validate();
        CoordinationOptions captured = options.CopyValidated();
        options.LeaseDuration = TimeSpan.FromHours(1);
        Assert.AreEqual(TimeSpan.FromSeconds(30), captured.LeaseDuration);
        captured.RenewalInterval = TimeSpan.FromSeconds(23);
        Assert.ThrowsExactly<ArgumentException>(() => captured.Validate());
        options.MaximumManagedLeases = 1;
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [TestMethod]
    public void TerminationDistinguishesPermanentLossFromNormalRelease()
    {
        var lost = new CoordinationLeaseTermination(CoordinationLeaseState.Lost, CoordinationLossReason.Expired);
        Assert.AreEqual(CoordinationLossReason.Expired, lost.LossReason);
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationLeaseTermination(CoordinationLeaseState.Owned));
        Assert.ThrowsExactly<ArgumentException>(() => new CoordinationLeaseTermination(CoordinationLeaseState.Released, CoordinationLossReason.Expired));
        var refusal = new CoordinationGuardResult(CoordinationRejectionReason.Superseded);
        CoordinationGuardRejectedException error = Assert.ThrowsExactly<CoordinationGuardRejectedException>(refusal.ThrowIfRejected);
        Assert.AreEqual(CoordinationRejectionReason.Superseded, error.Reason);
    }

    private static CoordinationGrant Grant(long generation = 1)
    {
        return new(Guid.Parse("16387f8d-5c26-45b2-b398-7d04244f6d2f"),
            new(new("test", "wallet", "compaction"), "wallet-service", "process", Guid.Parse("96ad53e2-a6ec-4f9e-8e28-36bc29913eb6")),
            generation, new DateTimeOffset(2026, 11, 22, 12, 0, 30, TimeSpan.Zero));
    }
}
