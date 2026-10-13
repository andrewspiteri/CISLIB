"""Isolated proof of early NuGet props and packaged task hosting, not a CIS.Core package."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
import xml.sax.saxutils as xml
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("--repo-root", type=Path, required=True)
parser.add_argument("--output-root", type=Path, required=True)
parser.add_argument("--dotnet", default="dotnet")
args = parser.parse_args()
root = args.output_root.resolve()
root.mkdir(parents=True, exist_ok=True)
language = args.repo_root.resolve() / "implementations/csharp"

def write(path, value):
    target = root / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(value, encoding="utf-8")

def run(name, *arguments, expected_error=None):
    result = subprocess.run([args.dotnet, *arguments], cwd=root, text=True, capture_output=True)
    write("logs/" + name + ".log", result.stdout + result.stderr)
    if expected_error:
        if result.returncode == 0 or expected_error not in result.stdout + result.stderr:
            raise RuntimeError(f"{name} did not reject with {expected_error}")
    elif result.returncode != 0:
        raise RuntimeError(f"{name} failed: {result.stdout} {result.stderr}")
    return result.stdout.strip()

for name in ["global.json", "Directory.Build.props"]:
    shutil.copyfile(language / name, root / name)
for fixture in ["Contracts", "Transitive", "Unused"]:
    source = language / "tests/fixtures/TypeManager" / fixture
    for path in source.iterdir():
        if path.is_file() and path.suffix in [".cs", ".csproj", ".resx"]:
            write("fixtures/" + fixture + "/" + path.name, path.read_text(encoding="utf-8"))

targets = ET.parse(language / "build/type-manager/CIS.TypeManager.targets").getroot()
for group in targets.findall("PropertyGroup"):
    for element in list(group):
        if element.tag in ["UseCurrentRuntimeIdentifier", "_CisTypeManagerToolProject"]:
            group.remove(element)
        elif element.tag == "_CisTypeManagerToolAssembly":
            element.text = "$(MSBuildThisFileDirectory)../tools/net10.0/CIS.TypeManager.Build.dll"
for group in targets.findall("ItemGroup"):
    for item in group.findall("ProjectReference"):
        group.remove(item)
task = targets.find("UsingTask")
task.set("TaskFactory", "TaskHostFactory")
task.set("Runtime", "NET")
locator = next(target for target in targets.findall("Target") if target.get("Name") == "CisLocateTypeManagerTool")
for element in list(locator):
    if element.tag != "Error" or "missing" not in element.get("Text", ""):
        locator.remove(element)
# Experimental SDK-owned second restore, isolated from canonical integration.
restore = ET.SubElement(targets, "Target", {"Name": "CisRestoreSelectedRuntimeGraph", "BeforeTargets": "ResolvePackageAssets", "Condition": "'$(CisTypeManagerEnabled)' == 'true' and '$(DesignTimeBuild)' != 'true' and '$(_CisInventoryRestore)' != 'true'"})
ET.SubElement(restore, "MSBuild", {"Projects": "$(MSBuildProjectFullPath)", "Targets": "Restore", "Properties": "RuntimeIdentifier=$(RuntimeIdentifier);_CisInventoryRestore=true", "BuildInParallel": "false"})
props = """<Project><PropertyGroup>
<UseCurrentRuntimeIdentifier Condition="'$(CisTypeManagerEnabled)' == 'true' and '$(RuntimeIdentifier)' == '' and '$(UseCurrentRuntimeIdentifier)' == ''">true</UseCurrentRuntimeIdentifier>
</PropertyGroup></Project>"""
feed = root / "feed"
feed.mkdir(exist_ok=True)
with zipfile.ZipFile(feed / "CIS.TypeManager.BuildProof.1.0.0.nupkg", "w", zipfile.ZIP_DEFLATED) as package:
    package.writestr("CIS.TypeManager.BuildProof.nuspec", """<package><metadata><id>CIS.TypeManager.BuildProof</id><version>1.0.0</version><authors>CIS verification</authors><description>Isolated early-import proof only.</description><developmentDependency>true</developmentDependency></metadata></package>""")
    package.writestr("buildTransitive/CIS.TypeManager.BuildProof.props", props)
    package.writestr("buildTransitive/CIS.TypeManager.BuildProof.targets", ET.tostring(targets, encoding="utf-8"))
    package.write(language / "eng/CIS.TypeManager.Build/bin/Release/net10.0/CIS.TypeManager.Build.dll", "tools/net10.0/CIS.TypeManager.Build.dll")
write("NuGet.Config", f'<configuration><packageSources><clear/><add key="proof" value={xml.quoteattr(str(feed))}/></packageSources><config><add key="globalPackagesFolder" value={xml.quoteattr(str(root / "packages"))}/></config></configuration>')
early_props = '<Project><Import Project="../Directory.Build.props"/><PropertyGroup><CisTypeManagerEnabled>true</CisTypeManagerEnabled></PropertyGroup></Project>'
write("host/Directory.Build.props", early_props)
write("host/Host.csproj", """<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><OutputType>Exe</OutputType><AssemblyName>PackageImportHost</AssemblyName><UseAppHost>false</UseAppHost></PropertyGroup>
<ItemGroup><ProjectReference Include="../fixtures/Unused/Unused.csproj"/><PackageReference Include="CIS.TypeManager.BuildProof" Version="1.0.0" PrivateAssets="all"/></ItemGroup>
</Project>""")
write("host/Program.cs", 'System.Console.WriteLine("early-package-import-host");')
run("build", "build", "host/Host.csproj", "-c", "Release", "-m:1", "--nologo")
directory = Path(run("output", "msbuild", "host/Host.csproj", "-p:Configuration=Release", "-getProperty:TargetDir", "-nologo"))
results = []
for mode in ["build", "publish-no-build"]:
    output = directory if mode == "build" else root / "publish"
    if mode != "build":
        (output / "PackageImportHost.types.json").unlink(missing_ok=True)
        run(mode, "publish", "host/Host.csproj", "-c", "Release", "--no-build", "-o", str(output), "--nologo")
    manifest = json.loads((output / "PackageImportHost.types.json").read_text(encoding="utf-8"))
    expected = {"PackageImportHost", "CIS.TypeManager.Fixture.Contracts", "CIS.TypeManager.Fixture.Transitive", "CIS.TypeManager.Fixture.Unused"}
    observed = {item["assemblyName"].split(",")[0] for item in manifest["assemblies"]}
    if observed != expected or len(manifest["assemblies"]) != 4:
        raise RuntimeError("Incorrect packaged-task selection")
    if (output / "CIS.TypeManager.Build.dll").exists():
        raise RuntimeError("Build helper leaked into output")
    run(mode + "-host", str(output / "PackageImportHost.dll"))
    results.append({"mode": mode, "passed": True, "selected": sorted(observed)})
write("host/Directory.Build.props", '<Project><Import Project="../Directory.Build.props"/></Project>')
project_path = root / "host/Host.csproj"
project_path.write_text(project_path.read_text().replace("<OutputType>", "<CisTypeManagerEnabled>true</CisTypeManagerEnabled><OutputType>"), encoding="utf-8")
run("late-flag", "msbuild", "host/Host.csproj", "-t:CisValidateTypeManagerProfile", "-nologo", expected_error="CISTM004")
results.append({"mode": "late-flag-rejected", "passed": True})
write("results.json", json.dumps({"scope": "Build-only package import proof; not a CIS.Core package/release", "results": results}, indent=2))
print(json.dumps(results, indent=2))
