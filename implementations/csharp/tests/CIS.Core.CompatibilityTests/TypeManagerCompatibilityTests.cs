using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.TypeManagerCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests;

[TestClass]
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
        from mode in new[] { "frozen", "source" }
        select new object[] { mode, item!["id"]!.GetValue<string>() };


    [TestMethod]
    [DynamicData(nameof(BehaviorCases))]
    public async Task FrozenBehaviorIsPreserved(string mode, string caseId)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string candidate = f.Core;
        string binaries = mode == "source" ? await f.Source("behavior-source", Path.Combine(f.Baseline, "source"), f.Core) : Path.Combine(f.Baseline, "compiled");
        await f.Behavior(mode, binaries, candidate, caseId);
    }

    [TestMethod]
    public async Task OriginalApiRebuildMatchesFrozenConsumerBytes()
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string binaries = await f.Source("behavior-original-api", Path.Combine(f.Baseline, "source"), Path.Combine(f.Original, "reference/CIS.Core.dll"));
        Assert.AreEqual(Baseline(f.Baseline, BehaviorHash)["compiled/CIS.TypeManager.BehaviorConsumer.dll"], Hash(Path.Combine(binaries, "CIS.TypeManager.BehaviorConsumer.dll")));
        f.Unchanged();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalConsumersRemainCompatible(bool source)
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        string candidate = f.Core;
        await f.Historical("candidate" + (source ? "-historical-source" : "-historical-frozen"), candidate, source);
    }

    [TestMethod]
    public async Task FrozenConsumerRequiresApprovedDependencyDeployment()
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        await f.Historical("missing-dependency-manifest", f.Core, false, missingDependencies: true);
    }

    [TestMethod]
    public async Task PublicApiRemainsCompatible()
    {
        TypeManagerCompatibilityFixture f = await Shared.Value;
        await f.Api("api", f.Core);
    }

    [ClassCleanup]
    public static async Task CheckPreservedInputs()
    {
        if (Shared.IsValueCreated && Shared.Value.IsCompletedSuccessfully)
        {
            TypeManagerCompatibilityFixture fixture = await Shared.Value;
            try { fixture.Unchanged(); } finally { fixture.Dispose(); }
        }
    }
}
