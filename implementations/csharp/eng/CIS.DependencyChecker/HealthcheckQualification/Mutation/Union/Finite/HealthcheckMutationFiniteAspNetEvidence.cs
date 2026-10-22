using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static CIS.Verification.HealthcheckMutationFiniteAssertionValidation;
using static CIS.Verification.HealthcheckMutationUnionModel;
using C = CIS.Verification.HealthcheckMutationFiniteAspNetCatalog;
using V = CIS.Verification.HealthcheckMutationFiniteAspNetValidation;

namespace CIS.Verification;

internal static class HealthcheckMutationFiniteAspNetEvidence
{
    private static string Text(JsonObject value, string name) => HealthcheckMutationFiniteAssertionValidation.Text(value, name);
    internal static DateTimeOffset ObservationUtc(string text)
    {
        Need(Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)), "Invalid ASP finite UTC format/precision.");
        Need(DateTimeOffset.TryParseExact(text, ["yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss'Z'"],
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset value) && value.Offset == TimeSpan.Zero, "Invalid ASP finite UTC observation.");
        return value;
    }
    private static DateTimeOffset Utc(string text) => ObservationUtc(text);

    private sealed record Result(int Pid, string Guid, string Method, int Outcome, string Message, string Stack);

    internal static JsonObject Native(NativeAuditResult audit, string[] logs, string trace)
    {
        Need(audit.HostCount == 5 && audit.NormalHostCount == 5 && audit.AssertionKilledIds.Order(StringComparer.Ordinal).SequenceEqual(C.Targets.Select(value => value.Id).Order(StringComparer.Ordinal))
            && audit.ReviewedSemanticKilledIds.Length == 0 && (int?)audit.Observations["directAssertionWitnessCount"] == 8
            && (int?)audit.Observations["discoveryHostCount"] == 1 && (int?)audit.Observations["normalDiscoveryHostCount"] == 1 && (int?)audit.Observations["discoveredGuidCount"] == 6,
            "ASP finite native discovery/normality/direct witness cohort differs.");
        var results = new Dictionary<(int, string), Result>();
        foreach (string path in logs)
        {
            string? first = File.ReadLines(path).FirstOrDefault(); if (first?.Contains(", testhost.dll,", StringComparison.Ordinal) != true) { continue; }
            Match match = Regex.Match(first, @"^TpTrace \w+: 0 : (\d+),", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); Need(match.Success, "Missing ASP native host PID.");
            int pid = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (string line in File.ReadLines(path))
            {
                const string marker = "TestRequestHandler.SendData: sending data from testhost: "; int offset = line.IndexOf(marker, StringComparison.Ordinal); if (offset < 0) { continue; }
                JsonObject message = JsonNode.Parse(line[(offset + marker.Length)..])!.AsObject();
                JsonArray? rows = (string?)message["MessageType"] switch
                {
                    "TestExecution.StatsChange" => message["Payload"]?["NewTestResults"] as JsonArray,
                    "TestExecution.Completed" => message["Payload"]?["LastRunTests"]?["NewTestResults"] as JsonArray,
                    _ => null
                };
                if (rows is null) { continue; }
                foreach (JsonObject row in rows.Select(value => value!.AsObject()))
                {
                    JsonObject test = Object(row, "TestCase"); string guid = Text(test, "Id");
                    Need(results.TryAdd((pid, guid), new(pid, guid, Text(test, "FullyQualifiedName"), (int)row["Outcome"]!, (string?)row["ErrorMessage"] ?? "", (string?)row["ErrorStackTrace"] ?? "")), "Duplicate ASP native result.");
                }
            }
        }
        Need(results.Count == 30, "ASP finite must retain all30 native case results.");
        var secondaries = new JsonArray(); var expectedIssues = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string id, int pid) in new[] { ("baseline", 948292), ("195", 949732), ("196", 949748), ("197", 949786), ("278", 949805) })
        {
            Result[] cohort = results.Values.Where(value => value.Pid == pid).ToArray();
            Need(cohort.Select(value => value.Guid + "\t" + value.Method).Order(StringComparer.Ordinal).SequenceEqual(C.Cases), "ASP native host does not execute the own six-case cohort.");
            string[] direct = id == "baseline" ? [] : C.DirectGuids(id);
            foreach (Result result in cohort)
            {
                if (direct.Contains(result.Guid, StringComparer.Ordinal))
                {
                    string expected = result.Method == C.Registration ? C.RegistrationMessage : C.CancellationMessage;
                    Need(result.Outcome == 2 && result.Message.Replace("\r\n", "\n", StringComparison.Ordinal) == expected, "ASP direct assertion changed.");
                    JsonObject[] witnesses = audit.Observations["witnessSamples"]!.AsArray().Select(value => value!.AsObject()).Where(value => (string?)value["mutantId"] == id && (string?)value["guid"] == result.Guid).ToArray();
                    Need(witnesses.Length == 1 && (int?)witnesses[0]["hostPid"] == pid && (int?)witnesses[0]["actualOsExitCode"] == 0 && Text(witnesses[0], "method") == result.Method, "ASP exact direct host/GUID/method witness missing.");
                }
                else if (V.Secondary(id, pid, result.Guid, result.Method, result.Outcome, result.Message, result.Stack))
                {
                    string issueKey = id + "\t" + pid.ToString(CultureInfo.InvariantCulture) + "\t" + result.Guid; expectedIssues.Add(issueKey);
                    secondaries.Add(new JsonObject { ["mutantId"] = id, ["hostPid"] = pid, ["guid"] = result.Guid, ["exceptionType"] = "System.ArgumentException", ["messageSha256"] = V.Sha(result.Message), ["stackSha256"] = V.Sha(result.Stack), ["additionalKillCredit"] = false });
                }
                else { Need(result.Outcome == 1 && result.Message.Length == 0 && result.Stack.Length == 0, "Unreviewed ASP native failure or contradictory Passed result."); }
            }
        }
        Need(secondaries.Count == 4 && audit.Issues.Length == 4, "ASP exact four retained secondary exceptions differ.");
        var actualIssues = new HashSet<string>(StringComparer.Ordinal);
        foreach (IntegrityIssue issue in audit.Issues)
        {
            Match match = Regex.Match(issue.Detail, @"^Host (\d+), GUID ([0-9a-f-]{36}) has neither a direct assertion nor an exact reviewed source-bound semantic witness;", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            Need(issue.Code == "NativeFailureNotDirectAssertion" && issue.MutantIds.Length == 1 && match.Success,
                "Unexpected/global ASP native integrity issue.");
            Need(actualIssues.Add(issue.MutantIds[0] + "\t" + match.Groups[1].Value + "\t" + match.Groups[2].Value), "Duplicate ASP secondary issue.");
        }
        Need(actualIssues.SetEquals(expectedIssues), "ASP native issue ownership differs from its actual reviewed secondary rows.");
        Kernels(trace);
        return new JsonObject { ["nativeCases"] = 30, ["directAssertionRows"] = 8, ["classifiedSecondaryRows"] = secondaries, ["normalExecutionHosts"] = 5, ["normalDiscoveryHosts"] = 1, ["oldFullIssuesWaived"] = false };
    }

    internal static void Kernels(string path)
    {
        string[] lines = File.ReadAllLines(path);
        foreach ((int pid, int code) in new[] { (945057, 0), (948154, 1), (948155, 1), (948292, 0), (949732, 0), (949748, 0), (949786, 0), (949805, 0) })
        {
            string pattern = "^" + pid.ToString(CultureInfo.InvariantCulture) + @"\s+[0-9.]+\s+\+\+\+ exited with " + code.ToString(CultureInfo.InvariantCulture) + @" \+\+\+$";
            Need(lines.Count(line => Regex.IsMatch(line, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) == 1, "ASP actual CLI/controller/host kernel exit missing or contradictory.");
        }
        Need(lines.Any(line => line.StartsWith("945057 ", StringComparison.Ordinal) && line.Contains("execve(", StringComparison.Ordinal) && line.Contains("Stryker.CLI.dll", StringComparison.Ordinal)
            && line.Contains("8.0.31", StringComparison.Ordinal) && line.Contains("--roll-forward", StringComparison.Ordinal) && line.Contains("Disable", StringComparison.Ordinal)), "ASP native CLI launch/runtime is not the retained launch.");
        string[] prohibited = ["HealthcheckHttpHost.dll", "CisHealthProbe.dll", "HealthcheckPipeHost.dll", "HealthcheckCollectorHost.dll"];
        Need(!lines.Where(line => line.Contains("execve(", StringComparison.Ordinal)).Any(line => prohibited.Any(value => line.Contains(value, StringComparison.Ordinal))), "An external fixture invalidates the in-process-only ASP finite profile.");
    }

    internal static JsonObject Captures(string beforeRoot, string preFreshRoot, string freshRoot, JsonObject before, JsonObject preFresh, JsonObject fresh,
        DateTimeOffset nativeStarted, DateTimeOffset nativeFinished, DateTimeOffset initialFinished, DateTimeOffset freshStarted, DateTimeOffset freshFinished)
    {
        JsonObject[] Rows(JsonObject receipt, string root, int count)
        {
            Need(Text(receipt, "kind") == "ActualFiniteNativeRoleBodyObservation" && Text(receipt, "sourceRoot") == C.SourceRoot && Text(receipt, "manifestSha256") == C.DerivedManifest
                && Text(receipt, "configSha256") == C.FiniteConfig && (int?)receipt["sourceInputsVerified"] == 1442 && (bool?)receipt["complete"] == true, "ASP capture source/config/scope differs.");
            DateTimeOffset start = Utc(Text(receipt, "startedUtc")), end = Utc(Text(receipt, "completedUtc")); Need(start < end, "ASP capture times reversed.");
            JsonObject[] rows = receipt["files"]!.AsArray().Select(value => value!.AsObject()).ToArray();
            Need(rows.Length == count && rows.Select(row => Text(row, "retainedPath")).Distinct(StringComparer.Ordinal).Count() == count, "ASP capture role set missing/duplicate.");
            foreach (JsonObject row in rows)
            {
                string role = Text(row, "retainedPath"); Need(Safe(role) == role && Text(row, "originalPath") == C.LanguageRoot + "/" + role, "ASP role native origin differs.");
                string path = Path.Combine(root, role); Need(Hash(path) == Text(row, "sha256") && new FileInfo(path).Length == (long?)row["bytes"], "ASP role physical body differs.");
                Need(Utc(Text(row, "capturedUtc")) >= start && Utc(Text(row, "capturedUtc")) <= end, "ASP restored role capture time differs.");
                SelectorObservation(row, count == 33);
                if (role.EndsWith(".dll", StringComparison.Ordinal) || role.EndsWith(".stryker-unchanged", StringComparison.Ordinal))
                {
                    using FileStream file = File.OpenRead(path); using var pe = new PEReader(file); MetadataReader metadata = pe.GetMetadataReader();
                    Need(metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString() == Text(row, "mvid"), "ASP physical role MVID differs.");
                    if (role.EndsWith("CIS.Core.dll", StringComparison.Ordinal) || role.EndsWith("CIS.Core.dll.stryker-unchanged", StringComparison.Ordinal))
                    {
                        Need(!metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle)).Any(type =>
                        {
                            string[] fields = type.GetFields().Select(handle => metadata.GetString(metadata.GetFieldDefinition(handle).Name)).ToArray();
                            return fields.Contains("ActiveMutant", StringComparer.Ordinal) && fields.Contains("CaptureCoverage", StringComparer.Ordinal)
                                && type.GetMethods().Any(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "IsActive");
                        }), "Retained restored Core still contains the actual native selector shape.");
                    }
                }
                else { Need(row["mvid"] is null, "ASP nonassembly invented a MVID."); }
            }
            return rows;
        }
        JsonObject[] first = Rows(before, beforeRoot, 33), restored = Rows(preFresh, preFreshRoot, 34), last = Rows(fresh, freshRoot, 34);
        string backup = C.TestRoles + "CIS.Core.dll.stryker-unchanged";
        Need(first.All(row => Text(row, "retainedPath") != backup) && restored.Select(row => Text(row, "retainedPath")).ToHashSet(StringComparer.Ordinal).SetEquals(last.Select(row => Text(row, "retainedPath")))
            && restored.Where(row => Text(row, "retainedPath") != backup).Select(row => Text(row, "retainedPath")).ToHashSet(StringComparer.Ordinal).SetEquals(first.Select(row => Text(row, "retainedPath"))), "ASP capture physical role sets differ.");
        Need(initialFinished < Utc(Text(before, "startedUtc")) && Utc(Text(before, "completedUtc")) < nativeStarted && nativeFinished < Utc(Text(preFresh, "startedUtc"))
            && Utc(Text(preFresh, "completedUtc")) < freshStarted && freshFinished < Utc(Text(fresh, "startedUtc")), "ASP native/body/fresh chronology differs.");
        foreach (JsonObject receipt in new[] { preFresh, fresh }) { Need((bool?)receipt["actualBackupPresent"] == true && (bool?)receipt["requireRestored"] == true && (bool?)receipt["actualRestorationVerified"] == true, "ASP actual postnative backup/restoration observation missing."); }
        int normalized = 0;
        foreach (JsonObject row in restored)
        {
            string role = Text(row, "retainedPath"); JsonObject after = last.Single(value => Text(value, "retainedPath") == role);
            if (Text(row, "sha256") != Text(after, "sha256")) { Need(V.AdmitsSymbolRole(role, Text(row, "sha256"), Text(after, "sha256")), "Unreviewed ASP postfresh role changed."); normalized++; }
            else { Need((long?)row["bytes"] == (long?)after["bytes"], "ASP unchanged role length differs."); }
            Need(JsonNode.DeepEquals(row["mvid"], after["mvid"]), "ASP postfresh MVID changed.");
            if (role != backup)
            {
                JsonObject old = first.Single(value => Text(value, "retainedPath") == role);
                Need(Text(old, "sha256") == Text(after, "sha256") && (long?)old["bytes"] == (long?)after["bytes"] && JsonNode.DeepEquals(old["mvid"], after["mvid"]), "ASP original/fresh ordinary role differs.");
            }
        }
        Need(normalized == 1, "The exact ASP stale test-Core symbol discrepancy must remain explicit.");
        JsonArray symbols = Symbols(preFreshRoot, freshRoot);
        foreach (string root in new[] { preFreshRoot, freshRoot })
        {
            foreach (string role in new[] { C.CoreRoles + "CIS.Core.dll", C.TestRoles + "CIS.Core.dll", backup })
            { Need(Hash(Path.Combine(root, role)) == C.Core && CoreIdentity(Path.Combine(root, role)).ToString() == C.CoreMvid, "ASP actual source/test/backup Core identity differs."); }
        }
        return new JsonObject
        {
            ["proofDeficits"] = new JsonArray(),
            ["preFreshRoles"] = 34,
            ["freshRoles"] = 34,
            ["ordinaryEqualRoles"] = 33,
            ["symbolNormalization"] = symbols,
            ["stalePreFreshTestCorePdbUntrusted"] = true,
            ["all34BodiesEqualClaim"] = false,
            ["historicalLoadedSymbolsClaim"] = false,
            ["historicalInstrumentedCoreBodyRetained"] = false
        };
    }

    private static JsonArray Symbols(string before, string after)
    {
        var pairs = new JsonArray();
        foreach ((string phase, string root, string dll, string pdb, string expectedDll, string expectedPdb) in new[]
        {
            ("pre-source", before, C.CoreRoles + "CIS.Core.dll", C.CoreRoles + "CIS.Core.pdb", C.Core, C.CleanPdb),
            ("fresh-source", after, C.CoreRoles + "CIS.Core.dll", C.CoreRoles + "CIS.Core.pdb", C.Core, C.CleanPdb),
            ("fresh-test", after, C.TestRoles + "CIS.Core.dll", C.TestRoles + "CIS.Core.pdb", C.Core, C.CleanPdb),
            ("pre-receiving", before, C.TestRoles + "CIS.Core.IntegrationTests.dll", C.TestRoles + "CIS.Core.IntegrationTests.pdb", C.IntegrationDll, C.IntegrationPdb),
            ("fresh-receiving", after, C.TestRoles + "CIS.Core.IntegrationTests.dll", C.TestRoles + "CIS.Core.IntegrationTests.pdb", C.IntegrationDll, C.IntegrationPdb)
        })
        {
            string assembly = Path.Combine(root, dll), symbols = Path.Combine(root, pdb); Need(Hash(assembly) == expectedDll && Hash(symbols) == expectedPdb, "ASP exact PE/PDB normalization role differs.");
            var binding = HealthcheckMutationPortablePdbValidation.Read(assembly, symbols); Need(binding.Matches, "ASP PE/PDB must match both content ID and zero-ID checksum.");
            JsonObject details = binding.Details(); details["role"] = phase; pairs.Add(details);
        }
        string stalePath = Path.Combine(before, C.TestRoles + "CIS.Core.pdb"); Need(Hash(stalePath) == C.StalePdb, "ASP stale PDB hash differs.");
        var stale = HealthcheckMutationPortablePdbValidation.Read(Path.Combine(before, C.TestRoles + "CIS.Core.dll"), stalePath);
        Need(!stale.ContentIdMatches && !stale.ChecksumMatches, "ASP reviewed old symbol discrepancy absent."); JsonObject bad = stale.Details(); bad["role"] = "pre-test-untrusted"; pairs.Add(bad);
        foreach (string root in new[] { before, after }) { ReceivingSymbols(root); }
        return pairs;
    }

    private static void ReceivingSymbols(string root)
    {
        using FileStream assembly = File.OpenRead(Path.Combine(root, C.TestRoles + "CIS.Core.IntegrationTests.dll")); using var pe = new PEReader(assembly);
        MetadataReader metadata = pe.GetMetadataReader(); using FileStream pdb = File.OpenRead(Path.Combine(root, C.TestRoles + "CIS.Core.IntegrationTests.pdb"));
        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdb); MetadataReader symbols = provider.GetMetadataReader();
        foreach ((string method, int line) in new[] { (C.Registration, 65), (C.Cancellation, 348) })
        {
            string name = method[(method.LastIndexOf('.') + 1)..]; bool found = false;
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle); if (!metadata.GetString(type.Name).StartsWith("<" + name + ">", StringComparison.Ordinal)) { continue; }
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    if (metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name) != "MoveNext") { continue; }
                    MethodDebugInformation debug = symbols.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(methodHandle)));
                    foreach (SequencePoint point in debug.GetSequencePoints().Where(point => !point.IsHidden && point.StartLine == line))
                    {
                        Document document = symbols.GetDocument(point.Document.IsNil ? debug.Document : point.Document);
                        Need(symbols.GetString(document.Name) == C.SourceRoot + "/" + C.TestPath && Convert.ToHexStringLower(symbols.GetBlobBytes(document.Hash)) == C.NewTest,
                            "ASP direct receiving symbol document does not bind the exact detector source."); found = true;
                    }
                }
            }
            Need(found, "ASP direct receiving method/sequence point absent from unchanged actual Integration symbols.");
        }
    }

    internal static string[] Baselines(string root)
    {
        string At(string relative) => Path.Combine(root, relative);
        string[] initial = HealthcheckMutationBaselineValidation.ReadPassedCases(At(C.Initial + "selected.trx"), "integration"), fresh = HealthcheckMutationBaselineValidation.ReadPassedCases(At(C.Fresh + "selected.trx"), "integration");
        V.Cases(initial, fresh, C.Cases);
        foreach ((string name, string directory) in new[] { ("baseline", C.Initial), ("fresh", C.Fresh) })
        {
            Need(File.ReadAllText(At(C.Run + "-" + name + ".exit")).Trim() == "0", "ASP finite baseline native process failed.");
            JsonObject summary = JsonNode.Parse(File.ReadAllBytes(At(directory + "summary.json")))!.AsObject();
            Need((bool?)summary["passed"] == true && summary["failure"] is null && Text(summary, "Filter") == C.Filter && Text(summary, "Configuration") == "Release", "ASP baseline summary differs.");
            string[] args = JsonNode.Parse(File.ReadAllBytes(At(directory + "test-arguments.json")))!.AsArray().Select(value => value!.GetValue<string>()).ToArray();
            Need(args.SequenceEqual(new[] { "test", "tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj", "-c", "Release", "--no-build", "--no-restore", "--logger", "trx;LogFileName=selected.trx", "--results-directory", C.Task + directory.TrimEnd('/'), "--filter", C.Filter }), "ASP baseline actual native argv differs.");
            XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"; XElement times = XDocument.Load(At(directory + "selected.trx")).Descendants(ns + "Times").Single();
            BaselineWindow(File.ReadAllText(At(C.Run + "-" + name + ".started.utc")).Trim(), File.ReadAllText(At(C.Run + "-" + name + ".completed.utc")).Trim(),
                (string)times.Attribute("start")!, (string)times.Attribute("finish")!);
        }
        return initial;
    }

    // These shell files were emitted by date -u to whole seconds. Their
    // preserved text denotes a one-second interval, not a subsecond clock read.
    // This profile-specific interpretation does not alter other finite runs.
    internal static void BaselineWindow(string shellStarted, string shellFinished, string trxStarted, string trxFinished)
    {
        const string format = "yyyy-MM-dd'T'HH:mm:ss'Z'";
        Need(DateTimeOffset.TryParseExact(shellStarted, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset start), "ASP shell start must retain its exact whole-second UTC format.");
        Need(DateTimeOffset.TryParseExact(shellFinished, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset end), "ASP shell completion must retain its exact whole-second UTC format.");
        DateTimeOffset actualStart = Utc(trxStarted), actualFinish = Utc(trxFinished);
        Need(start <= end && start <= actualStart && actualStart < actualFinish && actualFinish < end.AddSeconds(1), "ASP native baseline chronology differs.");
    }

    internal static void SelectorObservation(JsonObject row, bool preNative)
    {
        Need(preNative ? !row.ContainsKey("selectorTypes") : row["selectorTypes"] is JsonArray { Count: 0 },
            "ASP selector observation differs from its exact recorder phase/schema.");
    }

    internal static string Safe(string relative)
    { Need(!Path.IsPathRooted(relative) && !relative.Contains('\\') && !relative.Split('/').Any(value => value is "" or "." or ".."), "Unsafe ASP finite relative path."); return relative; }
}
