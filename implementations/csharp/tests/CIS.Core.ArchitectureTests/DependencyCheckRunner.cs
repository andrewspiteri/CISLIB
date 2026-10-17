using CIS.TestSupport;

namespace CIS.Tests.ArchitectureTests;

internal static class DependencyCheckRunner
{
    internal static Task<ProcessResult> Run(string language, string root, string project, params string[] extra)
    {
        string assets = "-", assembly = "-", package = "-";
        for (int i = 0; i < extra.Length; i += 2)
        {
            switch (extra[i])
            {
                case "-AssetsPath": assets = extra[i + 1]; break;
                case "-AssemblyPath": assembly = extra[i + 1]; break;
                case "-PackagePath": package = extra[i + 1]; break;
                default: throw new ArgumentException("Unknown dependency fixture argument: " + extra[i]);
            }
        }
        string tool = Path.Combine(language, "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll");
        return ProcessRunner.RunAsync("dotnet", [tool, "dependencies", root, project, assets, assembly, package], root, TimeSpan.FromMinutes(1));
    }
}
