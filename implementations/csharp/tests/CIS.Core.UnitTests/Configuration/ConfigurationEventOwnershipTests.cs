using CIS.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Configuration;

[TestClass]
public sealed class ConfigurationEventOwnershipTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DirectInterfaceAndOptionalBasePublishTheRetainedSenderInOrderDespiteSubscriberAndReporterFailure(bool direct)
    {
        IConfigurationObject root = direct ? new W1DirectSettings() : new W1Settings();
        Type type = root.GetType();
        var order = new List<string>();
        var arguments = new List<ConfigurationChangedEventArgs>();
        int failures = 0;
        root.ConfigurationChanged += (sender, change) =>
        {
            Assert.AreSame(root, sender);
            order.Add("first");
            arguments.Add(change);
            throw new InvalidOperationException("A synchronous consumer failed.");
        };
        root.ConfigurationChanged += (sender, change) =>
        {
            Assert.AreSame(root, sender);
            order.Add("second");
            arguments.Add(change);
        };
        var options = ConfigurationObjectTestSupport.Options(factories:
            new Dictionary<Type, Func<IConfigurationObject>> { [type] = () => root });
        ConfigurationCatalog catalog = ConfigurationCatalog.Create(new[] { type }, options);
        Assert.HasCount(0, order, "Constructing the retained baseline must not emit a reload notification.");
        Assert.AreSame(root, catalog.Entries.Single().Instance);

        ConfigurationEvents.Publish(root, 7, _ => { failures++; throw new InvalidOperationException("Diagnostic observer failed."); });

        CollectionAssert.AreEqual(new[] { "first", "second" }, order);
        Assert.AreEqual(1, failures);
        Assert.HasCount(2, arguments);
        foreach (ConfigurationChangedEventArgs change in arguments)
        {
            Assert.AreSame(type, change.ConfigurationType);
            Assert.AreEqual(7L, change.Revision);
        }
    }

    [TestMethod]
    public void PublicationUsesASubscriberSnapshotAndReleasesItsSubscriptionGateBeforeCallingConsumers()
    {
        var root = new W1DirectSettings();
        ConfigurationCatalog.Create(new[] { typeof(W1DirectSettings) }, ConfigurationObjectTestSupport.Options(
            factories: new Dictionary<Type, Func<IConfigurationObject>> { [typeof(W1DirectSettings)] = () => root }));
        var order = new List<string>();
        var failures = new List<Exception>();
        EventHandler<ConfigurationChangedEventArgs> second = (_, _) => order.Add("second");
        EventHandler<ConfigurationChangedEventArgs> third = (_, _) => order.Add("third");
        bool changedSubscriptions = false;
        root.ConfigurationChanged += (_, _) =>
        {
            order.Add("first");
            if (changedSubscriptions) { return; }
            changedSubscriptions = true;
            Task.Run(() => root.ConfigurationChanged += third)
                .WaitAsync(ConfigurationObjectTestSupport.Deadline).GetAwaiter().GetResult();
            root.ConfigurationChanged -= second;
        };
        root.ConfigurationChanged += second;

        ConfigurationEvents.Publish(root, 1, failures.Add);
        CollectionAssert.AreEqual(new[] { "first", "second" }, order,
            "A subscription change inside a handler affects the next publication.");
        Assert.HasCount(0, failures, "An independent subscription must not block behind a handler-held helper gate.");
        order.Clear();
        ConfigurationEvents.Publish(root, 2, failures.Add);
        CollectionAssert.AreEqual(new[] { "first", "third" }, order);
        Assert.HasCount(0, failures);
    }

    [TestMethod]
    public void DuplicateSubscriptionRemovalPreservesNormalEventSemantics()
    {
        var root = new W1DirectSettings();
        int notifications = 0;
        EventHandler<ConfigurationChangedEventArgs> handler = (_, _) => notifications++;
        root.ConfigurationChanged += handler;
        root.ConfigurationChanged += handler;
        root.ConfigurationChanged -= handler;
        ConfigurationEvents.Publish(root, 1);
        Assert.AreEqual(1, notifications);
        root.ConfigurationChanged -= handler;
        ConfigurationEvents.Publish(root, 2);
        Assert.AreEqual(1, notifications);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnconnectedFieldEventsAndAnUnusableRemoveAccessorAreRejectedBeforeDelivery(bool brokenRemove)
    {
        Type type = brokenRemove ? typeof(W1BrokenRemoveSettings) : typeof(W1FieldEventSettings);
        ConfigurationObjectTestSupport.AssertIssue("Configuration.EventWiring", () =>
            ConfigurationCatalog.Create(new[] { type }, ConfigurationObjectTestSupport.Options()));
    }

    [TestMethod]
    public void AFactoryCannotShareOneLiveEventOwnerBetweenSeparateCatalogs()
    {
        var root = new W1DirectSettings();
        var factories = new Dictionary<Type, Func<IConfigurationObject>> { [typeof(W1DirectSettings)] = () => root };
        ConfigurationCatalog first = ConfigurationCatalog.Create(new[] { typeof(W1DirectSettings) },
            ConfigurationObjectTestSupport.Options("HostA", factories));
        ConfigurationObjectTestSupport.AssertIssue("Configuration.Ownership", () =>
            ConfigurationCatalog.Create(new[] { typeof(W1DirectSettings) }, ConfigurationObjectTestSupport.Options("HostB", factories)));
        Assert.AreEqual(5, first.Reader.Read(root, current => current.Value));
        int notifications = 0;
        root.ConfigurationChanged += (_, _) => notifications++;
        ConfigurationEvents.Publish(root, 1);
        Assert.AreEqual(1, notifications, "Rejecting another owner must not break the first retained object.");
    }
}
