using System.Globalization;
using CIS.Jobs;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Catalog;

[TestClass]
public sealed class JobExecutionContractTests
{
    [TestMethod]
    public void CompatibilityIsAnImmutableExactDeclarationWithoutReleaseOrVersionInference()
    {
        var current = new MessageContract("customer.execution", 3);
        var previous = new MessageContract("customer.execution", 2);
        MessageContract[] starts = [previous, current];
        var recoveries = new List<MessageContract> { previous };
        var checkpoint = new MessageContract("customer.checkpoint", 1);
        var profile = new JobExecutionContract(current, starts, recoveries, checkpoint, "Customer.Dormancy");

        starts[0] = new("unrelated", 1);
        recoveries.Clear();

        Assert.IsTrue(profile.SupportsStart(new("customer.execution", 3)));
        Assert.IsTrue(profile.SupportsStart(new("customer.execution", 2)));
        Assert.IsTrue(profile.SupportsRecovery(new("customer.execution", 2)));
        Assert.IsFalse(profile.SupportsRecovery(new("customer.execution", 3)));
        Assert.IsFalse(profile.SupportsStart(new("customer.execution", 4)));
        Assert.IsFalse(profile.SupportsStart(new("Customer.execution", 3)));
        Assert.AreSame(checkpoint, profile.CheckpointContract);
        Assert.AreEqual("Customer.Dormancy", profile.OperationName);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<MessageContract>)profile.SupportedStarts).Add(current));
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<MessageContract>)profile.SupportedRecoveries).Clear());
    }

    [TestMethod]
    public void CurrentStartsAreRequiredAndEachListHasAnIndependentSixteenContractBound()
    {
        MessageContract[] contracts = Enumerable.Range(1, 17).Select(version => new MessageContract("execution", version)).ToArray();
        var maximum = new JobExecutionContract(contracts[15], contracts.Take(16), contracts.Take(16));
        var inspectionOnly = new JobExecutionContract(contracts[0], [contracts[0]], []);

        Assert.AreEqual(16, maximum.SupportedStarts.Count);
        Assert.AreEqual(16, maximum.SupportedRecoveries.Count);
        Assert.AreEqual(0, inspectionOnly.SupportedRecoveries.Count);
        Assert.IsNull(inspectionOnly.OperationName);
        Assert.IsNull(inspectionOnly.CheckpointContract);
        Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contracts[0], [], []));
        Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contracts[0], [contracts[1]], []));
        Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contracts[0], contracts, []));
        Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contracts[0], [contracts[0]], contracts));
        Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contracts[0], [contracts[0], new("execution", 1)], []));
        Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contracts[0], [contracts[0]], [contracts[1], new("execution", 2)]));
        Assert.ThrowsExactly<ArgumentNullException>(() => new JobExecutionContract(null!, [contracts[0]], []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new JobExecutionContract(contracts[0], null!, []));
        Assert.ThrowsExactly<ArgumentNullException>(() => new JobExecutionContract(contracts[0], [contracts[0]], [null!]));
    }

    [TestMethod]
    public void OperationAliasReusesTheNativeOperationRulesAndIsNeverInvented()
    {
        var contract = new MessageContract("execution", 1);
        string maximumAlias = "A" + new string('b', 63);
        Assert.AreEqual(maximumAlias, new JobExecutionContract(contract, [contract], [], operationName: maximumAlias).OperationName);

        foreach (string alias in new[] { "", " ", "_alias", "bad alias", "bad/alias", new string('a', 65) })
        {
            Assert.ThrowsExactly<ArgumentException>(() => new JobExecutionContract(contract, [contract], [], operationName: alias));
        }
    }

    [TestMethod]
    public void AddingAProfileKeepsLegacyBindingsAndCodecCapturePassiveAndUnchanged()
    {
        var continuous = Definition(JobExecutionMode.Continuous);
        var scheduled = Definition(JobExecutionMode.Scheduled);
        var combined = new JobDefinition("customer-service", "combined", "Combined", "lifecycle",
            [JobExecutionMode.Continuous, JobExecutionMode.Scheduled], ["jobs.execute"]);
        var codec = new PassiveCodec();
        var current = new MessageContract("execution", 1);
        var profile = new JobExecutionContract(current, [current], [current]);
        JobHandlerBinding[] bindings =
        [
            JobHandlerBinding.Continuous<PassiveHandler>(continuous),
            JobHandlerBinding.Scheduled<int, PassiveHandler>(scheduled, codec),
            JobHandlerBinding.Combined<int, PassiveHandler>(combined, codec)
        ];

        foreach (JobHandlerBinding legacy in bindings)
        {
            JobHandlerBinding selected = legacy.WithExecutionContract(profile);
            Assert.IsNull(legacy.ExecutionContract);
            Assert.AreNotSame(legacy, selected);
            Assert.AreSame(legacy.Definition, selected.Definition);
            Assert.AreSame(legacy.HandlerType, selected.HandlerType);
            Assert.AreSame(legacy.InputContract, selected.InputContract);
            Assert.AreSame(profile, selected.ExecutionContract);
            Assert.ThrowsExactly<ArgumentNullException>(() => legacy.WithExecutionContract(null!));
        }

        Assert.AreEqual(2, codec.ContractReads, "Only the existing scheduled and combined factories read codec metadata.");
        Assert.AreEqual(0, codec.Decodes);
    }

    [TestMethod]
    public void LoadedMetadataCaptureDoesNotConstructOrInitializeTheHandlerAndIsNotAttestation()
    {
        CaptureProbe.Initializations = 0;
        CaptureProbe.Constructions = 0;
        Type type = typeof(InitializedHandler);
        JobWorkerBuildIdentity first = JobWorkerBuildIdentity.Capture("release+display-label", type, "source-commit");
        JobWorkerBuildIdentity second = JobWorkerBuildIdentity.Capture("release+display-label", type, "source-commit");

        Assert.AreEqual(first, second);
        Assert.AreEqual("release+display-label", first.ApplicationVersion);
        Assert.AreEqual("module-mvid:" + type.Module.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture), first.BuildId);
        Assert.IsTrue(first.HandlerId.Contains("/type:" + type.MetadataToken.ToString("x8", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.AreEqual("source-commit", first.SourceRevision);
        Assert.AreEqual(0, CaptureProbe.Initializations);
        Assert.AreEqual(0, CaptureProbe.Constructions);
    }

    [TestMethod]
    public void ClosedGenericAndArrayShapeImplementationsHaveDifferentMetadataIdentities()
    {
        JobWorkerBuildIdentity integers = JobWorkerBuildIdentity.Capture("1", typeof(GenericHandler<int>));
        JobWorkerBuildIdentity strings = JobWorkerBuildIdentity.Capture("1", typeof(GenericHandler<string>));
        JobWorkerBuildIdentity vector = JobWorkerBuildIdentity.Capture("1", typeof(GenericHandler<int[]>));
        Type rankOneArray = typeof(int).MakeArrayType(1);
        JobWorkerBuildIdentity matrix = JobWorkerBuildIdentity.Capture("1", typeof(GenericHandler<>).MakeGenericType(rankOneArray));

        Assert.AreEqual(integers.BuildId, strings.BuildId);
        Assert.AreNotEqual(integers.HandlerId, strings.HandlerId);
        Assert.AreNotEqual(vector.HandlerId, matrix.HandlerId);
        Assert.IsNull(integers.SourceRevision);
        Assert.ThrowsExactly<ArgumentException>(() => JobWorkerBuildIdentity.Capture("1", typeof(GenericHandler<>)));
        Assert.ThrowsExactly<ArgumentException>(() => JobWorkerBuildIdentity.Capture("1", typeof(IContinuousJobHandler)));
        Assert.ThrowsExactly<ArgumentException>(() => JobWorkerBuildIdentity.Capture("1", typeof(int)));
    }

    [TestMethod]
    public void MetadataCaptureRefusesAnUnboundedClosedTypeGraph()
    {
        Type nested = typeof(int);
        for (int index = 0; index < 17; index++)
        {
            nested = typeof(List<>).MakeGenericType(nested);
        }

        Type handler = typeof(GenericHandler<>).MakeGenericType(nested);
        Assert.ThrowsExactly<ArgumentException>(() => JobWorkerBuildIdentity.Capture("1", handler));
    }

    [TestMethod]
    public void CallbackMetadataFollowsTheActualInheritedOrOverriddenInterfaceImplementation()
    {
        JobWorkerBuildIdentity original = JobWorkerBuildIdentity.Capture("1", typeof(OriginalHandler));
        JobWorkerBuildIdentity inherited = JobWorkerBuildIdentity.Capture("1", typeof(InheritedHandler));
        JobWorkerBuildIdentity overridden = JobWorkerBuildIdentity.Capture("1", typeof(OverriddenHandler));
        const string callbackMarker = "/callbacks-sha256:";
        string originalCallback = original.HandlerId[original.HandlerId.IndexOf(callbackMarker, StringComparison.Ordinal)..];
        string inheritedCallback = inherited.HandlerId[inherited.HandlerId.IndexOf(callbackMarker, StringComparison.Ordinal)..];
        string overriddenCallback = overridden.HandlerId[overridden.HandlerId.IndexOf(callbackMarker, StringComparison.Ordinal)..];

        Assert.AreNotEqual(original.HandlerId, inherited.HandlerId, "The exact constructed handler type stays distinct.");
        Assert.AreEqual(originalCallback, inheritedCallback, "Inheritance keeps the original actual callback implementation.");
        Assert.AreNotEqual(originalCallback, overriddenCallback, "An override must identify its own actual target method.");
    }

    [TestMethod]
    public void WorkerMetadataUsesFiniteUnicodeByteBoundsAndPreservesExactText()
    {
        string maximum = new('é', 256);
        string revision = new('é', 64);
        var identity = new JobWorkerBuildIdentity(maximum, maximum, maximum, revision);
        Assert.AreEqual(maximum, identity.ApplicationVersion);
        Assert.AreEqual(revision, identity.SourceRevision);
        Assert.AreEqual(" Release ", new JobWorkerBuildIdentity(" Release ", "build", "handler").ApplicationVersion);
        Assert.ThrowsExactly<ArgumentException>(() => new JobWorkerBuildIdentity(maximum + "é", "build", "handler"));
        Assert.ThrowsExactly<ArgumentException>(() => new JobWorkerBuildIdentity("1", maximum + "é", "handler"));
        Assert.ThrowsExactly<ArgumentException>(() => new JobWorkerBuildIdentity("1", "build", maximum + "é"));
        Assert.ThrowsExactly<ArgumentException>(() => new JobWorkerBuildIdentity("1", "build", "handler", revision + "é"));
        Assert.ThrowsExactly<ArgumentException>(() => new JobWorkerBuildIdentity("1", "build\n", "handler"));
        Assert.ThrowsExactly<ArgumentException>(() => new JobWorkerBuildIdentity("1", "build", "handler", "\ud800"));
    }

    private static JobDefinition Definition(JobExecutionMode mode)
        => new("customer-service", mode.ToString(), "Customer job", "lifecycle", [mode], ["jobs.execute"]);

    private sealed class PassiveHandler : IContinuousJobHandler, IScheduledJobHandler<int>
    {
        public PassiveHandler() => throw new AssertFailedException("Passive binding/profile capture must not construct a handler.");

        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Passive capture must not enter a callback.");

        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Passive capture must not enter a callback.");
    }

    private sealed class PassiveCodec : IMessageCodec<int>
    {
        internal int ContractReads
        {
            get; private set;
        }

        internal int Decodes
        {
            get; private set;
        }

        public MessageContract Contract
        {
            get
            {
                ContractReads++;
                return new("input", 1);
            }
        }

        public byte[] Encode(int value) => throw new AssertFailedException("Passive capture must not encode input.");

        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            Decodes++;
            throw new AssertFailedException("Passive capture must not decode input.");
        }
    }

    private static class CaptureProbe
    {
        internal static int Initializations;
        internal static int Constructions;
    }

    private sealed class InitializedHandler : IContinuousJobHandler
    {
        static InitializedHandler()
        {
            CaptureProbe.Initializations++;
        }

        public InitializedHandler()
        {
            CaptureProbe.Constructions++;
        }

        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Metadata capture does not execute a handler.");
    }

    private sealed class GenericHandler<T> : IContinuousJobHandler
    {
        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Metadata capture does not execute a handler.");
    }

    private class OriginalHandler : IContinuousJobHandler
    {
        public virtual Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Metadata capture does not execute a callback.");
    }

    private sealed class InheritedHandler : OriginalHandler
    {
    }

    private sealed class OverriddenHandler : OriginalHandler
    {
        public override Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Metadata capture does not execute a callback.");
    }
}
