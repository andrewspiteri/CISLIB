using CIS.Configuration;
using CIS.Hosting;
using CIS.TypeManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Application;

[TestClass]
public sealed class CISApplicationSetupTests
{
    [TestMethod]
    public async Task OrdinaryConstructorFormsCollectSetupWithoutResolvingOrBuildingServices()
    {
        int callbacks = 0, constructions = 0;
        Action<CISApplicationBuilder> configure = builder =>
        {
            callbacks++;
            Assert.AreSame(builder.Host.Services, builder.Services);
            Assert.IsNull(builder.WebHost);
            Assert.AreEqual(0, builder.RestClients.Count);
            builder.Services.AddSingleton(_ => { constructions++; return new object(); });
        };
        await using var first = new CISApplication();
        await using var second = new CISApplication([]);
        await using var third = new CISApplication(configure);
        await using var fourth = new CISApplication([], configure);
        Assert.AreEqual(2, callbacks);
        Assert.AreEqual(0, constructions);
        Assert.Throws<InvalidOperationException>(() => _ = fourth.Services);
    }

    [TestMethod]
    public async Task WebConstructionDoesNotBuildMapRoutesOrStartAListener()
    {
        int maps = 0;
        await using CISApplication app = CISApplication.CreateWeb([], builder =>
        { Assert.IsNotNull(builder.WebHost); }, _ => maps++);
        Assert.AreEqual(0, maps);
        Assert.Throws<InvalidOperationException>(() => _ = app.Services);
        await app.StopAsync();
        await Assert.ThrowsAsync<TaskCanceledException>(() => app.StartAsync());
        Assert.AreEqual(0, maps);
    }

    [TestMethod]
    public void ConflictingBootstrapIdentityFailsDuringSetupWithoutReadingAStore()
    {
        var store = new NeverReadStore();
        Assert.Throws<ArgumentException>(() => new CISApplication(builder =>
        {
            builder.TypeManager = new TypeManagerOptions(typeof(CISApplicationSetupTests).Assembly);
            builder.Configuration = new ConfigurationOptions("another-host", store);
        }));
        Assert.AreEqual(0, store.Reads);
    }

    private sealed class NeverReadStore : IConfigurationStore
    {
        internal int Reads;
        public ConfigurationNotificationMode NotificationMode => ConfigurationNotificationMode.None;
        public ValueTask<ConfigurationReadResult> ReadAsync(ConfigurationRecordKey key, string? sourceToken, CancellationToken cancellationToken)
        { Reads++; throw new InvalidOperationException("Setup must not read storage."); }
    }
}
