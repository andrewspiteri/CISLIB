"""Compile real RID/package fixtures in an isolated workspace and inspect SDK output.

No test package or build helper is added to shipping runtime dependencies.
All generated files and command logs remain under --output-root.
"""
import argparse
import hashlib
import json
import os
import platform
from pathlib import Path
import shutil
import subprocess
import sys
import xml.sax.saxutils as xml
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo-root", type=Path, required=True)
    parser.add_argument("--output-root", type=Path, required=True)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--build-targets", type=Path)
    parser.add_argument("--host-sdk", choices=["console", "web"], default="console")
    parser.add_argument("--configuration", choices=["Debug", "Release"], default="Release")
    parser.add_argument("--native-windows", type=Path, required=True)
    parser.add_argument("--native-linux", type=Path, required=True)
    args = parser.parse_args()
    if not __debug__:
        parser.error("Run this verification without Python optimization; its assertions must remain enabled.")
    if platform.machine().lower() not in ("amd64", "x86_64") or sys.platform not in ("win32", "linux"):
        parser.error("This matrix supports Windows x64 and Linux x64 only.")
    for native_asset in [args.native_windows, args.native_linux]:
        if not native_asset.is_file():
            parser.error(f"Native fixture does not exist: {native_asset}")
    root = args.output_root.resolve()
    root.mkdir(parents=True, exist_ok=True)
    language = args.repo_root.resolve() / "implementations/csharp"
    workspace = root / "workspace"
    workspace.mkdir(exist_ok=True)
    logs = root / "logs"
    logs.mkdir(exist_ok=True)
    results = []
    (root / "results.json").write_text(json.dumps({"complete": False, "results": []}), encoding="utf-8")

    def write(relative, content):
        path = workspace / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def command(name, *arguments, required=True):
        process = subprocess.run([args.dotnet, *arguments], cwd=workspace,
                                 text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        (logs / (name + ".log")).write_text(process.stdout + process.stderr, encoding="utf-8")
        if required and process.returncode:
            raise RuntimeError(f"{name} failed ({process.returncode}); see {logs / (name + '.log')}")
        return process

    for relative in ["global.json", "Directory.Build.props",
                     "build/type-manager/CIS.TypeManager.targets",
                     "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj",
                     "eng/CIS.TypeManager.Build/WriteTypeInventory.cs"]:
        destination = workspace / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(language / relative, destination)
    if args.build_targets:
        shutil.copyfile(args.build_targets, workspace / "build/type-manager/CIS.TypeManager.targets")
    for fixture in ["Contracts", "Transitive", "Unused"]:
        source = language / "tests/fixtures/TypeManager" / fixture
        for path in source.iterdir():
            if path.is_file() and path.suffix in [".cs", ".csproj", ".resx"]:
                destination = workspace / "fixtures" / fixture / path.name
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(path, destination)

    write("asset/Asset.csproj", """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CIS.TypeManager.RidAsset</AssemblyName>
    <DefineConstants>$(DefineConstants);$(FixtureVariant)</DefineConstants>
  </PropertyGroup>
</Project>
""")
    write("asset/Variant.cs", """namespace InventoryFixture;
public static class Variant
{
#if WINDOWS_ASSET
    public static string Value => "win-x64";
#elif LINUX_ASSET
    public static string Value => "linux-x64";
#else
    public static string Value => "neutral";
#endif
}
""")
    for variant, define in [("neutral", "NEUTRAL_ASSET"), ("win-x64", "WINDOWS_ASSET"), ("linux-x64", "LINUX_ASSET")]:
        command("compile-" + variant, "build", "asset/Asset.csproj", "-c", "Release", "-m:1",
                "--nologo", "-p:FixtureVariant=" + define, "-o", str(root / "variants" / variant))

    feed = root / "feed"
    feed.mkdir(exist_ok=True)
    package_path = feed / "CIS.TypeManager.RidFixture.1.0.0.nupkg"
    with zipfile.ZipFile(package_path, "w", zipfile.ZIP_DEFLATED) as package:
        package.writestr("CIS.TypeManager.RidFixture.nuspec", """<?xml version="1.0"?>
<package><metadata><id>CIS.TypeManager.RidFixture</id><version>1.0.0</version>
<authors>CIS inventory tests</authors><description>Local verification-only fixture.</description>
<dependencies><group targetFramework="net10.0" /></dependencies></metadata></package>""")
        package.write(workspace / "asset/obj/Release/net10.0/ref/CIS.TypeManager.RidAsset.dll",
                      "ref/net10.0/CIS.TypeManager.RidAsset.dll")
        for variant in ["neutral", "win-x64", "linux-x64"]:
            folder = "lib/net10.0" if variant == "neutral" else f"runtimes/{variant}/lib/net10.0"
            package.write(root / "variants" / variant / "CIS.TypeManager.RidAsset.dll",
                          folder + "/CIS.TypeManager.RidAsset.dll")
        package.write(args.native_windows, "runtimes/win-x64/native/cistm-native.dll")
        package.write(args.native_linux, "runtimes/linux-x64/native/libcistm-native.so")
        package.write(args.native_linux, "runtimes/linux-x64/native/cistm-native.dll")

    # Content-keyed private cache avoids stale bytes when a caller repeats the same output root.
    package_hash = hashlib.sha256(package_path.read_bytes()).hexdigest()
    write("NuGet.Config", f"""<configuration>
<packageSources><clear/><add key="fixture" value="{xml.escape(str(feed))}" /></packageSources>
<config><add key="globalPackagesFolder" value="{xml.escape(str(root / 'packages' / package_hash))}" /></config>
</configuration>""")
    write("host/Host.csproj", """<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup>
  <OutputType>Exe</OutputType><AssemblyName>InventoryMatrixHost</AssemblyName>
  <UseAppHost>false</UseAppHost><CisTypeManagerEnabled>true</CisTypeManagerEnabled>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="../fixtures/Unused/Unused.csproj" />
  <PackageReference Include="CIS.TypeManager.RidFixture" Version="1.0.0" />
  <Content Include="decoy.txt" Link="content-decoy.dll" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
<Import Project="../build/type-manager/CIS.TypeManager.targets" />
<Target Name="CaptureInventorySelections" AfterTargets="CisWriteBuildTypeInventory">
  <WriteLinesToFile File="$(TargetDir)sdk-runtime-items.txt" Overwrite="true"
    Lines="@(ReferenceCopyLocalPaths->'%(Identity)|%(AssetType)|%(DestinationSubDirectory)|%(NuGetPackageId)|%(RuntimeIdentifier)')" />
</Target>
</Project>
""")
    if args.host_sdk == "web":
        project = workspace / "host/Host.csproj"
        project.write_text(project.read_text(encoding="utf-8").replace('Sdk="Microsoft.NET.Sdk"', 'Sdk="Microsoft.NET.Sdk.Web"'), encoding="utf-8")
        write("host/Properties/PublishProfiles/Folder.pubxml", """<Project>
  <PropertyGroup><WebPublishMethod>FileSystem</WebPublishMethod><DeleteExistingFiles>false</DeleteExistingFiles></PropertyGroup>
</Project>
""")
    write("host/decoy.txt", "Ordinary content with a DLL destination is not a runtime asset.\n")
    write("host/Program.cs", """using System.Reflection;
using System.Text.Json;
var initiallyLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CIS.TypeManager.RidAsset");
var value = Assembly.Load("CIS.TypeManager.RidAsset").GetType("InventoryFixture.Variant")!
    .GetProperty("Value")!.GetValue(null);
Console.WriteLine(JsonSerializer.Serialize(new { initiallyLoaded, variant = value }));
return initiallyLoaded ? 1 : 0;
""")

    native_rid = "win-x64" if os.name == "nt" else "linux-x64"
    cross_rid = "linux-x64" if native_rid == "win-x64" else "win-x64"
    for rid in ["default", native_rid, cross_rid]:
        selected_rid = native_rid if rid == "default" else rid
        properties = [] if rid == "default" else ["-p:RuntimeIdentifier=" + rid, "-p:SelfContained=false"]
        evaluated = command("output-path-" + rid, "msbuild", "host/Host.csproj", "-p:Configuration=" + args.configuration,
                            "-getProperty:TargetDir", *properties)
        output = Path(evaluated.stdout.strip())
        # A prior mode or run must never satisfy the generation assertion.
        (output / "InventoryMatrixHost.types.json").unlink(missing_ok=True)
        build = command("build-" + rid, "build", "host/Host.csproj", "-c", args.configuration, "-m:1",
                        "--nologo", *properties, required=False)
        if build.returncode:
            results.append({"case": rid, "passed": False, "phase": "build", "exitCode": build.returncode})
            continue
        try:
            for mode in ["build", "publish-no-build", "publish-build"]:
                directory = output
                if mode != "build":
                    directory = root / "published" / rid / mode
                    (directory / "InventoryMatrixHost.types.json").unlink(missing_ok=True)
                    publish_arguments = ["publish", "host/Host.csproj", "-c", args.configuration, "-m:1", "--nologo",
                                         "-o", str(directory), *properties]
                    if args.host_sdk == "web":
                        publish_arguments.append("-p:PublishProfile=Folder")
                    if mode == "publish-no-build":
                        publish_arguments.append("--no-build")
                    command(mode + "-" + rid, *publish_arguments)
                manifest_path = directory / "InventoryMatrixHost.types.json"
                assert manifest_path.is_file(), "The current command did not generate its inventory"
                manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
                selected = {item["assemblyName"].split(",")[0]: item for item in manifest["assemblies"]}
                native_name = "cistm-native.dll" if selected_rid == "win-x64" else "libcistm-native.so"
                assert (directory / native_name).is_file(), "The native exclusion fixture was not deployed"
                assert (directory / "cistm-native.dll").is_file(), "The native DLL classification fixture was not deployed"
                assert (directory / "fr/CIS.TypeManager.Fixture.Unused.resources.dll").is_file(), "The satellite was not deployed"
                assert (directory / "content-decoy.dll").is_file(), "The unrelated DLL content was not deployed"
                expected = {"InventoryMatrixHost", "CIS.TypeManager.RidAsset", "CIS.TypeManager.Fixture.Contracts",
                            "CIS.TypeManager.Fixture.Transitive", "CIS.TypeManager.Fixture.Unused"}
                assert set(selected) == expected, selected
                assert manifest["schemaVersion"] == 1
                assert len(manifest["assemblies"]) == len(expected), "Duplicate manifest entries"
                assert manifest["hostAssembly"] == selected["InventoryMatrixHost"]["assemblyName"]
                identities = [item["assemblyName"] for item in manifest["assemblies"]]
                assert identities == sorted(identities), "Manifest order is not ordinal"
                chosen_asset = directory / selected["CIS.TypeManager.RidAsset"]["relativePath"]
                expected_asset = root / "variants" / selected_rid / "CIS.TypeManager.RidAsset.dll"
                assert hashlib.sha256(chosen_asset.read_bytes()).digest() == hashlib.sha256(expected_asset.read_bytes()).digest()
                observed = None
                if selected_rid == native_rid:
                    process = command(mode + "-run-" + rid, str(directory / "InventoryMatrixHost.dll"))
                    observed = json.loads(process.stdout)
                    assert observed == {"initiallyLoaded": False, "variant": native_rid}, observed
                shutil.copyfile(manifest_path, root / (mode + "-" + rid + ".types.json"))
                results.append({"case": rid, "mode": mode, "passed": True, "assemblyCount": len(selected),
                                "runtime": observed})
        except (RuntimeError, AssertionError, OSError, ValueError, KeyError) as error:
            results.append({"case": rid, "passed": False, "error": str(error)})
    report = {"complete": len(results) == 9 and all(result["passed"] for result in results),
              "hostSdk": args.host_sdk, "configuration": args.configuration, "nativeRid": native_rid, "sdk": command("sdk-version", "--version").stdout.strip(),
              "publishScope": "dotnet CLI with explicit -o; PublishUrl/Visual Studio profile copy is not qualified",
              "packageSha256": package_hash, "results": results}
    (root / "results.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0 if all(result["passed"] for result in results) else 1


if __name__ == "__main__":
    sys.exit(main())
