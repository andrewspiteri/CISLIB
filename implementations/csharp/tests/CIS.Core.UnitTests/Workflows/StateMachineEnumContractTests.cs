using CIS.Workflows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Workflows;

[TestClass]
public sealed class StateMachineEnumContractTests
{
    [TestMethod]
    [DataRow("byte")]
    [DataRow("sbyte")]
    [DataRow("short")]
    [DataRow("ushort")]
    [DataRow("int")]
    [DataRow("uint")]
    [DataRow("long")]
    [DataRow("ulong")]
    public async Task AllEightIntegralUnderlyingsPreserveDeclaredGapsAndExtremeValues(string underlying)
    {
        await using var host = new StateMachineTestHost();
        switch (underlying)
        {
            case "byte":
                await Direct(host, ByteValues.Source, ByteValues.Target);
                break;
            case "sbyte":
                await Direct(host, SByteValues.Source, SByteValues.Target);
                break;
            case "short":
                await Direct(host, ShortValues.Source, ShortValues.Target);
                break;
            case "ushort":
                await Direct(host, UShortValues.Source, UShortValues.Target);
                break;
            case "int":
                await Direct(host, IntValues.Source, IntValues.Target);
                break;
            case "uint":
                await Direct(host, UIntValues.Source, UIntValues.Target);
                break;
            case "long":
                await Direct(host, LongValues.Source, LongValues.Target);
                break;
            case "ulong":
                await Direct(host, ULongValues.Source, ULongValues.Target);
                break;
            default:
                throw new AssertFailedException("Unknown underlying fixture.");
        }
        Assert.AreEqual(0, host.Scopes);
        Assert.IsEmpty(host.Context.Instances);
        Assert.IsEmpty(host.Observation.Phases);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task FlagsAreRejectedOnEitherEnumEvenWithOneOrdinaryValue(bool state)
    {
        await using var host = new StateMachineTestHost();
        if (state)
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Builder<FlagValues, MachineTrigger>().Build(FlagValues.One));
        }
        else
        {
            Assert.ThrowsExactly<ArgumentException>(() => host.Builder<MachineState, FlagValues>().Build(MachineState.Idle));
        }
        Assert.IsEmpty(host.Definitions);
        Assert.AreEqual(0, host.Scopes);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NumericAliasesAreRejectedWithBothConflictingNames(bool state)
    {
        await using var host = new StateMachineTestHost();
        ArgumentException error = state
            ? Assert.ThrowsExactly<ArgumentException>(() => host.Builder<AliasedValues, MachineTrigger>().Build(AliasedValues.First))
            : Assert.ThrowsExactly<ArgumentException>(() => host.Builder<MachineState, AliasedValues>().Build(MachineState.Idle));
        Assert.IsTrue(error.Message.Contains("First", StringComparison.Ordinal));
        Assert.IsTrue(error.Message.Contains("Second", StringComparison.Ordinal));
        Assert.IsEmpty(host.Definitions);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("target")]
    [DataRow("trigger")]
    [DataRow("initial")]
    public async Task UndefinedConfiguredValuesFailBeforeMetadataAdmissionOrActivation(string invalid)
    {
        await using var host = new StateMachineTestHost();
        var builder = host.Builder();
        if (invalid == "source")
        {
            Assert.ThrowsExactly<ArgumentException>(() => builder.State((MachineState)999));
        }
        else
        {
            builder.State(MachineState.Idle)
                .On(invalid == "trigger" ? (MachineTrigger)999 : MachineTrigger.Start,
                    invalid == "target" ? (MachineState)999 : MachineState.Ready);
            Assert.ThrowsExactly<ArgumentException>(() => builder.Build(invalid == "initial" ? (MachineState)999 : MachineState.Idle));
        }
        Assert.IsEmpty(host.Definitions);
        Assert.AreEqual(0, host.Scopes);
        Assert.IsEmpty(host.Context.Instances);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DuplicateSourceTriggerPairsNeverUseFirstOrLastRegistrationWins(bool sameTarget)
    {
        await using var host = new StateMachineTestHost();
        var builder = host.Builder();
        builder.State(MachineState.Idle).On(MachineTrigger.Start, MachineState.Ready)
            .On(MachineTrigger.Start, sameTarget ? MachineState.Ready : MachineState.Done);
        Assert.ThrowsExactly<ArgumentException>(() => builder.Build(MachineState.Idle));
        Assert.IsEmpty(host.Definitions);
    }

    [TestMethod]
    public async Task TerminalOnlyDefinitionWithAnEmptyTriggerEnumIsValidAndQuietToInspect()
    {
        await using var host = new StateMachineTestHost();
        IStateMachine<MachineState, NoTriggers> machine = host.Builder<MachineState, NoTriggers>().Build(MachineState.Done);
        Assert.AreEqual(MachineState.Done, machine.CurrentState);
        Assert.IsNull(machine.LastTransition);
        Assert.IsEmpty(machine.PermittedTriggers);
        Assert.IsFalse(machine.CanTrigger((NoTriggers)0));
        Assert.AreEqual(0, host.Observation.Calls);
        StateTransitionResult<MachineState, NoTriggers> result = await machine.Trigger((NoTriggers)0);
        Assert.AreEqual(StateTransitionOutcome.Rejected, result.Outcome);
        Assert.AreEqual("StateMachine.InvalidTrigger", result.ErrorCode);
        Assert.AreEqual(MachineState.Done, result.CurrentState);
        Assert.AreSame(result, machine.LastTransition);
        Assert.AreEqual(0, host.Scopes);
    }

    [TestMethod]
    public async Task ExplicitHandlerSelfTransitionRunsFreshWorkWhileStateChangedRemainsFalse()
    {
        await using var host = new StateMachineTestHost();
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Reset, MachineState.Idle);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Assert.AreEqual(0, host.Scopes);
        StateTransitionResult<MachineState, MachineTrigger> first = await machine.Trigger(MachineTrigger.Reset);
        StateTransitionResult<MachineState, MachineTrigger> second = await machine.Trigger(MachineTrigger.Reset);
        Assert.IsTrue(first.Success);
        Assert.IsTrue(second.Success);
        Assert.IsFalse(first.StateChanged);
        Assert.IsFalse(second.StateChanged);
        Assert.AreEqual(MachineState.Idle, machine.CurrentState);
        Assert.AreEqual(2, host.Scopes);
        Assert.HasCount(2, host.Context.Instances);
        Assert.AreNotSame(host.Context.Instances[0], host.Context.Instances[1]);
        Assert.IsTrue(host.Context.Instances.All(instance => instance.AsyncDisposals == 1 && instance.SyncDisposals == 0));
        Assert.IsEmpty(host.Observation.Phases.Where(phase => phase.Phase == PipelinePhase.Error).ToArray());
    }

