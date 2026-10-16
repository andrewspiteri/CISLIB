namespace CIS.Tests.DiscoveryFixture;

// Writes bytes directly so process-capture tests do not pass through Console's text encoding.
internal static class ProcessStreamFixture
{
    internal static int Run(string stdout, string stderr)
    {
        using Stream output = Console.OpenStandardOutput(), error = Console.OpenStandardError();
        output.Write(Convert.FromBase64String(stdout));
        error.Write(Convert.FromBase64String(stderr));
        return 0;
    }

    internal static int Pressure()
    {
        using Stream output = Console.OpenStandardOutput(), error = Console.OpenStandardError();
        output.Write(Enumerable.Repeat((byte)'o', 1048576).ToArray());
        error.Write(Enumerable.Repeat((byte)'e', 1048576).ToArray());
        return 0;
    }
}
