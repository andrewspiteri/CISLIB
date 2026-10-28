using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Verification;

// The installed shared verifier establishes execution facts. This reader checks
// that its bounded response actually binds the requested profiles and artifacts.
internal static class HealthcheckSharedMutationProtocol
{
    internal sealed record Profile(string Path, string Sha256, JsonObject Value);
    internal sealed record Input(string Path, string Sha256, string[] Roles, bool CompareCurrent);

    internal static JsonObject Parse(string text)
    {
        Need(Encoding.UTF8.GetByteCount(text) is > 0 and <= 1048576, "Shared receipt exceeds its bounded protocol size.");
        using JsonDocument document = JsonDocument.Parse(text);
        Unique(document.RootElement);
        return JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("Expected a shared receipt object.");
    }

    internal static Input[] Validate(JsonObject receipt, Profile[] profiles, string snapshot,
        string testingPath, string testingSha256, string helperPath, string helperSha256, string lane,
        DateTimeOffset startedUtc, DateTimeOffset finishedUtc)
    {
        Need(IsVerifiedResponse(receipt, 0), "A successful shared verification receipt is required.");
        Need(lane is "ubuntu" or "windows", "Unknown native platform lane.");
        Need(profiles.Length > 0 && profiles.Select(profile => profile.Path).Distinct(StringComparer.Ordinal).Count() == profiles.Length,
            "Original and rerun profiles must be distinct.");
        Profile original = profiles[0];
        Need(Number(receipt, "schemaVersion") == 1 && Text(receipt, "policy") == "cis-incremental-evidence-v1"
            && Text(receipt, "status") == "verified" && Number(receipt, "exitCode") == 0
            && Flag(receipt, "scopeComplete") && !Flag(receipt, "qualified"), "Shared evidence is not a complete development-scope receipt.");
        _ = Flag(receipt, "thresholdMet"); // Qualification is computed over the complete private union.
        Need(Text(receipt, "profileId") == Text(original.Value, "id") && Text(receipt, "profilePath") == original.Path
            && Text(receipt, "profileSha256") == original.Sha256 && Text(receipt, "testingProducerSha256") == testingSha256
            && Text(receipt, "helperProducerSha256") == helperSha256, "Shared profile or producer identity differs.");
        Hash(receipt, "environmentSha256");
        Need(Text(receipt, "graphBuildId").Length > 0 && Text(receipt, "osDescription").Length > 0
            && Text(receipt, "runtimeIdentifier").StartsWith(lane == "ubuntu" ? "linux-" : "win-", StringComparison.Ordinal),
            "Shared graph or native platform identity is missing or foreign.");
        DateTimeOffset verified = Time(receipt, "verifiedUtc");
        Need(verified >= startedUtc.AddSeconds(-1) && verified <= finishedUtc.AddSeconds(1), "Shared receipt is outside this verification invocation.");
        _ = Strings(receipt, "diagnostics");

        Input[] inputs = Objects(receipt, "inputs").Select(value => ReadInput(value, snapshot)).ToArray();
        Need(inputs.Length is > 0 and <= 16384 && inputs.Select(input => input.Path).Distinct(StringComparer.Ordinal).Count() == inputs.Length,
            "Shared input inventory is empty, duplicate or excessive.");
        foreach (Profile profile in profiles)
        {
            RequireInput(profile.Path, "profile", profile.Sha256);
            foreach ((string property, string role) in new[] { ("sourceFiles", "source"), ("testFiles", "test"), ("configFiles", "configuration"), ("toolFiles", "tool") })
            {
                foreach (string path in Strings(profile.Value, property)) { RequireInput(PortableInput(snapshot, path), role); }
            }
        }
        RequireInput(PortableInput(snapshot, testingPath), "producer", testingSha256);
        RequireInput(PortableInput(snapshot, helperPath), "producer", helperSha256);
        foreach (Input input in inputs.Where(input => input.Roles.Contains("profile", StringComparer.Ordinal)))
        {
            Need(profiles.Any(profile => profile.Path == input.Path && profile.Sha256 == input.Sha256), "An unrequested profile appears in the shared input inventory.");
        }
        foreach (Input input in inputs.Where(input => input.Roles.Contains("producer", StringComparer.Ordinal)))
        {
            Need((input.Path == PortableInput(snapshot, testingPath) && input.Sha256 == testingSha256)
                || (input.Path == PortableInput(snapshot, helperPath) && input.Sha256 == helperSha256), "Unknown shared producer input.");
        }
        string[] helpers = inputs.Where(input => input.Roles.Contains("producer-history", StringComparer.Ordinal)
            && Path.GetFileName(input.Path) == "Cis.Abstractions.dll").Select(input => input.Sha256).Append(helperSha256).Distinct(StringComparer.Ordinal).ToArray();

        JsonObject[] closures = Objects(receipt, "closures");
        Need(closures.Length is > 0 and <= 46 && closures.Select(closure => Text(closure, "id")).Distinct(StringComparer.Ordinal).Count() == closures.Length,
            "Shared closures are missing or duplicated.");
        foreach (JsonObject closure in closures)
        {
            Fields(closure, "id", "testInputIndices", "dependencyInputIndices"); Hash(closure, "id");
            Indices(closure, "testInputIndices", "test", true);
            Indices(closure, "dependencyInputIndices", "dependency", false);
        }
        string[] sourceFiles = Strings(original.Value, "sourceFiles");
        JsonObject[] batches = Objects(receipt, "batches");
        Need(batches.Length == sourceFiles.Length && batches.Select(batch => Text(batch, "sourceFile")).Distinct(StringComparer.Ordinal).Count() == batches.Length
            && batches.Select(batch => Text(batch, "sourceFile")).ToHashSet(StringComparer.Ordinal).SetEquals(sourceFiles), "Shared batches do not exactly cover their original profile.");
        foreach (JsonObject batch in batches)
        {
            Fields(batch, "sourceFile", "closureId", "unitFingerprint", "mutationFingerprint", "testFilterSha256", "fullFile", "unitReportPath",
                "unitReportSha256", "unitReportBytes", "unitStageSha256", "unitFinishedUtc", "unitPassed", "mutationReportPath", "mutationReportSha256",
                "mutationReportBytes", "mutationStageSha256", "raw", "executions", "fullPostflightReceiptVerified", "rawPostflightRetained");
            Need(Flag(batch, "fullFile") && Flag(batch, "fullPostflightReceiptVerified") && !Flag(batch, "rawPostflightRetained")
                && Number(batch, "unitPassed") > 0 && closures.Any(closure => Text(closure, "id") == Text(batch, "closureId")),
                "Shared batch lacks its full-file, baseline or closure binding.");
            foreach (string field in new[] { "unitFingerprint", "mutationFingerprint", "testFilterSha256", "unitStageSha256", "mutationStageSha256" }) { Hash(batch, field); }
            Need(Time(batch, "unitFinishedUtc") <= finishedUtc, "Shared unit baseline is in the future.");
            Report(batch, "unitReportPath", "unitReportSha256", "unitReportBytes", original, "unit");
            Report(batch, "mutationReportPath", "mutationReportSha256", "mutationReportBytes", original, "mutation");
            Executions(batch, ["unit", "mutation", "recovery", "postflight"], helpers);
            Counts(Object(batch, "raw"));
        }

        JsonObject[] refinements = Objects(receipt, "refinements");
        Need(refinements.Length == profiles.Length - 1
            && refinements.Select(refinement => Text(refinement, "rerunProfilePath")).Distinct(StringComparer.Ordinal).Count() == refinements.Length,
            "Shared refinements do not match the explicit rerun request.");
        foreach (JsonObject refinement in refinements)
        {
            Fields(refinement, "rerunProfilePath", "rerunProfileSha256", "bindings", "baselineBindings", "rerunReports");
            Profile? rerun = profiles.Skip(1).SingleOrDefault(profile => profile.Path == Text(refinement, "rerunProfilePath"));
            Need(rerun is not null && rerun.Sha256 == Text(refinement, "rerunProfileSha256"), "A refinement names an unrequested or changed profile.");
            ValidateRefinement(refinement, rerun!, batches, helpers, finishedUtc);
        }
        JsonObject[] credits = Objects(receipt, "credits");
        Need(credits.Select(credit => (Text(credit, "sourceFile"), Text(credit, "mutantId"))).Distinct().Count() == credits.Length,
            "Duplicate shared refinement credit is forbidden.");
        foreach (JsonObject credit in credits)
        {
            Fields(credit, "sourceFile", "mutantId", "definitionSha256", "killingTestIds", "killingTestDefinitionSha256");
            Hash(credit, "definitionSha256");
            string[] tests = Strings(credit, "killingTestIds"), definitions = Strings(credit, "killingTestDefinitionSha256");
            Need(tests.Length > 0 && tests.Length == definitions.Length && tests.Distinct(StringComparer.Ordinal).Count() == tests.Length
                && tests.All(test => Guid.TryParse(test, out _)) && definitions.All(IsHash), "Refinement killing-test identities are missing, duplicate or malformed.");
            Need(sourceFiles.Contains(Text(credit, "sourceFile"), StringComparer.Ordinal) && Text(credit, "mutantId").Length > 0
                && refinements.Any(refinement => Objects(refinement, "bindings").Any(binding => Text(binding, "sourceFile") == Text(credit, "sourceFile"))),
                "A shared credit has no requested original/rerun binding.");
        }
        var raw = Counts(Object(receipt, "raw"));
        var effective = Counts(Object(receipt, "effective"));
        var measured = batches.Select(batch => Counts(Object(batch, "raw"))).ToArray();
        Need(raw == (measured.Sum(count => count.Killed), measured.Sum(count => count.Survived), measured.Sum(count => count.Timeout),
                measured.Sum(count => count.Uncovered), measured.Sum(count => count.Denominator))
            && effective == (raw.Killed + credits.Length, raw.Survived, raw.Timeout - credits.Length, raw.Uncovered, raw.Denominator),
            "Shared raw/effective counts do not match the received batches and distinct credits.");
        return inputs;

        void RequireInput(string path, string role, string? sha256 = null) => Need(inputs.Any(input => input.Path == path
            && input.Roles.Contains(role, StringComparer.Ordinal) && (sha256 is null || input.Sha256 == sha256)), "A declared shared input or producer pin is missing: " + role);
        void Indices(JsonObject closure, string name, string role, bool required)
        {
            JsonArray indices = Array(closure, name);
            long[] values = indices.Select(node => IntegerValue(node, "closure index")).ToArray();
            Need((!required || values.Length > 0) && values.Distinct().Count() == values.Length
                && values.All(index => index >= 0 && index < inputs.Length && inputs[(int)index].Roles.Contains(role, StringComparer.Ordinal)),
                "A closure references missing or wrongly classified inputs.");
        }
    }

