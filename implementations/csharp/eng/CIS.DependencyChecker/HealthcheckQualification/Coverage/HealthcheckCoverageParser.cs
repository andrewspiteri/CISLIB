using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckCoverageVerification
{
    private const string HealthcheckPrefix = "src/CIS.Core/Healthcheck/";
    internal sealed record Source(string Path, string Sha256, bool IsHealthcheck, string? NonExecutableReason = null);
    internal readonly record struct Identity(string Path, string Class, long Line);
    internal readonly record struct Point(bool Hit, long Covered, long Total);
    internal sealed record NativeReport(Dictionary<Identity, Point> Points, long LinesCovered, long LinesTotal, long BranchesCovered, long BranchesTotal);

    internal static NativeReport ParseCoverage(XDocument xml, string language, IReadOnlyDictionary<string, Source> sources)
    {
        XElement root = xml.Root ?? throw new InvalidDataException("Missing native coverage root.");
        Require(root.Name == "coverage", "Unexpected native coverage root.");
        XElement[] packages = root.Elements("packages").Elements("package").ToArray();
        Require(packages.Length == 1 && Required(packages[0], "name") == "CIS.Core", "Native coverage must contain exactly the complete CIS.Core module.");
        var points = new Dictionary<Identity, Point>();
        var classes = new HashSet<(string Path, string Class)>();
        long lc = 0, lt = 0, bc = 0, bt = 0;
        foreach (XElement cls in packages[0].Elements("classes").Elements("class"))
        {
            string path = ResolveNativeSource(language, Required(cls, "filename"));
            Require(sources.TryGetValue(path, out Source? source), "Unknown or outside Core native source: " + path);
            string name = Required(cls, "name");
            Require(name.Length > 0 && classes.Add((path, name)), "Duplicate native class/source identity.");
            Require(source!.IsHealthcheck == name.StartsWith("CIS.Healthcheck.", StringComparison.Ordinal), "Healthcheck native class/source namespace masquerade: " + path + "/" + name);
            foreach (XElement line in cls.Elements("lines").Elements("line"))
            {
                long number = Number(line, "number"), hits = Number(line, "hits");
                Require(number > 0 && hits >= 0, "Invalid native line number or hit count.");
                var identity = new Identity(path, name, number);
                long covered = 0, total = 0;
                string branch = Required(line, "branch");
                Require(branch.Equals("true", StringComparison.OrdinalIgnoreCase) || branch.Equals("false", StringComparison.OrdinalIgnoreCase), "Invalid native branch flag.");
                if (branch.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    Match match = Regex.Match(Required(line, "condition-coverage"), @"^[0-9]+(?:\.[0-9]+)?% \(([0-9]+)/([0-9]+)\)$",
                        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    Require(match.Success, "Missing or malformed native branch counts.");
                    covered = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    total = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                    Require(total > 0 && covered >= 0 && covered <= total, "Invalid native branch numerator or denominator.");
                }
                else { Require(line.Attribute("condition-coverage") is null, "A nonbranch line carries branch counts."); }
                Require(points.TryAdd(identity, new Point(hits > 0, covered, total)), "Duplicate native class/source/line identity.");
                Require(source.NonExecutableReason is null, "Declaration-only source has native executable sequence points: " + path);
                checked { lc += hits > 0 ? 1 : 0; lt++; bc += covered; bt += total; }
            }
        }
        Require(lt > 0 && bt > 0, "Native report has an empty executable line or branch denominator.");
        Require(lc == Number(root, "lines-covered") && lt == Number(root, "lines-valid")
            && bc == Number(root, "branches-covered") && bt == Number(root, "branches-valid"), "Complete parsed native totals differ from the unfiltered report totals.");
        return new NativeReport(points, lc, lt, bc, bt);
    }

    internal static Dictionary<Identity, Point> Aggregate(IReadOnlyList<NativeReport> reports)
    {
        Require(reports.Count > 0, "No native coverage reports supplied.");
        var aggregate = new Dictionary<Identity, Point>(reports[0].Points);
        foreach (NativeReport report in reports.Skip(1))
        {
            Require(aggregate.Keys.ToHashSet().SetEquals(report.Points.Keys), "Complete native source/class/line denominator drift between families.");
            foreach ((Identity identity, Point point) in report.Points)
            {
                Point previous = aggregate[identity];
                Require(previous.Total == point.Total, "Native branch denominator drift: " + identity);
                aggregate[identity] = new Point(previous.Hit || point.Hit, Math.Max(previous.Covered, point.Covered), point.Total);
            }
        }
        return aggregate;
    }

    internal static JsonObject Summarize(IReadOnlyDictionary<string, Source> sources, IReadOnlyDictionary<Identity, Point> aggregate)
    {
        Source[] selected = sources.Values.Where(source => source.IsHealthcheck).OrderBy(source => source.Path, StringComparer.Ordinal).ToArray();
        Require(selected.Length > 0, "The actual Healthcheck source inventory is empty.");
        var files = new JsonArray(); var all = new List<Point>(); var gaps = new JsonArray();
        foreach (Source source in selected)
        {
            KeyValuePair<Identity, Point>[] points = aggregate.Where(row => row.Key.Path == source.Path).OrderBy(row => row.Key.Class, StringComparer.Ordinal).ThenBy(row => row.Key.Line).ToArray();
            Require(points.Length > 0 || source.NonExecutableReason is not null, "Unmeasured executable Healthcheck source: " + source.Path);
            Require(points.Length == 0 || source.NonExecutableReason is null, "Declaration-only source has executable observations: " + source.Path);
            JsonObject file = Counts(points.Select(row => row.Value));
            file["path"] = source.Path; file["sha256"] = source.Sha256; file["nonExecutableReason"] = source.NonExecutableReason;
            file["uncoveredLines"] = JsonSerializer.SerializeToNode(points.Where(row => !row.Value.Hit).Select(row => row.Key.Line).Distinct().Order());
            file["partialBranchLines"] = JsonSerializer.SerializeToNode(points.Where(row => row.Value.Covered < row.Value.Total).Select(row => row.Key.Line).Distinct().Order());
            files.Add(file); all.AddRange(points.Select(row => row.Value));
            foreach ((Identity identity, Point point) in points.Where(row => !row.Value.Hit || row.Value.Covered < row.Value.Total))
            {
                gaps.Add(new JsonObject
                {
                    ["path"] = identity.Path,
                    ["class"] = identity.Class,
                    ["line"] = identity.Line,
                    ["lineHit"] = point.Hit,
                    ["branchesCoveredLowerBound"] = point.Covered,
                    ["branchesTotal"] = point.Total
                });
            }
        }
        JsonObject totals = Counts(all);
        return new JsonObject
        {
            ["sourceFileCount"] = selected.Length,
            ["declarationOnlyFileCount"] = selected.Count(source => source.NonExecutableReason is not null),
            ["aggregate"] = totals,
            ["qualified"] = totals["thresholdPassed"]!.DeepClone(),
            ["sources"] = files,
            ["rawGaps"] = gaps
        };
    }

    private static JsonObject Counts(IEnumerable<Point> input)
    {
        Point[] points = input.ToArray();
        long lc = points.LongCount(point => point.Hit), lt = points.LongLength;
        long bc = points.Sum(point => point.Covered), bt = points.Sum(point => point.Total);
        return new JsonObject
        {
            ["linesCovered"] = lc,
            ["linesTotal"] = lt,
            ["branchesCoveredLowerBound"] = bc,
            ["branchesTotal"] = bt,
            ["lineRate"] = lt > 0 ? (double)lc / lt : null,
            ["branchRateLowerBound"] = bt > 0 ? (double)bc / bt : null,
            ["thresholdPassed"] = lt > 0 && bt > 0 && (decimal)lc * 100 >= (decimal)lt * 95 && (decimal)bc * 100 >= (decimal)bt * 90
        };
    }

    private static string ResolveNativeSource(string language, string native)
    {
        string normalized = native.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        Require(Path.IsPathFullyQualified(normalized), "Native source origin must be an absolute path.");
        string relative = Path.GetRelativePath(Path.GetFullPath(language), Path.GetFullPath(normalized)).Replace('\\', '/');
        Require(relative.StartsWith("src/CIS.Core/", StringComparison.Ordinal) && !relative.Split('/').Any(part => part is "." or ".." or "bin" or "obj"),
            "Native source is outside the exact Core producer tree: " + native);
        return relative;
    }

    private static string Required(XElement element, string name) => (string?)element.Attribute(name) ?? throw new InvalidDataException("Missing XML attribute: " + name);
    private static long Number(XElement element, string name)
    {
        Require(long.TryParse(Required(element, name), NumberStyles.None, CultureInfo.InvariantCulture, out long number), "Malformed native integer: " + name);
        return number;
    }
    private static XDocument ReadXml(string path)
    {
        using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
}
