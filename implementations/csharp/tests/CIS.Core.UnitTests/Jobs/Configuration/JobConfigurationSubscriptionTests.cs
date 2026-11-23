using CIS.Configuration;
using CIS.Jobs;
using CIS.Tests.UnitTests.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Configuration;

[TestClass]
public sealed class JobConfigurationSubscriptionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task ConstructionIsPassiveAndConnectCapturesAllSettingsInsideTheExistingCoordinatedReader()
    {
        var fixture = new Fixture();
        int subscriptions = fixture.Root.Adds;
        JobConfigurationSnapshot defaults = fixture.Runtime.Configuration;
        int captures = 0;
        await using var binding = fixture.Bind((root, revision) =>
        {
            Assert.IsTrue(fixture.Reader.InsideCallback);
            captures++;
            return Capture(root, revision);
        });
        Assert.AreEqual(subscriptions, fixture.Root.Adds);
        Assert.AreEqual(0, fixture.Reader.Calls);
        Assert.AreEqual(0, captures);
        Assert.AreSame(defaults, fixture.Runtime.Configuration);

        binding.Connect();
        binding.Connect();
        Assert.AreEqual(subscriptions + 1, fixture.Root.Adds);
        Assert.AreEqual(1, captures);
        Assert.AreEqual(1, fixture.Reader.Calls);
        Assert.IsTrue(fixture.Runtime.Configuration.Enabled);
        Assert.IsTrue(fixture.Runtime.Configuration.Jobs[("wallet-service", "wallet.compaction")].Enabled);
        Assert.AreEqual(3, fixture.Runtime.Configuration.Categories[("wallet-service", "maintenance")].MaximumInvocations);
        Assert.AreEqual(0L, fixture.Runtime.Configuration.Revision);
    }

    [TestMethod]
    public async Task AcceptedEventsAdoptWholeRevisionsAndAnInvalidUpdatePreservesTheLastCompleteSnapshot()
    {
        var fixture = new Fixture();
        await using var binding = fixture.Bind(Capture);
        binding.Connect();
        fixture.Update(root => { root.MaximumInvocations = 2; root.CategoryEnabled = false; }, 1);
        JobConfigurationSnapshot accepted = fixture.Runtime.Configuration;
        Assert.AreEqual(1L, accepted.Revision);
        Assert.IsFalse(accepted.Categories[("wallet-service", "maintenance")].Enabled);
        Assert.AreEqual(2, accepted.Categories[("wallet-service", "maintenance")].MaximumInvocations);

        fixture.Update(root => { root.HostEnabled = false; root.CategoryId = "unknown"; }, 2);
        Assert.AreSame(accepted, fixture.Runtime.Configuration);
        Assert.IsTrue(fixture.Runtime.Configuration.Enabled, "The invalid candidate must not partially disable the accepted host.");
        Assert.IsNotNull(fixture.Runtime.RejectedConfiguration);
        Assert.AreEqual(2L, fixture.Runtime.RejectedConfiguration.Revision);
        Assert.AreEqual("jobs.configuration.invalid", fixture.Runtime.RejectedConfiguration.ReasonCode);

        fixture.Update(root => { root.CategoryId = "maintenance"; root.HostEnabled = false; }, 3);
        Assert.AreEqual(3L, fixture.Runtime.Configuration.Revision);
        Assert.IsFalse(fixture.Runtime.Configuration.Enabled);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CaptureFailureOrWrongSuppliedRevisionRecordsSafeRejectionWithoutReplacingSettings(bool wrongRevision)
    {
        var fixture = new Fixture();
        await using var binding = fixture.Bind((root, revision) => revision == 0 ? Capture(root, revision)
            : wrongRevision ? Capture(root, revision + 1) : throw new FormatException("private configuration detail"));
        binding.Connect();
        JobConfigurationSnapshot accepted = fixture.Runtime.Configuration;
        fixture.Update(root => root.HostEnabled = false, 4);
        Assert.AreSame(accepted, fixture.Runtime.Configuration);
        Assert.IsNotNull(fixture.Runtime.RejectedConfiguration);
        Assert.AreEqual(4L, fixture.Runtime.RejectedConfiguration.Revision);
        Assert.AreEqual("jobs.configuration.capture_failed", fixture.Runtime.RejectedConfiguration.ReasonCode);
    }

    [TestMethod]
    public async Task EventObservedAfterSubscriptionBeforeInitialReadWinsOverInitialRevisionZero()
    {
        var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.BeforeRead = call =>
        {
            if (call == 1)
            {
                entered.TrySetResult();
                Assert.IsTrue(release.Wait(Deadline));
            }
        };
        await using var binding = fixture.Bind(Capture);
        Task connecting = Task.Run(binding.Connect);
        try
        {
            await entered.Task.WaitAsync(Deadline);
            // The first reader is blocked BEFORE entering the value gate. The actual event can
            // therefore perform its own coordinated read and adoption before initial reading resumes.
            fixture.Update(root => root.MaximumInvocations = 1, 1);
            JobConfigurationSnapshot accepted = fixture.Runtime.Configuration;
            Assert.AreEqual(1L, accepted.Revision);
            release.Set();
            await connecting.WaitAsync(Deadline);
            Assert.AreSame(accepted, fixture.Runtime.Configuration);
            Assert.AreEqual(2, fixture.Reader.Calls);
            Assert.IsNull(fixture.Runtime.RejectedConfiguration);
        }
        finally
        {
            release.Set();
            await connecting;
        }
    }

    [TestMethod]
    public async Task DisposalUnsubscribesAndJoinsAnAdmittedReadWithoutApplyingItsLateSnapshot()
    {
        var fixture = new Fixture();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Reader.BeforeRead = call =>
        {
            if (call == 2)
            {
                entered.TrySetResult();
                Assert.IsTrue(release.Wait(Deadline));
            }
        };
        var binding = fixture.Bind(Capture);
        binding.Connect();
        int removals = fixture.Root.Removes;
        JobConfigurationSnapshot accepted = fixture.Runtime.Configuration;
        Task updating = Task.Run(() => fixture.Update(root => root.HostEnabled = false, 1));
        Task? disposing = null;
        try
        {
            await entered.Task.WaitAsync(Deadline);
            disposing = binding.DisposeAsync().AsTask();
            Assert.AreEqual(removals + 1, fixture.Root.Removes);
            Assert.IsFalse(disposing.IsCompleted);
            release.Set();
            await updating.WaitAsync(Deadline);
            await disposing.WaitAsync(Deadline);
            Assert.AreSame(accepted, fixture.Runtime.Configuration);
            fixture.Update(root => root.MaximumInvocations = 1, 2);
            Assert.AreEqual(2, fixture.Reader.Calls, "No callback/read can be admitted after unsubscription.");
            Assert.IsNull(fixture.Runtime.RejectedConfiguration);
            await fixture.Runtime.StartAsync(); // The borrowed runtime was not disposed by the binding.
        }
        finally
        {
            release.Set();
            await updating;
            if (disposing is not null)
            {
                await disposing;
            }
            await binding.DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InvalidInitialCaptureFailsClearlyAndRemovesItsActualSubscription(bool wrongRevision)
    {
        var fixture = new Fixture();
        int adds = fixture.Root.Adds;
        int removes = fixture.Root.Removes;
        await using var binding = fixture.Bind((root, revision) => wrongRevision ? Capture(root, revision + 1)
            : throw new FormatException("private initial detail"));
        Assert.ThrowsExactly<InvalidOperationException>(binding.Connect);
        Assert.AreEqual(adds + 1, fixture.Root.Adds);
        Assert.AreEqual(removes + 1, fixture.Root.Removes);
        Assert.IsNotNull(fixture.Runtime.RejectedConfiguration);
        Assert.AreEqual(0L, fixture.Runtime.RejectedConfiguration.Revision);
        Assert.IsFalse(fixture.Runtime.Configuration.Enabled);
        fixture.Update(root => root.HostEnabled = false, 1);
        Assert.AreEqual(1, fixture.Reader.Calls);
        Assert.ThrowsExactly<ObjectDisposedException>(binding.Connect);
    }

    private static JobConfigurationSnapshot Capture(JobRoot root, long revision) => new(revision, root.HostEnabled,
        [new("wallet-service", "maintenance", root.CategoryEnabled, root.MaximumInvocations)],
        [new("wallet-service", "wallet.compaction", new(JobExecutionMode.Continuous), root.JobEnabled, root.CategoryId)]);

    private sealed class Fixture
    {
        private readonly ConfigurationEntry entry;

        internal Fixture()
        {
            Root = new();
            ConfigurationCatalog configuration = ConfigurationCatalog.Create([typeof(JobRoot)], ConfigurationObjectTestSupport.Options(
                factories: new Dictionary<Type, Func<IConfigurationObject>> { [typeof(JobRoot)] = () => Root }));
            entry = configuration.Entries.Single();
            Reader = new(configuration.Reader);
            var catalog = new JobCatalog([new("wallet-service", "maintenance", "Maintenance")],
                [JobHandlerBinding.Continuous<NeverConstructedHandler>(new("wallet-service", "wallet.compaction", "Compaction", "maintenance",
                    [JobExecutionMode.Continuous], ["jobs.execute"]))]);
            Runtime = new(catalog, new UnusedScopeFactory());
        }

        internal JobRoot Root
        {
            get;
        }
        internal ObservedReader Reader
        {
            get;
        }
        internal JobRuntime Runtime
        {
            get;
        }

        internal JobConfigurationSubscription<JobRoot> Bind(Func<JobRoot, long, JobConfigurationSnapshot> capture)
            => new(Runtime, Reader, Root, capture);

        internal void Update(Action<JobRoot> update, long revision)
        {
            lock (entry.ValueGate)
            {
                update(Root);
            }
            ConfigurationEvents.Publish(Root, revision);
        }
    }

    private sealed class ObservedReader(IConfigurationReader inner) : IConfigurationReader
    {
        private int callbackThread;
        internal int Calls;
        internal Action<int>? BeforeRead;
        internal bool InsideCallback => Volatile.Read(ref callbackThread) == Environment.CurrentManagedThreadId;

        public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> reader)
            where TConfiguration : class, IConfigurationObject
        {
            int call = Interlocked.Increment(ref Calls);
            BeforeRead?.Invoke(call); // Deliberately outside the real value gate.
            return inner.Read(configuration, current =>
            {
                Volatile.Write(ref callbackThread, Environment.CurrentManagedThreadId);
                try
                {
                    return reader(current);
                }
                finally
                {
                    Volatile.Write(ref callbackThread, 0);
                }
            });
        }
    }

    private sealed class JobRoot : IConfigurationObject
    {
        public JobRoot()
        {
        }
        public bool HostEnabled { get; set; } = true;
        public bool CategoryEnabled { get; set; } = true;
        public bool JobEnabled { get; set; } = true;
        public int MaximumInvocations { get; set; } = 3;
        public string CategoryId { get; set; } = "maintenance";
        internal int Adds;
        internal int Removes;

        public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged
        {
            add
            {
                Interlocked.Increment(ref Adds);
                ConfigurationEvents.Add(this, value);
            }
            remove
            {
                Interlocked.Increment(ref Removes);
                ConfigurationEvents.Remove(this, value);
            }
        }
    }

    private sealed class UnusedScopeFactory : IJobExecutionScopeFactory
    {
        public ValueTask<IJobExecutionScope> CreateAsync(JobScopeRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Configuration binding must not create workload scopes.");
    }

    private sealed class NeverConstructedHandler : IContinuousJobHandler
    {
        public NeverConstructedHandler() => throw new AssertFailedException("Configuration binding must not construct handlers.");

        public Task<ContinuousIterationResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Configuration binding must not execute a handler.");
    }
}