    [TestMethod]
    public async Task LaterBuilderMappingsCannotChangeAnEarlierBuiltMachine()
    {
        await using var host = new StateMachineTestHost();
        var builder = host.Builder();
        builder.State(MachineState.Idle).On(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> first = builder.Build(MachineState.Idle);
        builder.State(MachineState.Ready).On(MachineTrigger.Finish, MachineState.Done);
        IStateMachine<MachineState, MachineTrigger> second = builder.Build(MachineState.Idle);
        Assert.IsTrue((await first.Trigger(MachineTrigger.Start)).Success);
        Assert.IsTrue((await second.Trigger(MachineTrigger.Start)).Success);
        Assert.IsFalse(first.CanTrigger(MachineTrigger.Finish));
        Assert.IsTrue(second.CanTrigger(MachineTrigger.Finish));
        Assert.AreEqual(StateTransitionOutcome.Rejected, (await first.Trigger(MachineTrigger.Finish)).Outcome);
        Assert.IsTrue((await second.Trigger(MachineTrigger.Finish)).Success);
        Assert.AreEqual(MachineState.Ready, first.CurrentState);
        Assert.AreEqual(MachineState.Done, second.CurrentState);
    }

    [TestMethod]
    public async Task BuildCapturesCleanupBudgetAndLoggingFlagsBeforeMutableOptionsChange()
    {
        await using var host = new StateMachineTestHost();
        var options = new StateMachineOptions { ErrorHandlingTimeout = TimeSpan.FromMilliseconds(10), LogCalls = false };
        StateMachineCallInfo? observed = null;
        host.ObserveCall = info => observed = info;
        bool requested = false;
        host.Context.Forward = static (_, _) => Task.FromResult(TransitionActionResult.Failed("expected"));
        host.Context.Compensate = (_, _, token) =>
        {
            host.Clock.Advance(TimeSpan.FromMilliseconds(10));
            requested = token.IsCancellationRequested;
            return Task.CompletedTask;
        };
        var builder = host.Builder(options);
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready);
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        options.ErrorHandlingTimeout = Timeout.InfiniteTimeSpan;
        options.LogCalls = true;
        options.LogRoutineAsInformation = true;
        StateTransitionResult<MachineState, MachineTrigger> result = await machine.Trigger(MachineTrigger.Start);
        Assert.IsTrue(requested);
        Assert.IsTrue(result.CleanupBudgetExpired);
        Assert.IsEmpty(result.ErrorHandlingFailures);
        Assert.AreEqual(StateTransitionOutcome.Failed, result.Outcome);
        Assert.IsNotNull(observed);
        Assert.IsFalse(observed.LogCalls);
        Assert.IsFalse(observed.LogRoutineAsInformation);
    }

