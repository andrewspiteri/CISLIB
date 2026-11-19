namespace CIS.Security.LocalDevelopment;

internal static class Program
{
    private static int Main(string[] args) => LocalDevelopmentCommand.Run(args, Console.Out, Console.Error);
}
