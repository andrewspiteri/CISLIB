using CIS.CommonTesting;
using CIS.Configuration;
using CIS.Hosting;
using CIS.Management;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Examples.CountryData.UnitTests.Configuration;

[TestClass]
public sealed class CountryCentralConfigurationExamples
{
    [TestMethod]
    public async Task OptInIsPassiveAndKeepsOnlyTheRestartRequiredSettingsWithExistingDefaultsAndValidators()
    {
        var script = new ScriptedManagementConfigurationClient();
        var legacy = new ManagementConfigurationStore(script);
        ConfigurationOptions? selected = null;
        int prepared = 0;
        int validated = 0;
        Func<IConfigurationObject> factory = () => new CountrySettings { SourceUrl = "https://provider.invalid/countries" };
        Action<IConfigurationObject> otherValidator = _ => { };
        CISApplicationCentralConfigurationOptions central = Bootstrap(() => prepared++);
        await using CISApplication app = CountryApplication.CreateService([], builder =>
        {
            builder.TypeManager = new(typeof(CountryApplication).Assembly);
            builder.Configuration = new(typeof(CountryApplication).Assembly.GetName().Name!, legacy)
            {
                IsHealthCritical = false,
                Factories = new Dictionary<Type, Func<IConfigurationObject>> { [typeof(CountrySettings)] = factory },
                Validators = new Dictionary<Type, Action<IConfigurationObject>>
                {
                    [typeof(CountrySettings)] = _ => validated++,
                    [typeof(CountrySecuritySettings)] = otherValidator
                }
            };
            CountryCentralConfiguration.Configure(builder, central, TimeSpan.FromSeconds(30));
            selected = builder.Configuration;
            Assert.AreSame(central, builder.CentralConfiguration);
        });
        Assert.IsNotNull(selected);
        Assert.AreEqual("CIS.CountryData", selected.HostApplicationName);
        Assert.AreSame(legacy, selected.Store);
        Assert.AreSame(factory, selected.Factories[typeof(CountrySettings)]);
        Assert.AreSame(otherValidator, selected.Validators[typeof(CountrySecuritySettings)]);
        Assert.IsFalse(selected.IsHealthCritical!.Value);
        Assert.AreEqual(TimeSpan.FromSeconds(30), selected.PollingInterval);
        Assert.IsNull(selected.SnapshotTarget);
        Assert.HasCount(1, selected.Enrollments);
        ConfigurationEnrollment enrollment = selected.Enrollments.Single();
        Assert.AreEqual(typeof(CountrySettings), enrollment.ConfigurationType);
        Assert.AreEqual("country.settings", enrollment.ConfigurationId);
        Assert.AreEqual("1", enrollment.SchemaVersion);
        Assert.AreEqual(ConfigurationUpdateMode.RestartRequired, enrollment.UpdateMode);
        Assert.AreEqual(0, validated);
        selected.Validators[typeof(CountrySettings)](new CountrySettings());
        Assert.AreEqual(1, validated);
        Assert.AreEqual(0, prepared);
        Assert.AreEqual(0L, script.TotalCalls, "Selecting the tutorial helper performs no credential or configuration request.");
    }

    [TestMethod]
    [DataRow("scheme")]
    [DataRow("userinfo")]
    [DataRow("refresh-zero")]
    [DataRow("refresh-too-long")]
    [DataRow("timeout-negative")]
    [DataRow("timeout-too-long")]
    [DataRow("retry")]
    public async Task TypedCandidateValidationRejectsUnsafeSettingsBeforeRestartStaging(string invalid)
    {
        ConfigurationOptions? options = null;
        await using CISApplication app = CountryApplication.CreateService([], builder =>
        {
            builder.TypeManager = new(typeof(CountryApplication).Assembly);
            CountryCentralConfiguration.Configure(builder, Bootstrap(), TimeSpan.FromSeconds(30));
            options = builder.Configuration;
        });
        var settings = new CountrySettings();
        switch (invalid)
        {
            case "scheme":
                settings.SourceUrl = "file:///C:/private/countries.json";
                break;
            case "userinfo":
                settings.SourceUrl = "https://user:secret@provider.invalid/countries";
                break;
            case "refresh-zero":
                settings.RefreshInterval = TimeSpan.Zero;
                break;
            case "refresh-too-long":
                settings.RefreshInterval = TimeSpan.MaxValue;
                break;
            case "timeout-negative":
                settings.RequestTimeout = TimeSpan.FromMilliseconds(-1);
                break;
            case "timeout-too-long":
                settings.RequestTimeout = TimeSpan.MaxValue;
                break;
            case "retry":
                settings.RetryPolicyName = "not-installed";
                break;
            default:
                Assert.Fail("Select a declared invalid candidate.");
                break;
        }
        Assert.ThrowsExactly<ArgumentException>(() => options!.Validators[typeof(CountrySettings)](settings));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("transient-http")]
    public async Task TutorialCandidatesPermitExplicitNoneAndTheActuallyInstalledPreset(string? retry)
    {
        ConfigurationOptions? options = null;
        await using CISApplication app = CountryApplication.CreateService([], builder =>
        {
            builder.TypeManager = new(typeof(CountryApplication).Assembly);
            CountryCentralConfiguration.Configure(builder, Bootstrap(), TimeSpan.FromSeconds(30));
            options = builder.Configuration;
        });
        options!.Validators[typeof(CountrySettings)](new CountrySettings { RetryPolicyName = retry });
    }

    [TestMethod]
    public async Task HelperDoesNotSilentlyEnrollSecurityOrReplaceAnotherCentralProfile()
    {
        await using CISApplication app = CountryApplication.CreateService([], builder =>
        {
            builder.TypeManager = new(typeof(CountryApplication).Assembly);
            builder.Configuration = new(typeof(CountryApplication).Assembly.GetName().Name!)
            {
                Enrollments = [new(typeof(CountrySecuritySettings), "security.settings", "1", ConfigurationUpdateMode.RestartRequired)]
            };
            Assert.ThrowsExactly<ArgumentException>(() => CountryCentralConfiguration.Configure(builder, Bootstrap(), TimeSpan.FromSeconds(30)));
            Assert.IsNull(builder.CentralConfiguration);
            Assert.AreEqual(typeof(CountrySecuritySettings), builder.Configuration.Enrollments.Single().ConfigurationType);
            builder.Configuration = null;
            CountryCentralConfiguration.Configure(builder, Bootstrap(), TimeSpan.FromSeconds(30));
            Assert.ThrowsExactly<ArgumentException>(() => CountryCentralConfiguration.Configure(builder, Bootstrap(), TimeSpan.FromSeconds(30)));
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task PollingMustBeSelectedExplicitlyWithAPositiveSupportedInterval(int seconds)
    {
        await using CISApplication app = CountryApplication.CreateService([], builder =>
        {
            builder.TypeManager = new(typeof(CountryApplication).Assembly);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CountryCentralConfiguration.Configure(builder, Bootstrap(), TimeSpan.FromSeconds(seconds)));
            Assert.IsNull(builder.CentralConfiguration);
        });
    }

    private static CISApplicationCentralConfigurationOptions Bootstrap(Action? invoked = null)
    {
        var management = new ManagementClientOptions("example-management") { BaseUrl = "https://management.invalid/" };
        var configuration = new ManagementConfigurationClientOptions("example-configuration") { BaseUrl = management.BaseUrl };
        return new(management, configuration, new(management.RestClientName), _ =>
        {
            invoked?.Invoke();
            return Task.FromException<CISApplicationCentralConfigurationPreparation>(new InvalidOperationException("Passive unit examples must not acquire native credentials."));
        });
    }
}