    [TestMethod]
    public async Task ConfigurationBuildAndInspectionNeverConstructHandlersOrAcquireScopes()
    {
        await using var host = new StateMachineTestHost();
        host.Context.Construct = static () => throw new InvalidOperationException("Build activated application work.");
        var builder = host.Builder();
        builder.State(MachineState.Idle).On<StateMachineTestHandler>(MachineTrigger.Start, MachineState.Ready, "selected");
        builder.State(MachineState.Ready).On<StateMachineTestHandler>(MachineTrigger.Finish, MachineState.Done, "later");
        IStateMachine<MachineState, MachineTrigger> machine = builder.Build(MachineState.Idle);
        Assert.IsTrue(machine.CanTrigger(MachineTrigger.Start));
        Assert.IsFalse(machine.CanTrigger(MachineTrigger.Finish));
        Assert.HasCount(1, machine.PermittedTriggers);
        Assert.IsFalse(machine.IsExecuting);
        Assert.IsNull(machine.LastTransition);
        Assert.HasCount(1, host.Definitions);
        Assert.AreEqual(0, host.Scopes);
        Assert.IsEmpty(host.Context.Instances);
        Assert.AreEqual(0, host.Observation.Calls);
    }

    private static async Task Direct<TEnum>(StateMachineTestHost host, TEnum source, TEnum target) where TEnum : struct, Enum
    {
        var builder = host.Builder<TEnum, TEnum>();
        builder.State(source).On(source, target);
        IStateMachine<TEnum, TEnum> machine = builder.Build(source);
        StateTransitionResult<TEnum, TEnum> result = await machine.Trigger(source);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(source, result.SourceState);
        Assert.AreEqual(target, result.TargetState);
        Assert.AreEqual(target, machine.CurrentState);
        Assert.AreEqual(source, result.Trigger);
        Assert.HasCount(1, host.Definitions);
        CollectionAssert.AreEquivalent(new[] { "Source", "Target" }, host.Definitions[0].States.ToArray());
    }

    private enum ByteValues : byte
    {
        Source = 7, Target = 249
    }
    private enum SByteValues : sbyte
    {
        Source = -101, Target = 100
    }
    private enum ShortValues : short
    {
        Source = -23001, Target = 21991
    }
    private enum UShortValues : ushort
    {
        Source = 17, Target = 64001
    }
    private enum IntValues : int
    {
        Source = int.MinValue + 1, Target = 1700000000
    }
    private enum UIntValues : uint
    {
        Source = 3000000001U, Target = 4000000001U
    }
    private enum LongValues : long
    {
        Source = long.MinValue + 13, Target = long.MaxValue - 7
    }
    private enum ULongValues : ulong
    {
        Source = (1UL << 63) + 7, Target = ulong.MaxValue - 1
    }
    [Flags]
    private enum FlagValues
    {
        One = 1
    }
    private enum AliasedValues
    {
        First = 1, Second = 1
    }
    private enum NoTriggers
    {
    }
}
