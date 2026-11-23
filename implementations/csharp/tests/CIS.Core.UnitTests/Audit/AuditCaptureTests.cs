using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditCaptureTests
{
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-11-23T12:34:56.1234567Z");

    [TestMethod]
    public async Task CaptureOwnsSnapshotAndCapturesIdentityAndTimeOnce()
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        var clock = new CountingClock();
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), clock, budget);
        var accessor = new CallerAccessor { Current = Caller(SecurityActorKind.Application) };
        IAuditCapture capture = new AuditCapture(engine, accessor);
        var details = new List<string> { "12345678901234567890.0100" };
        var codec = new StringListCodec();
        var definition = new AuditEventDefinition<List<string>>("ledger.applied", 1, AuditCategory.Business, codec);

        CapturedAuditRecord captured = capture.Capture(definition, details, new(AuditOutcome.Succeeded,
            target: new("resource-owner", "test", "ledger", "ledger-1"), reason: new("approved", "Selected explanation"),
            operationReference: new("ledger.apply", "tenant-one", "opaque-key"), correlationId: "correlation",
            causationAuditId: Guid.Parse("22222222-2222-4222-8222-222222222222"), domainEventId: "domain-1"));
        byte[] original = captured.OriginalBytes.ToArray();
        details[0] = "changed";
        accessor.Current = null;
        clock.Advance(TimeSpan.FromHours(1));

        Assert.AreEqual(1, clock.Reads);
        Assert.AreEqual(1, codec.Validations);
        Assert.AreEqual(ObservedAt, captured.Record.RecordedAtUtc);
        Assert.AreEqual(captured.Record.RecordedAtUtc, captured.Record.OccurredAtUtc);
        Assert.AreEqual(EnvironmentOptions.ApplicationName, captured.Record.Producer.Application);
        Assert.AreEqual(EnvironmentOptions.EnvironmentName, captured.Record.Producer.Environment);
        Assert.AreEqual(AuditInstanceKind.Reported, captured.Record.Producer.Instance!.Kind);
        Assert.AreEqual(EnvironmentOptions.InstanceId, captured.Record.Producer.Instance.ReportedIdentifier);
        Assert.AreEqual(AuditActorKind.Application, captured.Record.Actor.Kind);
        Assert.AreEqual("caller-app", captured.Record.Actor.Application);
        Assert.AreEqual("issuer", captured.Record.Actor.Subject!.Issuer);
        Assert.AreEqual("human-issuer", captured.Record.Actor.HumanSubject!.Issuer);
        Assert.AreEqual("delegation-proof", captured.Record.Actor.VerifiedDelegation);
        Assert.AreEqual("reported", captured.Record.Actor.Instance!.ReportedIdentifier);
        Assert.AreEqual("verified", captured.Record.Actor.Instance.VerifiedIdentifier);
        Assert.AreEqual("resource-owner", captured.Record.Target!.Application);
        CollectionAssert.AreEqual(original, new AuditRecordCodec().Encode(captured.Record));
        StringAssert.Contains(Encoding.UTF8.GetString(original), "2026-11-23T12:34:56.1234567Z");
        StringAssert.Contains(Encoding.UTF8.GetString(captured.Record.DetailsJson.Span), "12345678901234567890.0100");
        Assert.AreEqual(captured.AuditId, captured.Record.AuditId);
    }

    [TestMethod]
    public async Task MissingCallerStaysUnknownAndSystemCaptureIsExplicit()
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), new CountingClock(), budget);
        var accessor = new CallerAccessor();
        IAuditCapture capture = new AuditCapture(engine, accessor);
        var definition = Definition();

        CapturedAuditRecord unknown = capture.Capture(definition, []);
        accessor.Current = Caller(SecurityActorKind.User);
        CapturedAuditRecord user = capture.Capture(definition, []);
        CapturedAuditRecord system = capture.CaptureLocalSystem(definition, []);

        Assert.AreEqual(AuditActorKind.Unknown, unknown.Record.Actor.Kind);
        Assert.IsNull(unknown.Record.Actor.Subject);
        Assert.AreEqual(AuditOutcome.Unknown, unknown.Record.Outcome);
        Assert.AreEqual(AuditActorKind.User, user.Record.Actor.Kind);
        Assert.AreEqual(AuditActorKind.System, system.Record.Actor.Kind);
        Assert.IsNull(system.Record.Actor.Subject);
        Assert.IsNull(system.Record.Actor.HumanSubject);
        Assert.AreEqual(EnvironmentOptions.ApplicationName, system.Record.Actor.Application);
        Assert.AreNotEqual(unknown.AuditId, user.AuditId);
        Assert.AreNotEqual(user.AuditId, system.AuditId);
    }

    [TestMethod]
    public async Task SelectedSchemaRequiredFactsAndOriginalCodecFaultsRemainObservable()
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), new CountingClock(), budget);
        var required = new AuditEventDefinition<List<string>>("ledger.applied", 1, AuditCategory.Business, new StringListCodec(),
            requireTarget: true, requireReason: true, requireOperationReference: true);
        Assert.ThrowsExactly<ArgumentException>(() => engine.Capture(required, [], new(), null));

        var fault = new InvalidOperationException("original selected-codec fault");
        var throwing = new AuditEventDefinition<List<string>>("ledger.applied", 1, AuditCategory.Business, new ThrowingCodec(fault));
        Exception observed = Assert.ThrowsExactly<InvalidOperationException>(() => engine.Capture(throwing, [], null, null));
        Assert.AreSame(fault, observed);
        var array = new AuditEventDefinition<List<string>>("ledger.applied", 1, AuditCategory.Business, new ArrayCodec());
        Assert.ThrowsExactly<ArgumentException>(() => engine.Capture(array, [], null, null));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        OperationCanceledException cancellation = Assert.ThrowsExactly<OperationCanceledException>(
            () => engine.Capture(Definition(), [], null, null, cancellationToken: cancelled.Token));
        Assert.AreEqual(cancelled.Token, cancellation.CancellationToken);
    }

    [TestMethod]
    public async Task CaptureAdmitsSharedBytesNonblockingAndReleasesThemAfterFailure()
    {
        MessagingSettings settings = new MessagingOptions().Capture();
        await using var budget = new MessagingResourceBudget(settings);
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), new CountingClock(), budget);
        Assert.IsTrue(budget.TryAcquireBytes(checked((int)settings.MaximumEncodedBufferBytes), out IDisposable? held));
        try
        {
            AuditCaptureException refusal = Assert.ThrowsExactly<AuditCaptureException>(() => engine.Capture(Definition(), [], null, null));
            Assert.AreEqual(AuditCaptureFailure.Overloaded, refusal.Failure);
            Assert.AreNotEqual(Guid.Empty, refusal.AuditId);
        }
        finally
        {
            held!.Dispose();
        }
        CapturedAuditRecord captured = engine.Capture(Definition(), [], null, null);
        Assert.AreNotEqual(Guid.Empty, captured.AuditId);
        Assert.IsTrue(budget.TryAcquireBytes(checked((int)settings.MaximumEncodedBufferBytes), out IDisposable? allBytes));
        allBytes!.Dispose();
    }

    [TestMethod]
    public async Task PayloadLimitRejectsWithoutTruncationAndARecordAtTheSelectedBoundFits()
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        var clock = new CountingClock();
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), clock, budget);
        AuditEventDefinition<List<string>> definition = Definition();
        CapturedAuditRecord first = engine.Capture(definition, ["amount"], null, null);
        int exactLength = first.OriginalBytes.Length;
        await using var exact = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(exactLength), clock, budget);

        CapturedAuditRecord fitted = exact.Capture(definition, ["amount"], null, null);

        Assert.AreEqual(exactLength, fitted.OriginalBytes.Length);
        await using var smaller = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(exactLength - 1), clock, budget);
        Assert.ThrowsExactly<ArgumentException>(() => smaller.Capture(definition, ["amount"], null, null));
        Assert.ThrowsExactly<ArgumentException>(() => exact.Capture(definition, [new string('a', exactLength * 2)], null, null));
        CapturedAuditRecord next = exact.Capture(definition, ["amount"], null, null);
        Assert.AreEqual(exactLength, next.OriginalBytes.Length);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AuditLimits(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AuditLimits(1_048_577));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new AuditLimits(maximumJsonDepth: 33));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LongStringEncoderHintsDoNotRejectAnOtherwiseValidExactPayload(bool unicode)
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        var clock = new CountingClock();
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), clock, budget);
        string value = unicode ? string.Concat(Enumerable.Repeat("😀", 6000)) : "<" + new string('a', 32_000);
        AuditEventDefinition<List<string>> definition = Definition();
        CapturedAuditRecord first = engine.Capture(definition, [value], null, null);
        int length = first.OriginalBytes.Length;
        await using var exact = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(length), clock, budget);

        CapturedAuditRecord captured = exact.Capture(definition, [value], null, null);

        Assert.AreEqual(length, captured.OriginalBytes.Length);
        using JsonDocument details = JsonDocument.Parse(captured.Record.DetailsJson);
        Assert.AreEqual(value, details.RootElement.GetProperty("values")[0].GetString());
        await using var smaller = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(length - 1), clock, budget);
        Assert.ThrowsExactly<ArgumentException>(() => smaller.Capture(definition, [value], null, null));
        Assert.IsTrue(budget.TryAcquireBytes(checked((int)new MessagingOptions().Capture().MaximumEncodedBufferBytes), out IDisposable? allBytes));
        allBytes!.Dispose();
    }

    [TestMethod]
    public async Task ExtraEncoderCapacityMustBeAdmittedBeforeItsAllocationAndRefusesWithoutWaiting()
    {
        MessagingSettings settings = new MessagingOptions().Capture();
        await using var budget = new MessagingResourceBudget(settings);
        var clock = new CountingClock();
        await using var baseline = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), clock, budget);
        int length = baseline.Capture(Definition(), [], null, null).OriginalBytes.Length;
        Assert.IsLessThan(1000, length, "This fixture exercises the minimum encoder chunk above its ordinary four-payload reservation.");
        await using var exact = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(length), clock, budget);
        int occupied = checked((int)settings.MaximumEncodedBufferBytes - 4 * length - 128);
        Assert.IsTrue(budget.TryAcquireBytes(occupied, out IDisposable? held));
        try
        {
            AuditCaptureException refusal = Assert.ThrowsExactly<AuditCaptureException>(() => exact.Capture(Definition(), [], null, null));
            Assert.AreEqual(AuditCaptureFailure.Overloaded, refusal.Failure);
            Assert.AreNotEqual(Guid.Empty, refusal.AuditId);
        }
        finally
        {
            held!.Dispose();
        }
        Assert.AreEqual(length, exact.Capture(Definition(), [], null, null).OriginalBytes.Length);
        Assert.IsTrue(budget.TryAcquireBytes(checked((int)settings.MaximumEncodedBufferBytes), out IDisposable? allBytes));
        allBytes!.Dispose();
    }

    [TestMethod]
    public async Task SelectedWriteFaultSurvivesAnOversizedPartialFlushDuringWriterDisposal()
    {
        MessagingSettings settings = new MessagingOptions().Capture();
        await using var budget = new MessagingResourceBudget(settings);
        var clock = new CountingClock();
        await using var baseline = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), clock, budget);
        int length = baseline.Capture(Definition(), [], null, null).OriginalBytes.Length;
        await using var exact = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(length), clock, budget);
        var original = new InvalidOperationException("original selected mapping fault");
        var definition = new AuditEventDefinition<List<string>>("ledger.applied", 1, AuditCategory.Business,
            new WriteThenThrowCodec(original, length));

        Exception observed = Assert.ThrowsExactly<InvalidOperationException>(() => exact.Capture(definition, [], null, null));

        Assert.AreSame(original, observed, "Disposing the partial JSON writer cannot replace the selected mapping's original fault.");
        Assert.IsTrue(budget.TryAcquireBytes(checked((int)settings.MaximumEncodedBufferBytes), out IDisposable? allBytes));
        allBytes!.Dispose();
        Assert.AreEqual(length, exact.Capture(Definition(), [], null, null).OriginalBytes.Length);
    }

    [TestMethod]
    public async Task CaptureShutdownClosesAdmissionAndJoinsActualBlockedCodec()
    {
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), new CountingClock(), budget);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var definition = new AuditEventDefinition<List<string>>("ledger.applied", 1, AuditCategory.Business, new HoldingCodec(entered, release));
        Task<CapturedAuditRecord> pending = Task.Run(() => engine.Capture(definition, [], null, null));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)), "The selected codec must actually enter before shutdown.");
            Task drain = engine.DrainAsync();
            Assert.IsFalse(drain.IsCompleted, "A cancellation request cannot settle a running synchronous codec.");
            AuditCaptureException refusal = Assert.ThrowsExactly<AuditCaptureException>(() => engine.Capture(Definition(), [], null, null));
            Assert.AreEqual(AuditCaptureFailure.Unavailable, refusal.Failure);
            Assert.IsFalse(pending.IsCompleted);
            release.Set();

            CapturedAuditRecord captured = await pending.WaitAsync(TimeSpan.FromSeconds(10));
            await drain.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreNotEqual(Guid.Empty, captured.AuditId);
        }
        finally
        {
            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static AuditEventDefinition<List<string>> Definition()
        => new("ledger.applied", 1, AuditCategory.Business, new StringListCodec());

    private static CallerContext Caller(SecurityActorKind kind)
        => new("native", "method", new("issuer", "subject"), kind, "caller-app", "caller-env",
            new(ObservedAt), "policy", new("reported", "verified"), new("human-issuer", "human"),
            kind == SecurityActorKind.Application ? "delegation-proof" : null);

    private sealed class CallerAccessor : ICallerContextAccessor
    {
        public CallerContext? Current
        {
            get; set;
        }
    }

    private sealed class CountingClock() : ManualTimeProvider(ObservedAt)
    {
        internal int Reads
        {
            get; private set;
        }

        public override DateTimeOffset GetUtcNow()
        {
            Reads++;
            return base.GetUtcNow();
        }
    }

    private class StringListCodec : IAuditDetailsCodec<List<string>>
    {
        internal int Validations
        {
            get; private set;
        }

        public virtual void Validate(List<string> details)
        {
            ArgumentNullException.ThrowIfNull(details);
            Validations++;
        }

        public virtual void Write(Utf8JsonWriter writer, List<string> details)
        {
            writer.WriteStartObject();
            writer.WriteStartArray("values");
            foreach (string value in details)
            {
                writer.WriteStringValue(value);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    private sealed class ThrowingCodec(Exception fault) : StringListCodec
    {
        public override void Validate(List<string> details) => throw fault;
    }

    private sealed class ArrayCodec : StringListCodec
    {
        public override void Write(Utf8JsonWriter writer, List<string> details)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
        }
    }

    private sealed class WriteThenThrowCodec(Exception fault, int length) : StringListCodec
    {
        public override void Write(Utf8JsonWriter writer, List<string> details)
        {
            writer.WriteStartObject();
            writer.WriteString("value", new string('a', length));
            throw fault;
        }
    }

    private sealed class HoldingCodec(ManualResetEventSlim entered, ManualResetEventSlim release) : StringListCodec
    {
        public override void Write(Utf8JsonWriter writer, List<string> details)
        {
            entered.Set();
            release.Wait();
            base.Write(writer, details);
        }
    }
}
