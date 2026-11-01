using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CIS.Verification;

// A real native child for ProcessRunner's byte, exit and inherited-pipe controls.
internal static class ProcessCaptureFixture
{
    internal static int Run(string[] args)
    {
        switch (args[0])
        {
            case "environment":
                Console.Write(Environment.GetEnvironmentVariable(args[1]));
                return 0;
            case "text":
                Write(Console.OpenStandardOutput(), Convert.FromBase64String(args[1]));
                Write(Console.OpenStandardError(), Convert.FromBase64String(args[2]));
                return int.Parse(args[3], CultureInfo.InvariantCulture);
            case "repeat":
                Write(Console.OpenStandardOutput(), Encoding.UTF8.GetBytes(new string(args[1][0], int.Parse(args[2], CultureInfo.InvariantCulture)) + args[3]));
                Write(Console.OpenStandardError(), Encoding.UTF8.GetBytes(new string(args[4][0], int.Parse(args[5], CultureInfo.InvariantCulture)) + args[6]));
                return int.Parse(args[7], CultureInfo.InvariantCulture);
            case "wait":
                using (Stream output = Console.OpenStandardOutput())
                using (var wait = new ManualResetEvent(false))
                { output.Write(Encoding.UTF8.GetBytes(args[1])); output.Flush(); wait.WaitOne(); }
                return 0;
            case "inherited-child":
                using (Stream output = Console.OpenStandardOutput())
                using (var wait = new ManualResetEvent(false))
                {
                    output.Write("owned-child"u8);
                    output.Flush();
                    File.WriteAllText(Environment.GetEnvironmentVariable("CIS_CAPTURE_CHILD_READY")!, "ready");
                    wait.WaitOne();
                }
                return 0;
            case "inherited-parent":
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                foreach (string value in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
                    typeof(ProcessCaptureFixture).Assembly.Location, "process-capture-fixture", "inherited-child" })
                { start.ArgumentList.Add(value); }
                using (Process child = Process.Start(start)!)
                {
                    File.WriteAllText(Environment.GetEnvironmentVariable("CIS_CAPTURE_CHILD_PID")!, child.Id.ToString(CultureInfo.InvariantCulture));
                    var clock = Stopwatch.StartNew();
                    while (!File.Exists(Environment.GetEnvironmentVariable("CIS_CAPTURE_CHILD_READY")))
                    {
                        if (child.HasExited || clock.Elapsed.TotalSeconds > 3) { throw new InvalidOperationException("Owned child did not become ready"); }
                        Thread.Sleep(10);
                    }
                }
                return 0;
            default: throw new ArgumentException("Unknown process-capture fixture mode.");
        }
    }

    private static void Write(Stream stream, byte[] bytes)
    {
        using (stream) { stream.Write(bytes); stream.Flush(); }
    }
}
