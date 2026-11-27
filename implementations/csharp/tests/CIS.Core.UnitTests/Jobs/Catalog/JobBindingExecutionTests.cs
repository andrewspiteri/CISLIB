using System.Text;
using CIS.Coordination;
using CIS.Jobs;
using CIS.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Catalog;

[TestClass]
public sealed class JobBindingExecutionTests
{
    [TestMethod]
    public async Task ExactBindingsResolveOnlyTheMatchingScopedHandlerAndRejectAnUnrelatedContext()
    {
        var services = new ServiceCollection();
        services.AddScoped<WalletHandler>();
        services.AddScoped<CustomerHandler>();
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var wallet = new JobDefinition("wallet-service", "wallet.compaction", "Compaction", "maintenance",
            [JobExecutionMode.Continuous], ["jobs.execute"]);
        var customer = new JobDefinition("customer-service", "customer.dormancy", "Dormancy", "lifecycle",
            [JobExecutionMode.Scheduled], ["jobs.execute"]);
        JobHandlerBinding walletBinding = JobHandlerBinding.Continuous<WalletHandler>(wallet);
        JobHandlerBinding customerBinding = JobHandlerBinding.Scheduled<int, CustomerHandler>(customer, new IntegerCodec());
        var walletContext = new JobExecutionContext(wallet, "partition", Lease(wallet), scope.ServiceProvider, new(JobExecutionMode.Continuous));
        JobExecutionContext customerContext = Context(customer, scope.ServiceProvider, new("dormancy-input", 1), "42");

        Assert.AreEqual(ContinuousIterationOutcome.Idle, (await walletBinding.InvokeContinuousAsync(walletContext, default)).Outcome);
        Assert.AreEqual(ScheduledJobOutcome.Completed, (await customerBinding.InvokeScheduledAsync(customerContext, default)).Outcome);
        Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<WalletHandler>().Calls);
        Assert.AreEqual(42, scope.ServiceProvider.GetRequiredService<CustomerHandler>().Input);
        Assert.ThrowsExactly<ArgumentException>(() => walletBinding.InvokeContinuousAsync(customerContext, default));
        Assert.AreEqual(1, scope.ServiceProvider.GetRequiredService<WalletHandler>().Calls);

        var execution = new MessageContract("execution", 1);
        var profile = new JobExecutionContract(execution, [execution], [execution]);
        Assert.AreEqual(ContinuousIterationOutcome.Idle,
            (await walletBinding.WithExecutionContract(profile).InvokeContinuousAsync(walletContext, default)).Outcome);
        Assert.AreEqual(ScheduledJobOutcome.Completed,
            (await customerBinding.WithExecutionContract(profile).InvokeScheduledAsync(customerContext, default)).Outcome);
        Assert.AreEqual(2, scope.ServiceProvider.GetRequiredService<WalletHandler>().Calls);
        Assert.AreEqual(42, scope.ServiceProvider.GetRequiredService<CustomerHandler>().Input);
    }

    [TestMethod]
    public async Task IncompatiblePayloadIsRejectedBeforeResolvingTheScheduledHandler()
    {
        var services = new ServiceCollection();
        services.AddScoped<CustomerHandler>(_ => throw new AssertFailedException("Invalid input must not construct the handler."));
        await using ServiceProvider provider = services.BuildServiceProvider();
        var definition = new JobDefinition("customer-service", "customer.dormancy", "Dormancy", "lifecycle",
            [JobExecutionMode.Scheduled], ["jobs.execute"]);
        JobHandlerBinding binding = JobHandlerBinding.Scheduled<int, CustomerHandler>(definition, new IntegerCodec());
        JobExecutionContext context = Context(definition, provider, new("unsupported-input", 1), "42");

        ScheduledJobResult result = await binding.InvokeScheduledAsync(context, default);
        Assert.AreEqual(ScheduledJobOutcome.Unsupported, result.Outcome);
        Assert.AreEqual("jobs.payload_unsupported", result.ReasonCode);
    }

    private static JobExecutionContext Context(JobDefinition definition, IServiceProvider services, MessageContract contract, string json)
    {
        var settings = new JobExecutionSettings(JobExecutionMode.Scheduled,
            schedule: JobScheduleDescriptor.Daily(new(2, 0), "Europe/Malta", JobMissedRunPolicy.RunLatestOnce));
        var operation = new MessageOperationReference("Dormancy", "partition", "same-original-operation");
        EncodedMessage input = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, contract, "scheduler", "test",
            DateTimeOffset.UtcNow, Encoding.UTF8.GetBytes(json), operation: operation));
        var run = new AdmittedJobRun(definition.ExecutionScope, definition.JobId, "partition", "same-occurrence", operation,
            1, settings, input, "controlled-trigger", "admission-1");
        return new(definition, "partition", Lease(definition), services, settings, run);
    }

    private static ICoordinationLease Lease(JobDefinition definition)
        => new ContextLease(new(Guid.NewGuid(), new(new("test", definition.ExecutionScope, definition.JobId, "partition"),
            "host-application", Guid.NewGuid().ToString("D"), Guid.NewGuid()), 1, DateTimeOffset.UtcNow.AddMinutes(1)));

    private sealed class ContextLease(CoordinationGrant grant) : ICoordinationLease
    {
        public CoordinationGrant Grant => grant;
        public CoordinationLeaseState State => CoordinationLeaseState.Owned;
        public CoordinationLossReason? LossReason => null;
        public CancellationToken OwnershipLost => CancellationToken.None;
        public Task<CoordinationLeaseTermination> Termination => throw new NotSupportedException("This metadata test exercises no lease lifecycle.");
        public void BeginDraining(TimeSpan? drainBudget = null) => throw new NotSupportedException();
        public Task<CoordinationMutationResult> ReleaseAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class WalletHandler : IContinuousJobHandler
    {
        public WalletHandler()
        {
        }
        internal int Calls
        {
            get; private set;
        }
        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ContinuousIterationResult(ContinuousIterationOutcome.Idle));
        }
    }

    private sealed class CustomerHandler : IScheduledJobHandler<int>
    {
        public CustomerHandler()
        {
        }
        internal int Input
        {
            get; private set;
        }
        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
        {
            Input = input;
            return Task.FromResult(new ScheduledJobResult(ScheduledJobOutcome.Completed));
        }
    }

    private sealed class IntegerCodec : IMessageCodec<int>
    {
        public MessageContract Contract => new("dormancy-input", 1);
        public byte[] Encode(int value) => Encoding.UTF8.GetBytes(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
            => int.TryParse(Encoding.UTF8.GetString(json.Span), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
