using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class ProcessView
{
    internal sealed record Source(long Offset, int Bytes, string Sha256, string Text);
    internal sealed record Line(string Text, Source[] Sources, string Rule);
    internal static Line[] Derive(Source[] raw)
    {
        var result = new List<Line>(); Source? pendingExec = null; string? pendingName = null;
        Source? pendingExit = null; string? exitKind = null; bool needsTerminal = false;
        foreach (Source source in raw)
        {
            Match record = Regex.Match(source.Text, @"^(\d+(?:\.\d+)?) (.*)$", RegexOptions.CultureInvariant);
            Need(record.Success, "Raw PID record framing differs."); string stamp = record.Groups[1].Value, body = record.Groups[2].Value;
            if (pendingExit is not null)
            {
                if (body == "<... " + exitKind + " resumed>) = ?")
                {
                    Match first = Regex.Match(pendingExit.Text, @"^(\d+(?:\.\d+)?) ((?:exit|exit_group)\(-?\d+) <unfinished \.\.\.>$", RegexOptions.CultureInvariant);
                    Need(first.Success, "Exit_group entry code is malformed.");
                    result.Add(new Line(first.Groups[1].Value + " " + first.Groups[2].Value + ") = ?", [pendingExit, source], "exact-paired-nonreturning-" + exitKind + "-observation"));
                    pendingExit = null; exitKind = null; needsTerminal = true; continue;
                }
                Need(Regex.IsMatch(body, @"^\+\+\+ (?:exited with -?\d+|killed by .*) \+\+\+$", RegexOptions.CultureInvariant), "Unfinished exit_group has intervening raw process activity.");
                result.Add(new Line(pendingExit.Text, [pendingExit], "exact-selected-process-record")); pendingExit = null;
            }
            if (needsTerminal)
            {
                Need(Regex.IsMatch(body, @"^\+\+\+ (?:exited with -?\d+|killed by .*) \+\+\+$", RegexOptions.CultureInvariant), "Resumed exit_group has intervening raw process activity."); needsTerminal = false;
            }
            if (pendingExec is not null)
            {
                Match resumed = Regex.Match(body, @"^<\.\.\. (execve|execveat) resumed>(.*)$", RegexOptions.CultureInvariant);
                Need(resumed.Success && resumed.Groups[1].Value == pendingName, "Exec resume missing or has intervening raw process activity.");
                Match first = Regex.Match(pendingExec.Text, @"^(\d+(?:\.\d+)?) (.*)$", RegexOptions.CultureInvariant);
                string firstBody = first.Groups[2].Value;
                string joined = firstBody[..firstBody.IndexOf(" <unfinished ...>", StringComparison.Ordinal)] + resumed.Groups[2].Value;
                Need(!joined.Contains("<unfinished", StringComparison.Ordinal), "Repeated unfinished exec unsupported.");
                result.Add(new Line(first.Groups[1].Value + " " + NormalizeExec(joined), [pendingExec, source], "exact-paired-exec-return-and-hex-quoted-bytes"));
                pendingExec = null; pendingName = null; continue;
            }
            Match exec = Regex.Match(body, @"^(execve|execveat)\(", RegexOptions.CultureInvariant);
            if (exec.Success)
            {
                if (body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { pendingExec = source; pendingName = exec.Groups[1].Value; continue; }
                result.Add(new Line(stamp + " " + NormalizeExec(body), [source], "complete-exec-hex-quoted-bytes")); continue;
            }
            Need(!Regex.IsMatch(body, @"^<\.\.\. execve(?:at)? resumed>", RegexOptions.CultureInvariant), "Unbound exec resume.");
            bool process = Regex.IsMatch(body, @"^(?:(?:clone3?|fork|vfork|exit|exit_group)\(|<\.\.\. (?:clone3?|fork|vfork|exit|exit_group) resumed>|\+\+\+ )", RegexOptions.CultureInvariant);
            if (!process) { continue; }
            if ((body.StartsWith("exit_group(", StringComparison.Ordinal) || body.StartsWith("exit(", StringComparison.Ordinal)) && body.EndsWith(" <unfinished ...>", StringComparison.Ordinal)) { pendingExit = source; exitKind = body[..body.IndexOf('(')]; continue; }
            result.Add(new Line(source.Text, [source], "exact-selected-process-record"));
        }
        Need(pendingExec is null && pendingExit is null && !needsTerminal, "Unfinished process identity at EOF."); return result.ToArray();
    }
    internal static string NormalizeExec(string body)
    {
        Need(!body.StartsWith("execveat(", StringComparison.Ordinal) || !body.EndsWith(" = 0", StringComparison.Ordinal), "Successful execveat is unsupported by the unchanged kernel reader.");
        var result = new StringBuilder(); int at = 0;
        while (at < body.Length)
        {
            if (body[at] != '"') { result.Append(body[at++]); continue; }
            at++; using var bytes = new MemoryStream();
            while (at < body.Length && body[at] != '"')
            {
                Need(at + 3 < body.Length && body[at] == '\\' && body[at + 1] == 'x' && Uri.IsHexDigit(body[at + 2]) && Uri.IsHexDigit(body[at + 3]), "Exec quoted bytes are not complete -xx hex form.");
                bytes.WriteByte(byte.Parse(body.AsSpan(at + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); at += 4;
            }
            Need(at < body.Length && body[at++] == '"', "Unterminated raw exec string.");
            result.Append(JsonSerializer.Serialize(new UTF8Encoding(false, true).GetString(bytes.ToArray())));
        }
        Need(!Regex.IsMatch(result.ToString(), @"\[[^\]]*\.\.\.", RegexOptions.CultureInvariant), "Abbreviated exec argument array unsupported.");
        return result.ToString();
    }
    private static void Need([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value, string detail) { if (!value) { throw new InvalidDataException(detail); } }
}
