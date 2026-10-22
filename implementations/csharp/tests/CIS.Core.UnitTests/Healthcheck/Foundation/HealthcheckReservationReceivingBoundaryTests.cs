using System.Numerics;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthcheckReservationReceivingBoundaryTests
{
    [TestMethod]
    [DataRow("measurementObservationRevisions")]
    [DataRow("stateHandle")]
    public async Task ReservationRejectsMissingRequiredInputsAtItsReceivingBoundary(string missing)
    {
        await using var scope = new ReservationScope();
        HealthCollectionReservation issued = scope.Reserve();

        ArgumentNullException error = Assert.ThrowsExactly<ArgumentNullException>(() => Copy(issued,
            missing == "measurementObservationRevisions" ? null! : new Dictionary<string, BigInteger>(),
            missing == "stateHandle" ? null! : issued.StateHandle));

        Assert.AreEqual(missing, error.ParamName);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(32)]
    public async Task ReservationOwnsItsInclusiveBaselineMapAndPreservesTheActualStateIdentity(int count)
    {
        await using var scope = new ReservationScope();
        HealthCollectionReservation issued = scope.Reserve();
        var supplied = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        for (int index = 0; index < count; index++) { supplied.Add("metric-" + index, new BigInteger(index)); }

        HealthCollectionReservation copied = Copy(issued, supplied, issued.StateHandle);

        Assert.AreEqual(count, copied.MeasurementObservationRevisions.Count);
        foreach ((string key, BigInteger value) in supplied) { Assert.AreEqual(value, copied.MeasurementObservationRevisions[key]); }
        Assert.AreEqual(issued.OperationId, copied.OperationId);
        Assert.AreEqual(issued.MonitorId, copied.MonitorId);
        Assert.AreEqual(issued.CollectionRegistrationId, copied.CollectionRegistrationId);
        Assert.AreEqual(issued.Generation, copied.Generation);
        Assert.AreEqual(issued.HealthObservationRevision, copied.HealthObservationRevision);
        Assert.AreEqual(issued.LifetimeCancellation, copied.LifetimeCancellation);
        Assert.AreSame(issued.StateHandle, copied.StateHandle);
        supplied.Clear();
        supplied.Add("replacement", BigInteger.MinusOne);
        Assert.AreEqual(count, copied.MeasurementObservationRevisions.Count);
        Assert.IsFalse(copied.MeasurementObservationRevisions.ContainsKey("replacement"));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<string, BigInteger>)copied.MeasurementObservationRevisions)
            .Add("later", BigInteger.One));
    }

    [TestMethod]
    public async Task ReservationRejectsThe33rdBaselineInsteadOfTruncatingItsObservationHistory()
    {
        await using var scope = new ReservationScope();
        HealthCollectionReservation issued = scope.Reserve();
        var supplied = new Dictionary<string, BigInteger>(StringComparer.Ordinal);
        for (int index = 0; index < 33; index++) { supplied.Add("metric-" + index, new BigInteger(index)); }

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => Copy(issued, supplied, issued.StateHandle));

        Assert.AreEqual("measurementObservationRevisions", error.ParamName);
        Assert.AreEqual(33, supplied.Count);
    }

    [TestMethod]
    public void RetiredReservationDecisionHasNoLiveOperation()
    {
        var result = new HealthCollectionReservationResult(HealthCollectionReservationDisposition.Retired, null);

        Assert.AreEqual(HealthCollectionReservationDisposition.Retired, result.Disposition);
        Assert.IsNull(result.Reservation);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(3)]
    [DataRow(int.MaxValue)]
    public void UndefinedReservationDecisionsFailBeforeInterpretingAnOperation(int disposition)
    {
        ArgumentOutOfRangeException error = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new HealthCollectionReservationResult((HealthCollectionReservationDisposition)disposition, null));

        Assert.AreEqual("disposition", error.ParamName);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void LiveReservationDecisionsRequireAnOperation(int disposition)
    {
        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new HealthCollectionReservationResult((HealthCollectionReservationDisposition)disposition, null));

        Assert.AreEqual("reservation", error.ParamName);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task LiveReservationDecisionsRetainTheActualStateIssuedOperation(int disposition)
    {
        await using var scope = new ReservationScope();
        HealthCollectionReservation issued = scope.Reserve();

        var result = new HealthCollectionReservationResult((HealthCollectionReservationDisposition)disposition, issued);

        Assert.AreEqual((HealthCollectionReservationDisposition)disposition, result.Disposition);
        Assert.AreSame(issued, result.Reservation);
    }

    [TestMethod]
    public async Task RetiredReservationDecisionCannotCarryALiveOperation()
    {
        await using var scope = new ReservationScope();
        HealthCollectionReservation issued = scope.Reserve();

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
            new HealthCollectionReservationResult(HealthCollectionReservationDisposition.Retired, issued));

        Assert.AreEqual("reservation", error.ParamName);
    }

    private static HealthCollectionReservation Copy(HealthCollectionReservation issued,
        IReadOnlyDictionary<string, BigInteger> baselines, IHealthCollectionOperationHandle handle) => new(
        issued.OperationId, issued.MonitorId, issued.CollectionRegistrationId, issued.Generation,
        issued.HealthObservationRevision, baselines, issued.LifetimeCancellation, handle);

    private sealed class ReservationScope : IAsyncDisposable
    {
        private readonly HealthcheckService service = new(new(),
            new HealthApplicationIdentity { Name = "reservation receiving boundary", InstanceId = Guid.NewGuid() });
        private readonly IConditionMonitor monitor;
        private readonly IHealthCollectionState state;
        private readonly HealthCollectionTarget target;
        private HealthCollectionReservation? reservation;

        internal ReservationScope()
        {
            monitor = service.CreateCondition(new() { IsCritical = false, InitiallyFlagged = false });
            state = ((IHealthStateAccess)service).CollectionState;
            target = state.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new() { IsRequired = false });
        }

        internal HealthCollectionReservation Reserve()
        {
            // The handle used by constructor controls comes only from the real state owner.
            Exception? failure = null;
            try { reservation = state.TryReserve(target).Reservation; }
            catch (Exception error) { failure = error; }
            Assert.IsNull(failure, "The valid state reservation must be admitted before testing its detached receiving boundary.");
            Assert.IsNotNull(reservation);
            return reservation;
        }

        public async ValueTask DisposeAsync()
        {
            if (reservation is not null)
            {
                state.Complete(reservation, new(HealthCollectionCompletionReason.Settled, DateTimeOffset.UtcNow));
            }
            monitor.Withdraw();
            Task completion = service.DisposeAsync().AsTask();
            // A deliberately broken decision constructor can reject after state admission,
            // before returning the reservation. Preserve that assertion instead of awaiting
            // the unreturnable operation's incorrect terminal barrier in mutant cleanup.
            if (reservation is not null) { await completion.WaitAsync(TimeSpan.FromSeconds(10)); }
            else if (completion.IsCompleted) { await completion; }
            GC.KeepAlive(monitor);
        }
    }
}
