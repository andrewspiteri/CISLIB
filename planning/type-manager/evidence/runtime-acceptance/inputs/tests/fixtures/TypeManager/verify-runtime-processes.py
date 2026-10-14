"""Build actual Core and real loader fixtures; each scenario runs in a fresh process.

Requires the repository's pinned SDK and restored development dependencies.
Generated version/reference variants and reports are confined to --output-root.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
from xml.sax.saxutils import escape


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--replay-root", type=Path, help="Replay frozen managed fixtures without rebuilding them")
    parser.add_argument("--expect-core-sha256", help="Require exact candidate bytes, particularly for cross-platform replay")
    args = parser.parse_args()
    language = args.repo_root.resolve() / "implementations/csharp"
    fixtures = language / "tests/fixtures/TypeManager/Runtime"
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error("Use an empty output root; stale fixture outputs must not affect a new run.")
    root.mkdir(parents=True, exist_ok=True)
    logs = root / "logs"
    logs.mkdir(exist_ok=True)
    # Replace the report before work begins, so an interrupted run cannot look successful.
    report = {"scope": "Actual Core development source; fresh processes, not package qualification",
              "complete": False, "verifierSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              "sourceProvenanceScope": "Core C# sources and five named project/config files; not all transitive MSBuild inputs",
              "results": []}

    def save():
        (root / "results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")

    def core_inputs():
        source = language / "src/CIS.Core"
        files = {str(path.relative_to(source)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
                 for path in sorted(source.rglob("*.cs"))
                 if not set(path.relative_to(source).parts).intersection({"bin", "obj"})}
        projects = {name: hashlib.sha256((language / name).read_bytes()).hexdigest()
                    for name in ["src/CIS.Core/CIS.Core.csproj", "global.json", "Directory.Build.props", "Directory.Packages.props", "NuGet.config"]}
        return files, projects

    def run(name, *arguments, expected_error=None):
        process = subprocess.run([args.dotnet, *map(str, arguments)], cwd=language,
                                 text=True, encoding="utf-8", errors="replace", stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (logs / (name + ".log")).write_text(process.stdout, encoding="utf-8")
        if expected_error:
            if process.returncode == 0 or expected_error not in process.stdout:
                raise RuntimeError(f"{name} did not reject the fixture: {logs / (name + '.log')}")
        elif process.returncode:
            raise RuntimeError(f"{name} failed: {logs / (name + '.log')}")
        return process.stdout

    save()
    report["sdk"] = run("sdk", "--version").strip()
    report["mode"] = "frozen-managed-binary-replay" if args.replay_root else "build-and-run"
    if args.replay_root:
        source_report = json.loads((args.replay_root / "results.json").read_text(encoding="utf-8-sig"))
        if not source_report.get("complete"):
            raise RuntimeError("Cannot replay an incomplete producer report")
        for key in ["coreSourceInputs", "coreProjectInputs", "sourceGitHead"]:
            report[key] = source_report[key]
        for name in ["Broken", "Forwarder", "Forwarded", "Host", "versions", "helper"]:
            shutil.copytree(args.replay_root.resolve() / name, root / name, dirs_exist_ok=True)
    else:
        report["coreSourceInputs"], report["coreProjectInputs"] = core_inputs()
        git_head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=language, text=True, capture_output=True)
        report["sourceGitHead"] = git_head.stdout.strip() if git_head.returncode == 0 else None
        for name in ["Broken", "Forwarder", "Host"]:
            output = root / name / "bin/Release/net10.0"
            run("build-" + name, "build", fixtures / name / (name + ".csproj"),
                "-c", "Release", "-m:1", "--no-incremental", "--nologo", "-o", output)
        forwarder = root / "Forwarder/bin/Release/net10.0"
        forwarded = root / "Forwarded/bin/Release/net10.0"
        forwarded.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(forwarder / "CIS.TypeManager.ForwardedFixture.dll", forwarded / "CIS.TypeManager.ForwardedFixture.dll")
        # Explicit standalone props keep generated fixture projects independent of parent directories.
        generated = root / "generated"
        generated.mkdir(exist_ok=True)
        (generated / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
        for variant, version in [("v1", "1.0.0.0"), ("v2", "2.0.0.0"), ("ref", "1.0.0.0"), ("self-reference", "1.0.0.0")]:
            directory = generated / variant
            directory.mkdir(exist_ok=True)
            properties = "<ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>" if variant == "ref" else ""
            (directory / "Fixture.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>
        <AssemblyName>CIS.TypeManager.ConflictFixture</AssemblyName><AssemblyVersion>{version}</AssemblyVersion>
        <TreatWarningsAsErrors>true</TreatWarningsAsErrors><IsPackable>false</IsPackable>{properties}
      </PropertyGroup>
    </Project>
    ''', encoding="utf-8")
            shutil.copyfile(fixtures / "Conflict/Collision.cs", directory / "Collision.cs")
            if variant == "self-reference":
                (directory / "ReferenceAttribute.cs").write_text('''#pragma warning disable CS0436 // Intentional local attribute constructor metadata fixture.
    [assembly: System.Runtime.CompilerServices.ReferenceAssembly]
    namespace System.Runtime.CompilerServices;
    [AttributeUsage(AttributeTargets.Assembly)]
    internal sealed class ReferenceAssemblyAttribute : Attribute;
    ''', encoding="utf-8")
            run("build-" + variant, "build", directory / "Fixture.csproj", "-c", "Release", "-m:1",
                "--nologo", "-o", root / "versions" / variant)
        if core_inputs() != (report["coreSourceInputs"], report["coreProjectInputs"]):
            raise RuntimeError("Core source inputs changed during the producer build")
    host = root / "Host/bin/Release/net10.0/CIS.TypeManager.RuntimeHost.dll"
    core = host.parent / "CIS.Core.dll"
    report["coreSha256"] = hashlib.sha256(core.read_bytes()).hexdigest()
    report["fixtureFiles"] = {
        str(path.relative_to(root)).replace("\\", "/"): hashlib.sha256(path.read_bytes()).hexdigest()
        for name in ["Broken", "Forwarder", "Forwarded", "Host", "versions"]
        for path in sorted((root / name).rglob("*")) if path.is_file()
    }
    if args.expect_core_sha256 and report["coreSha256"] != args.expect_core_sha256.lower():
        raise RuntimeError("Candidate Core SHA-256 does not match the required frozen bytes")
    if args.replay_root:
        original = json.loads((args.replay_root / "results.json").read_text(encoding="utf-8-sig"))
        if not original.get("complete") or report["coreSha256"] != original["coreSha256"]:
            raise RuntimeError("Replay source is incomplete or its Core bytes have changed")
        if report["verifierSha256"] != original.get("verifierSha256"):
            raise RuntimeError("Replay verifier differs from the completed build report")
        if report["fixtureFiles"] != original.get("fixtureFiles"):
            raise RuntimeError("Replay fixture bytes do not match the completed build report")
    modes = ["outside-catalog-late-load", "manifest-version-conflict", "manifest-version-conflict-reversed", "partial-types", "validation-before-load", "dynamic-name-collision", "foreign-resolver-result", "file-identity-conflict", "loaded-identity-conflict", "tpa-selected-newer-version",
             "reference-reuse", "self-defined-reference-reuse", "invalid-executable", "locationless-host", "forwarding-and-collisions"]
    for mode in modes:
        observed = json.loads(run(mode, host, mode, root, root / "deployments" / mode))
        if observed.get("success") is not True or observed.get("mode") != mode:
            raise RuntimeError(f"Invalid result for {mode}: {observed}")
        if observed["containerConstructed"] != (mode in ["forwarding-and-collisions", "dynamic-name-collision", "outside-catalog-late-load"]):
            raise RuntimeError(f"Incorrect container boundary for {mode}")
        report["results"].append(observed)
        save()
    # Both metadata encodings of ReferenceAssemblyAttribute must also fail generation.
    if not args.replay_root:
        run("build-helper", "build", language / "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj",
            "-c", "Release", "-m:1", "--nologo")
        shutil.copytree(language / "eng/CIS.TypeManager.Build/bin/Release/net10.0", root / "helper", dirs_exist_ok=True)
    helper = root / "helper/CIS.TypeManager.Build.dll"
    report["helperSha256"] = hashlib.sha256(helper.read_bytes()).hexdigest()
    if args.replay_root and report["helperSha256"] != original["helperSha256"]:
        raise RuntimeError("Replay build helper bytes have changed")
    task_project = root / "CheckReference.proj"
    task_project.write_text(f'''<Project>
  <UsingTask TaskName="CIS.TypeManagement.Build.WriteTypeInventory" TaskFactory="TaskHostFactory" Runtime="NET" AssemblyFile="{escape(str(helper))}" />
  <Target Name="Check">
    <WriteTypeInventory OutputDirectory="$(FixtureDirectory)" HostFileName="CIS.TypeManager.ConflictFixture.dll" Assets="@(Empty)" />
  </Target>
</Project>
''', encoding="utf-8")
    for variant in ["ref", "self-reference"]:
        run("generator-rejects-" + variant, "msbuild", task_project, "-t:Check", "-m:2", "-nologo",
            "-p:FixtureDirectory=" + str(root / "versions" / variant), expected_error="is a reference assembly")
        report["results"].append({"mode": "generator-rejects-" + variant, "success": True})
    report["complete"] = True
    save()
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
