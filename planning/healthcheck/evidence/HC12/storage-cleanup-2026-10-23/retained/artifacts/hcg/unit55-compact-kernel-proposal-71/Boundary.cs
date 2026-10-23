using System.Globalization;
using System.Text.RegularExpressions;

internal sealed class Boundary
{
    private sealed class State { internal decimal Previous = -1; internal string? Pending; internal bool Terminal; internal long Offset; }
    private readonly Dictionary<int, State> states = [];
    internal (bool Process, long Offset) Observe(int pid, string text, int bytes)
    {
        if (!states.TryGetValue(pid, out State? state)) { state = new(); states.Add(pid, state); }
        Match parsed = Regex.Match(text, @"^(\d+(?:\.\d+)?) (.*)$", RegexOptions.CultureInvariant);
        Need(parsed.Success, "Malformed timestamped raw PID record.");
        decimal stamp = decimal.Parse(parsed.Groups[1].Value, CultureInfo.InvariantCulture); string body = parsed.Groups[2].Value;
        Need(stamp >= state.Previous, "Raw perPID clock order reversed."); state.Previous = stamp;
        long offset = state.Offset; state.Offset += bytes;
        bool terminal = Regex.IsMatch(body, @"^\+\+\+ (?:exited with -?\d+|killed by .*) \+\+\+$", RegexOptions.CultureInvariant);
        if (state.Pending is { } name)
        {
            bool resumed = body.StartsWith("<... " + name + " resumed>", StringComparison.Ordinal);
            Need(resumed || name is "exit" or "exit_group" && terminal, "Intervening raw record before pending process operation returned.");
            state.Pending = null; state.Terminal = resumed && name is "exit" or "exit_group";
        }
        else if (state.Terminal) { Need(terminal, "Intervening raw record after nonreturning process operation."); state.Terminal = false; }
        Match operation = Regex.Match(body, @"^(execve|execveat|exit|exit_group)\(", RegexOptions.CultureInvariant);
        if (operation.Success && body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { state.Pending = operation.Groups[1].Value; }
        bool significant = Regex.IsMatch(body, @"^(?:(?:clone3?|fork|vfork|execve|execveat|exit|exit_group)\(|<\.\.\. (?:clone3?|fork|vfork|execve|execveat|exit|exit_group) resumed>|\+\+\+ )", RegexOptions.CultureInvariant);
        return (significant, offset);
    }
    internal void Finish() => Need(states.Values.All(x => x.Pending is null && !x.Terminal), "Pending process operation at raw EOF.");
    internal static void Controls()
    {
        var valid = new Boundary(); valid.Observe(1, "1 exit(0 <unfinished ...>", 1); valid.Observe(1, "2 <... exit resumed>) = ?", 1); valid.Observe(1, "3 +++ exited with 0 +++", 1); valid.Finish();
        Reject(() => { var x = new Boundary(); x.Observe(1, "2 execve(\"\\x2f\", [], 0 <unfinished ...>", 1); x.Observe(1, "1 <... execve resumed>) = 0", 1); });
        Reject(() => { var x = new Boundary(); x.Observe(1, "1 exit(0 <unfinished ...>", 1); x.Observe(1, "2 write(0x1, 0x123, 1) = 1", 1); });
        Reject(() => { var x = new Boundary(); x.Observe(1, "1 exit(0 <unfinished ...>", 1); x.Observe(1, "2 <... exit resumed>) = ?", 1); x.Observe(1, "3 write(0x1, 0x123, 1) = 1", 1); });
        Reject(() => { var x = new Boundary(); x.Observe(1, "1 exit(0 <unfinished ...>", 1); x.Finish(); });
        static void Reject(Action action) { try { action(); throw new InvalidOperationException("Boundary negative control accepted."); } catch (InvalidDataException) { } }
    }
    private static void Need(bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
