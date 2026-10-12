using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class StartupLifecycleQualificationTests
{
    [TestMethod]
    public void SharedNoResetAndSideEffectCaseIsBoundToActualCompiledCoreGuards()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string? path = null;
        while (directory is not null && path is null)
        {
            string candidate = Path.Combine(directory.FullName, "contracts", "logging", "v1", "environment-contract.json");
            if (File.Exists(candidate))
            {
                path = candidate;
            }
            directory = directory.Parent;
        }
        Assert.IsNotNull(path);
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        JsonObject expected = row["expected"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "productionResetApi", "mutableRuntimeFacts", "globalOperationContext", "createsDirectories", "opensOutputs", "startsWorkers", "subscribesGlobalExceptions", "contactsInfrastructure" },
            expected.Select(pair => pair.Key).ToArray());
        foreach ((string rule, JsonNode? value) in expected)
        {
            Assert.AreEqual(false, value!.GetValue<bool>(), rule);
        }
        var guards = new ConfigurationAmbientStateTests();
        guards.ConfigurationHelpersDoNotQueryAmbientPresentationOrDumpOrMutateVariables();
        guards.ConfigurationValidationAndStoredGettersHaveNoOutputProbeNetworkOrWorkerCalls();
        guards.ContainerFactsAreReadonlyAndGettersHaveNoLifecycleEffects();
    }
}
