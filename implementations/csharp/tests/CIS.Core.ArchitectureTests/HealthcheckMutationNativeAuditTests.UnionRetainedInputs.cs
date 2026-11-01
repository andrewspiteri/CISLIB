using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

public sealed partial class HealthcheckMutationNativeAuditTests
{
    [TestMethod]
    [DataRow("cis.healthcheck.native-parent-extents/v1-development")]
    [DataRow("cis.healthcheck.current194-explicit-parent-extents/v1")]
    public void MaintainedUnionReaderBindsTheExactRetainedParentMap(string schema)
    {
        using var fixture = new TraceFixture();
        using var metadata = new TemporaryDirectory();
        using var output = new TemporaryDirectory();
        _ = fixture.Retained(true);
        (JsonObject descriptor, string[] physicalLogs) = WriteUnionExtentMetadata(fixture, metadata.Path, schema);
        string outputRoot = Path.Combine(output.Path, "union-output");

        object retained = BindMaintainedUnionExtents(metadata.Path, outputRoot, descriptor, physicalLogs);

        Assert.AreEqual("CIS.Verification.RetainedDiagnosticInputs", retained.GetType().FullName);
        Assert.AreEqual("CIS.DependencyChecker", retained.GetType().Assembly.GetName().Name);
        PropertyInfo logicalPaths = retained.GetType().GetProperty("LogicalPaths", BindingFlags.Instance | BindingFlags.NonPublic)!;
        CollectionAssert.AreEqual(new[]
        {
            Path.Combine(fixture.DiagnosticRoot, "Runner 0-log.host.synthetic.txt"),
            Path.Combine(fixture.DiagnosticRoot, "Runner 0-log.txt")
        }, (string[])logicalPaths.GetValue(retained)!);
        Assert.IsFalse(Directory.Exists(outputRoot), "Metadata binding must not create a union output.");
    }

    [TestMethod]
    public void PendingUnionExtentInputsRejectOutputInsideTheOriginalLogRoot()
    {
        using var fixture = new TraceFixture();
        using var metadata = new TemporaryDirectory();
        _ = fixture.Retained(true);
        (JsonObject descriptor, string[] physicalLogs) = WriteUnionExtentMetadata(fixture, metadata.Path);
        string outputRoot = Path.Combine(fixture.DiagnosticRoot, "union-output");
        Assert.IsFalse(Directory.Exists(outputRoot));

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            BindMaintainedUnionExtents(metadata.Path, outputRoot, descriptor, physicalLogs));

        Assert.AreEqual("Output must be separate from immutable input roots.", error.Message);
        Assert.IsFalse(Directory.Exists(outputRoot), "Reject the pending output before creating anything in original logs.");
    }

    private static (JsonObject Descriptor, string[] PhysicalLogs) WriteUnionExtentMetadata(TraceFixture fixture, string metadata,
        string schema = "cis.healthcheck.native-parent-extents/v1-development")
    {
        string root = fixture.DiagnosticRoot;
        string[] paths = Directory.GetFiles(root).Order(StringComparer.Ordinal).ToArray();
        JsonObject Pin(string path) => new()
        {
            ["path"] = path,
            ["bytes"] = new FileInfo(path).Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))
        };
        var completePhysical = new JsonArray();
        var logEntries = new JsonArray();
        foreach (string path in paths)
        {
            JsonObject pin = Pin(path);
            completePhysical.Add(new JsonObject
            {
                ["Path"] = path,
                ["Bytes"] = pin["bytes"]!.DeepClone(),
                ["Sha256"] = pin["sha256"]!.DeepClone()
            });
            pin["path"] = Path.GetFileName(path);
            logEntries.Add(pin);
        }

        string logsPath = Path.Combine(metadata, "logs.json");
        File.WriteAllText(logsPath, new JsonObject
        {
            ["schema"] = "cis.healthcheck.native-log-inventory/v1-development",
            ["root"] = root,
            ["files"] = logEntries
        }.ToJsonString());
        string mapPath = Path.Combine(metadata, "parents.json");
        File.WriteAllText(mapPath, new JsonObject
        {
            ["schema"] = schema,
            ["originalLogRoot"] = root,
            ["completePhysicalLogs"] = completePhysical,
            ["mappings"] = new JsonArray(new JsonObject
            {
                ["logicalPath"] = Path.Combine(root, "Runner 0-log.txt"),
                ["extents"] = new JsonArray(Pin(Path.Combine(root, "Runner 0-log.bak.txt")), Pin(Path.Combine(root, "Runner 0-log.txt")))
            }),
            ["semanticContextSupplied"] = false,
            ["qualificationClaim"] = false,
            ["appliedKillCredit"] = 0
        }.ToJsonString());
        return (new JsonObject
        {
            ["state"] = "Pending",
            ["logsManifest"] = Pin(logsPath),
            ["parentExtentManifest"] = Pin(mapPath),
            // The reader checks declaration shape only. This placeholder is not
            // native kernel evidence and is never passed to ReadKernelInputs.
            ["kernelManifest"] = new JsonObject()
        }, paths);
    }

    private static object BindMaintainedUnionExtents(string repository, string output, JsonObject descriptor, string[] physicalLogs)
    {
        DirectoryInfo? language = new(AppContext.BaseDirectory);
        while (language is not null && !File.Exists(Path.Combine(language.FullName, "CisLib.slnx")))
        {
            language = language.Parent;
        }

        Assert.IsNotNull(language);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string checkerPath = Path.Combine(language.FullName, "eng", "CIS.DependencyChecker", "bin", configuration, "net10.0", "CIS.DependencyChecker.dll");
        Assert.IsTrue(File.Exists(checkerPath), "Build the maintained checker in the test configuration before this metadata control.");
        Assembly checker = Assembly.LoadFrom(checkerPath);
        // A project reference may already have loaded the copy-local checker
        // with this identity. Require the actual maintained bytes in either location.
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(checkerPath))),
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(checker.Location))));
        Type union = checker.GetType("CIS.Verification.HealthcheckMutationUnionVerification", true)!;
        Type pinnedType = union.GetNestedType("PinnedInputs", BindingFlags.NonPublic)!;
        object pinned = Activator.CreateInstance(pinnedType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, [repository, output], null)!;
        MethodInfo reader = union.GetMethod("ReadRetainedDiagnosticInputs", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            object? retained = reader.Invoke(null, [pinned, descriptor, physicalLogs, "ubuntu"]);
            Assert.IsNotNull(retained);
            return retained;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }
}
