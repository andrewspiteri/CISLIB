using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests.Publications;

internal sealed class PublicationFixture : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly TemporaryDirectory directory = new();
    private readonly Dictionary<int, JsonObject> records = [];

    internal PublicationFixture()
    {
        Ledger = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["registryId"] = "cislib.csharp.publications",
            ["implementation"] = "csharp",
            ["identities"] = new JsonArray(new JsonObject
            {
                ["identityId"] = "cis.core.csharp",
                ["packageId"] = "CIS.Core",
                ["assemblyName"] = "CIS.Core",
                ["rootNamespace"] = "CIS",
                ["compatibilityLine"] = "permanent"
            }),
            ["publications"] = new JsonArray()
        };
        Directory.CreateDirectory(Path.Combine(Root, "artifacts"));
        Directory.CreateDirectory(Path.Combine(Root, "records"));
        File.WriteAllText(PayloadPath, "synthetic format bytes\n", new UTF8Encoding(false));
        FreezePrevious();
        AddPublication("0.1.0-rc.1");
    }

    internal string Root => directory.Path;
    internal string LedgerPath => Path.Combine(Root, "ledger.json");
    internal string PreviousPath => Path.Combine(Root, "previous.json");
    internal string ValidationPath => Path.Combine(Root, "validation.json");
    internal string PayloadPath => Path.Combine(Root, "artifacts", "format-only.bin");
    internal JsonObject Ledger { get; }
    internal string PreviousHash { get; private set; } = string.Empty;
    internal bool UsePrevious { get; set; } = true;
    internal bool SupplyPreviousHash { get; set; } = true;
    internal JsonArray Publications => Ledger["publications"]!.AsArray();
    internal JsonObject FirstRecord => records[1];

    internal void FreezePrevious()
    {
        Write(PreviousPath, Ledger);
        PreviousHash = Hash(PreviousPath);
    }

    internal void AddPublication(string version)
    {
        int sequence = Publications.Count + 1;
        records.Add(sequence, SyntheticPublication.Build(sequence, version, Reference(PayloadPath, "application/octet-stream")));
        SaveRecord(sequence);
    }

    internal void SaveRecord(int sequence = 1)
    {
        string path = RecordPath(sequence);
        Write(path, records[sequence]);
        var entry = new JsonObject
        {
            ["sequence"] = sequence,
            ["identityId"] = records[sequence]["identityId"]!.DeepClone(),
            ["packageVersion"] = records[sequence]["packageVersion"]!.DeepClone(),
            ["record"] = Reference(path, "application/json")
        };
        if (Publications.Count < sequence)
        {
            Publications.Add(entry);
        }
        else
        {
            Publications[sequence - 1] = entry;
        }
    }

    internal string RecordPath(int sequence = 1) => Path.Combine(Root, "records", sequence + ".json");

    internal async Task<JsonObject> Validate(string repositoryRoot, int expectedExit)
    {
        if (!File.Exists(LedgerPath))
        {
            Write(LedgerPath, Ledger);
        }

        string[] inputs = Snapshot();
        var arguments = new List<string>
        {
            "-NoProfile", "-File", Path.Combine(repositoryRoot, "eng", "check-publications.ps1"),
            "-LedgerPath", LedgerPath, "-ArtifactRoot", Root, "-OutputPath", ValidationPath
        };
        if (UsePrevious)
        {
            arguments.AddRange(["-PreviousLedgerPath", PreviousPath]);
            if (SupplyPreviousHash)
            {
                arguments.AddRange(["-PreviousLedgerSha256", PreviousHash]);
            }
        }

        ProcessResult result = await ProcessRunner.RunAsync("pwsh", arguments, repositoryRoot, TimeSpan.FromSeconds(30));
        Assert.AreEqual(expectedExit, result.ExitCode, result.StandardOutput + result.StandardError);
        CollectionAssert.AreEqual(inputs, Snapshot(), "Validator must not modify ledger, previous anchor, records or artifacts.");
        return JsonNode.Parse(File.ReadAllText(ValidationPath))!.AsObject();
    }

    public void Dispose() => directory.Dispose();

    private JsonObject Reference(string path, string mediaType) => new()
    {
        ["path"] = Path.GetRelativePath(Root, path).Replace('\\', '/'),
        ["sha256"] = Hash(path),
        ["bytes"] = new FileInfo(path).Length,
        ["mediaType"] = mediaType
    };

    private string[] Snapshot() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(path => path != ValidationPath).Order(StringComparer.Ordinal)
        .Select(path => Path.GetRelativePath(Root, path) + ':' + Hash(path)).ToArray();

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Write(string path, JsonObject value) => File.WriteAllText(path, value.ToJsonString(JsonOptions), new UTF8Encoding(false));
}
