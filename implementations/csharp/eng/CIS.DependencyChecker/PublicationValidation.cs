using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CIS.Verification;

internal static class PublicationValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static int Run(string[] args)
    {
        try
        {
            if (args is not [string repository, string ledger, string root, string previous, string hash, string output])
            { throw new ArgumentException("Usage: publications <repository> <ledger> <artifact-root> <previous-ledger|-> <trusted-sha256|-> <output>"); }
            JsonObject report = Validate(ledger, root, Path.Combine(repository, "compatibility/csharp/publications"), previous == "-" ? null : previous, hash == "-" ? null : hash);
            Write(repository, ledger, previous == "-" ? null : previous, output, report);
            return report["exitCode"]!.GetValue<int>();
        }
        catch (Exception error) when (IsInputError(error)) { Console.Error.WriteLine(error.Message); return 1; }
    }

    internal static JsonObject Validate(string ledger, string root, string schemas, string? previous = null, string? trustedHash = null)
    {
        try { return ValidateCore(ledger, root, schemas, previous, trustedHash); }
        catch (Exception error) when (IsInputError(error))
        {
            string code = error.Message.Contains(':', StringComparison.Ordinal) ? error.Message.Split(':', 2)[0] : "InvalidPublicationInput";
            return new JsonObject
            {
                ["schema"] = "cis.publication-validation/v1",
                ["exitCode"] = 2,
                ["compatibilityQualified"] = false,
                ["issues"] = new JsonArray(new JsonObject { ["code"] = code, ["detail"] = error.Message })
            };
        }
    }

    internal static void Write(string repository, string ledger, string? previous, string output, JsonObject result)
    {
        string full = Path.GetFullPath(output), owned = Path.GetFullPath(Path.Combine(repository, ".artifacts/csharp/publications/validation.json"));
        string storage = Path.GetFullPath(Path.Combine(repository, "compatibility/csharp/publications"));
        Require(!full.Equals(Path.GetFullPath(ledger), PathComparison) && (previous is null || !full.Equals(Path.GetFullPath(previous), PathComparison)), "Refuse to overwrite a publication ledger with validation output.");
        Require(!full.Equals(storage, PathComparison) && !full.StartsWith(storage + Path.DirectorySeparatorChar, PathComparison), "Refuse to write validation reports into retained publication storage.");
        Require(!File.Exists(full) || full.Equals(owned, PathComparison), "Refuse to overwrite an existing custom output file.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, result.ToJsonString(JsonOptions), new UTF8Encoding(false));
        Console.WriteLine(result.ToJsonString(JsonOptions));
    }

    private static JsonObject ValidateCore(string ledger, string root, string schemas, string? previous, string? trustedHash)
    {
        JsonElement current = Read(ledger, Path.Combine(schemas, "ledger.schema.json"), "LedgerSchemaInvalid");
        Require(string.IsNullOrEmpty(previous) == string.IsNullOrEmpty(trustedHash), "MissingHistoryAnchor: previous ledger path and externally trusted SHA-256 must be supplied together.");
        if (!string.IsNullOrEmpty(previous))
        {
            Require(Regex.IsMatch(trustedHash!, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) && Hash(previous) == trustedHash, "HistoryAnchorMismatch: previous ledger bytes differ from the trusted anchor.");
            JsonElement old = Read(previous, Path.Combine(schemas, "ledger.schema.json"), "LedgerSchemaInvalid");
            foreach (string name in new[] { "schemaVersion", "registryId", "implementation" })
            { Require(JsonElement.DeepEquals(current.GetProperty(name), old.GetProperty(name)), "HistoryRewrite: registry identity changed."); }
            foreach (string name in new[] { "identities", "publications" })
            {
                JsonElement before = old.GetProperty(name), after = current.GetProperty(name);
                Require(after.GetArrayLength() >= before.GetArrayLength(), "HistoryRemoval: historical membership was removed.");
                for (int i = 0; i < before.GetArrayLength(); i++)
                { Require(JsonElement.DeepEquals(before[i], after[i]), "HistoryRewrite: historical entries changed or were reordered."); }
            }
        }
        var identities = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement identity in Items(current, "identities"))
        { Require(identities.TryAdd(Text(identity, "identityId"), identity) && packages.Add(Text(identity, "packageId")), "DuplicateIdentity: package/contract membership must be unique."); }
        var versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int position = 0;
        foreach (JsonElement publication in Items(current, "publications"))
        {
            position++;
            Require(JsonElement.DeepEquals(publication.GetProperty("sequence"), JsonSerializer.SerializeToElement(position)), "InvalidSequence: publication order must be contiguous from one.");
            string version = Text(publication, "packageVersion"), identityId = Text(publication, "identityId");
            if (version.Contains('-', StringComparison.Ordinal))
            {
                foreach (string part in version.Split('-', 2)[1].Split('.'))
                { Require(!(part.All(char.IsAsciiDigit) && part.Length > 1 && part[0] == '0'), "InvalidPackageVersion: numeric prerelease labels must be canonical."); }
            }
            Require(identities.ContainsKey(identityId), "UnknownIdentity: publication is absent from retained package membership.");
            Require(versions.Add(identityId + "|" + version), "DuplicatePublication: normalized package version was already published.");
            string recordPath = Artifact(root, publication.GetProperty("record"));
            JsonElement record = Read(recordPath, Path.Combine(schemas, "baseline.schema.json"), "BaselineSchemaInvalid");
            foreach (string name in new[] { "sequence", "identityId", "packageVersion" })
            { Require(JsonElement.DeepEquals(record.GetProperty(name), publication.GetProperty(name)), "RecordIdentityMismatch: ledger and immutable publication record disagree."); }
            JsonElement identity = identities[identityId];
            Require(Text(record, "packageId") == Text(identity, "packageId"), "RecordIdentityMismatch: record package differs from immutable membership.");
            Require(DateTimeOffset.TryParseExact(Text(record, "exposedAtUtc"), "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _), "InvalidPublicationTime: impossible UTC exposure instant.");
            Links(record, identity);
            Artifacts(root, record);
        }
        return new JsonObject
        {
            ["schema"] = "cis.publication-validation/v1",
            ["exitCode"] = 0,
            ["identityCount"] = identities.Count,
            ["publicationCount"] = position,
            ["appendOnlyStatus"] = string.IsNullOrEmpty(previous) ? "NotCompared" : "ComparedWithTrustedPrevious",
            ["historyStatus"] = position == 0 ? "NoPublicationsRecorded" : "RecordedPublicationsValidated",
            ["firstReleaseHarnessRequired"] = true,
            ["actualPublicationAttested"] = false,
            ["compatibilityChecksExecuted"] = 0,
            ["compatibilityQualified"] = false,
            ["issues"] = new JsonArray()
        };
    }

    private static void Links(JsonElement record, JsonElement identity)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var frameworks = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement target in Items(record, "targets"))
        {
            Require(targets.Add(Text(target, "targetFramework") + "|" + target.GetProperty("runtimeIdentifier").GetString() + "|" + Text(target, "assetPath")), "DuplicateTarget: baseline target asset is duplicated.");
            frameworks.Add(Text(target, "targetFramework"));
            Require(Text(target.GetProperty("assemblyIdentity"), "name") == Text(identity, "assemblyName"), "RecordIdentityMismatch: assembly identity differs from package membership.");
        }
        var sources = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement fixture in Items(record, "sourceFixtures"))
        {
            Require(sources.Add(Text(fixture, "fixtureId")), "DuplicateFixture: source fixture ID is duplicated.");
            Require(frameworks.Contains(Text(fixture, "contractTargetFramework")), "UnknownFixtureTarget: source fixture target has no retained asset.");
        }
        var binaries = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement fixture in Items(record, "binaryFixtures"))
        {
            Require(binaries.Add(Text(fixture, "fixtureId")), "DuplicateFixture: binary fixture ID is duplicated.");
            Require(sources.Contains(Text(fixture, "sourceFixtureId")), "UnknownSourceFixture: binary fixture has no preserved source origin.");
        }
        var executions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement execution in Items(record, "executions"))
        {
            Require(executions.TryAdd(Text(execution, "executionId"), execution), "DuplicateExecution: execution ID is duplicated.");
            Require(frameworks.Contains(Text(execution, "targetFramework")), "UnknownFixtureTarget: execution target has no retained asset.");
            foreach (JsonElement id in Items(execution, "consumerFixtureIds"))
            { Require(binaries.Contains(id.GetString()!), "UnknownBinaryFixture: execution references an absent binary consumer."); }
        }
        foreach (JsonElement fixture in Items(record, "binaryFixtures"))
        { Require(executions.TryGetValue(Text(fixture, "originalExecutionId"), out JsonElement original) && Items(original, "consumerFixtureIds").Any(id => id.GetString() == Text(fixture, "fixtureId")), "UnknownOriginalExecution: binary origin execution does not include its consumer."); }
        var contracts = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement contract in Items(record, "contracts"))
        { Require(contracts.Add(Text(contract, "contractId") + "|" + Text(contract, "contractVersion")), "DuplicateContract: contract membership is duplicated."); }
    }

    private static JsonElement Read(string path, string schema, string code)
    {
        using JsonDocument document = StrictJson(path);
        using JsonDocument definition = StrictJson(schema);
        try { PublicationSchema.Validate(document.RootElement, definition.RootElement); }
        catch (Exception error) when (IsInputError(error)) { throw new InvalidDataException(code + ": " + error.Message, error); }
        return document.RootElement.Clone();
    }

    private static JsonDocument StrictJson(string path)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path))); }
        catch (Exception error) when (IsInputError(error)) { throw new InvalidDataException("InvalidJson: strict UTF-8 JSON is required.", error); }
        try { UniqueProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }
    private static void UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            { Require(names.Add(property.Name), "DuplicateJsonProperty: duplicate object member."); UniqueProperties(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement child in value.EnumerateArray()) { UniqueProperties(child); } }
    }
    private static string Artifact(string root, JsonElement reference)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string relative = Text(reference, "path"), path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        Require(path.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison), "ArtifactPathEscape: artifact escapes the retained root.");
        Require(File.Exists(path), "MissingArtifact: " + relative);
        for (FileSystemInfo? item = new FileInfo(path); item is not null; item = item is FileInfo file ? file.Directory : ((DirectoryInfo)item).Parent)
        {
            Require((item.Attributes & FileAttributes.ReparsePoint) == 0, "ArtifactPathEscape: linked artifacts are not immutable retained files.");
            if (item.FullName.Equals(fullRoot, PathComparison)) { break; }
        }
        Require(new FileInfo(path).Length == reference.GetProperty("bytes").GetDecimal(), "ArtifactLengthMismatch: " + relative);
        Require(Hash(path) == Text(reference, "sha256"), "ArtifactHashMismatch: " + relative);
        return path;
    }
    private static void Artifacts(string root, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (new[] { "path", "sha256", "bytes", "mediaType" }.All(name => value.TryGetProperty(name, out _))) { Artifact(root, value); return; }
            foreach (JsonProperty property in value.EnumerateObject()) { Artifacts(root, property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) { foreach (JsonElement child in value.EnumerateArray()) { Artifacts(root, child); } }
    }
    private static JsonElement.ArrayEnumerator Items(JsonElement value, string name) => value.GetProperty(name).EnumerateArray();
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    private static bool IsInputError(Exception error) => error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ArgumentException or InvalidOperationException or FormatException or OverflowException or RegexMatchTimeoutException or DecoderFallbackException;
}
