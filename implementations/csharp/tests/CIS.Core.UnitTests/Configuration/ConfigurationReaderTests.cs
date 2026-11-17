using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DetachedCandidatesAndAnotherHostsRootCannotBeReadThroughThisCatalog(bool anotherHost)
    {
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options("HostA"));
        var foreign = anotherHost
            ? (W1Settings)ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options("HostB")).Entries.Single().Instance
            : new W1Settings();
        bool called = false;
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Reader", () =>
            catalog.Reader.Read(foreign, current => { called = true; return current.First; }));
        Assert.IsFalse(called);
        var retained = (W1Settings)catalog.Entries.Single().Instance;
        Assert.AreEqual(10, catalog.Reader.Read(retained, current => current.First));
    }

    [TestMethod]
    public void ReaderUsesReferenceIdentityEvenWhenObjectsClaimValueEqualityAndHaveAnUnusableHashCode()
    {
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(EqualSettings) }, ConfigurationObjectTestSupport.Options());
        var retained = (EqualSettings)catalog.Entries.Single().Instance;
        var candidate = new EqualSettings();
        Assert.IsTrue(retained.Equals(candidate));
        Assert.AreEqual(17, catalog.Reader.Read(retained, current => current.Value));
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Reader", () => catalog.Reader.Read(candidate, current => current.Value));
        IConfigurationObject interfaceReference = retained;
        Assert.AreEqual(17, catalog.Reader.Read(interfaceReference, current => ((EqualSettings)current).Value));
    }

    [TestMethod]
    public void CoordinatedReaderWaitsForThePrivateCommitGateAndNeverReturnsThePartiallyAssignedTuple()
    {
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options());
        ConfigurationEntry entry = catalog.Entries.Single();
        var retained = (W1Settings)entry.Instance;
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        (int First, int Second)? observed = null;
        Exception? error = null;
        var readerThread = new Thread(() =>
        {
            attempted.TrySetResult();
            try { observed = catalog.Reader.Read(retained, current => (current.First, current.Second)); }
            catch (Exception failure) { error = failure; }
            finally { finished.TrySetResult(); }
        }) { IsBackground = true };
        bool started = false;
        try
        {
            // This is the legitimate internal commit gate, held between two
            // prepared assignments. No storage, setters with I/O, or sleep is used.
            lock (entry.ValueGate)
            {
                retained.First = 100;
                readerThread.Start();
                started = true;
                Assert.IsTrue(attempted.Task.Wait(ConfigurationObjectTestSupport.Deadline));
                Assert.IsTrue(SpinWait.SpinUntil(() => finished.Task.IsCompleted
                    || (readerThread.ThreadState & ThreadState.WaitSleepJoin) != 0, ConfigurationObjectTestSupport.Deadline),
                    "Observe either actual completion or the reader blocked on the held gate.");
                Assert.IsFalse(finished.Task.IsCompleted, "A reader must not return while only the first assignment is visible.");
                retained.Second = 200;
            }
            Assert.IsTrue(finished.Task.Wait(ConfigurationObjectTestSupport.Deadline));
            Assert.IsNull(error);
            Assert.IsTrue(observed.HasValue);
            Assert.AreEqual((100, 200), observed.GetValueOrDefault());
        }
        finally
        {
            // The gate has been released even if an assertion failed.
            if (started) { Assert.IsTrue(readerThread.Join(ConfigurationObjectTestSupport.Deadline)); }
        }
    }

    [TestMethod]
    public void PublicObjectLockDoesNotControlTheReadersPrivateSynchronizationGate()
    {
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { typeof(W1Settings) }, ConfigurationObjectTestSupport.Options());
        var retained = (W1Settings)catalog.Entries.Single().Instance;
        lock (retained)
        {
            int value = Task.Run(() => catalog.Reader.Read(retained, current => current.First))
                .WaitAsync(ConfigurationObjectTestSupport.Deadline).GetAwaiter().GetResult();
            Assert.AreEqual(10, value);
        }
    }

    private sealed class EqualSettings : ConfigurationObject
    {
        public EqualSettings() { }
        public int Value { get; set; } = 17;
        public override bool Equals(object? obj) => obj is EqualSettings;
        public override int GetHashCode() => throw new InvalidOperationException("Retained ownership must not use application hashing.");
    }
}
