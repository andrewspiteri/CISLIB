using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Messaging.Hosting;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditHostingTests
{
    [TestMethod]
    public async Task PreCancelledMessagingStartDoesNotResolveOrPrepareAudit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var startable = new MessagingStartable(null!, new UnexpectedResolution());

        OperationCanceledException failure = await Assert.ThrowsAsync<OperationCanceledException>(
            () => startable.StartAsync(cancellation.Token));

        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
    }

    [TestMethod]
    public async Task PublicDefinitionOnlyCaptureWorksWithExplicitTestingWriterWithoutNativeProvidersOrFakeCaller()
    {
        var services = new ServiceCollection();
        var scripted = new ScriptedAuditWriter();
        services.ConfigureCisAudit(new("audit"));
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(IAuditWriter)),
            "A passive definition cannot supply a successful production writer.");
        services.AddSingleton<IAuditWriter>(scripted);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IAuditCapture capture = scope.ServiceProvider.GetRequiredService<IAuditCapture>();
        var definition = new AuditEventDefinition<int>("example.changed", 1, AuditCategory.Business, new NumberDetails());

        CapturedAuditRecord unknown = capture.Capture(definition, 42);
        CapturedAuditRecord system = capture.CaptureLocalSystem(definition, 43, new(AuditOutcome.Succeeded));
        MessageSubmission submission = new(MessageSubmissionOutcome.NotSubmitted,
            error: new("test.not_submitted", "Only scripted facts were supplied."));
        scripted.EnqueueSubmit(submission);
        AuditSubmissionResult result = await provider.GetRequiredService<IAuditWriter>().SubmitAsync(system);

        Assert.AreEqual(AuditActorKind.Unknown, unknown.Record.Actor.Kind);
        Assert.AreEqual(AuditActorKind.System, system.Record.Actor.Kind);
        Assert.AreEqual(EnvironmentOptions.ApplicationName, system.Record.Producer.Application);
        Assert.AreEqual(EnvironmentOptions.EnvironmentName, system.Record.Producer.Environment);
        Assert.AreSame(submission, result.Submission);
        Assert.AreEqual(system.AuditId, result.AuditId);
        Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted, result.Outcome);
        Assert.IsNull(provider.GetService<ICallerContextAccessor>());
        Assert.IsNull(provider.GetService<IMessagingStore>());
        Assert.IsNull(provider.GetService<IMessageTransport>());
        Assert.IsNull(provider.GetService<ILocalTransactionFactory>());
        Assert.IsNull(provider.GetService<IMessageOutbox>());
        Assert.IsNull(provider.GetService<AuditRuntime>());
    }

    [TestMethod]
    public async Task NativeSelectionIsPassiveAndRepeatedSelectionDoesNotConstructDependencies()
    {
        int constructors = 0;
        int applicationCallbacks = 0;
        var services = new ServiceCollection();
        services.ConfigureCisAudit(new("audit"));
        services.AddCisMessageStore(new("Messaging", "Ledger"));
        services.AddCisMessageSubscription(new MessageSubscriptionDefinition<AuditRecord>("audit-ingress", "Ledger", "audit",
            new AuditRecordCodec(), _ =>
            {
                applicationCallbacks++;
                throw new InvalidOperationException("Registration cannot resolve a handler.");
            }, (_, _, _, _) =>
            {
                applicationCallbacks++;
                throw new InvalidOperationException("Registration cannot resolve a target.");
            }, ["audit.receive"]));
        services.AddSingleton<IMessagingStore>(_ =>
        {
            constructors++;
            throw new InvalidOperationException("Registration cannot construct a store.");
        });
        services.AddSingleton<IMessageTransport>(_ =>
        {
            constructors++;
            throw new InvalidOperationException("Registration cannot construct a transport.");
        });
        services.AddSingleton<ILocalTransactionFactory>(_ =>
        {
            constructors++;
            throw new InvalidOperationException("Registration cannot construct a data-source adapter.");
        });

        services.AddCisMessaging();
        services.AddCisAudit();
        int selectedCount = services.Count;
        services.AddCisMessaging();
        services.AddCisAudit();
        Assert.AreEqual(selectedCount, services.Count);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        CapturedAuditRecord record = scope.ServiceProvider.GetRequiredService<IAuditCapture>()
            .CaptureLocalSystem(new("example.changed", 1, AuditCategory.Administration, new NumberDetails()), 1);

        Assert.AreNotEqual(Guid.Empty, record.AuditId);
        Assert.AreEqual(0, constructors);
        Assert.AreEqual(0, applicationCallbacks);
        Assert.AreEqual(1, services.Count(descriptor => descriptor.ServiceType == typeof(IAuditWriter) && !descriptor.IsKeyedService));
    }

    [TestMethod]
    public void DefinitionIsIdempotentOnlyForTheSameImmutableProfileAndPrecedesMessaging()
    {
        var services = new ServiceCollection();
        services.ConfigureCisAudit(new("audit", new(4096, 8)));
        int count = services.Count;
        services.ConfigureCisAudit(new("audit", new(4096, 8)));
        Assert.AreEqual(count, services.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => services.ConfigureCisAudit(new("different", new(4096, 8))));
        Assert.ThrowsExactly<InvalidOperationException>(() => services.ConfigureCisAudit(new("audit", new(4097, 8))));
        Assert.ThrowsExactly<InvalidOperationException>(() => services.ConfigureCisAudit(new("audit", new(4096, 9))));

        var late = new ServiceCollection();
        late.AddCisMessageStore(new("Messaging", "Ledger"));
        late.AddCisMessaging();
        Assert.ThrowsExactly<InvalidOperationException>(() => late.ConfigureCisAudit(new("audit")));
    }

    [TestMethod]
    public void ActualDeliveryNeedsItsDefinitionAndCompletedMessagingSelection()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new ServiceCollection().AddCisAudit());
        var definitionsOnly = new ServiceCollection();
        definitionsOnly.ConfigureCisAudit(new("audit"));
        definitionsOnly.AddCisMessageStore(new("Messaging", "Ledger"));
        Assert.ThrowsExactly<InvalidOperationException>(() => definitionsOnly.AddCisAudit());

        var explicitTestingWriter = new ServiceCollection();
        explicitTestingWriter.ConfigureCisAudit(new("audit"));
        explicitTestingWriter.AddCisMessageStore(new("Messaging", "Ledger"));
        explicitTestingWriter.AddCisMessaging();
        explicitTestingWriter.AddSingleton<IAuditWriter>(new ScriptedAuditWriter());
        Assert.ThrowsExactly<InvalidOperationException>(() => explicitTestingWriter.AddCisAudit());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AuditPayloadAndDepthLimitsMustFitTheActualMessagingRuntime(bool excessiveDepth)
    {
        var services = new ServiceCollection();
        services.ConfigureCisAudit(new("audit", excessiveDepth ? new(1024, 17) : new(2048, 16)));
        services.ConfigureCisMessaging(new()
        {
            MaximumPayloadBytes = 1024,
            MaximumJsonDepth = 16
        });
        services.AddCisMessageStore(new("Messaging", "Ledger"));
        services.AddCisMessaging();

        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisAudit());
    }

    [TestMethod]
    public void DeliveryCannotSelectAReplacedCommandOrForeignCodecRoute()
    {
        MessageRouteDefinition[] invalidRoutes =
        [
            new MessageRouteDefinition<AuditRecord>("audit", MessageKind.Command, new AuditRecordCodec()),
            new MessageRouteDefinition<int>("audit", MessageKind.Event, new NumberMessageCodec())
        ];
        foreach (MessageRouteDefinition route in invalidRoutes)
        {
            var services = new ServiceCollection();
            services.ConfigureCisAudit(new("audit"));
            services.RemoveAll<MessagingSnapshot>();
            services.AddSingleton(new MessagingSnapshot(new(), [route], [new("Messaging", "Ledger")]));
            services.AddCisMessaging();

            Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisAudit());
        }
    }

    [TestMethod]
    public async Task FailedBuildFallbackClosesNewCaptureAndJoinsTheActualUncooperativeCaptureBeforeDisposal()
    {
        var services = new ServiceCollection();
        services.ConfigureCisAudit(new("audit"));
        await using ServiceProvider provider = services.BuildServiceProvider();
        AuditHostingRegistration registration = provider.GetRequiredService<AuditHostingRegistration>();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IAuditCapture capture = scope.ServiceProvider.GetRequiredService<IAuditCapture>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var definition = new AuditEventDefinition<int>("example.held", 1, AuditCategory.Business, new HoldingDetails(entered, release));
        Task<CapturedAuditRecord> capturing = Task.Run(() => capture.CaptureLocalSystem(definition, 1));
        Task? stopping = null;
        Task? disposing = null;
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)), "The actual registered capture did not enter its details mapper.");
            Assert.IsNull(provider.GetService<AuditRuntime>(), "Failed Build may have constructed capture without constructing delivery lifetime.");
            stopping = registration.StopAsync();
            Assert.IsFalse(stopping.IsCompleted, "Fallback cannot replace the held mapper with a settled token.");
            AuditCaptureException closed = Assert.ThrowsExactly<AuditCaptureException>(() => capture.CaptureLocalSystem(
                new AuditEventDefinition<int>("example.changed", 1, AuditCategory.Business, new NumberDetails()), 2));
            Assert.AreEqual(AuditCaptureFailure.Unavailable, closed.Failure);
            disposing = provider.DisposeAsync().AsTask();
            Assert.IsFalse(disposing.IsCompleted, "Provider disposal must join the same actual registered capture owner.");

            release.Set();
            CapturedAuditRecord completed = await capturing.WaitAsync(TimeSpan.FromSeconds(10));
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));
            await disposing.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.AreNotEqual(Guid.Empty, completed.AuditId);
        }
        finally
        {
            release.Set();
            await capturing;
            if (stopping is not null)
            {
                await stopping;
            }
            if (disposing is not null)
            {
                await disposing;
            }
        }
    }

    [TestMethod]
    public async Task FailedBuildCloseAlsoClosesACaptureConstructedAfterTheFallbackWasObserved()
    {
        var services = new ServiceCollection();
        services.ConfigureCisAudit(new("audit"));
        await using ServiceProvider provider = services.BuildServiceProvider();
        AuditHostingRegistration registration = provider.GetRequiredService<AuditHostingRegistration>();
        await registration.StopAsync();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IAuditCapture capture = scope.ServiceProvider.GetRequiredService<IAuditCapture>();

        AuditCaptureException closed = Assert.ThrowsExactly<AuditCaptureException>(() => capture.CaptureLocalSystem(
            new AuditEventDefinition<int>("example.changed", 1, AuditCategory.Business, new NumberDetails()), 1));

        Assert.AreEqual(AuditCaptureFailure.Unavailable, closed.Failure);
    }

    private sealed class UnexpectedResolution : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException("Startup cancellation must precede service resolution.");
    }

    private class NumberDetails : IAuditDetailsCodec<int>
    {
        public void Validate(int details)
        {
        }

        public virtual void Write(Utf8JsonWriter writer, int details)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", details);
            writer.WriteEndObject();
        }
    }

    private sealed class HoldingDetails(ManualResetEventSlim entered, ManualResetEventSlim release) : NumberDetails
    {
        public override void Write(Utf8JsonWriter writer, int details)
        {
            entered.Set();
            release.Wait();
            base.Write(writer, details);
        }
    }

    private sealed class NumberMessageCodec : IMessageCodec<int>
    {
        public MessageContract Contract { get; } = new("foreign.number", 1);
        public byte[] Encode(int value) => JsonSerializer.SerializeToUtf8Bytes(value);
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
        {
            value = JsonSerializer.Deserialize<int>(json.Span);
            return contract == Contract;
        }
    }
}
