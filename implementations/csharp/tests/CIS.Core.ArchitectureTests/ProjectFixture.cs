using CIS.TestSupport;

namespace CIS.Tests.ArchitectureTests;

internal sealed class ProjectFixture : IDisposable
{
    private readonly TemporaryDirectory directory = new();

    public ProjectFixture(string languageRoot)
    {
        string[] paths = [
            "global.json", "Directory.Build.props", "Directory.Packages.props",
            "src/CIS.Core/CIS.Core.csproj", "eng/project-boundaries.json",
            "eng/runtime-dependencies.json", "eng/development-dependencies.json",
            "build/type-manager/package/CIS.TypeManager.Pack.targets"
        ];
        foreach (string relative in paths)
        {
            string destination = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            File.Copy(System.IO.Path.Combine(languageRoot, relative), destination);
        }
    }

    public string Path => directory.Path;

    public void Dispose() => directory.Dispose();
}
