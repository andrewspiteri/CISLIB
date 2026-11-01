using System.IO.Compression;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ShippingCoverageCollectionTests
{
    [TestMethod]
    public void ExplicitPreparationMayOwnItsPackageOutsideTheRepository()
    {
        using var external = new TemporaryDirectory();
        using var fixture = new Fixture(Path.Combine(external.Path, "preparation"));
        string[] inputs = ShippingCoverageCollection.DeveloperInputs(fixture.Root, Path.GetDirectoryName(fixture.Summary)!);
        CollectionAssert.Contains(inputs, fixture.Package);
        Assert.AreEqual("..", Path.GetRelativePath(fixture.Root, fixture.Package).Split(Path.DirectorySeparatorChar)[0]);
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-dll")]
    [DataRow("extra-dll")]
    [DataRow("duplicate-dll")]
    [DataRow("changed-canonical")]
    [DataRow("changed-copy")]
    [DataRow("missing-core-copy")]
    [DataRow("absent-optional-helper")]
    public void PackageInventoryAndEveryLoadedCopyMustMatch(string mode)
    {
        using var fixture = new Fixture();
        Assert.HasCount(2, ShippingCoverageCollection.ValidateCandidate(fixture.Language, fixture.Policy, fixture.Package));
        if (mode == "missing-dll") { fixture.PackageEntries.Remove("tools/net10.0/CIS.TypeManager.Build.dll"); }
        if (mode == "extra-dll") { fixture.PackageEntries.Add("extra.dll", "extra"); }
        if (mode == "duplicate-dll") { fixture.DuplicateEntry = true; }
        if (mode == "changed-canonical") { File.AppendAllText(fixture.Core, "changed"); }
        if (mode == "changed-copy") { File.AppendAllText(fixture.CoreCopy, "changed"); }
        if (mode == "missing-core-copy") { File.Delete(fixture.CoreCopy); }
        if (mode == "absent-optional-helper") { File.Delete(fixture.HelperCopy); }
        fixture.SavePackage();
        if (mode is "valid" or "absent-optional-helper")
        { Assert.HasCount(mode == "valid" ? 2 : 1, ShippingCoverageCollection.ValidateCandidate(fixture.Language, fixture.Policy, fixture.Package)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.ValidateCandidate(fixture.Language, fixture.Policy, fixture.Package)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("failed")]
    [DataRow("wrong-profile")]
    [DataRow("inspection-pin")]
    [DataRow("reference-pin")]
    [DataRow("package-pin")]
    [DataRow("inspection-escape")]
    [DataRow("reference-escape")]
    [DataRow("package-escape")]
    public void DeveloperEvidenceAndStartupReferenceRemainPinnedAndContained(string mode)
    {
        using var fixture = new Fixture();
        Assert.HasCount(5, ShippingCoverageCollection.DeveloperInputs(fixture.Root, Path.GetDirectoryName(fixture.Summary)!));
        JsonObject summary = Read(fixture.Summary), inspection = Read(fixture.Inspection), startup = Read(fixture.Startup);
        if (mode == "failed") { summary["exitCode"] = 1; }
        if (mode == "wrong-profile") { summary["profile"] = "Release"; }
        if (mode == "reference-pin") { startup["referenceAssembly"]!["sha256"] = "changed"; }
        if (mode == "package-pin") { inspection["package"]!["sha256"] = "changed"; }
        if (mode == "inspection-escape") { summary["architectureInspection"]!["path"] = "../outside.json"; }
        if (mode == "reference-escape") { startup["referenceAssembly"]!["path"] = "../outside.dll"; }
        if (mode == "package-escape") { inspection["package"]!["path"] = Path.Combine(fixture.Root, "..", "outside.nupkg"); }
        WriteJson(fixture.Startup, startup); WriteJson(fixture.Inspection, inspection);
        if (mode != "inspection-escape") { summary["architectureInspection"]!["sha256"] = Hash(fixture.Inspection); }
        if (mode == "inspection-pin") { summary["architectureInspection"]!["sha256"] = "changed"; }
        WriteJson(fixture.Summary, summary);
        if (mode == "valid") { Assert.HasCount(5, ShippingCoverageCollection.DeveloperInputs(fixture.Root, Path.GetDirectoryName(fixture.Summary)!)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.DeveloperInputs(fixture.Root, Path.GetDirectoryName(fixture.Summary)!)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("changed")]
    [DataRow("added")]
    [DataRow("deleted")]
    public void CompleteSourceMembershipCannotDrift(string mode)
    {
        SourceInventoryEntry[] before = [new("a.cs", "a"), new("contracts/shared.json", "b")];
        SourceInventoryEntry[] after = mode switch
        {
            "changed" => [before[0], new("contracts/shared.json", "changed")],
            "added" => [.. before, new("extra.cs", "extra")],
            "deleted" => [before[0]],
            _ => [before[1], before[0]]
        };
        if (mode == "valid") { ShippingCoverageCollection.RequireSameSources(before, after); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.RequireSameSources(before, after)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("changed-binary")]
    [DataRow("changed-pdb")]
    [DataRow("changed-shared")]
    [DataRow("missing")]
    public void BinarySymbolsAndSharedInputsAreHashPinned(string mode)
    {
        using var fixture = new Fixture();
        string pdb = Path.ChangeExtension(fixture.Core, ".pdb"), shared = Path.Combine(fixture.Root, "contracts/shared.json");
        Write(pdb, "opaque symbols"); Write(shared, "shared contract");
        SourceInventoryEntry[] pins = ShippingCoverageCollection.PinInputs(fixture.Language, [fixture.Core, pdb, shared, fixture.Core]);
        Assert.HasCount(3, pins);
        ShippingCoverageCollection.RequireUnchangedInputs(fixture.Language, pins);
        if (mode == "changed-binary") { File.AppendAllText(fixture.Core, "changed"); }
        if (mode == "changed-pdb") { File.AppendAllText(pdb, "changed"); }
        if (mode == "changed-shared") { File.AppendAllText(shared, "changed"); }
        if (mode == "missing") { File.Delete(pdb); }
        if (mode == "valid") { ShippingCoverageCollection.RequireUnchangedInputs(fixture.Language, pins); }
        else if (mode == "missing") { Assert.ThrowsExactly<FileNotFoundException>(() => ShippingCoverageCollection.RequireUnchangedInputs(fixture.Language, pins)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.RequireUnchangedInputs(fixture.Language, pins)); }
    }

    [TestMethod]
    [DataRow("new")]
    [DataRow("empty")]
    [DataRow("populated")]
    [DataRow("file")]
    [DataRow("focused")]
    public void OutputOwnershipAndFocusedOverridesFailBeforeWriting(string mode)
    {
        using var fixture = new TemporaryDirectory();
        string output = Path.Combine(fixture.Path, "output");
        if (mode is "empty" or "populated") { Directory.CreateDirectory(output); }
        if (mode == "populated") { File.WriteAllText(Path.Combine(output, "original"), "original"); }
        if (mode == "file") { File.WriteAllText(output, "original"); }
        if (mode == "new") { ShippingCoverageCollection.RequireNewOutput(output, " "); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.RequireNewOutput(output, mode == "focused" ? "fixture" : null)); }
        if (mode is "new" or "focused") { Assert.IsFalse(Path.Exists(output)); }
        if (mode == "file") { Assert.AreEqual("original", File.ReadAllText(output)); }
        if (mode == "populated") { Assert.AreEqual("original", File.ReadAllText(Path.Combine(output, "original"))); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("duplicate-assembly")]
    [DataRow("duplicate-entry")]
    [DataRow("project-escape")]
    [DataRow("source-escape")]
    [DataRow("settings-escape")]
    [DataRow("family-escape")]
    [DataRow("missing-core")]
    public void CoveragePolicyCannotEscapeOrDuplicateItsScope(string mode)
    {
        using var fixture = new Fixture();
        JsonArray assemblies = fixture.Policy["assemblies"]!.AsArray();
        if (mode == "duplicate-assembly") { assemblies.Add(assemblies[0]!.DeepClone()); }
        if (mode == "duplicate-entry") { assemblies[1]!["packageEntry"] = assemblies[0]!["packageEntry"]!.DeepClone(); }
        if (mode == "project-escape") { assemblies[0]!["project"] = "../outside.csproj"; }
        if (mode == "source-escape") { assemblies[0]!["sourceRoot"] = "../outside"; }
        if (mode == "settings-escape") { fixture.Policy["settings"] = "../outside.runsettings"; }
        if (mode == "family-escape")
        {
            fixture.Policy["families"]!["unit"] = "../outside";
            Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.ValidatePolicy(fixture.Language, fixture.Policy));
            fixture.Policy["families"] = new JsonObject { [".."] = "UnitTests" };
        }
        if (mode == "missing-core") { assemblies.RemoveAt(0); }
        if (mode == "valid") { ShippingCoverageCollection.ValidatePolicy(fixture.Language, fixture.Policy); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ShippingCoverageCollection.ValidatePolicy(fixture.Language, fixture.Policy)); }
    }

    [TestMethod]
    public void CollectorUsesExistingReleaseBinariesAndNativeReportFormat()
    {
        CollectionAssert.AreEqual(new[]
        {
            "test", "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj", "-c", "Release", "--no-build", "--no-restore",
            "--settings", "coverage.runsettings", "--collect", "Code Coverage;Format=cobertura",
            "--results-directory", "output with spaces", "--logger", "trx;LogFileName=report.trx",
            "--filter", "FullyQualifiedName!=CIS.Tests.UnitTests.IncomingMeasurementsTests.RecordQuietFullContendedAndCompleteCallerCosts"
        }, ShippingCoverageCollection.CollectorArguments("UnitTests", "coverage.runsettings", "output with spaces"));
    }

    [TestMethod]
    [DataRow("IntegrationTests")]
    [DataRow("ArchitectureTests")]
    [DataRow("BusinessCases")]
    public void OtherShippingFamiliesRemainUnfiltered(string suffix)
    {
        string[] arguments = ShippingCoverageCollection.CollectorArguments(suffix, "coverage.runsettings", "native");
        Assert.IsFalse(arguments.Contains("--filter", StringComparer.Ordinal));
        Assert.IsTrue(arguments.Contains("--no-build", StringComparer.Ordinal));
        Assert.IsTrue(arguments.Contains("--no-restore", StringComparer.Ordinal));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Root => directory.Path;
        internal string Language { get; }
        internal string Package { get; }
        internal string Core { get; }
        internal string CoreCopy { get; }
        internal string HelperCopy { get; }
        internal string Summary { get; }
        internal string Inspection { get; }
        internal string Startup { get; }
        internal JsonObject Policy { get; }
        internal Dictionary<string, string> PackageEntries { get; } = new(StringComparer.Ordinal);
        internal bool DuplicateEntry { get; set; }

        internal Fixture(string? preparationRoot = null)
        {
            string dev = preparationRoot ?? Path.Combine(Root, ".artifacts/csharp/verification/Developer");
            Language = Path.Combine(Root, "implementations/csharp");
            Package = Path.Combine(dev, "packages/CIS.Core.fixture.nupkg");
            Policy = new JsonObject
            {
                ["settings"] = "coverage.runsettings",
                ["families"] = new JsonObject { ["unit"] = "UnitTests" },
                ["assemblies"] = new JsonArray(
                    new JsonObject { ["name"] = "CIS.Core", ["project"] = "src/CIS.Core/CIS.Core.csproj", ["sourceRoot"] = "src/CIS.Core", ["packageEntry"] = "lib/net10.0/CIS.Core.dll" },
                    new JsonObject { ["name"] = "CIS.TypeManager.Build", ["project"] = "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj", ["sourceRoot"] = "eng/CIS.TypeManager.Build", ["packageEntry"] = "tools/net10.0/CIS.TypeManager.Build.dll" })
            };
            Core = Path.Combine(Language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            string helper = Path.Combine(Language, "eng/CIS.TypeManager.Build/bin/Release/net10.0/CIS.TypeManager.Build.dll");
            CoreCopy = Path.Combine(Language, "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.dll");
            HelperCopy = Path.Combine(Language, "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.TypeManager.Build.dll");
            Write(Core, "opaque Core fixture"); Write(helper, "opaque helper fixture");
            Write(CoreCopy, File.ReadAllText(Core)); Write(HelperCopy, File.ReadAllText(helper));
            PackageEntries.Add("lib/net10.0/CIS.Core.dll", File.ReadAllText(Core)); PackageEntries.Add("tools/net10.0/CIS.TypeManager.Build.dll", File.ReadAllText(helper));
            SavePackage();
            Summary = Path.Combine(dev, "summary.json"); Inspection = Path.Combine(dev, "architecture.json");
            Startup = Path.Combine(dev, "contracts/startup/summary.json");
            string reference = Path.Combine(dev, "contracts/startup/reference.dll"); Write(reference, "opaque reference fixture");
            WriteJson(Startup, new JsonObject { ["referenceAssembly"] = new JsonObject { ["path"] = "reference.dll", ["sha256"] = Hash(reference) } });
            WriteJson(Inspection, new JsonObject { ["package"] = new JsonObject { ["path"] = Package, ["sha256"] = Hash(Package) } });
            WriteJson(Summary, new JsonObject
            {
                ["exitCode"] = 0,
                ["profile"] = "Developer",
                ["candidatePackage"] = new JsonObject { ["path"] = "packages/CIS.Core.fixture.nupkg", ["sha256"] = Hash(Package) },
                ["architectureInspection"] = new JsonObject { ["path"] = "architecture.json", ["sha256"] = Hash(Inspection) }
            });
        }
        internal void SavePackage()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Package)!);
            if (File.Exists(Package)) { File.Delete(Package); }
            using ZipArchive archive = ZipFile.Open(Package, ZipArchiveMode.Create);
            foreach ((string name, string value) in PackageEntries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(value);
            }
            if (DuplicateEntry)
            {
                using var writer = new StreamWriter(archive.CreateEntry("lib/net10.0/CIS.Core.dll").Open());
                writer.Write("duplicate");
            }
        }
        public void Dispose() => directory.Dispose();
    }
}
