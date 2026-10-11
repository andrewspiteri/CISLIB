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
            string candidate = Path.Combine(directory.FullName, "conformance", "logging", "startup-lifecycle-cases.json");
            if (File.Exists(candidate))
            {
                path = candidate;
            }
            directory = directory.Parent;
        }
        Assert.IsNotNull(path);
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray()
            .Single(value => value!["id"]!.GetValue<string>() == "STARTUP-NO-RESET-OR-SIDE-EFFECTS")!.AsObject();
        JsonObject expected = row["expected"]!.AsObject();
        CollectionAssert.AreEquivalent(new[] { "productionResetApi", "mutableStaticOptionValues", "globalOperationContext", "createsDirectories", "opensOutputs", "startsWorkers", "subscribesGlobalExceptions", "contactsInfrastructure" },
            expected.Select(pair => pair.Key).ToArray());
        foreach ((string rule, JsonNode? value) in expected)
        {
            Assert.IsFalse(value!.GetValue<bool>(), rule);
        }
        var guards = new ConfigurationAmbientStateTests();
        guards.ConfigurationHelpersDoNotQueryAmbientPresentationOrDumpOrMutateVariables();
        guards.ConfigurationValidationAndStoredGettersHaveNoOutputProbeNetworkOrWorkerCalls();
        guards.ActualFacadeHasOnlyPrivatePublicationStateAndATrivialExplicitStaticConstructor();
        new StartupOwnershipArchitectureTests().PublishedSnapshotGraphContainsOnlyReadonlyOwnedTypesAndImmutableValues();
    }
}
