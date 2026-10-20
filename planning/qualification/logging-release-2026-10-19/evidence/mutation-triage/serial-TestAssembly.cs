using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace CIS.Tests;

[TestClass]
public sealed class TestAssemblySetup
{
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // An IDE can inherit only the machine PATH even when PowerShell is installed for the user.
        if (OperatingSystem.IsWindows())
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] locations =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps")
            ];
            foreach (string location in locations)
            {
                if (File.Exists(Path.Combine(location, "pwsh.exe")) && !path.Split(Path.PathSeparator).Contains(location, StringComparer.OrdinalIgnoreCase))
                {
                    path = path.Length == 0 ? location : path + Path.PathSeparator + location;
                }
            }
            Environment.SetEnvironmentVariable("PATH", path);
        }

        // Fixture builds must not depend on an IDE's shared compiler or reusable MSBuild nodes.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UseSharedCompilation")))
        {
            Environment.SetEnvironmentVariable("UseSharedCompilation", "false");
        }
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSBUILDDISABLENODEREUSE")))
        {
            Environment.SetEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1");
        }
    }
}
