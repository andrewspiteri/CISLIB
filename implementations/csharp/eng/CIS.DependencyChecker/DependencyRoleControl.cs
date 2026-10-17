using System.Security.Cryptography;
using System.Text.Json;
using CIS.TestSupport;

namespace CIS.Verification;

// Compile the actual checker with only its role guard removed. No test-only policy switch.
internal static class DependencyRoleControl
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length != 4) { throw new ArgumentException("Usage: dependency-role-control <canonical-language> <isolated-language> <project> <output-root>"); }
            string canonical = Path.GetFullPath(args[0]), isolated = Path.GetFullPath(args[1]);
            string root = Path.Combine(Path.GetFullPath(args[3]), "role-gate-mutant");
            Require(!Directory.Exists(root), "Use a new role-gate mutant output directory.");
            string path = Path.Combine(canonical, "eng/CIS.DependencyChecker/DependencyChecks.cs");
            string source = File.ReadAllText(path);
            const string anchor = "role == \"example\" && ";
            Require(source.IndexOf(anchor, StringComparison.Ordinal) >= 0 && source.IndexOf(anchor, StringComparison.Ordinal) == source.LastIndexOf(anchor, StringComparison.Ordinal), "Role-gate mutation anchor is not unique.");
            Directory.CreateDirectory(root);
            File.Copy(Path.Combine(canonical, "global.json"), Path.Combine(root, "global.json"));
            File.Copy(Path.Combine(canonical, "NuGet.config"), Path.Combine(root, "NuGet.config"));
            string changed = Path.Combine(root, "DependencyChecks.cs");
            File.WriteAllText(changed, source.Replace(anchor, "", StringComparison.Ordinal));
            foreach (string name in new[] { "ProcessRunner", "AssemblyReferences" })
            { File.Copy(Path.Combine(canonical, "tests/CIS.Core.TestSupport", name + ".cs"), Path.Combine(root, name + ".cs")); }
            File.WriteAllText(Path.Combine(root, "Program.cs"), "return await CIS.Verification.DependencyChecks.RunAsync(args);");
            File.WriteAllText(Path.Combine(root, "Mutant.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsPackable>false</IsPackable>
                  </PropertyGroup>
                </Project>
                """);
            ProcessResult build = await ProcessRunner.RunAsync("dotnet", ["build", "Mutant.csproj", "-c", "Release", "--nologo", "-v", "quiet"], root, TimeSpan.FromMinutes(2));
            File.WriteAllText(Path.Combine(root, "build.log"), build.StandardOutput + build.StandardError);
            Require(build.ExitCode == 0, "Role-gate mutant did not compile. See its build.log.");
            ProcessResult result = await ProcessRunner.RunAsync("dotnet", [Path.Combine(root, "bin/Release/net10.0/Mutant.dll"), isolated, args[2], "-", "-", "-"], isolated, TimeSpan.FromMinutes(1));
            File.WriteAllText(Path.Combine(root, "check.log"), result.StandardOutput + result.StandardError);
            Require(result.ExitCode == 0, "Role-gate control is masked by another rejection; inspect the mutant log.");
            Console.WriteLine(JsonSerializer.Serialize(new { roleGuardMutationObserved = true, sourceSha256 = Hash(path), mutantSourceSha256 = Hash(changed) }));
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException or System.ComponentModel.Win32Exception)
        { Console.Error.WriteLine(error.Message); return 1; }
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool condition, string detail) { if (!condition) { throw new InvalidDataException(detail); } }
}
