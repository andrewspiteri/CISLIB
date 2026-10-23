using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

internal static class ProjectionControls
{
    internal static JsonArray Run()
    {
        var result = new JsonArray();
        void Check(string name, Action body) { body(); result.Add(new JsonObject { ["name"] = name, ["passed"] = true }); }
        void Reject(string name, Action body)
        {
            try { body(); throw new InvalidOperationException("Control accepted: " + name); }
            catch (Exception error) when (error is InvalidDataException or DecoderFallbackException)
            { result.Add(new JsonObject { ["name"] = name, ["passed"] = true, ["expectedRejection"] = error.Message }); }
        }
        Check("complete-hex-exec-decodes-real-byte-strings", () => Need(ProcessView.Derive(Rows("1 execve(\"\\x2f\\x64\", [\"\\x2f\\x64\"], 0x0) = 0")).Single().Text == "1 execve(\"/d\", [\"/d\"], 0x0) = 0"));
        Check("paired-exec-keeps-two-origins-and-start-time", () => { ProcessView.Line line = ProcessView.Derive(Rows("1 execve(\"\\x2f\\x64\", [\"\\x2f\\x64\"], 0x0 <unfinished ...>", "2 <... execve resumed>) = 0")).Single(); Need(line.Sources.Length == 2 && line.Text == "1 execve(\"/d\", [\"/d\"], 0x0) = 0"); });
        Reject("orphan-exec-resume", () => ProcessView.Derive(Rows("2 <... execve resumed>) = 0")));
        Reject("wrong-exec-kind-resume", () => ProcessView.Derive(Rows("1 execve(\"\\x2f\\x64\", [], 0x0 <unfinished ...>", "2 <... execveat resumed>) = 0")));
        Reject("intervening-activity-before-exec-resume", () => ProcessView.Derive(Rows("1 execve(\"\\x2f\\x64\", [], 0x0 <unfinished ...>", "2 write(0x1, 0x1, 0x1) = 0x1", "3 <... execve resumed>) = 0")));
        Reject("truncated-hex-exec-string", () => ProcessView.NormalizeExec("execve(\"\\x2\", [], 0x0) = 0"));
        Reject("non-utf8-exec-string", () => ProcessView.NormalizeExec("execve(\"\\xff\", [], 0x0) = 0"));
        Reject("abbreviated-exec-argv", () => ProcessView.NormalizeExec("execve(\"\\x2f\", [\"\\x2f\", ...], 0x0) = 0"));
        Reject("unsupported-successful-execveat", () => ProcessView.NormalizeExec("execveat(0, \"\\x2f\", [], 0, 0) = 0"));
        Check("creation-flags-and-return-exact", () => { const string text = "1 clone3({flags=CLONE_THREAD|CLONE_FILES}, 88) = 20"; Need(ProcessView.Derive(Rows(text)).Single().Text == text); });
        Check("exit-group-unfinished-to-terminal-exact", () => Need(ProcessView.Derive(Rows("1 exit_group(0 <unfinished ...>", "2 +++ exited with 0 +++"))[0].Text == "1 exit_group(0 <unfinished ...>"));
        Check("exit-group-observed-resume-two-origins", () => { ProcessView.Line line = ProcessView.Derive(Rows("1 exit_group(0 <unfinished ...>", "2 <... exit_group resumed>) = ?", "3 +++ exited with 0 +++"))[0]; Need(line.Text == "1 exit_group(0) = ?" && line.Sources.Length == 2); });
        Check("thread-exit-observed-resume-preserves-kind", () => { ProcessView.Line line = ProcessView.Derive(Rows("1 exit(0 <unfinished ...>", "2 <... exit resumed>) = ?", "3 +++ exited with 0 +++"))[0]; Need(line.Text == "1 exit(0) = ?" && line.Sources.Length == 2); });
        Reject("thread-exit-resume-cannot-become-exit-group", () => ProcessView.Derive(Rows("1 exit(0 <unfinished ...>", "2 <... exit_group resumed>) = ?", "3 +++ exited with 0 +++")));
        Check("nonzero-exit-code-never-normalized-to-zero", () => Need(ProcessView.Derive(Rows("1 exit_group(42 <unfinished ...>", "2 <... exit_group resumed>) = ?", "3 +++ exited with 42 +++"))[0].Text == "1 exit_group(42) = ?"));
        Reject("unsupported-exit-resume-return", () => ProcessView.Derive(Rows("1 exit_group(0 <unfinished ...>", "2 <... exit_group resumed>) = 0", "3 +++ exited with 0 +++")));
        Reject("exit-resume-code-invention", () => ProcessView.Derive(Rows("1 exit_group(42 <unfinished ...>", "2 <... exit_group resumed>0) = ?", "3 +++ exited with 0 +++")));
        Reject("activity-between-exit-entry-resume", () => ProcessView.Derive(Rows("1 exit_group(0 <unfinished ...>", "2 write(1, 0x123, 1) = 1", "3 +++ exited with 0 +++")));
        Reject("activity-between-exit-resume-terminal", () => ProcessView.Derive(Rows("1 exit_group(0 <unfinished ...>", "2 <... exit_group resumed>) = ?", "3 write(1, 0x123, 1) = 1", "4 +++ exited with 0 +++")));
        Reject("missing-terminal-after-exit-resume", () => ProcessView.Derive(Rows("1 exit_group(0 <unfinished ...>", "2 <... exit_group resumed>) = ?")));
        return result;
    }
    private static ProcessView.Source[] Rows(params string[] values)
    {
        long offset = 0;
        return values.Select(text => { byte[] bytes = Encoding.UTF8.GetBytes(text + "\n"); var source = new ProcessView.Source(offset, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)), text); offset += bytes.Length; return source; }).ToArray();
    }
    private static void Need(bool value) { if (!value) { throw new InvalidOperationException("Positive projection control failed."); } }
}
