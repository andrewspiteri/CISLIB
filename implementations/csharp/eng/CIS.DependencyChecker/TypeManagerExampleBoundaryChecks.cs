using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerExampleBoundaryChecks
{
    private static readonly string[] Projects =
    [
        "examples/type-manager/Contracts/Contracts.csproj", "examples/type-manager/Unused/Unused.csproj",
        "examples/type-manager/Console/Console.csproj", "examples/type-manager/Web/Web.csproj",
        "tests/fixtures/TypeManager/ExampleLate/ExampleLate.csproj"
    ];

    internal static async Task<int> RunAsync(string[] args)
    {
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "language-root", "output-root"], []);
        string isolated = Path.TrimEndingDirectorySeparator(input.PathOf("language-root"));
        string[] files = [.. Projects, "src/CIS.Core/CIS.Core.csproj", "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj",
            "eng/project-boundaries.json", "eng/runtime-dependencies.json", "eng/development-dependencies.json",
            "global.json", "Directory.Build.props", "Directory.Packages.props"];
        if (File.Exists(Path.Combine(isolated, "Directory.Build.targets"))) { files = [.. files, "Directory.Build.targets"]; }
        ValidateIsolated(input.Language, isolated, files);
        input.EmptyOutput();
        foreach (string name in new[] { "project-boundaries.json", "runtime-dependencies.json", "development-dependencies.json" })
        { Copy(Path.Combine(input.Language, "eng", name), Path.Combine(isolated, "eng", name)); }
        var hashes = new JsonObject(files.Select(file => new KeyValuePair<string, JsonNode?>(file, JsonValue.Create(Hash(Path.Combine(isolated, file))))));
        string tool = typeof(TypeManagerExampleBoundaryChecks).Assembly.Location;
        var report = new JsonObject
        {
            ["complete"] = false,
            ["inputScope"] = "Checked project metadata, copied policy and named language configuration; not full transitive build provenance",
            ["inputs"] = hashes,
            ["verifierSha256"] = Hash(Path.Combine(input.Language, "eng/CIS.DependencyChecker/TypeManagerExampleBoundaryChecks.cs")),
            ["checkerSha256"] = Hash(Path.Combine(input.Language, "eng/check-dependencies.ps1")),
            ["nativeCheckerSha256"] = Hash(Path.Combine(input.Language, "eng/CIS.DependencyChecker/DependencyChecks.cs")),
            ["toolSha256"] = Hash(tool),
            ["results"] = new JsonArray()
        };
        string reportPath = Path.Combine(input.Output, "results.json");
        void Save() => WriteJson(reportPath, report);
        void Passed(string name, string? error = null)
        { report["results"]!.AsArray().Add(new JsonObject { ["case"] = name, ["passed"] = true, ["expectedError"] = error ?? "" }); Save(); }
        async Task Check(string name, string project, string? error = null)
        {
            await input.Run(name, [tool, "dependencies", isolated, project, "-", "-", "-"], isolated, expectedError: error);
            Passed(name, error);
        }
        void Framework(string path, bool implicitReference = false, string name = "Microsoft.AspNetCore.App")
        {
            XDocument document = XDocument.Load(path);
            var reference = new XElement("FrameworkReference", new XAttribute("Include", name));
            if (implicitReference) { reference.SetAttributeValue("IsImplicitlyDefined", "true"); }
            document.Root!.Add(new XElement("ItemGroup", reference));
            document.Save(path);
        }
        string Error(string project) => "Forbidden framework reference from " + project + ": Microsoft.AspNetCore.App";
        Save();
        try
        {
            foreach (string project in Projects) { await Check(Path.GetFileNameWithoutExtension(project), project); }
            foreach (string project in new[] { "src/CIS.Core/CIS.Core.csproj", "examples/type-manager/Console/Console.csproj" })
            {
                string path = Path.Combine(isolated, project);
                byte[] original = File.ReadAllBytes(path);
                try
                {
                    if (project == "src/CIS.Core/CIS.Core.csproj")
                    {
                        Framework(path, name: "Unapproved.Framework");
                        await Check("reject-framework-" + Path.GetFileNameWithoutExtension(project), project,
                            "Forbidden framework reference from " + project + ": Unapproved.Framework");
                    }
                    else { Framework(path); await Check("reject-framework-" + Path.GetFileNameWithoutExtension(project), project, Error(project)); }
                }
                finally { File.WriteAllBytes(path, original); }
            }
            string policyPath = Path.Combine(isolated, "eng/project-boundaries.json");
            byte[] originalPolicy = File.ReadAllBytes(policyPath);
            try
            {
                JsonObject policy = Read(policyPath);
                policy["projects"]!.AsArray().Single(row => (string?)row!["path"] == Projects[3])!.AsObject().Remove("additionalImplicitFrameworkReferences");
                WriteJson(policyPath, policy);
                await Check("reject-unregistered-web-framework", Projects[3], Error(Projects[3]));
            }
            finally { File.WriteAllBytes(policyPath, originalPolicy); }
            foreach (string project in new[] { "src/CIS.Core/CIS.Core.csproj", Projects[4] })
            {
                string path = Path.Combine(isolated, project);
                byte[] originalProject = File.ReadAllBytes(path);
                try
                {
                    JsonObject policy = Read(policyPath);
                    JsonObject projectRule = policy["projects"]!.AsArray().Single(row => (string?)row!["path"] == project)!.AsObject();
                    projectRule["additionalImplicitFrameworkReferences"] = new JsonArray("Microsoft.AspNetCore.App");
                    projectRule.Remove("additionalExplicitFrameworkReferences");
                    WriteJson(policyPath, policy);
                    XDocument document = XDocument.Load(path);
                    document.Descendants("FrameworkReference").Where(reference => (string?)reference.Attribute("Include") == "Microsoft.AspNetCore.App").Remove();
                    document.Save(path);
                    Framework(path, implicitReference: true);
                    await Check("reject-policy-" + Path.GetFileNameWithoutExtension(project), project, Error(project));
                    if (project == "src/CIS.Core/CIS.Core.csproj")
                    {
                        await input.Run("role-gate-mutant", [tool, "dependency-role-control", input.Language, isolated, project, input.Output], isolated);
                        Passed("role-gate-mutant-detected");
                    }
                }
                finally { File.WriteAllBytes(path, originalProject); File.WriteAllBytes(policyPath, originalPolicy); }
            }
            foreach (string file in files) { Require(Hash(Path.Combine(isolated, file)) == (string?)hashes[file], "Input was not restored: " + file); }
            Require(report["results"]!.AsArray().Count == 11, "Unexpected boundary check count.");
            report["complete"] = true;
            Console.WriteLine("Type Manager example boundary checks passed: 11.");
            return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }

    internal static void ValidateIsolated(string canonical, string isolated, string[] files)
    {
        canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(canonical));
        isolated = Path.TrimEndingDirectorySeparator(Path.GetFullPath(isolated));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Require(!isolated.Equals(canonical, comparison) && !isolated.StartsWith(canonical + Path.DirectorySeparatorChar, comparison),
            "Use an isolated example source tree outside the canonical language directory; this check temporarily mutates projects.");
        Require(Directory.Exists(isolated), "Isolated input directory is missing.");
        // Check both ancestors and every named descendant before copying or mutating.
        foreach (string start in files.Select(file => Path.Combine(isolated, file)).Prepend(isolated))
        {
            for (string? path = start; path is not null; path = Path.GetDirectoryName(path))
            {
                if (File.Exists(path) || Directory.Exists(path))
                { Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Isolated input uses a reparse point: " + path); }
            }
        }
    }
}