    internal static bool IsVerifiedResponse(JsonObject receipt, int processExit)
    {
        Fields(receipt, "status", "exitCode", "schemaVersion", "policy", "qualified", "profileId", "profilePath", "profileSha256",
            "graphBuildId", "runtimeIdentifier", "osDescription", "environmentSha256", "testingProducerSha256", "helperProducerSha256",
            "verifiedUtc", "scopeComplete", "thresholdMet", "raw", "effective", "inputs", "closures", "batches", "refinements", "credits", "diagnostics");
        Need(Number(receipt, "schemaVersion") == 1 && Text(receipt, "policy") == "cis-incremental-evidence-v1"
            && !Flag(receipt, "qualified"), "Unsupported shared development-evidence protocol.");
        _ = Flag(receipt, "thresholdMet");
        string status = Text(receipt, "status");
        if (status == "verified")
        {
            Need(processExit == 0 && Number(receipt, "exitCode") == 0 && Flag(receipt, "scopeComplete"), "Shared completion flags or process exit disagree.");
            return true;
        }
        Need(status == "verification-failed" && processExit == 4 && Number(receipt, "exitCode") == 4 && !Flag(receipt, "scopeComplete"),
            "Unknown or inconsistent shared verification outcome.");
        return false;
    }

    internal static void ValidateRawPopulation(JsonObject raw, string[] statuses)
    {
        Need(statuses.All(status => status is "Killed" or "Survived" or "Timeout" or "NoCoverage" or "CompileError" or "Ignored"),
            "Shared completed evidence contains unfinished or infrastructure outcomes.");
        var counts = Counts(raw);
        Need(counts == (statuses.LongCount(status => status == "Killed"), statuses.LongCount(status => status == "Survived"),
            statuses.LongCount(status => status == "Timeout"), statuses.LongCount(status => status == "NoCoverage"),
            statuses.LongCount(status => status is "Killed" or "Survived" or "Timeout" or "NoCoverage")), "Shared receipt counts differ from the pinned raw report.");
    }

