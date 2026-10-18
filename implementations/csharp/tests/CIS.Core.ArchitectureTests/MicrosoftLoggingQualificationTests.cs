using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class MicrosoftLoggingQualificationTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-source")]
    [DataRow("extra-source")]
    [DataRow("duplicate-source")]
    [DataRow("missing-slice")]
    [DataRow("duplicate-slice")]
    [DataRow("foreign-source")]
    public void MatrixRequiresExactlyThePhysicalAdapterSources(string mode)
    {
        using var fixture = new Fixture();
        JsonArray slices = fixture.Matrix["shippingProjects"]![0]!["moduleSlices"]!.AsArray();
        JsonArray sources = slices[0]!["sources"]!.AsArray();
        if (mode == "missing-source") { File.Delete(Path.Combine(fixture.Root, fixture.Paths[0])); }
        if (mode == "extra-source") { Write(Path.Combine(fixture.Language, "src/CIS.Core/Logging/MicrosoftLogger/Extra.cs"), "extra"); }
        if (mode == "duplicate-source") { sources.Add(sources[0]!.DeepClone()); }
        if (mode == "missing-slice") { slices.Clear(); }
        if (mode == "duplicate-slice") { slices.Add(slices[0]!.DeepClone()); }
        if (mode == "foreign-source") { sources[0] = "../outside.cs"; }
        if (mode == "valid") { CollectionAssert.AreEqual(fixture.Paths, MicrosoftLoggingQualification.AdapterPaths(fixture.Language, fixture.Matrix)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingQualification.AdapterPaths(fixture.Language, fixture.Matrix)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("identical-copy")]
    [DataRow("missing-class")]
    [DataRow("no-lines")]
    [DataRow("unexecuted")]
    [DataRow("invalid-line")]
    [DataRow("zero-line")]
    [DataRow("invalid-hits")]
    [DataRow("negative-hits")]
    [DataRow("invalid-branch")]
    [DataRow("missing-condition")]
    [DataRow("invalid-branches")]
    [DataRow("zero-denominator")]
    [DataRow("conflict")]
    [DataRow("missing-attachment")]
    [DataRow("duplicate-attachment")]
    [DataRow("missing-report")]
    [DataRow("conflicting-report")]
    [DataRow("empty-tests")]
    [DataRow("failed-tests")]
    [DataRow("skipped-tests")]
    [DataRow("missing-counters")]
    [DataRow("incomplete")]
    public void CoverageRequiresPassingNativeTestsAndUnambiguousMeasuredSources(string mode)
    {
        using var fixture = new Fixture();
        XElement first = fixture.Cobertura.Descendants("class").First();
        XElement line = first.Descendants("line").First();
        XElement counters = fixture.Trx.Descendants("Counters").Single();
        if (mode == "missing-class") { first.Remove(); }
        if (mode == "no-lines") { first.Element("lines")!.RemoveNodes(); }
        if (mode == "unexecuted") { foreach (XElement item in first.Descendants("line")) { item.SetAttributeValue("hits", "0"); } }
        if (mode == "invalid-line") { line.SetAttributeValue("number", "bad"); }
        if (mode == "zero-line") { line.SetAttributeValue("number", "0"); }
        if (mode == "invalid-hits") { line.SetAttributeValue("hits", "bad"); }
        if (mode == "negative-hits") { line.SetAttributeValue("hits", "-1"); }
        if (mode == "invalid-branch") { line.SetAttributeValue("branch", "maybe"); }
        if (mode == "missing-condition") { line.Attribute("condition-coverage")!.Remove(); }
        if (mode == "invalid-branches") { line.SetAttributeValue("condition-coverage", "150% (3/2)"); }
        if (mode == "zero-denominator") { line.SetAttributeValue("condition-coverage", "0% (0/0)"); }
        if (mode == "conflict")
        {
            var duplicate = new XElement(first);
            duplicate.Descendants("line").First().SetAttributeValue("condition-coverage", "50% (2/4)");
            first.AddAfterSelf(duplicate);
        }
        if (mode == "missing-attachment") { fixture.Trx.Descendants("UriAttachment").Single().Remove(); }
        if (mode == "duplicate-attachment") { fixture.Trx.Root!.Add(new XElement(fixture.Trx.Descendants("UriAttachment").Single())); }
        if (mode == "empty-tests") { foreach (XAttribute item in counters.Attributes()) { item.Value = "0"; } }
        if (mode == "failed-tests") { counters.SetAttributeValue("passed", "1"); counters.SetAttributeValue("failed", "1"); }
        if (mode == "skipped-tests") { counters.SetAttributeValue("executed", "1"); counters.SetAttributeValue("passed", "1"); }
        if (mode == "missing-counters") { counters.Remove(); }
        if (mode == "incomplete") { fixture.Trx.Descendants("ResultSummary").Single().SetAttributeValue("outcome", "Aborted"); }
        fixture.SaveCoverage();
        if (mode == "missing-report") { File.Delete(fixture.Report); }
        if (mode is "identical-copy" or "conflicting-report")
        { Write(Path.Combine(fixture.Output, "duplicate/coverage.cobertura.xml"), mode == "identical-copy" ? File.ReadAllText(fixture.Report) : "different"); }
        if (mode is "valid" or "identical-copy")
        {
            JsonObject result = MicrosoftLoggingQualification.Coverage(fixture.Output, fixture.Root, fixture.Paths);
            Assert.HasCount(3, result["files"]!.AsArray());
            Assert.AreEqual(Hash(fixture.Report), (string?)result["coberturaSha256"]);
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "report.trx")), (string?)result["trxSha256"]);
            StringAssert.Contains(result["thresholdEvaluation"]!.GetValue<string>(), "not release qualification");
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingQualification.Coverage(fixture.Output, fixture.Root, fixture.Paths)); }
    }

    [TestMethod]
    [DataRow("more-hits")]
    [DataRow("more-covered-branches")]
    [DataRow("no-branches")]
    public void GeneratedClassesMergeBySourceLineWithoutAddingDenominators(string mode)
    {
        using var fixture = new Fixture();
        XElement first = fixture.Cobertura.Descendants("class").First(), duplicate = new(first);
        XElement line = duplicate.Descendants("line").First();
        if (mode == "more-hits") { line.SetAttributeValue("hits", "9"); }
        if (mode == "more-covered-branches") { line.SetAttributeValue("condition-coverage", "100% (2/2)"); }
        if (mode == "no-branches") { line.SetAttributeValue("branch", "false"); line.Attribute("condition-coverage")!.Remove(); }
        first.AddAfterSelf(duplicate); fixture.SaveCoverage();
        JsonNode file = MicrosoftLoggingQualification.Coverage(fixture.Output, fixture.Root, fixture.Paths)["files"]![0]!;
        Assert.AreEqual(2, file["linesTotal"]!.GetValue<int>());
        Assert.AreEqual(1, file["linesCovered"]!.GetValue<int>());
        Assert.AreEqual(2L, file["branchesTotal"]!.GetValue<long>());
        Assert.AreEqual(mode == "more-covered-branches" ? 2L : 1L, file["branchesCovered"]!.GetValue<long>());
    }

    [TestMethod]
    public void CoverageRejectsDtdBeforeReadingExternalContent()
    {
        using var fixture = new Fixture();
        Write(Path.Combine(fixture.Output, "report.trx"), "<!DOCTYPE x [<!ENTITY external SYSTEM 'file:///not-read'>]><TestRun>&external;</TestRun>");
        Assert.ThrowsExactly<XmlException>(() => MicrosoftLoggingQualification.Coverage(fixture.Output, fixture.Root, fixture.Paths));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("backslash")]
    [DataRow("all-statuses")]
    [DataRow("missing-file")]
    [DataRow("extra-file")]
    [DataRow("duplicate-basename")]
    [DataRow("zero-mutants")]
    [DataRow("zero-denominator")]
    [DataRow("blank-status")]
    [DataRow("no-adapters")]
    public void MutationKeepsExactFilesAndConservativeTestableDenominator(string mode)
    {
        using var fixture = new Fixture();
        JsonObject files = fixture.Mutants["files"]!.AsObject();
        string first = files.First().Key;
        if (mode == "backslash")
        {
            foreach ((string key, JsonNode? value) in files.ToArray()) { files.Remove(key); files.Add(key.Replace('/', '\\'), value); }
        }
        if (mode == "all-statuses")
        {
            files[first]!["mutants"] = new JsonArray(new[] { "Killed", "Timeout", "Survived", "NoCoverage", "Ignored", "CompileError" }
                .Select(status => (JsonNode)new JsonObject { ["status"] = status }).ToArray());
        }
        if (mode == "missing-file") { files.Remove(first); }
        if (mode == "extra-file") { files.Add("Logging/MicrosoftLogger/Extra.cs", files[first]!.DeepClone()); }
        if (mode == "duplicate-basename") { files.Add("elsewhere/" + first, files[first]!.DeepClone()); }
        if (mode == "zero-mutants") { files[first]!["mutants"] = new JsonArray(); }
        if (mode == "zero-denominator")
        { foreach ((_, JsonNode? file) in files) { file!["mutants"]![0]!["status"] = "Ignored"; } }
        if (mode == "blank-status") { files[first]!["mutants"]![0]!["status"] = ""; }
        if (mode == "no-adapters") { files.Clear(); }
        // Foreign module mutants never contribute kill credit to this slice.
        files.Add("Logging/Other.cs", new JsonObject { ["mutants"] = new JsonArray(new JsonObject { ["status"] = "Killed" }) });
        WriteJson(fixture.MutationReport, fixture.Mutants);
        if (mode is "valid" or "backslash" or "all-statuses")
        {
            JsonObject result = MicrosoftLoggingQualification.Mutation(fixture.MutationReport, fixture.Paths);
            Assert.AreEqual(mode == "all-statuses" ? 6L : 3L, result["testableDenominator"]!.GetValue<long>());
            Assert.AreEqual(3, result["counts"]!["Killed"]!.GetValue<int>());
            Assert.AreEqual(Hash(fixture.MutationReport), (string?)result["nativeReportSha256"]);
            if (mode == "all-statuses") { Assert.AreEqual(1, result["counts"]!["CompileError"]!.GetValue<int>()); }
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingQualification.Mutation(fixture.MutationReport, fixture.Paths)); }
    }

    [TestMethod]
    [DataRow("unchanged")]
    [DataRow("changed")]
    [DataRow("deleted")]
    public void SnapshotAndCanonicalPinsCannotDrift(string mode)
    {
        using var fixture = new Fixture();
        SourceInventoryEntry[] pins = fixture.Paths.Select(path => new SourceInventoryEntry(path, Hash(Path.Combine(fixture.Root, path)))).ToArray();
        if (mode == "changed") { File.AppendAllText(Path.Combine(fixture.Root, fixture.Paths[0]), "changed"); }
        if (mode == "deleted") { File.Delete(Path.Combine(fixture.Root, fixture.Paths[0])); }
        if (mode == "unchanged") { MicrosoftLoggingQualification.RequireUnchanged(fixture.Root, pins); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingQualification.RequireUnchanged(fixture.Root, pins)); }
    }

    [TestMethod]
    [DataRow("new")]
    [DataRow("directory")]
    [DataRow("file")]
    public void ExistingOutputsAreNeverOverwritten(string mode)
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Root, "retained");
        if (mode == "directory") { Directory.CreateDirectory(path); }
        if (mode == "file") { File.WriteAllText(path, "retained"); }
        if (mode == "new") { MicrosoftLoggingQualification.RequireNewOutput(path); }
        else
        {
            Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingQualification.RequireNewOutput(path));
            if (mode == "file") { Assert.AreEqual("retained", File.ReadAllText(path)); }
        }
    }

    [TestMethod]
    [DataRow("Unit")]
    [DataRow("Integration")]
    public void CollectorPreservesAdapterFiltersAndNativeReportFormat(string family)
    {
        string[] arguments = MicrosoftLoggingQualification.CollectorArguments(family, "reports");
        CollectionAssert.Contains(arguments, $"FullyQualifiedName~CIS.Tests.{family}Tests.Provider");
        CollectionAssert.Contains(arguments, "Code Coverage;Format=cobertura");
        CollectionAssert.Contains(arguments, "--no-build");
        CollectionAssert.Contains(arguments, "--no-restore");
        CollectionAssert.Contains(arguments, "verification/coverage/microsoft-logging.runsettings");
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("invalid-utf8")]
    [DataRow("empty")]
    public void MutationDiagnosticsRetainOriginalBytesInsteadOfReencodingDisplayText(string mode)
    {
        using var fixture = new Fixture();
        byte[] stdout = mode switch { "invalid-utf8" => [65, 255, 66], "empty" => [], _ => [65, 66] };
        byte[] stderr = mode == "empty" ? [] : [254, 0, 67];
        var result = new ProcessResult(2, "diagnostic stdout", "diagnostic stderr")
        { ProcessId = 42, StandardOutputBytes = stdout, StandardErrorBytes = stderr };
        MicrosoftLoggingQualification.RecordMutationProcess(fixture.Output, result);
        CollectionAssert.AreEqual(stdout, File.ReadAllBytes(Path.Combine(fixture.Output, "logs/mutation.stdout.bin")));
        CollectionAssert.AreEqual(stderr, File.ReadAllBytes(Path.Combine(fixture.Output, "logs/mutation.stderr.bin")));
        JsonObject process = Read(Path.Combine(fixture.Output, "logs/mutation.process.json"));
        Assert.AreEqual(Digest(stdout), (string?)process["stdoutSha256"]);
        Assert.AreEqual(Digest(stderr), (string?)process["stderrSha256"]);
        Assert.AreEqual(2, (int?)process["exitCode"]);
        Assert.AreEqual("diagnostic stdoutdiagnostic stderr", File.ReadAllText(Path.Combine(fixture.Output, "logs/mutation.log")));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Root => directory.Path;
        internal string Language => Path.Combine(Root, "implementations/csharp");
        internal string Output => Path.Combine(Root, "reports");
        internal string Report => Path.Combine(Output, "attachment/coverage.cobertura.xml");
        internal string MutationReport => Path.Combine(Output, "mutation-report.json");
        internal string[] Paths { get; }
        internal JsonObject Matrix { get; }
        internal JsonObject Mutants { get; }
        internal XDocument Trx { get; }
        internal XDocument Cobertura { get; }

        internal Fixture()
        {
            string[] names = ["A.cs", "B.cs", "C.cs"];
            Paths = names.Select(name => "implementations/csharp/src/CIS.Core/Logging/MicrosoftLogger/" + name).ToArray();
            foreach (string path in Paths) { Write(Path.Combine(Root, path), "source"); }
            Matrix = new JsonObject
            {
                ["shippingProjects"] = new JsonArray(new JsonObject
                {
                    ["moduleSlices"] = new JsonArray(new JsonObject
                    {
                        ["id"] = "logging-microsoft",
                        ["sources"] = new JsonArray(Paths.Select(path => (JsonNode)JsonValue.Create(path["implementations/csharp/".Length..])).ToArray())
                    })
                })
            };
            Trx = XDocument.Parse("<TestRun><ResultSummary outcome='Completed'><Counters total='2' executed='2' passed='2' failed='0'/></ResultSummary><UriAttachment><A href='attachment/coverage.cobertura.xml'/></UriAttachment></TestRun>");
            var classes = new XElement("classes");
            foreach (string path in Paths)
            {
                classes.Add(new XElement("class", new XAttribute("filename", Path.GetFullPath(Path.Combine(Root, path)).Replace('\\', '/')),
                    new XElement("lines",
                        new XElement("line", new XAttribute("number", "1"), new XAttribute("hits", "1"),
                            new XAttribute("branch", "true"), new XAttribute("condition-coverage", "50% (1/2)")),
                        new XElement("line", new XAttribute("number", "2"), new XAttribute("hits", "0")))));
            }
            Cobertura = new XDocument(new XElement("coverage", new XElement("packages", new XElement("package", classes))));
            var files = new JsonObject();
            foreach (string name in names)
            { files["Logging/MicrosoftLogger/" + name] = new JsonObject { ["mutants"] = new JsonArray(new JsonObject { ["status"] = "Killed" }) }; }
            Mutants = new JsonObject { ["files"] = files };
            SaveCoverage();
        }
        internal void SaveCoverage()
        {
            Write(Path.Combine(Output, "report.trx"), Trx.ToString());
            Write(Report, Cobertura.ToString());
        }
        public void Dispose() => directory.Dispose();
    }
}
