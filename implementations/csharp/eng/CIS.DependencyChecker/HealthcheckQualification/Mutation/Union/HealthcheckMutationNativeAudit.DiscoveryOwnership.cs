using System.Globalization;
using System.Text.Json.Nodes;

namespace CIS.Verification;

internal static partial class HealthcheckMutationNativeAudit
{
    // Classify by the actual typed protocol, never by a friendly file name.
    // The complete controller is still parsed in its assigned phase; mixed-role
    // or malformed records therefore cannot disappear through classification.
    private static bool ContainsDiscoveryRequest(string path, RetainedDiagnosticInputs? retained = null)
    {
        foreach (string line in retained?.ReadLines(path) ?? File.ReadLines(path))
        {
            CheckLine(line);
            int marker = line.IndexOf(RequestMarker, StringComparison.Ordinal);
            if (marker < 0) { continue; }
            string suffix = line[(marker + RequestMarker.Length)..];
            int separator = suffix.IndexOf(") -> ", StringComparison.Ordinal);
            Require(separator >= 0, "Malformed controller envelope during role classification.");
            JsonObject message = Parse(suffix[(separator + 5)..]);
            string type = Text(message, "MessageType");
            Require(type == suffix[..separator], "Controller role classification envelope disagrees with its typed message.");
            if (type == "TestDiscovery.Start") { return true; }
        }
        return false;
    }

    private static bool DiscoverySelectionComplete(HashSet<string> found, DiscoverySpec spec,
        Dictionary<string, TestIdentity> tests)
    {
        // An unambiguous source-owned case is mandatory in its own discovery.
        // A linked case's actual owning DLL is learned from typed TestCase.Source.
        // The later global equality check requires EVERY linked GUID exactly once.
        return found.IsSubsetOf(spec.Selected)
            && spec.Selected.Where(guid => tests[guid].Sources.Count == 1).All(found.Contains);
    }

    private static void ResolveLinkedDiscoveryOwnership(Dictionary<string, TestIdentity> tests,
        Dictionary<int, DiscoveryRequest> discoveries, Dictionary<string, string> ownership, AuditState state)
    {
        if (tests.Values.All(test => test.Sources.Count == 1)) { return; }
        Require(discoveries.Count > 0 && state.DiscoveryHostCount > 0
            && state.DiscoveryHostCount == state.NormalDiscoveryHostCount,
            "Ambiguous linked test origins require complete normal actual discovery, not guessed project ownership.");
        Require(discoveries.Values.All(request => request.Trusted
            && (!request.SequentialControllerClosureRequired || request.ControllerClosed)
            && request.LaunchLines.Count > 0 && request.LaunchLines.Count == request.HostCount
            && request.ObservedSources.SetEquals(request.Spec.Sources)),
            "Linked ownership requires complete trusted controller/host/source discovery closure.");
        Require(ownership.Count == tests.Count && ownership.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(tests.Keys),
            "Typed discovery must cover every original declared GUID exactly once across actual project DLLs.");
        foreach ((string guid, TestIdentity test) in tests.ToArray())
        {
            Require(ownership.TryGetValue(guid, out string? origin) && origin is not null && test.Sources.Contains(origin),
                "Typed discovery reports a GUID outside its permitted actual project origin.");
            // Tests from one linked file originally share a candidate HashSet.
            // Replace each immutable record with its OWN singleton; never mutate
            // that shared candidate set or assign both linked IDs to both DLLs.
            tests[guid] = test with { Sources = new HashSet<string>([origin], StringComparer.Ordinal) };
        }
    }

    private static void CloseDiscoveryController(int controller, string path, NativeRequest? pending,
        Dictionary<int, DiscoveryRequest> discoveries, Dictionary<int, NativeExit> exits,
        DiscoveryControllerClosure closure)
    {
        Require(pending is null, "A sequential discovery segment cannot contain a pending mutation launch.");
        Require(discoveries.TryGetValue(controller, out DiscoveryRequest? request)
            && request.Trusted && !request.ControllerClosed && request.Path == path,
            "Only a unique typed discovery controller may end a sequential physical-log segment.");
        request.SequentialControllerClosureRequired = true;
        Require(closure.TerminateCount == 1 && closure.ExecutorExitCount == 1
            && closure.ExecutorExitCode == 0 && closure.ExecutorExitLine > closure.TerminateLine,
            "A discovery segment must fully terminate and record its own ExecutorExit0 before another PID or EOF.");
        NativeExit[] actual = exits.Values.Where(exit => exit.ControllerPid == controller && exit.Path == path).ToArray();
        Require(request.LaunchLines.Count > 0 && actual.Length == request.LaunchLines.Count
            && actual.All(exit => exit.Code == 0 && exit.Error.Length == 0
                && exit.Line > request.LaunchLines.Min() && exit.Line < closure.TerminateLine),
            "Sequential discovery requires all own actual host callbacks to exit0 before controller termination.");
        request.ControllerClosed = true;
    }

    private sealed class DiscoveryControllerClosure
    {
        internal int TerminateCount { get; private set; }
        internal int TerminateLine { get; private set; }
        internal int ExecutorExitCount { get; private set; }
        internal int ExecutorExitLine { get; private set; }
        internal int ExecutorExitCode { get; private set; }

        internal void Terminate(int line) { TerminateCount++; TerminateLine = line; }
        internal void Observe(string line, int lineNumber)
        {
            const string marker = "Executor.Execute: Exiting with exit code of ";
            int start = line.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) { return; }
            string suffix = line[(start + marker.Length)..];
            Require(int.TryParse(suffix, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int code),
                "Malformed actual controller executor exit code.");
            ExecutorExitCount++; ExecutorExitLine = lineNumber; ExecutorExitCode = code;
        }
    }
}
