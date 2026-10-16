using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.TypeManagerCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests;

[TestClass]
[DoNotParallelize]
public sealed class TypeManagerCompatibilityTests
{
    private static readonly Lazy<Task<TypeManagerCompatibilityFixture>> Shared = new(async () =>
    {
        var fixture = new TypeManagerCompatibilityFixture();
        await fixture.Initialize();
        return fixture;
    });

    public static IEnumerable<object[]> BehaviorCases =>
        from item in Read(Path.Combine(TypeManagerCompatibilityFixture.Repository(), "implementations/csharp/verification/contracts/type-manager/v1-behavior-development/cases.json"))["cases"]!.AsArray()
        from mode in new[] { "frozen", "source", "module-positive" }
        select new object[] { mode, item!["id"]!.GetValue<string>() };

    public static IEnumerable<object[]> RegressionCases => TypeManagerCompatibilityFixture.Controls.Select(control => new object[] { control.Name });

    [TestMethod]
    [DynamicData(nameof(BehaviorCases))]
    public async Task FrozenBehaviorIsPreserved(string mode, string caseId)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string candidate = mode == "module-positive" ? await f.Module("positive", []) : f.Core;
        string binaries = mode == "source" ? await f.Source("behavior-source", Path.Combine(f.Baseline, "source"), f.Core) : Path.Combine(f.Baseline, "compiled");
        await f.Behavior(mode, binaries, candidate, caseId);
    }

    [TestMethod]
    [DynamicData(nameof(RegressionCases))]
    public async Task DeliberateBehaviorRegressionsAreDetected(string name)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        TypeManagerCompatibilityFixture.Control control = TypeManagerCompatibilityFixture.Controls.Single(item => item.Name == name);
        string candidate = await f.Module(name, control.Changes);
        await f.Behavior(name, Path.Combine(f.Baseline, "compiled"), candidate, control.Case, control.Field, control.Value);
    }

    [TestMethod]
    public async Task ChangedFailureValueIsAlsoDetectedWhenThrown()
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string candidate = await f.Module("failure-value", TypeManagerCompatibilityFixture.Controls[0].Changes);
        await f.Behavior("failure-value-thrown", Path.Combine(f.Baseline, "compiled"), candidate, "malformed", "outcome.value", JsonValue.Create(99));
    }

    [TestMethod]
    public async Task OriginalApiRebuildMatchesFrozenConsumerBytes()
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string binaries = await f.Source("behavior-original-api", Path.Combine(f.Baseline, "source"), Path.Combine(f.Original, "reference/CIS.Core.dll"));
        Assert.AreEqual(Baseline(f.Baseline, BehaviorHash)["compiled/CIS.TypeManager.BehaviorConsumer.dll"], Hash(Path.Combine(binaries, "CIS.TypeManager.BehaviorConsumer.dll")));
        f.Passed("behavior-original-api-build", new JsonObject { ["exactFrozenBinary"] = true });
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task OriginalConsumersRemainCompatible(bool module, bool source)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string candidate = module ? await f.Module("positive", []) : f.Core;
        await f.Historical((module ? "module" : "candidate") + (source ? "-historical-source" : "-historical-frozen"), candidate, source);
    }

    [TestMethod]
    public async Task FrozenConsumerRequiresApprovedDependencyDeployment()
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        await f.Historical("missing-dependency-manifest", f.Core, false, missingDependencies: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublicApiRemainsCompatible(bool module)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        await f.Api(module ? "module-api-positive" : "api", module ? await f.Module("positive", []) : f.Core);
    }

    [TestMethod]
    [DataRow("api")]
    [DataRow("source")]
    [DataRow("binary")]
    public async Task RenamedPropertyIsRejectedAtEveryBoundary(string boundary)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string broken = await f.Module("renamed-public-property", [new("*", "AssemblyManifestPath", "RenamedManifestPath")]);
        if (boundary == "api") { await f.Api("api-negative", broken, true); }
        else if (boundary == "binary") { await f.Historical("binary-negative", broken, false, true); }
        else
        {
            await f.Source("source-negative", Path.Combine(f.Original, "source"), broken, true, ["CS0117", "AssemblyManifestPath"]);
            f.Passed("source-negative");
        }
    }

    [ClassCleanup]
    public static async Task CheckPreservedInputs()
    {
        if (Shared.IsValueCreated && Shared.Value.IsCompletedSuccessfully) { (await Shared.Value).Unchanged(); }
    }
}