    internal static string DefinitionSha256(HealthcheckMutationUnionModel.Source source, HealthcheckMutationUnionModel.Definition definition)
    {
        Need(definition.SourcePath == source.Path && definition.SourceSha256 == source.Sha256
            && definition.Start >= 0 && definition.End > definition.Start && definition.End <= source.Text.Length
            && source.Text[definition.Start..definition.End] == definition.Original, "Refinement definition is outside its current source.");
        (int startLine, int startColumn) = Position(definition.Start);
        (int endLine, int endColumn) = Position(definition.End);
        var value = new
        {
            source = source.Text,
            id = definition.Id,
            mutatorName = definition.Mutator,
            replacement = definition.Replacement,
            location = new { startLine, startColumn, endLine, endColumn }
        };
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })));

        (int Line, int Column) Position(int offset)
        {
            int line = 1, column = 1;
            for (int index = 0; index < offset; index++)
            {
                if (source.Text[index] == '\n') { line++; column = 1; }
                else { column++; }
            }
            return (line, column);
        }
    }

    private static void ValidateRefinement(JsonObject refinement, Profile rerun, JsonObject[] originals, string[] helpers, DateTimeOffset finishedUtc)
    {
        JsonObject[] bindings = Objects(refinement, "bindings"), reports = Objects(refinement, "rerunReports"), baselines = Objects(refinement, "baselineBindings");
        string[] selected = Strings(rerun.Value, "sourceFiles");
        Need(bindings.Length == selected.Length && bindings.Select(binding => Text(binding, "sourceFile")).Distinct(StringComparer.Ordinal).Count() == bindings.Length
            && bindings.Select(binding => Text(binding, "sourceFile")).ToHashSet(StringComparer.Ordinal).SetEquals(selected), "Refinement report bindings omit or duplicate their source scope.");
        Need(reports.Length == selected.Length * 2 && reports.Select(report => (Text(report, "sourceFile"), Text(report, "role"))).Distinct().Count() == reports.Length,
            "Refinement unit/mutation report roles are missing or duplicated.");
        foreach (JsonObject report in reports)
        {
            Fields(report, "sourceFile", "role", "path", "sha256", "bytes", "fingerprint", "stageSha256", "finishedUtc", "executions");
            string role = Text(report, "role");
            Need(selected.Contains(Text(report, "sourceFile"), StringComparer.Ordinal) && role is "unit" or "mutation", "Unknown refinement source/report role.");
            Report(report, "path", "sha256", "bytes", rerun, role); Hash(report, "fingerprint"); Hash(report, "stageSha256");
            Need(Time(report, "finishedUtc") <= finishedUtc, "Refinement report is in the future.");
            Executions(report, role == "unit" ? ["unit"] : ["mutation", "recovery", "postflight"], helpers);
        }
        foreach (JsonObject binding in bindings)
        {
            Fields(binding, "sourceFile", "originalReportPath", "originalReportSha256", "originalFingerprint", "rerunReportPath", "rerunReportSha256",
                "rerunFingerprint", "originalStageSha256", "rerunStageSha256");
            JsonObject? original = originals.SingleOrDefault(batch => Text(batch, "sourceFile") == Text(binding, "sourceFile"));
            JsonObject rerunReport = reports.Single(report => Text(report, "sourceFile") == Text(binding, "sourceFile") && Text(report, "role") == "mutation");
            Need(original is not null && Text(binding, "originalReportPath") == Text(original, "mutationReportPath")
                && Text(binding, "originalReportSha256") == Text(original, "mutationReportSha256") && Text(binding, "originalFingerprint") == Text(original, "mutationFingerprint")
                && Text(binding, "originalStageSha256") == Text(original, "mutationStageSha256") && Text(binding, "rerunReportPath") == Text(rerunReport, "path")
                && Text(binding, "rerunReportSha256") == Text(rerunReport, "sha256") && Text(binding, "rerunFingerprint") == Text(rerunReport, "fingerprint")
                && Text(binding, "rerunStageSha256") == Text(rerunReport, "stageSha256"), "Refinement original/rerun report bindings are stale or foreign.");
        }
        Need(baselines.Length == originals.Length && baselines.Select(baseline => Text(baseline, "sourceFile")).Distinct(StringComparer.Ordinal).Count() == baselines.Length,
            "Refinement baseline inventory is incomplete or duplicated.");
        foreach (JsonObject baseline in baselines)
        {
            Fields(baseline, "sourceFile", "reportPath", "reportSha256", "fingerprint", "finishedUtc");
            JsonObject? original = originals.SingleOrDefault(batch => Text(batch, "sourceFile") == Text(baseline, "sourceFile"));
            Need(original is not null && Text(baseline, "reportPath") == Text(original, "unitReportPath") && Text(baseline, "reportSha256") == Text(original, "unitReportSha256")
                && Text(baseline, "fingerprint") == Text(original, "unitFingerprint") && Time(baseline, "finishedUtc") == Time(original, "unitFinishedUtc"),
                "Refinement baseline bindings do not match current original unit evidence.");
        }
    }

    private static Input ReadInput(JsonObject value, string snapshot)
    {
        Fields(value, "path", "sha256", "roles"); Hash(value, "sha256");
        string path = Text(value, "path");
        string[] roles = Strings(value, "roles");
        string[] allowed = ["profile", "source", "test", "configuration", "tool", "dependency", "producer", "producer-history"];
        Need(roles.Length > 0 && roles.Distinct(StringComparer.Ordinal).Count() == roles.Length && roles.All(role => allowed.Contains(role, StringComparer.Ordinal)),
            "Shared input roles are empty, duplicate or unknown.");
        bool compare = roles.Any(role => role is "source" or "test" or "configuration" or "dependency");
        Need(!compare || !Path.IsPathFullyQualified(path), "Source/test/build closure must use repository-relative paths.");
        if (roles.Any(role => role is "producer" or "producer-history"))
        {
            Need(Path.IsPathFullyQualified(path) && !Inside(snapshot, path), "Producer binaries and compatibility history must be outside the native snapshot.");
        }
        if (!Path.IsPathFullyQualified(path)) { SafeRelative(path); }
        return new(path, Text(value, "sha256"), roles, compare);
    }

    private static void Executions(JsonObject value, string[] required, string[] helpers)
    {
        JsonObject[] executions = Objects(value, "executions");
        Need(executions.Length == required.Length && executions.Select(execution => Text(execution, "role")).ToHashSet(StringComparer.Ordinal).SetEquals(required),
            "Shared execution roles are missing, duplicate or unknown.");
        foreach (JsonObject execution in executions)
        {
            Fields(execution, "role", "status", "exitCode", "durationMilliseconds", "commandSha256", "helperProducerSha256", "outputTruncated", "hasError");
            Hash(execution, "commandSha256"); Hash(execution, "helperProducerSha256");
            Need(Text(execution, "status") == "exited" && !Flag(execution, "outputTruncated") && !Flag(execution, "hasError")
                && Number(execution, "durationMilliseconds") >= 0 && (Text(execution, "role") == "mutation" ? Number(execution, "exitCode") is 0 or 2 : Number(execution, "exitCode") == 0)
                && helpers.Contains(Text(execution, "helperProducerSha256"), StringComparer.Ordinal), "Shared native execution or producer receipt is unhealthy.");
        }
    }

    private static void Report(JsonObject value, string pathField, string hashField, string bytesField, Profile profile, string role)
    {
        string path = Text(value, pathField), id = Text(profile.Value, "id"); SafeRelative(path); SafeRelative(id);
        Need(!id.Contains('/') && path.StartsWith(".cis/local/testing/incremental/" + id + "/reports/", StringComparison.Ordinal)
            && path.EndsWith(role == "unit" ? "-unit.xml" : "-mutation.json", StringComparison.Ordinal) && Number(value, bytesField) > 0,
            "Shared report path or role leaves its original profile cache.");
        Hash(value, hashField);
    }

    private static (long Killed, long Survived, long Timeout, long Uncovered, long Denominator) Counts(JsonObject value)
    {
        Fields(value, "killed", "survived", "timedOut", "noCoverage", "denominator", "score");
        long killed = Number(value, "killed"), survived = Number(value, "survived"), timeout = Number(value, "timedOut"), uncovered = Number(value, "noCoverage"), denominator = Number(value, "denominator");
        Need(killed >= 0 && survived >= 0 && timeout >= 0 && uncovered >= 0 && denominator >= 0
            && killed <= int.MaxValue && survived <= int.MaxValue && timeout <= int.MaxValue && uncovered <= int.MaxValue
            && denominator == killed + survived + timeout + uncovered, "Shared mutation counts are invalid.");
        Need(denominator == 0 ? value["score"] is null : value["score"] is JsonValue score && score.TryGetValue(out double percent)
            && double.IsFinite(percent) && Math.Abs(percent - killed * 100d / denominator) < 0.00000001, "Shared mutation score differs from its raw population.");
        return (killed, survived, timeout, uncovered, denominator);
    }

    internal static string PortableInput(string snapshot, string path)
    {
        if (!Path.IsPathFullyQualified(path)) { SafeRelative(path); return path; }
        return Inside(snapshot, path) ? Path.GetRelativePath(snapshot, path).Replace('\\', '/') : Path.GetFullPath(path);
    }
    private static bool Inside(string root, string path) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static void SafeRelative(string path) => Need(!string.IsNullOrWhiteSpace(path) && !Path.IsPathFullyQualified(path) && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').All(part => part.Length > 0 && part is not ("." or "..")), "Unsafe shared artifact path.");
    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject()) { Need(names.Add(property.Name), "Duplicate shared receipt field."); Unique(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in value.EnumerateArray()) { Unique(item); }
        }
    }
    private static void Fields(JsonObject value, params string[] names) => Need(value.Count == names.Length
        && value.Select(field => field.Key).ToHashSet(StringComparer.Ordinal).SetEquals(names), "Unexpected or missing shared receipt fields.");
    private static bool IsHash(string text) => text.Length == 64 && text.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');
    private static void Hash(JsonObject value, string name) => Need(IsHash(Text(value, name)), "Malformed shared SHA-256 binding.");
    private static string Text(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out string? text)
        && text is not null ? text : throw new InvalidDataException("Malformed shared string: " + name);
    private static long Number(JsonObject value, string name) => IntegerValue(value[name], name);
    private static long IntegerValue(JsonNode? value, string name)
    {
        if (value is JsonValue item)
        {
            if (item.TryGetValue(out long number)) { return number; }
            if (item.TryGetValue(out int integer)) { return integer; }
        }
        throw new InvalidDataException("Malformed shared integer: " + name);
    }
    private static bool Flag(JsonObject value, string name) => value[name] is JsonValue item && item.TryGetValue(out bool flag)
        ? flag : throw new InvalidDataException("Malformed shared Boolean: " + name);
    private static DateTimeOffset Time(JsonObject value, string name) => DateTimeOffset.TryParse(Text(value, name), CultureInfo.InvariantCulture,
        DateTimeStyles.None, out DateTimeOffset time) ? time : throw new InvalidDataException("Malformed shared timestamp: " + name);
    private static JsonObject Object(JsonObject value, string name) => value[name] as JsonObject ?? throw new InvalidDataException("Malformed shared object: " + name);
    private static JsonArray Array(JsonObject value, string name) => value[name] as JsonArray ?? throw new InvalidDataException("Malformed shared array: " + name);
    private static JsonObject[] Objects(JsonObject value, string name) => Array(value, name).Select(node => node as JsonObject
        ?? throw new InvalidDataException("Malformed shared object array: " + name)).ToArray();
    private static string[] Strings(JsonObject value, string name) => Array(value, name).Select(node => node is JsonValue item && item.TryGetValue(out string? text)
        && text is not null ? text : throw new InvalidDataException("Malformed shared string array: " + name)).ToArray();
    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
