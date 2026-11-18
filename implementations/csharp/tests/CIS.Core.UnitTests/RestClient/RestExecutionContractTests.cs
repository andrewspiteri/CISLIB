using System.Collections;
using CIS.RestClient;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestExecutionContractTests
{
    [TestMethod]
    public void AuthenticationCanWriteOnlyDeclaredCredentialsAndFinalSealingProtectsSignedFields()
    {
        var request = new RestRequestContext("POST", "https://example.test/orders?q=one", new("orders", "create", 0), new(), new(), RestBody.Text("data"));
        request.Headers.Set("X-Version", "one");
        request.FinalizeOrdinary(["Authorization"], ["token"]);
        var authentication = new RestAuthenticationContext(request, (_, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(new byte[] { 1, 2 }));
        authentication.Headers.Set("Authorization", "Bearer test"); authentication.Query.Set("token", "test");
        Assert.ThrowsExactly<InvalidOperationException>(() => request.Url = "https://other.test/");
        Assert.ThrowsExactly<InvalidOperationException>(() => authentication.Headers.Set("X-Version", "two"));
        Assert.ThrowsExactly<InvalidOperationException>(() => authentication.Query.Set("ordinary", "two"));
        request.Seal();
        Assert.ThrowsExactly<InvalidOperationException>(() => authentication.Headers.Remove("Authorization"));
        CollectionAssert.AreEqual(new[] { "one" }, request.Headers.Snapshot()["X-Version"].ToArray());
    }

    [TestMethod]
    public void AppliedOrdinaryQueryIsClearedBeforeCredentialAdditionsWithoutChangingItsRawValues()
    {
        var query = new RestQuery(); query.Set("ordinary", "a b"); query.Append("ordinary", "a%20b");
        IReadOnlyList<RestQueryChange> ordinary = query.CaptureChanges(default);
        query.ClearApplied(); query.RestrictWrites(["credential"]); query.Set("credential", "new value");
        CollectionAssert.AreEqual(new[] { "a b", "a%20b" }, ordinary.Select(change => change.Value).ToArray());
        Assert.AreEqual("credential", query.CaptureChanges(default).Single().Name);
        Assert.ThrowsExactly<InvalidOperationException>(() => query.Append("ordinary", "later"));
    }

    [TestMethod]
    public void PresetIsConservativeWhileExplicitPolicyDataIsCopiedAndBoundedBeforeAccess()
    {
        RestRetryPolicy preset = RestRetryPolicy.TransientHttp;
        Assert.AreEqual(2, preset.MaxRetries); Assert.AreEqual(TimeSpan.FromMilliseconds(500), preset.InitialDelay);
        Assert.AreEqual(TimeSpan.FromSeconds(5), preset.MaxLocalDelay); Assert.IsTrue(preset.UseJitter);
        CollectionAssert.AreEqual(new[] { 408, 429, 502, 503, 504 }, preset.StatusCodes.ToArray());
        CollectionAssert.AreEqual(new[] { "GET", "HEAD", "OPTIONS" }, preset.Methods.ToArray());
        Assert.IsNull(preset.AttemptTimeout); Assert.HasCount(0, preset.FailureKinds); Assert.HasCount(0, preset.TransportFailures);
        string[] codes = ["orders.busy"];
        var custom = new RestRetryPolicy("custom", 1, TimeSpan.Zero, TimeSpan.FromSeconds(1), failureKinds: [RestFailureKind.Business], errorCodes: codes);
        codes[0] = "changed";
        CollectionAssert.AreEqual(new[] { "orders.busy" }, custom.ErrorCodes.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => new RestRetryPolicy("large", 1, TimeSpan.Zero, TimeSpan.Zero, methods: new OversizedMethods()));
    }

    [TestMethod]
    [DataRow("method")]
    [DataRow("retry-count")]
    [DataRow("delay")]
    [DataRow("failure-kind")]
    public void InvalidFinitePolicyInputsFailDuringConstruction(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new RestRetryPolicy("policy", invalid == "retry-count" ? 101 : 1,
            TimeSpan.Zero, invalid == "delay" ? TimeSpan.FromDays(2) : TimeSpan.FromSeconds(1),
            methods: invalid == "method" ? ["GET BAD"] : null,
            failureKinds: invalid == "failure-kind" ? [RestFailureKind.None] : null));
    }

    [TestMethod]
    public void SharedClockBudgetHasOneEntryOriginAndDoesNotResetForPolicyIterationsOrUtcChanges()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 17, 0, 0, 0, TimeSpan.Zero));
        using var cancellation = new CancellationTokenSource();
        var context = new RestRetryExecutionContext(clock, clock.GetTimestamp(), TimeSpan.FromSeconds(10), cancellation.Token, "GET", RestReplaySafety.Default, () => false);
        clock.Advance(TimeSpan.FromSeconds(3)); Assert.AreEqual(TimeSpan.FromSeconds(7), context.Remaining);
        clock.SetUtcNow(new DateTimeOffset(2050, 1, 1, 0, 0, 0, TimeSpan.Zero)); Assert.AreEqual(TimeSpan.FromSeconds(7), context.Remaining);
        clock.Advance(TimeSpan.FromSeconds(8)); Assert.AreEqual(TimeSpan.Zero, context.Remaining);
        Assert.AreSame(clock, context.TimeProvider); Assert.AreEqual(cancellation.Token, context.CancellationToken);
    }

    [TestMethod]
    public void PassiveCompositionPreservesOwnerIdentityAndRejectsOversizedOrMissingFallbackMetadata()
    {
        object identity = new();
        RestOperationIdentity[] pairs = [new("orders", "request"), new("orders", "create")];
        var marker = new RestClientComposition(identity, pairs);
        pairs[0] = new("changed", "request");
        Assert.AreSame(identity, marker.RegistrationIdentity);
        CollectionAssert.AreEqual(new[] { "orders" }, marker.ClientAliases.ToArray());
        Assert.AreEqual(new RestOperationIdentity("orders", "request"), marker.Operations[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new RestClientComposition(identity, [new("orders", "create")]));
        Assert.ThrowsExactly<ArgumentException>(() => new RestClientComposition(identity,
            Enumerable.Range(0, 17).Select(index => new RestOperationIdentity("client-" + index, "request")).ToArray()));
    }

    private sealed class OversizedMethods : IReadOnlyList<string>
    {
        public int Count => 33;
        public string this[int index] => throw new AssertFailedException("Bounded copy must reject before accessing an oversized collection.");
        public IEnumerator<string> GetEnumerator() => throw new AssertFailedException("Untrusted enumeration must not run.");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
