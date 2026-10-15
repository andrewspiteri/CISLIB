using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CIS.TestSupport;

namespace CIS.Verification;

// Cobertura identifies branch counts, not individual branch identities. Never sum hits across reports.
internal static class ShippingCoverage
{
    private sealed record Source(string Path, string Sha256, string? NonExecutableReason);
    private readonly record struct Identity(string Path, string Class, long Line);
    private readonly record struct Point(bool Hit, long Covered, long Total);
    private sealed record AssemblyData(Dictionary<string, Source> Sources, Dictionary<Identity, Point> Points);

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length < 3) { throw new ArgumentException("Usage: shipping-coverage <language-root> <new-output.json> <results-directory> [...]"); }
            string output = Path.GetFullPath(args[1]);
            if (File.Exists(output)) { throw new IOException("Use a new immutable output path."); }
            JsonObject result = await SummarizeAsync(Path.GetFullPath(args[0]), args[2..].Select(Path.GetFullPath).ToArray());
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, result, new JsonSerializerOptions { WriteIndented = true });
            }
            Console.WriteLine(new JsonObject { ["aggregate"] = result["aggregate"]!.DeepClone(), ["qualified"] = result["qualified"]!.DeepClone() }.ToJsonString());
            return result["qualified"]!.GetValue<bool>() ? 0 : 2;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or XmlException or FormatException or OverflowException or TimeoutException)
        {
            Console.Error.WriteLine("Shipping coverage failed: " + exception.Message);
            return 1;
        }
    }

    internal static async Task<JsonObject> SummarizeAsync(string language, IReadOnlyList<string> directories, Func<string, Task<string>>? evaluate = null)
    {
        if (directories.Count == 0) { throw new InvalidDataException("At least one native report is required."); }
        language = Path.GetFullPath(language);
        string policyPath = Path.Combine(language, "verification/coverage/shipping.json");
        JsonObject policy = JsonNode.Parse(File.ReadAllText(policyPath))!.AsObject();
        double lineMinimum = policy["minimumLineRate"]!.GetValue<double>();
        double branchMinimum = policy["minimumBranchRate"]!.GetValue<double>();
        if (!double.IsFinite(lineMinimum) || !double.IsFinite(branchMinimum) || lineMinimum is < 0 or > 1 || branchMinimum is < 0 or > 1)
        { throw new InvalidDataException("Invalid coverage thresholds."); }
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        var assemblies = new Dictionary<string, AssemblyData>(StringComparer.Ordinal);
        foreach (JsonNode? entry in policy["assemblies"]!.AsArray())
        {
            string name = entry!["name"]!.GetValue<string>();
            string sourceRoot = Contained(language, entry["sourceRoot"]!.GetValue<string>());
            JsonObject nonExecutable = entry["nonExecutable"]!.AsObject();
            var sources = new Dictionary<string, Source>(StringComparer.Ordinal);
            var relativeFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Slashes(Path.GetRelativePath(sourceRoot, file));
                if (Generated(relative)) { continue; }
                relativeFiles.Add(relative);
                string key = SourceKey(language, file);
                string? reason = nonExecutable[relative]?.GetValue<string>();
                sources.Add(key, new Source(key, Hash(file), reason));
                owners.Add(key, name);
            }
            if (nonExecutable.Any(item => !relativeFiles.Contains(item.Key)))
            { throw new InvalidDataException("Non-executable inventory names a missing source."); }
            string project = entry["project"]!.GetValue<string>();
            _ = Contained(language, project);
            string evaluated;
            if (evaluate is not null) { evaluated = await evaluate(project); }
            else
            {
                ProcessResult child = await ProcessRunner.RunAsync("dotnet", ["msbuild", project, "-getItem:Compile", "-p:Configuration=Release", "-p:TargetFramework=net10.0", "-nologo"], language, TimeSpan.FromSeconds(60));
                if (child.ExitCode != 0) { throw new InvalidDataException("Compile inventory evaluation failed: " + project + "\n" + child.StandardError); }
                evaluated = child.StandardOutput;
            }
            JsonArray items = JsonNode.Parse(evaluated)!["Items"]!["Compile"]!.AsArray();
            var compiled = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode? item in items)
            {
                string file = item!["FullPath"]!.GetValue<string>();
                if (!Generated(Slashes(file))) { compiled.Add(SourceKey(language, file)); }
            }
            if (!compiled.SetEquals(sources.Keys))
            { throw new InvalidDataException("Evaluated Compile inventory differs from the declared source scope: " + name); }
            assemblies.Add(name, new AssemblyData(sources, []));
        }

        var reports = new JsonArray();
        foreach (string directory in directories)
        {
            string trx = Path.Combine(directory, "report.trx");
            XDocument test = ReadXml(trx);
            XElement[] counters = test.Descendants().Where(element => element.Name.LocalName == "Counters").ToArray();
            if (counters.Length != 1 || Number(counters[0], "total") <= 0 || Number(counters[0], "total") != Number(counters[0], "passed"))
            { throw new InvalidDataException("Missing, failed or skipped test results: " + trx); }
            string[] attached = test.Descendants().Where(element => element.Name.LocalName == "A")
                .Select(element => (string?)element.Attribute("href")).OfType<string>().Where(href => href.EndsWith(".cobertura.xml", StringComparison.Ordinal)).ToArray();
            if (attached.Length != 1) { throw new InvalidDataException("Expected one native Microsoft Cobertura attachment: " + trx); }
            string filename = Slashes(attached[0]).Split('/')[^1];
            string[] matches = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(file => Path.GetFileName(file) == filename).Order(StringComparer.Ordinal).ToArray();
            if (matches.Length == 0 || matches.Select(Hash).Distinct(StringComparer.Ordinal).Count() != 1)
            { throw new InvalidDataException("Absent or conflicting native attachment."); }
            string report = matches[0];
            XDocument xml = ReadXml(report);
            var local = new HashSet<(string Assembly, Identity Identity)>();
            long lc = 0, lt = 0, bc = 0, bt = 0;
            foreach (XElement package in xml.Root!.Elements("packages").Elements("package"))
            {
                string name = Required(package, "name");
                if (!assemblies.TryGetValue(name, out AssemblyData? assembly))
                { throw new InvalidDataException("Unexpected shipping assembly: " + name); }
                foreach (XElement cls in package.Elements("classes").Elements("class"))
                {
                    string nativePath = Slashes(Required(cls, "filename"));
                    const string marker = "/implementations/csharp/";
                    int position = nativePath.LastIndexOf(marker, StringComparison.Ordinal);
                    if (position < 0) { throw new InvalidDataException("Unmapped source: " + nativePath); }
                    string path = "implementations/csharp/" + nativePath[(position + marker.Length)..];
                    if (!owners.TryGetValue(path, out string? owner) || owner != name)
                    { throw new InvalidDataException("Source outside inventoried shipping scope: " + path); }
                    if (!string.IsNullOrEmpty(assembly.Sources[path].NonExecutableReason))
                    { throw new InvalidDataException("A declared non-executable source has native sequence points: " + path); }
                    foreach (XElement line in cls.Elements("lines").Elements("line"))
                    {
                        var key = new Identity(path, Required(cls, "name"), Number(line, "number"));
                        if (!local.Add((name, key))) { throw new InvalidDataException("Duplicate native class/source/line identity."); }
                        long hits = Number(line, "hits");
                        if (key.Line <= 0 || hits < 0) { throw new InvalidDataException("Invalid native line observation."); }
                        long covered = 0, total = 0;
                        if (string.Equals((string?)line.Attribute("branch"), "true", StringComparison.OrdinalIgnoreCase))
                        {
                            Match match = Regex.Match((string?)line.Attribute("condition-coverage") ?? "", @"^.*\(([0-9]+)/([0-9]+)\)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                            if (!match.Success) { throw new InvalidDataException("Missing native branch counts."); }
                            covered = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                            total = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                            if (total <= 0 || covered > total) { throw new InvalidDataException("Invalid native branch counts."); }
                        }
                        bool previousExists = assembly.Points.TryGetValue(key, out Point previous);
                        if (previousExists && previous.Total != total) { throw new InvalidDataException("Branch denominator drift: " + key); }
                        assembly.Points[key] = new Point(hits > 0 || previous.Hit, Math.Max(covered, previous.Covered), total);
                        checked { lc += hits > 0 ? 1 : 0; lt++; bc += covered; bt += total; }
                    }
                }
            }
            long[] totals = [lc, lt, bc, bt];
            long[] native = [Number(xml.Root!, "lines-covered"), Number(xml.Root!, "lines-valid"), Number(xml.Root!, "branches-covered"), Number(xml.Root!, "branches-valid")];
            if (!totals.SequenceEqual(native)) { throw new InvalidDataException("Parsed denominator does not equal the native report totals."); }
            reports.Add(new JsonObject { ["trx"] = trx, ["trxSha256"] = Hash(trx), ["cobertura"] = report, ["coberturaSha256"] = Hash(report),
                ["testsPassed"] = Number(counters[0], "passed"), ["nativeTotals"] = JsonSerializer.SerializeToNode(totals) });
        }

        JsonObject Counts(IEnumerable<Point> values)
        {
            Point[] points = values.ToArray();
            long lc = points.LongCount(point => point.Hit), lt = points.LongLength, bc = points.Sum(point => point.Covered), bt = points.Sum(point => point.Total);
            return new JsonObject { ["linesCovered"] = lc, ["linesTotal"] = lt, ["branchesCoveredLowerBound"] = bc, ["branchesTotal"] = bt,
                ["lineRate"] = lt > 0 ? (double)lc / lt : null, ["branchRateLowerBound"] = bt > 0 ? (double)bc / bt : null,
                ["thresholdPassed"] = lt > 0 && bt > 0 && lc >= lineMinimum * lt && bc >= branchMinimum * bt };
        }
        var output = new JsonArray();
        var aggregate = new List<Point>();
        foreach ((string name, AssemblyData assembly) in assemblies)
        {
            var files = new JsonArray();
            foreach ((string path, Source source) in assembly.Sources)
            {
                KeyValuePair<Identity, Point>[] points = assembly.Points.Where(point => point.Key.Path == path).ToArray();
                if (points.Length == 0 && string.IsNullOrEmpty(source.NonExecutableReason))
                { throw new InvalidDataException("Unmeasured executable source: " + path); }
                JsonObject file = Counts(points.Select(point => point.Value));
                file["path"] = source.Path; file["sha256"] = source.Sha256; file["nonExecutableReason"] = source.NonExecutableReason;
                file["uncoveredLines"] = JsonSerializer.SerializeToNode(points.Where(point => !point.Value.Hit).Select(point => point.Key.Line).Distinct().Order());
                file["partiallyCoveredBranchLines"] = JsonSerializer.SerializeToNode(points.Where(point => point.Value.Covered < point.Value.Total).Select(point => point.Key.Line).Distinct().Order());
                files.Add(file);
            }
            if (assembly.Points.Values.Sum(point => point.Total) == 0) { throw new InvalidDataException("Assembly has no native branch denominator: " + name); }
            JsonObject item = Counts(assembly.Points.Values);
            item["assembly"] = name; item["sources"] = files; output.Add(item);
            aggregate.AddRange(assembly.Points.Values);
        }
        if (!assemblies.TryGetValue("CIS.Core", out AssemblyData? core)) { throw new InvalidDataException("Missing CIS.Core calibration assembly."); }
        Point[] calibration = core.Points.Where(point => point.Key.Path.EndsWith("/SeverityTokenParser.cs", StringComparison.Ordinal)).Select(point => point.Value).ToArray();
        if (calibration.Length == 0 || calibration.Sum(point => point.Total) == 0 || calibration.Sum(point => point.Covered) == 0)
        { throw new InvalidDataException("Known branching fixture has no observed branches."); }
        return new JsonObject { ["schema"] = "cis.csharp.shipping-coverage-observation/v1", ["policySha256"] = Hash(policyPath),
            ["aggregation"] = policy["aggregation"]!.DeepClone(), ["exclusions"] = policy["exclusions"]!.DeepClone(), ["reports"] = reports,
            ["assemblies"] = output, ["aggregate"] = Counts(aggregate), ["qualified"] = output.All(item => item!["thresholdPassed"]!.GetValue<bool>()), ["branchCalibration"] = Counts(calibration) };
    }

    private static string Contained(string root, string relative)
    {
        string path = Path.GetFullPath(relative, root);
        string check = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(check) || check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        { throw new InvalidDataException("Coverage path escapes the language root."); }
        return path;
    }
    private static string SourceKey(string language, string path) => "implementations/csharp/" + Slashes(Path.GetRelativePath(language, Contained(language, path)));
    private static bool Generated(string path) => path.Split('/').Any(part => part is "bin" or "obj");
    private static string Slashes(string path) => path.Replace('\\', '/');
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Required(XElement element, string name) => (string?)element.Attribute(name) ?? throw new InvalidDataException("Missing XML attribute: " + name);
    private static long Number(XElement element, string name) => long.Parse(Required(element, name), CultureInfo.InvariantCulture);
    private static XDocument ReadXml(string path)
    {
        using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
}
