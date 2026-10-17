using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CIS.Verification;

// Native implementation of the existing six-family evidence contract, not a new release gate.
internal static class VerificationEvidence
{
    private static readonly string[] KnownFamilies = ["unit", "integration", "mutation", "architecture", "compatibility", "business"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static int Run(string[] args)
    {
        try
        {
            JsonObject result;
            switch (args)
            {
                case ["validate", string root, string manifest, string inventory, string profile, string lane, string output]:
                    result = Validate(root, manifest, inventory, profile, lane);
                    if (output != "-") { Write(output, result); }
                    Console.WriteLine(result.ToJsonString(JsonOptions));
                    return Number(result, "exitCode");
                case ["reconcile", string root, string manifest, string inventory, string lane]:
                    result = Reconcile(root, Read(Path.Combine(root, "summary.json")), Read(manifest), Read(inventory), lane);
                    Write(Path.Combine(root, "discovery-reconciliation.json"), result);
                    break;
                case ["discovery", string root, string inventory, string family, string lane, string native, string output]:
                    result = FamilyDiscovery(root, Read(inventory), family, lane, native);
                    Write(output, result);
                    break;
                case ["trx", string path]: result = ReadTrx(path); break;
                case ["native-discovery", string path]: result = ReadDiscovery(path); break;
                default: throw new ArgumentException("Usage: release-evidence validate <root> <manifest> <inventory> <Developer|Release> <lane> <output|->; reconcile <root> <manifest> <inventory> <lane>; discovery <root> <inventory> <family> <lane> <native> <output>; trx|native-discovery <path>");
            }
            Console.WriteLine(result.ToJsonString(JsonOptions));
            return 0;
        }
        catch (Exception error) when (IsEvidenceError(error))
        { Console.Error.WriteLine(error.Message); return 2; }
    }

    internal static JsonObject Validate(string root, string manifest, string inventory, string profile, string lane)
    {
        JsonArray issues;
        try { issues = GetIssues(root, Read(manifest), Read(inventory), profile, lane); }
        catch (Exception error) when (IsEvidenceError(error)) { issues = [Issue("UnrecognizedReportSchema", error.Message)]; }
        return new JsonObject { ["schema"] = "cis.validation/v1", ["profile"] = profile, ["lane"] = lane, ["exitCode"] = issues.Count == 0 ? 0 : 2, ["issues"] = issues };
    }

    internal static JsonArray GetIssues(string root, JsonObject manifest, JsonObject inventory, string profile, string lane)
    {
        var issues = new JsonArray();
        void Add(string code, string? detail) => issues.Add(Issue(code, detail));
        try
        {
            JsonObject summary = Read(Path.Combine(root, "summary.json"));
            Require(Text(summary, "schema") == "cis.verification/v1" && Number(manifest, "schemaVersion") == 1 && Number(inventory, "schemaVersion") == 1,
                "UnrecognizedReportSchema: summary or inventory version.");
            Require(profile is "Developer" or "Release", "UnrecognizedReportSchema: unsupported profile.");
            JsonNode[] families = Items(manifest, "families");
            Require(families.Length == 6 && families.Select(item => Text(item, "id")).ToHashSet(StringComparer.Ordinal).SetEquals(KnownFamilies),
                "InvalidFamilyManifest: exactly six unique families are required.");
            string?[] lanes = Items(manifest, "lanes").Select(item => Text(item, "id")).ToArray();
            Require(lanes.Contains(lane, StringComparer.Ordinal), "UnsupportedLane: " + lane);
            JsonNode[] cases = Items(inventory, "cases");
            var caseIds = new HashSet<string?>(StringComparer.Ordinal);
            foreach (JsonNode item in cases)
            {
                JsonNode[] applicableLanes = Items(item, "lanes");
                Require(caseIds.Add(Text(item, "id")) && KnownFamilies.Contains(Text(item, "family"), StringComparer.Ordinal) &&
                    Text(item, "implementation") is "Implemented" or "Planned" && applicableLanes.Length > 0 &&
                    applicableLanes.All(value => lanes.Contains(value.GetValue<string>(), StringComparer.Ordinal)),
                    "InvalidCaseInventory: duplicate ID, family, state or lane.");
                _ = Bindings(item);
            }
            if (Text(summary, "lane") != lane) { Add("LaneMismatch", $"Evidence lane {Text(summary, "lane")}; requested {lane}."); }
            if (Text(summary, "profile") != profile) { Add("ProfileMismatch", $"Evidence profile {Text(summary, "profile")}; requested {profile}."); }
            JsonNode[] entries = Items(summary, "families");
            // Old foundation reports had no reconciliation. Current runners always declare it.
            if ((summary.ContainsKey("runId") || summary.ContainsKey("reconciliation")) && entries.Any(item => Text(item, "status") == "Executed"))
            {
                try
                {
                    JsonNode? declared = summary["reconciliation"];
                    Require(declared is not null && Text(declared, "status") == "Produced", "InvalidReconciliation: executed families require the declared reconciliation.");
                    JsonObject report = Read(Resolve(root, declared!["report"]));
                    Require(Text(report, "schema") == "cis.discovery-reconciliation/v1" && RequiredText(report, "runId") == RequiredText(summary, "runId") &&
                        Text(report, "lane") == Text(summary, "lane") && Text(report, "profile") == Text(summary, "profile") &&
                        RequiredText(report["source"], "commit") == RequiredText(summary["source"], "commit") &&
                        RequiredText(report["source"]?["inputInventory"], "path") == RequiredText(summary["source"]?["inputInventory"], "path") &&
                        RequiredText(report["source"]?["inputInventory"], "sha256") == RequiredText(summary["source"]?["inputInventory"], "sha256"),
                        "InvalidReconciliation: report belongs to different run/source/profile/lane.");
                }
                catch (Exception error) when (IsEvidenceError(error))
                { Add(Code(error, "InvalidReconciliation", "InvalidReconciliation", "ReportHashMismatch", "MissingReport", "EvidencePathOutsideRoot"), error.Message); }
            }
            foreach (JsonNode stage in Items(summary, "stages"))
            { if (stage["exitCode"] is not null && Number(stage, "exitCode") != 0) { Add("FailedStage", Text(stage, "id")); } }
            foreach (JsonNode family in families)
            {
                string id = RequiredText(family, "id");
                JsonNode[] matches = entries.Where(item => Text(item, "id") == id).ToArray();
                if (matches.Length != 1) { Add("MissingFamily", id); continue; }
                JsonNode entry = matches[0];
                if (profile == "Developer" && !(family["developerEnabled"]?.GetValue<bool>() ?? throw new InvalidDataException("UnrecognizedReportSchema: missing developerEnabled."))) { continue; }
                if (Text(entry, "status") != "Executed") { Add("MissingFamilyExecution", $"{id}: {Text(entry, "reason")}"); continue; }
                try
                {
                    JsonObject discovery = Read(Resolve(root, entry["discovery"]));
                    JsonObject execution = Read(Resolve(root, entry["execution"]));
                    string nativePath = Resolve(root, entry["report"]);
                    string schema = RequiredText(family, "reportSchema");
                    Require(Text(discovery, "schema") == "cis.discovery/v1" && Text(execution, "schema") == "cis.execution/v1" && Text(discovery, "family") == id && Text(execution, "family") == id,
                        "UnrecognizedReportSchema: family discovery/execution.");
                    Require(Text(entry["report"], "schema") == schema, "UnrecognizedReportSchema: native report declaration.");
                    if (schema == "vstest.trx/2010")
                    {
                        JsonObject native = ReadTrx(nativePath);
                        Require(Text(discovery["nativeReport"], "schema") == "cis.vstest-discovery/v1", "InvalidDiscovery: native discovery reference is required.");
                        JsonObject discovered = ReadDiscovery(Resolve(root, discovery["nativeReport"]));
                        JsonNode[] results = Items(native, "results"), tests = Items(discovered, "tests");
                        Dictionary<string, int> nativeCounts = Counts(results, "method"), foundCounts = Counts(tests, "Method");
                        Dictionary<string, JsonNode> byId = tests.ToDictionary(item => RequiredText(item, "Id"), StringComparer.OrdinalIgnoreCase);
                        bool identitiesMatch = Number(discovered, "total") == Number(native, "total") && Number(discovery, "discovered") == Number(discovered, "total");
                        foreach (JsonNode result in results)
                        {
                            if (!byId.TryGetValue(RequiredText(result, "testId"), out JsonNode? test) ||
                                Text(test, "Method") != Text(result, "method") ||
                                (Text(test, "Name") != Text(result, "name") && LegacyName(RequiredText(test, "Name")) != Text(result, "name")) ||
                                !string.Equals(Path.GetFullPath(RequiredText(test, "Source")), Path.GetFullPath(RequiredText(result, "source")), PathComparison))
                            { identitiesMatch = false; }
                        }
                        if (!identitiesMatch) { Add("DiscoveryExecutionMismatch", $"{id}: discovered IDs, methods, names, source paths or totals differ from execution."); }
                        if (Number(native, "total") <= 0 || Number(discovery, "discovered") <= 0) { Add("ZeroDiscoveredTests", id); }
                        foreach (JsonNode result in results)
                        {
                            if (Text(result, "outcome") == "Failed") { Add("FailedCase", $"{id}: {Text(result, "name")}"); }
                            if (Text(result, "outcome") == "NotExecuted") { Add("UnexpectedSkip", $"{id}: {Text(result, "name")}"); }
                        }
                        foreach (string field in new[] { "passed", "failed", "skipped" })
                        { if (Number(execution, field) != Number(native, field)) { Add("CounterMismatch", $"{id}: {field}"); } }
                        if (Number(execution, "executed") != Number(native, "total") - Number(native, "skipped")) { Add("CounterMismatch", $"{id}: executed"); }
                        foreach (JsonNode item in cases.Where(item => Text(item, "family") == id && Applies(item, lane) && (profile == "Release" || Text(item, "implementation") == "Implemented")))
                        {
                            JsonNode[] bindings = Bindings(item);
                            JsonNode[] declared = Items(discovery, "cases").Where(value => Text(value, "id") == Text(item, "id")).ToArray();
                            bool missing = bindings.Length == 0 || declared.Length != 1;
                            int discoveryCount = 0;
                            foreach (JsonNode binding in bindings)
                            {
                                string method = Text(binding, "method") ?? "";
                                int minimum = Number(binding, "minimumExecutions"), found = foundCounts.GetValueOrDefault(method);
                                discoveryCount += found;
                                if (method.Length == 0 || minimum <= 0 || nativeCounts.GetValueOrDefault(method) < minimum || found < minimum) { missing = true; }
                            }
                            if (missing) { Add("MissingRequiredCase", $"{id}: {Text(item, "id")}"); }
                            else if (Number(declared[0], "count") != discoveryCount) { Add("DiscoveryCaseMismatch", $"{id}: {Text(item, "id")} count differs from native methods."); }
                        }
                    }
                    else if (schema == "stryker.mutation/v3")
                    {
                        JsonObject mutation = Read(nativePath);
                        Require(Text(mutation, "schemaVersion") == "3", "UnrecognizedReportSchema: mutation report.");
                        int mutants = mutation["files"]!.AsObject().Sum(file => Items(file.Value!, "mutants").Length);
                        if (mutants == 0) { Add("ZeroDiscoveredTests", "mutation"); }
                        if (Text(entry, "scope") != "full-shipping-implementation") { Add("IncompleteMutationScope", Text(entry, "scope")); }
                        Add("MissingReleaseGate", "Full mutation matrix validator is not yet implemented.");
                    }
                    else { throw new InvalidDataException("UnrecognizedReportSchema: unsupported native format."); }
                }
                catch (Exception error) when (IsEvidenceError(error))
                { Add(Code(error, "UnrecognizedReportSchema", "EvidencePathOutsideRoot", "MissingReport", "ReportHashMismatch", "UnrecognizedReportSchema", "InvalidDiscovery", "InvalidCaseInventory"), error.Message); }
            }
            if (profile == "Release")
            {
                foreach (JsonNode item in cases.Where(item => Applies(item, lane) && Text(item, "implementation") != "Implemented")) { Add("MissingRequiredCase", Text(item, "id")); }
                foreach (JsonNode gate in Items(manifest, "releaseGates")) { Add("MissingReleaseGate", $"{Text(gate, "id")}: {Text(gate, "reason")}"); }
            }
        }
        catch (Exception error) when (IsEvidenceError(error))
        { Add(Code(error, "UnrecognizedReportSchema", "InvalidCaseInventory", "UnsupportedLane", "InvalidFamilyManifest", "UnrecognizedReportSchema", "InvalidReconciliation", "ReportHashMismatch", "MissingReport", "EvidencePathOutsideRoot"), error.Message); }
        return issues;
    }

    internal static JsonObject ReadTrx(string path)
    {
        XDocument xml = XDocument.Load(path);
        Require(xml.Root?.Name == XName.Get("TestRun", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"), "UnrecognizedReportSchema: expected VSTest TRX 2010.");
        var definitions = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement definition in xml.Descendants().Where(item => item.Name.LocalName == "TestDefinitions").Elements().Where(item => item.Name.LocalName == "UnitTest"))
        { definitions[Attribute(definition, "id")] = definition.Elements().First(item => item.Name.LocalName == "TestMethod"); }
        var results = new JsonArray();
        foreach (XElement result in xml.Descendants().Where(item => item.Name.LocalName == "Results").Elements().Where(item => item.Name.LocalName == "UnitTestResult"))
        {
            string id = Attribute(result, "testId"), outcome = Attribute(result, "outcome");
            Require(definitions.ContainsKey(id), "UnrecognizedReportSchema: result has no test definition.");
            Require(outcome is "Passed" or "Failed" or "NotExecuted", "UnrecognizedReportSchema: unsupported outcome " + outcome + ".");
            XElement method = definitions[id];
            results.Add(new JsonObject { ["testId"] = id, ["method"] = Attribute(method, "className") + "." + Attribute(method, "name"), ["source"] = Attribute(method, "codeBase"), ["name"] = Attribute(result, "testName"), ["outcome"] = outcome });
        }
        XElement? counters = xml.Descendants().Where(item => item.Name.LocalName == "ResultSummary").Elements().FirstOrDefault(item => item.Name.LocalName == "Counters");
        Require(counters is not null && Counter(counters, "total") == results.Count && results.Select(item => Text(item, "testId")).Distinct(StringComparer.OrdinalIgnoreCase).Count() == results.Count,
            "UnrecognizedReportSchema: inconsistent or duplicated native results.");
        int passed = results.Count(item => Text(item, "outcome") == "Passed"), failed = results.Count(item => Text(item, "outcome") == "Failed"), skipped = results.Count(item => Text(item, "outcome") == "NotExecuted");
        Require(Counter(counters!, "passed") == passed && Counter(counters!, "failed") == failed && Counter(counters!, "notExecuted") == skipped, "UnrecognizedReportSchema: native counters disagree with outcomes.");
        return new JsonObject { ["total"] = results.Count, ["passed"] = passed, ["failed"] = failed, ["skipped"] = skipped, ["results"] = results };
    }

    internal static JsonObject ReadDiscovery(string path)
    {
        JsonObject report = Read(path);
        Require(new[] { "schema", "aborted", "errors", "total", "tests" }.All(report.ContainsKey), "InvalidDiscovery: required native discovery fields are missing.");
        Require(Text(report, "schema") == "cis.vstest-discovery/v1" && report["aborted"] is JsonValue flag && flag.TryGetValue(out bool aborted) && !aborted && Number(report, "errors") == 0 &&
            Number(report, "total") > 0 && Number(report, "total") == Items(report, "tests").Length, "InvalidDiscovery: incomplete native discovery.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonNode test in Items(report, "tests"))
        {
            string? id = Text(test, "Id"), source = Text(test, "Source");
            Require(Guid.TryParse(id, out Guid parsed) && parsed != Guid.Empty && ids.Add(id!) && !string.IsNullOrWhiteSpace(Text(test, "Method")) &&
                !string.IsNullOrWhiteSpace(Text(test, "Name")) && !string.IsNullOrWhiteSpace(source) && Path.IsPathFullyQualified(source), "InvalidDiscovery: duplicate or invalid native test identity.");
        }
        return report;
    }

    internal static JsonObject FamilyDiscovery(string root, JsonObject inventory, string family, string lane, string nativePath)
    {
        JsonObject native = ReadDiscovery(nativePath);
        Dictionary<string, int> counts = Counts(Items(native, "tests"), "Method");
        var cases = new JsonArray();
        foreach (JsonNode item in Items(inventory, "cases").Where(item => Text(item, "family") == family && Text(item, "implementation") == "Implemented" && Applies(item, lane)))
        {
            var bindings = new JsonArray();
            int total = 0;
            foreach (JsonNode binding in Bindings(item))
            {
                string method = RequiredText(binding, "method");
                int count = counts.GetValueOrDefault(method);
                total += count;
                bindings.Add(new JsonObject { ["method"] = method, ["count"] = count });
            }
            cases.Add(new JsonObject { ["id"] = Text(item, "id"), ["count"] = total, ["bindings"] = bindings });
        }
        return new JsonObject { ["schema"] = "cis.discovery/v1", ["family"] = family, ["status"] = "Discovered", ["discovered"] = Number(native, "total"), ["cases"] = cases, ["nativeReport"] = Reference(root, nativePath, "cis.vstest-discovery/v1") };
    }

    internal static JsonObject Reconcile(string root, JsonObject summary, JsonObject manifest, JsonObject inventory, string lane)
    {
        var families = new JsonArray();
        foreach (JsonNode family in Items(manifest, "families"))
        {
            string id = RequiredText(family, "id");
            JsonNode entry = Items(summary, "families").First(item => Text(item, "id") == id);
            JsonNode[] results = [], discovered = [];
            if (Text(entry, "status") == "Executed" && Text(family, "reportSchema") == "vstest.trx/2010")
            {
                results = Items(ReadTrx(Resolve(root, entry["report"])), "results");
                JsonObject wrapper = Read(Resolve(root, entry["discovery"]));
                discovered = Items(ReadDiscovery(Resolve(root, wrapper["nativeReport"])), "tests");
            }
            Dictionary<string, int> found = Counts(discovered, "Method"), executed = Counts(results.Where(item => Text(item, "outcome") != "NotExecuted"), "method"),
                passed = Counts(results.Where(item => Text(item, "outcome") == "Passed"), "method"), failed = Counts(results.Where(item => Text(item, "outcome") == "Failed"), "method"),
                skipped = Counts(results.Where(item => Text(item, "outcome") == "NotExecuted"), "method");
            var required = new HashSet<string>(StringComparer.Ordinal);
            var inapplicable = new HashSet<string>(StringComparer.Ordinal);
            JsonObject CountsFor(string method) => new() { ["method"] = method, ["discovered"] = found.GetValueOrDefault(method), ["executed"] = executed.GetValueOrDefault(method), ["passed"] = passed.GetValueOrDefault(method), ["failed"] = failed.GetValueOrDefault(method), ["skipped"] = skipped.GetValueOrDefault(method) };
            var cases = new JsonArray();
            foreach (JsonNode item in Items(inventory, "cases").Where(item => Text(item, "family") == id))
            {
                bool applicable = Applies(item, lane);
                var bindings = new JsonArray();
                foreach (JsonNode binding in Bindings(item))
                {
                    string? method = Text(binding, "method");
                    if (string.IsNullOrEmpty(method)) { continue; }
                    if (Text(item, "implementation") == "Implemented") { (applicable ? required : inapplicable).Add(method); }
                    JsonObject counts = CountsFor(method);
                    counts["minimumExecutions"] = Number(binding, "minimumExecutions");
                    bindings.Add(counts);
                }
                cases.Add(new JsonObject { ["id"] = Text(item, "id"), ["title"] = item.AsObject().ContainsKey("title") ? Text(item, "title") : Text(item, "id"), ["implementation"] = Text(item, "implementation"), ["applicable"] = applicable, ["bindings"] = bindings, ["pending"] = Text(item, "implementation") == "Planned" ? "Required later roadmap work; no missing execution is waived." : null });
            }
            var supporting = new JsonArray();
            foreach (string method in found.Keys.Concat(executed.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Where(method => !required.Contains(method)))
            {
                JsonObject counts = CountsFor(method);
                counts["classification"] = inapplicable.Contains(method) ? "BoundOnlyToInapplicableCases" : "UnboundTechnical";
                supporting.Add(counts);
            }
            families.Add(new JsonObject { ["id"] = id, ["status"] = Text(entry, "status"), ["nativeFormat"] = Text(family, "reportSchema"), ["report"] = entry["report"]?.DeepClone(), ["discoveredRows"] = discovered.Length, ["reportedRows"] = results.Length, ["executedRows"] = results.Count(item => Text(item, "outcome") != "NotExecuted"), ["cases"] = cases, ["supportingMethods"] = supporting });
        }
        return new JsonObject
        {
            ["schema"] = "cis.discovery-reconciliation/v1",
            ["runId"] = summary["runId"]?.DeepClone(),
            ["source"] = summary["source"]?.DeepClone(),
            ["lane"] = lane,
            ["profile"] = Text(summary, "profile"),
            ["scope"] = "All preserved requirement/case IDs plus actual unbound technical methods. Group/leaf bindings may share rows; native row totals count each ID once. This is not full release qualification.",
            ["families"] = families,
            ["plannedCaseIds"] = new JsonArray(Items(inventory, "cases").Where(item => Text(item, "implementation") == "Planned").Select(item => item["id"]!.DeepClone()).ToArray()),
            ["remainingReleaseGates"] = manifest["releaseGates"]?.DeepClone()
        };
    }

    private static string Resolve(string root, JsonNode? reference)
    {
        Require(reference is not null, "UnrecognizedReportSchema: missing evidence reference.");
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(fullRoot, RequiredText(reference!, "path")));
        Require(path.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison), "EvidencePathOutsideRoot: report is outside its evidence directory.");
        Require(File.Exists(path), "MissingReport: " + Text(reference, "path"));
        Require(Hash(path) == Text(reference, "sha256"), "ReportHashMismatch: " + Text(reference, "path"));
        return path;
    }
    internal static JsonObject Reference(string root, string path, string schema) => new() { ["path"] = Path.GetRelativePath(root, path).Replace('\\', '/'), ["sha256"] = Hash(path), ["schema"] = schema };
    internal static void Write(string path, JsonObject value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, value.ToJsonString(JsonOptions) + Environment.NewLine); }
    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("UnrecognizedReportSchema: expected a JSON object.");
    private static JsonNode[] Items(JsonNode node, string key) => node[key]?.AsArray().Select(item => item ?? throw new InvalidDataException("UnrecognizedReportSchema: null array member.")).ToArray() ?? throw new InvalidDataException("UnrecognizedReportSchema: missing " + key + ".");
    private static string? Text(JsonNode? node, string key) => node?[key]?.GetValue<string>();
    private static string RequiredText(JsonNode? node, string key) => Text(node, key) ?? throw new InvalidDataException("UnrecognizedReportSchema: missing " + key + ".");
    private static int Number(JsonNode node, string key)
    {
        JsonNode value = node[key] ?? throw new InvalidDataException("UnrecognizedReportSchema: missing " + key + ".");
        if (value is JsonValue number && number.TryGetValue(out int integer)) { return integer; }
        // Historical PowerShell Measure-Object emitted integral counts with a .0 suffix.
        if (decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal historical) &&
            historical == decimal.Truncate(historical) && historical is >= int.MinValue and <= int.MaxValue) { return (int)historical; }
        throw new InvalidDataException("UnrecognizedReportSchema: expected an integer " + key + ".");
    }
    private static string Attribute(XElement node, string name) => node.Attribute(name)?.Value ?? throw new InvalidDataException("UnrecognizedReportSchema: missing native " + name + ".");
    private static int Counter(XElement node, string name) => int.Parse(Attribute(node, name), CultureInfo.InvariantCulture);
    private static bool Applies(JsonNode item, string lane) => Items(item, "lanes").Any(value => value.GetValue<string>() == lane);
    private static string Hash(string path) { using FileStream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static JsonObject Issue(string code, string? detail) => new() { ["code"] = code, ["detail"] = detail };
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
    private static bool IsEvidenceError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or XmlException or FormatException or OverflowException;
    private static string Code(Exception error, string fallback, params string[] allowed) => allowed.FirstOrDefault(code => error.Message.StartsWith(code + ":", StringComparison.Ordinal)) ?? fallback;
    private static string LegacyName(string name) => Regex.Replace(name, @"[\x00-\x08\x0B\x0C\x0E-\x1F\uD800-\uDFFF\uFFFE\uFFFF]", match => "\\u" + ((int)match.Value[0]).ToString("x4", CultureInfo.InvariantCulture));
    private static Dictionary<string, int> Counts(IEnumerable<JsonNode> tests, string property)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (JsonNode test in tests) { string method = RequiredText(test, property); counts[method] = counts.GetValueOrDefault(method) + 1; }
        return counts;
    }
    private static JsonNode[] Bindings(JsonNode item)
    {
        if (!item.AsObject().ContainsKey("bindings")) { return [item]; }
        JsonNode[] bindings = Items(item, "bindings");
        Require(string.IsNullOrEmpty(Text(item, "method")) && bindings.Length > 0, $"InvalidCaseInventory: {Text(item, "id")} has ambiguous or empty bindings.");
        var methods = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode binding in bindings)
        {
            string? method = Text(binding, "method");
            Require(!string.IsNullOrWhiteSpace(method) && Number(binding, "minimumExecutions") > 0 && methods.Add(method), $"InvalidCaseInventory: {Text(item, "id")} has invalid or duplicate group methods.");
        }
        return bindings;
    }
}
