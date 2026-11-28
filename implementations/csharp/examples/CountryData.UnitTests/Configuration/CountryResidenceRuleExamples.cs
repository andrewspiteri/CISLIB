using CIS.Audit;
using CIS.Configuration;
using CIS.Hosting;
using CIS.Management;
using CIS.Messaging;
using CIS.RuleEngine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Examples.CountryData.UnitTests.Configuration;

[TestClass]
public sealed class CountryResidenceRuleExamples
{
    [TestMethod]
    public async Task OrdinaryCountryDataSelectsNoManagementRuleEngineAuditOrDatabase()
    {
        await using CISApplication application = CountryApplication.CreateService([], builder =>
        {
            Assert.IsNull(builder.CentralConfiguration);
            Assert.IsNull(builder.Configuration);
            Assert.IsFalse(builder.Services.Any(item => item.ServiceType == typeof(IRuleEvaluator)
                || item.ServiceType == typeof(IAuditWriter) || item.ServiceType == typeof(IMessagingStore)
                || item.ServiceType == typeof(CIS.Transactions.ILocalTransactionFactory)));
        });
    }

    [TestMethod]
    public async Task ExplicitOptInPreservesExistingDefaultsAndStaysPassiveDuringComposition()
    {
        var management = new ManagementClientOptions("country-test-management") { BaseUrl = "https://management.invalid" };
        var configuration = new ManagementConfigurationClientOptions("country-test-configuration") { BaseUrl = management.BaseUrl };
        int prepared = 0;
        var central = new CISApplicationCentralConfigurationOptions(management, configuration, new(management.RestClientName), _ =>
        {
            prepared++;
            return Task.FromException<CISApplicationCentralConfigurationPreparation>(new InvalidOperationException("No preparation is expected."));
        });
        var factories = new Dictionary<Type, Func<IConfigurationObject>>
        {
            [typeof(CountrySettings)] = () => new CountrySettings()
        };
        await using CISApplication application = CountryApplication.CreateService([], builder =>
        {
            builder.Configuration = new("CIS.CountryData")
            {
                Factories = factories
            };
            CountryResidenceRules.Configure(builder, central, TimeSpan.FromSeconds(5));
            Assert.AreSame(central, builder.CentralConfiguration);
            Assert.AreSame(factories, builder.Configuration!.Factories);
            Assert.AreEqual("CIS.CountryData", builder.TypeManager.HostAssembly.GetName().Name);
            ConfigurationEnrollment enrollment = builder.Configuration.Enrollments.Single();
            Assert.AreEqual(typeof(CountryResidenceRules), enrollment.ConfigurationType);
            Assert.AreEqual(CountryResidenceRules.ConfigurationId, enrollment.ConfigurationId);
            Assert.AreEqual(CountryResidenceRules.SchemaVersion, enrollment.SchemaVersion);
            Assert.AreEqual(ConfigurationUpdateMode.HotReload, enrollment.UpdateMode);
            Assert.AreEqual(ServiceLifetime.Scoped, builder.Services.Single(item => item.ServiceType == typeof(IRuleEvaluator)).Lifetime);
            Assert.IsFalse(builder.Services.Any(item => item.ServiceType == typeof(IMessagingStore)));
        });
        Assert.AreEqual(0, prepared);
    }

    [TestMethod]
    [DataRow(RuleExecutionMode.FirstMatch)]
    [DataRow(RuleExecutionMode.AllMatching)]
    public void WorkedDraftRoundTripsBothModesWithExactWeightsAndMetadata(RuleExecutionMode mode)
    {
        RuleDefinition schema = CountryResidenceRules.CreateSchema();
        RuleDefinition draft = RuleDefinitionCodec.Decode(RuleDefinitionCodec.Encode(CountryResidenceRules.CreateDraft(mode)));
        Assert.AreEqual(schema.RulesetId, draft.RulesetId);
        Assert.AreEqual(schema.SchemaRevision, draft.SchemaRevision);
        Assert.HasCount(0, schema.Rows);
        Assert.AreEqual(mode, draft.Mode);
        CollectionAssert.AreEqual(new[] { "ResidenceCountry", "Pensioner" }, draft.Columns.Select(column => column.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "trial", "local", "fallback", "inactive" }, draft.Rows.Select(row => row.RuleId).ToArray());
        CollectionAssert.AreEqual(new[] { "100", "20", "80", "200" }, draft.Rows.Select(row => row.WeightText).ToArray());
        CollectionAssert.AreEqual(new[] { "Trial", "Local", "Fallback", "Inactive" },
            draft.Rows.Select(row => row.Metadata!.Value.GetString()).ToArray());
    }
}
