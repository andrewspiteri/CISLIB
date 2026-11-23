using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.CommonTesting;
using CIS.Hosting;
using CIS.Messaging;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.Application;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditApplicationIntegrationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FailedActualHostBuildPreservesItsFaultAndJoinsRegisteredCaptureBeforeDisposingDependencies(bool web)
    {
        using var directory = new TemporaryDirectory();
        TypeManagerOptions inventory = ApplicationTestSupport.Inventory(directory);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var ownedDependency = new ApplicationTestSupport.Dependency();
        var borrowedStore = new BorrowedStore();
        var scripted = new ScriptedAuditWriter();
        scripted.EnqueueSubmit(new(MessageSubmissionOutcome.NotSubmitted));
        var original = new InvalidOperationException("The actual host lifetime factory failed after starting Audit capture.");
        var definition = new AuditEventDefinition<int>("example.held", 1, AuditCategory.Business,
            new HeldDetails(entered, release, ownedDependency, borrowedStore));
        AsyncServiceScope? sourceScope = null;
        IAuditCapture? capture = null;
        Task<CapturedAuditRecord>? capturing = null;
        int factories = 0;
        int maps = 0;

        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.ConfigureCisAudit(new("audit"));
            builder.Services.AddSingleton<IAuditWriter>(scripted);
            builder.Services.AddSingleton(_ => ownedDependency);
            builder.Services.AddSingleton(borrowedStore);
            builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
            {
                Interlocked.Increment(ref factories);
                Assert.AreSame(ownedDependency, root.GetRequiredService<ApplicationTestSupport.Dependency>());
                Assert.AreSame(borrowedStore, root.GetRequiredService<BorrowedStore>());
                AsyncServiceScope actualScope = root.CreateAsyncScope();
                sourceScope = actualScope;
                IAuditCapture actualCapture = actualScope.ServiceProvider.GetRequiredService<IAuditCapture>();
                capture = actualCapture;
                capturing = Task.Run(() => actualCapture.CaptureLocalSystem(definition, 42));
                Assert.IsTrue(entered.Wait(ApplicationTestSupport.Deadline), "The actual registered capture never entered its mapper.");
                throw original;
            }));
        }

        CISApplication app = web
            ? CISApplication.CreateWeb([], Configure, _ =>
            {
                maps++;
            })
            : new CISApplication(Configure);
        Task startup = app.StartAsync();
        Task? stopping = null;
        Task? disposal = null;
        try
        {
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(startup));
            Assert.AreSame(original, app.Failure);
            Assert.IsTrue(startup.IsFaulted);
            Assert.AreEqual(1, factories);
            Assert.AreEqual(0, maps, "Web routes cannot map before the actual host Build returns.");
            Assert.Throws<InvalidOperationException>(() => _ = app.Services);
            Assert.IsNotNull(capturing);
            Assert.IsNotNull(capture);
            Assert.IsFalse(capturing.IsCompleted);

            stopping = app.StopAsync();
            Assert.AreSame(stopping, app.StopAsync());
            Assert.IsFalse(stopping.IsCompleted, "Facade Stop must join the actual registered capture even when no Audit runtime was built.");
            AuditCaptureException closed = Assert.ThrowsExactly<AuditCaptureException>(() => capture.CaptureLocalSystem(
                new AuditEventDefinition<int>("example.refused", 1, AuditCategory.Business, new NumberDetails()), 1));
            Assert.AreEqual(AuditCaptureFailure.Unavailable, closed.Failure);
            disposal = app.DisposeAsync().AsTask();
            Assert.AreSame(disposal, app.DisposeAsync().AsTask());
            Assert.IsFalse(disposal.IsCompleted, "Facade Dispose cannot dispose the provider while a capture mapper still owns its dependencies.");
            Assert.AreEqual(0, ownedDependency.Disposals);
            Assert.AreEqual(0, borrowedStore.Disposals);
            Assert.AreEqual(1, scripted.PendingScripts, "Passive capture cannot invoke or invent an Audit submission.");

            release.Set();
            CapturedAuditRecord completed = await capturing.WaitAsync(ApplicationTestSupport.Deadline);
            await stopping.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);

            Assert.AreNotEqual(Guid.Empty, completed.AuditId);
            Assert.AreEqual(AuditActorKind.System, completed.Record.Actor.Kind);
            Assert.AreEqual(1, ownedDependency.Disposals);
            Assert.AreEqual(0, borrowedStore.Disposals, "The application's instance registration remains caller owned.");
            Assert.AreEqual(1, borrowedStore.Uses);
            Assert.AreSame(original, app.Failure);
            Assert.AreSame(startup, app.StartAsync());
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(app.StartAsync()));
        }
        finally
        {
            release.Set();
            await ApplicationTestSupport.SettleAsync(capturing);
            await ApplicationTestSupport.SettleAsync(startup);
            await ApplicationTestSupport.SettleAsync(stopping);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
            if (sourceScope is { } scope)
            {
                await scope.DisposeAsync();
            }
            await borrowedStore.DisposeAsync();
        }
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

    private sealed class HeldDetails(ManualResetEventSlim entered, ManualResetEventSlim release,
        ApplicationTestSupport.Dependency dependency, BorrowedStore store) : NumberDetails
    {
        public override void Write(Utf8JsonWriter writer, int details)
        {
            entered.Set();
            release.Wait();
            dependency.Use();
            store.Use();
            base.Write(writer, details);
        }
    }

    // This application-owned object supplies no SQL/broker behavior or persistence evidence.
    private sealed class BorrowedStore : IAsyncDisposable
    {
        private int disposals;
        private int uses;

        internal int Disposals => Volatile.Read(ref disposals);
        internal int Uses => Volatile.Read(ref uses);

        internal void Use()
        {
            Assert.AreEqual(0, Disposals, "A captured dependency cannot be disposed before actual mapper settlement.");
            Interlocked.Increment(ref uses);
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref disposals);
            return ValueTask.CompletedTask;
        }
    }
}
