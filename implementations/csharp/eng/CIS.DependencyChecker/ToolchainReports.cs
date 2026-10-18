using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class ToolchainReports
{
    internal static int PassedTests(string path) => PassedTests(ToolchainInputs.Xml(File.ReadAllBytes(path)));
    private static int PassedTests(XDocument trx)
    {
        XElement[] summaries = trx.Descendants().Where(item => item.Name.LocalName == "ResultSummary").ToArray();
        Require(summaries.Length == 1 && (string?)summaries[0].Attribute("outcome") == "Completed", "Native tests did not complete.");
        XElement[] counters = summaries[0].Elements().Where(item => item.Name.LocalName == "Counters").ToArray();
        Require(counters.Length == 1, "Expected one native test counter.");
        long total = Number(counters[0], "total");
        Require(total is > 0 and <= int.MaxValue && Number(counters[0], "passed") == total &&
            Number(counters[0], "executed") == total && Number(counters[0], "failed") == 0, "Missing, failed or skipped tests.");
        return (int)total;
    }

    internal static JsonObject Coverage(string root, JsonObject policy)
    {
        string trxPath = Path.Combine(root, "parser.trx");
        byte[] trxBytes = File.ReadAllBytes(trxPath);
        XDocument trx = ToolchainInputs.Xml(trxBytes);
        int cases = PassedTests(trx);
        XElement[] collectors = trx.Descendants().Where(item => item.Name.LocalName == "Collector" &&
            (string?)item.Attribute("collectorDisplayName") == "Code Coverage").ToArray();
        Require(collectors.Length == 1 && string.Equals((string?)collectors[0].Attribute("uri"), (string?)policy["collector"], StringComparison.OrdinalIgnoreCase),
            "Expected the Microsoft Code Coverage collector.");
        string[] attachments = collectors[0].Descendants().Where(item => item.Name.LocalName == "A")
            .Select(item => (string?)item.Attribute("href") ?? "").ToArray();
        Require(attachments.Length == 1 && attachments[0].EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase), "Expected one native Cobertura attachment.");
        string name = attachments[0].Replace('\\', '/').Split('/')[^1];
        string[] paths = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) == name).ToArray();
        Require(paths.Length > 0, "Missing native coverage attachment.");
        byte[] bytes = File.ReadAllBytes(paths[0]); string hash = Digest(bytes);
        Require(paths.All(path => Hash(path) == hash), "Conflicting native coverage attachment copies.");
        XElement coverage = ToolchainInputs.Xml(bytes).Root!;
        Require(coverage.Name.LocalName == "coverage", "Expected Cobertura root.");
        long lines = Number(coverage, "lines-valid"), covered = Number(coverage, "lines-covered");
        long branches = Number(coverage, "branches-valid"), hitBranches = Number(coverage, "branches-covered");
        Require(lines > 0 && branches > 0 && covered <= lines && hitBranches <= branches, "Invalid coverage denominator.");
        double lineRate = Rate(coverage, "line-rate"), branchRate = Rate(coverage, "branch-rate");
        Require(Math.Abs(lineRate - (double)covered / lines) <= .00001 && Math.Abs(branchRate - (double)hitBranches / branches) <= .00001,
            "Coverage rates differ from native counts.");
        var result = new JsonObject
        {
            ["parserCases"] = cases,
            ["trxSha256"] = Digest(trxBytes),
            ["coberturaSha256"] = hash,
            ["nativeReport"] = Path.GetRelativePath(root, paths[0]).Replace('\\', '/'),
            ["linesCovered"] = covered,
            ["linesValid"] = lines,
            ["branchesCovered"] = hitBranches,
            ["branchesValid"] = branches,
            ["lineRate"] = lineRate,
            ["branchRate"] = branchRate,
            ["passed"] = lineRate >= policy["minimumLineRate"]!.GetValue<double>() && branchRate >= policy["minimumBranchRate"]!.GetValue<double>()
        };
        return result;
    }

    internal static JsonObject Mutation(string reportPath, string reviewPath, string language, string version)
    {
        JsonObject report = Read(reportPath, out string reportHash), review = Read(reviewPath, out string reviewHash);
        string relative = ToolchainInputs.Relative(ToolchainInputs.Text(review, "source"));
        string sourceHash = Hash(Path.Combine(language, relative));
        Require(sourceHash == (string?)review["sourceSha256"] && version == (string?)review["strykerVersion"],
            "Mutation disposition review is stale for this source/tool version.");
        JsonObject files = report["files"]!.AsObject();
        Require(files.Count == 1, "Expected the parser-only native mutation report.");
        var file = files.Single();
        string suffix = relative.StartsWith("src/CIS.Core/", StringComparison.Ordinal) ? relative["src/CIS.Core/".Length..] : relative;
        string fileName = file.Key.Replace('\\', '/');
        Require(fileName == suffix || fileName.EndsWith("/" + suffix, StringComparison.Ordinal), "Native mutation file differs from the reviewed parser.");
        JsonObject[] mutants = file.Value!["mutants"]!.AsArray().Select(item => item!.AsObject()).ToArray();
        Require(mutants.Select(item => ToolchainInputs.Text(item, "id")).Distinct(StringComparer.Ordinal).Count() == mutants.Length, "Duplicate native mutant ID.");
        string[] statuses = ["Killed", "Timeout", "Survived", "NoCoverage", "Ignored", "CompileError"];
        Require(mutants.All(item => statuses.Contains((string?)item["status"], StringComparer.Ordinal)), "Unrecognized native mutant status.");
        int killed = mutants.Count(item => (string?)item["status"] == "Killed");
        int denominator = mutants.Count(item => (string?)item["status"] is "Killed" or "Timeout" or "Survived" or "NoCoverage");
        Require(denominator > 0 && (double)killed / denominator >= .9, "Conservative mutation denominator or threshold failed.");
        JsonObject[] excluded = mutants.Where(item => (string?)item["status"] != "Killed").ToArray();
        JsonObject[] expected = review["mutants"]!.AsArray().Select(item => item!.AsObject()).ToArray();
        Require(excluded.Length == expected.Length && expected.Select(item => ToolchainInputs.Text(item, "id")).Distinct(StringComparer.Ordinal).Count() == expected.Length,
            "Mutation exclusions differ from the source-bound review.");
        foreach (JsonObject mutant in excluded)
        {
            JsonObject[] matches = expected.Where(item => (string?)item["id"] == (string?)mutant["id"]).ToArray();
            Require(matches.Length == 1, "Unreviewed non-killed mutant.");
            foreach (string field in new[] { "status", "statusReason", "mutatorName", "replacement" })
            { Require((string?)matches[0][field] == (string?)mutant[field], "Mutation disposition mismatch: " + field); }
        }
        return new JsonObject
        {
            ["nativeReportSha256"] = reportHash,
            ["reviewedInputSha256"] = reviewHash,
            ["sourceSha256"] = sourceHash,
            ["exclusions"] = review["mutants"]!.DeepClone(),
            ["numerator"] = killed,
            ["denominator"] = denominator,
            ["total"] = mutants.Length,
            ["conservativeScore"] = (double)killed / denominator,
            ["reclassifiedMutants"] = new JsonArray(),
            ["statuses"] = new JsonArray(mutants.GroupBy(item => (string?)item["status"]).Select(group => (JsonNode)new JsonObject { ["Name"] = group.Key, ["Count"] = group.Count() }).ToArray())
        };
    }
    private static long Number(XElement element, string name)
    {
        Require(long.TryParse((string?)element.Attribute(name), NumberStyles.None, CultureInfo.InvariantCulture, out long value), "Invalid native " + name + ".");
        return value;
    }
    private static double Rate(XElement element, string name)
    {
        Require(double.TryParse((string?)element.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
            double.IsFinite(value) && value is >= 0 and <= 1, "Invalid native " + name + ".");
        return value;
    }
}
