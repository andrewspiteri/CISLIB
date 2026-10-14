"""Verify an exact local CIS.Core package in clean SDK-style consumers.

The private feed/cache and all generated hosts stay under an empty output root.
The RID fixture is a test package, never a CIS.Core runtime dependency.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
import xml.sax.saxutils as xml
import zipfile


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo-root', type=Path, required=True)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--dependency-cache', type=Path, required=True)
    parser.add_argument('--rid-package', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    if platform.machine().lower() not in ('amd64', 'x86_64') or sys.platform not in ('win32', 'linux'):
        parser.error('Supported lanes are Windows x64 and Linux x64')
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output root')
    root.mkdir(parents=True, exist_ok=True)
    language = args.repo_root.resolve() / 'implementations/csharp'
    native_rid = 'win-x64' if sys.platform == 'win32' else 'linux-x64'
    cross_rid = 'linux-x64' if native_rid == 'win-x64' else 'win-x64'
    report = {'complete': False, 'nativeRid': native_rid, 'packageSha256': digest(args.package),
              'verifierSha256': digest(__file__), 'results': []}
    report_path = root / 'results.json'
    report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    environment = dict(os.environ, NUGET_PACKAGES=str(root / 'packages'),
                       NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), DOTNET_CLI_HOME=str(root / 'dotnet-home'),
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', MSBUILDDISABLENODEREUSE='1',
                       DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')

    def write(relative, content):
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')

    def run(name, *arguments, expected_error=None):
        result = subprocess.run([args.dotnet, *arguments], cwd=root, env=environment,
                                text=True, capture_output=True)
        log = result.stdout + result.stderr
        write('logs/' + name + '.log', log)
        if expected_error:
            if result.returncode == 0 or expected_error not in log:
                raise RuntimeError(name + ' did not fail for ' + expected_error)
        elif result.returncode:
            raise RuntimeError(name + ' failed; inspect its log')
        return result.stdout.strip()

    def passed(name, **details):
        report['results'].append({'case': name, 'passed': True, **details})
        report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')

    with zipfile.ZipFile(args.package) as package:
        nuspec = ET.fromstring(package.read('CIS.Core.nuspec'))
        version = nuspec.find('.//{*}version').text
        core_hash = hashlib.sha256(package.read('lib/net10.0/CIS.Core.dll')).hexdigest()
        helper_hash = hashlib.sha256(package.read('tools/net10.0/CIS.TypeManager.Build.dll')).hexdigest()
    with zipfile.ZipFile(args.rid_package) as fixture:
        variant_hashes = {rid: hashlib.sha256(fixture.read('runtimes/' + rid + '/lib/net10.0/CIS.TypeManager.RidAsset.dll')).hexdigest()
                          for rid in ('win-x64', 'linux-x64')}
    report.update(packageVersion=version, coreSha256=core_hash, helperSha256=helper_hash,
                  ridPackageSha256=digest(args.rid_package))
    shutil.copyfile(language / 'global.json', root / 'global.json')
    write('Directory.Build.props', '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup></Project>')
    write('BeforeExisting.targets', '<Project><PropertyGroup><ExistingHookSeen>true</ExistingHookSeen></PropertyGroup></Project>')
    props_path = root / 'Directory.Build.props'
    props = ET.parse(props_path).getroot()
    ET.SubElement(ET.SubElement(props, 'PropertyGroup'), 'BeforeMicrosoftNETSdkTargets').text = str(root / 'BeforeExisting.targets')
    props_path.write_bytes(ET.tostring(props, encoding='utf-8'))
    for fixture in ['Contracts', 'Transitive', 'Unused']:
        for path in (language / 'tests/fixtures/TypeManager' / fixture).iterdir():
            if path.is_file() and path.suffix in ['.cs', '.csproj', '.resx']:
                write('fixtures/' + fixture + '/' + path.name, path.read_text(encoding='utf-8'))
    unused = root / 'fixtures/Unused/Unused.csproj'
    unused.write_text(unused.read_text(encoding='utf-8').replace('</Project>', f'<ItemGroup><PackageReference Include="CIS.Core" Version="[{version}]" /></ItemGroup></Project>'), encoding='utf-8')
    feed = root / 'feed'
    feed.mkdir()
    shutil.copyfile(args.package, feed / ('CIS.Core.' + version + '.nupkg'))
    shutil.copyfile(args.rid_package, feed / 'CIS.TypeManager.RidFixture.1.0.0.nupkg')
    for name in ['microsoft.extensions.logging.abstractions', 'microsoft.extensions.dependencyinjection.abstractions']:
        source = args.dependency_cache / name / '10.0.12' / (name + '.10.0.12.nupkg')
        shutil.copyfile(source, feed / source.name)
    write('NuGet.Config', '<configuration><packageSources><clear/><add key="local" value=' +
          xml.quoteattr(str(feed)) + '/></packageSources></configuration>')

    program = '''using CIS.TypeManagement;
using CIS.TypeManagement.Fixtures.Contracts;
using System.Text.Json;
if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CIS.TypeManager.Fixture.Unused"))
{
    throw new InvalidOperationException("The unused discovery library was preloaded.");
}
var manager = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly)
    { PrecomputedContracts = [typeof(IDiscoveryMarker)] });
var implementation = manager.GetImplementations<IDiscoveryMarker>().Single();
if (implementation.FullName != "CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings")
{
    throw new InvalidOperationException("Incorrect discovered implementation.");
}
var variant = manager.Types.Single(t => t.FullName == "InventoryFixture.Variant");
Console.WriteLine(JsonSerializer.Serialize(new { selected = manager.Assemblies.Count,
    implementation = implementation.FullName, variant = (string)variant.GetProperty("Value")!.GetValue(null)! }));
'''

    def host(name, sdk='Microsoft.NET.Sdk', central=False, ordinary=False, duplicate=False):
        sdk_reference = '' if ordinary else f'<Sdk Name="CIS.Core" Version="{version}" />'
        core_reference = f'<PackageReference Include="CIS.Core" Version="{version}" />' if ordinary or duplicate else ''
        fixture_version = '' if central else ' Version="1.0.0"'
        write(name + '/Host.csproj', f'''<Project Sdk="{sdk}">
  {sdk_reference}
  <PropertyGroup><OutputType>Exe</OutputType><AssemblyName>TypeManagerPackageHost</AssemblyName><UseAppHost>false</UseAppHost><CisTypeManagerEnabled>true</CisTypeManagerEnabled></PropertyGroup>
  <ItemGroup><ProjectReference Include="../fixtures/Unused/Unused.csproj" />
    <PackageReference Include="CIS.TypeManager.RidFixture"{fixture_version} />{core_reference}
  </ItemGroup>
</Project>''')
        write(name + '/Program.cs', program)
        if central:
            write(name + '/Directory.Packages.props', '<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include="CIS.TypeManager.RidFixture" Version="1.0.0" /></ItemGroup></Project>')

    def property_value(name, key, *extra):
        return run(name + '-property-' + key, 'msbuild', name + '/Host.csproj', '-p:Configuration=Release',
                   '-getProperty:' + key, '-nologo', *extra)

    def validate(name, mode, directory, rid, execute=True):
        manifest_path = directory / 'TypeManagerPackageHost.types.json'
        if not manifest_path.is_file():
            raise RuntimeError(name + '/' + mode + ' did not produce a current manifest')
        manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
        entries = {item['assemblyName'].split(',')[0]: item for item in manifest['assemblies']}
        expected = {'TypeManagerPackageHost', 'CIS.Core', 'Microsoft.Extensions.Logging.Abstractions',
                    'Microsoft.Extensions.DependencyInjection.Abstractions', 'CIS.TypeManager.Fixture.Contracts',
                    'CIS.TypeManager.Fixture.Transitive', 'CIS.TypeManager.Fixture.Unused', 'CIS.TypeManager.RidAsset'}
        # ASP.NET supplies these from its shared framework, outside the deployed
        # application inventory; console consumers deploy them from the package.
        if name == 'web':
            expected -= {'Microsoft.Extensions.Logging.Abstractions',
                         'Microsoft.Extensions.DependencyInjection.Abstractions'}
        if set(entries) != expected or len(manifest['assemblies']) != len(expected) or manifest['schemaVersion'] != 1:
            raise RuntimeError('Incorrect package consumer inventory: ' + str(entries))
        if manifest['hostAssembly'] != entries['TypeManagerPackageHost']['assemblyName']:
            raise RuntimeError('Incorrect manifest host identity')
        if digest(directory / 'CIS.Core.dll') != core_hash:
            raise RuntimeError('Consumer is not using the exact packed Core DLL')
        if digest(root / 'packages/cis.core' / version.lower() / 'tools/net10.0/CIS.TypeManager.Build.dll') != helper_hash:
            raise RuntimeError('Consumer is not using the exact packed inventory helper')
        if digest(directory / entries['CIS.TypeManager.RidAsset']['relativePath']) != variant_hashes[rid]:
            raise RuntimeError('Wrong RID-selected managed asset')
        if not (directory / 'cistm-native.dll').is_file():
            raise RuntimeError('Native exclusion fixture was not deployed')
        if not (directory / 'fr/CIS.TypeManager.Fixture.Unused.resources.dll').is_file():
            raise RuntimeError('Satellite exclusion fixture was not deployed')
        deps = json.loads((directory / 'TypeManagerPackageHost.deps.json').read_text(encoding='utf-8'))
        if any('CIS.TypeManager.Build' in key or 'Microsoft.Build' in key for key in deps['libraries']):
            raise RuntimeError('Build tooling leaked into runtime dependencies')
        if (directory / 'CIS.TypeManager.Build.dll').exists():
            raise RuntimeError('Build helper leaked into deployment')
        runtime = None
        if execute:
            runtime = json.loads(run(name + '-' + mode + '-run', str(directory / 'TypeManagerPackageHost.dll')))
            if runtime != {'selected': len(expected), 'implementation': 'CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings', 'variant': rid}:
                raise RuntimeError('Unexpected prepared runtime catalog')
        shutil.copyfile(manifest_path, root / (name + '-' + mode + '.types.json'))
        passed(name + '-' + mode, selected=sorted(entries), rid=rid, runtime=runtime)

    try:
        report['sdk'] = run('sdk-version', '--version')
        if report['sdk'] != json.loads((root / 'global.json').read_text(encoding='utf-8-sig'))['sdk']['version']:
            raise RuntimeError('Consumer SDK differs from the pinned version')
        host('console')
        host('web', 'Microsoft.NET.Sdk.Web')
        write('Consumers.slnx', '<Solution><Folder Name="/console/"><Project Path="console/Host.csproj" /></Folder><Folder Name="/web/"><Project Path="web/Host.csproj" /></Folder><Folder Name="/fixtures/"><Project Path="fixtures/Contracts/Contracts.csproj" /><Project Path="fixtures/Transitive/Transitive.csproj" /><Project Path="fixtures/Unused/Unused.csproj" /></Folder></Solution>')
        run('parallel-clean-restore-build', 'build', 'Consumers.slnx', '-c', 'Release', '-p:CisTypeManagerEnabled=true', '-m:2', '--nologo')
        for name in ['console', 'web']:
            if property_value(name, 'NETCoreSdkRuntimeIdentifier') != native_rid:
                raise RuntimeError('SDK native RID differs from the qualification lane')
            validate(name, 'parallel-clean-build', Path(property_value(name, 'TargetDir')), native_rid)
        for name, sdk in [('console', 'Microsoft.NET.Sdk'), ('web', 'Microsoft.NET.Sdk.Web')]:
            project = name + '/Host.csproj'
            (Path(property_value(name, 'TargetDir')) / 'TypeManagerPackageHost.types.json').unlink()
            run(name + '-build', 'build', project, '-c', 'Release', '-m:1', '--nologo')
            directory = Path(property_value(name, 'TargetDir'))
            validate(name, 'build', directory, native_rid)
            if property_value(name, 'ExistingHookSeen') != 'true':
                raise RuntimeError('SDK import replaced an existing early SDK hook')
            passed(name + '-preserves-existing-sdk-hook')
            manifest = directory / 'TypeManagerPackageHost.types.json'
            before = (digest(manifest), manifest.stat().st_mtime_ns)
            assets = root / name / 'obj/project.assets.json'
            before_assets = (digest(assets), assets.stat().st_mtime_ns)
            run(name + '-no-restore', 'build', project, '-c', 'Release', '--no-restore', '-m:1', '--nologo')
            if before != (digest(manifest), manifest.stat().st_mtime_ns) or before_assets != (digest(assets), assets.stat().st_mtime_ns):
                raise RuntimeError('Incremental no-restore build rewrote unchanged assets or manifest')
            passed(name + '-incremental-no-restore-unchanged')
            relative = property_value(name, 'PublishDir').replace('\\', '/')
            published = (root / name / relative).resolve()
            (published / manifest.name).unlink(missing_ok=True)
            run(name + '-publish-no-build', 'publish', project, '-c', 'Release', '--no-build', '--no-restore', '-m:1', '--nologo')
            validate(name, 'publish-no-build', published, native_rid)
            cross_output = root / 'published' / name / cross_rid
            run(name + '-publish-cross', 'publish', project, '-c', 'Release', '-r', cross_rid, '-o', str(cross_output), '-m:1', '--nologo')
            validate(name, 'publish-cross', cross_output, cross_rid, execute=False)
            run(name + '-cross-then-native-no-restore', 'build', project, '-c', 'Release', '--no-restore', '-m:1', '--nologo', expected_error='NETSDK1047')
            passed(name + '-cross-rid-requires-native-restore')
            # The SDK import infers the native RID during ordinary restore too.
            # CLI restore -r sets RuntimeIdentifiers and requests extra framework
            # runtime packs, which are intentionally absent from this private feed.
            run(name + '-restore-native', 'restore', project, '--nologo')

        # Parallel solution build with already-restored native assets.
        for name in ['console', 'web']:
            (Path(property_value(name, 'TargetDir')) / 'TypeManagerPackageHost.types.json').unlink()
        run('parallel-no-restore', 'build', 'Consumers.slnx', '-c', 'Release', '--no-restore', '-m:2', '--nologo')
        for name in ['console', 'web']:
            validate(name, 'parallel-no-restore', Path(property_value(name, 'TargetDir')), native_rid)
        # Both paths are fixed descendants of the private proof workspace.
        offline_feed = root / 'offline-feed'
        if feed.resolve().parent != root or offline_feed.resolve().parent != root:
            raise RuntimeError('Offline feed move escaped its workspace')
        feed.rename(offline_feed)
        try:
            locks = [root / name / 'packages.lock.json' for name in ['console', 'web', 'fixtures/Unused', 'fixtures/Transitive', 'fixtures/Contracts']]
            if not all(path.is_file() for path in locks):
                raise RuntimeError('Offline qualification requires existing lock files')
            lock_hashes = [digest(path) for path in locks]
            run('offline-no-restore', 'build', 'Consumers.slnx', '-c', 'Release', '--no-restore', '-m:2', '--nologo')
            run('offline-locked-restore', 'restore', 'Consumers.slnx', '--force', '--locked-mode', '--nologo')
            if lock_hashes != [digest(path) for path in locks]:
                raise RuntimeError('Offline locked restore changed a lock file')
        finally:
            offline_feed.rename(feed)
        passed('offline-no-restore-and-locked-restore')

        host('central', central=True)
        run('central-build', 'build', 'central/Host.csproj', '-c', 'Release', '-m:1', '--nologo')
        validate('central', 'build', Path(property_value('central', 'TargetDir')), native_rid)
        central = root / 'central/Directory.Packages.props'
        central_text = central.read_text(encoding='utf-8')
        central.write_text(central_text.replace('</ItemGroup>', f'<PackageVersion Include="CIS.Core" Version="{version}" /></ItemGroup>'), encoding='utf-8')
        evaluated = json.loads(run('central-exact-version', 'msbuild', 'central/Host.csproj', '-getItem:PackageVersion', '-nologo'))
        core_versions = [item['Version'] for item in evaluated['Items']['PackageVersion'] if item['Identity'].lower() == 'cis.core']
        if core_versions != ['[' + version + ']']:
            raise RuntimeError('Matching central version is not constrained to the exact SDK runtime')
        run('central-matching-restore', 'restore', 'central/Host.csproj', '--nologo')
        passed('central-matching-version-pinned-exactly')
        central.write_text(central_text.replace('</ItemGroup>', '<PackageVersion Include="CIS.Core" Version="99.0.0" /></ItemGroup>'), encoding='utf-8')
        run('central-version-mismatch', 'restore', 'central/Host.csproj', '--nologo', expected_error='CISTM006: The central CIS.Core package version')
        passed('central-version-mismatch-rejected')
        host('duplicate', duplicate=True)
        run('duplicate-runtime-reference', 'restore', 'duplicate/Host.csproj', '--nologo', expected_error='CISTM006: The CIS.Core SDK supplies')
        passed('duplicate-runtime-reference-rejected')
        host('ordinary', ordinary=True)
        run('ordinary-package-reference', 'build', 'ordinary/Host.csproj', '-c', 'Release', '-m:1', '--nologo', expected_error='CISTM007: Packaged Type Manager requires')
        passed('ordinary-reference-needs-sdk')
        for setting in ['PublishSingleFile=true', 'PublishTrimmed=true', 'PublishAot=true', 'SelfContained=true', 'CopyLocalLockFileAssemblies=false']:
            for mode in ['build', 'publish']:
                destination = root / 'rejected' / setting.split('=')[0] / mode
                extra = ['--no-build', '-o', str(destination)] if mode == 'publish' else []
                # Self-contained applications require an apphost independently
                # of CIS; avoid an unrelated SDK error masking our profile guard.
                if setting == 'SelfContained=true':
                    extra += ['-p:UseAppHost=true']
                run('reject-' + mode + '-' + setting.split('=')[0], mode, 'console/Host.csproj', '-c', 'Release', '--no-restore', *extra, '-p:' + setting, '-m:1', '--nologo', expected_error='CISTM003: Type Manager requires')
                if destination.exists() and any(destination.iterdir()):
                    raise RuntimeError('Unsupported profile wrote publish files')
                passed('reject-' + mode + '-' + setting)
        run('reject-sdk-version', 'build', 'console/Host.csproj', '--no-restore', '-p:NETCoreSdkVersion=10.0.999', '--nologo', expected_error='CISTM009: This Type Manager package requires')
        passed('reject-unqualified-sdk')
        host('replaced-hook')
        hook_project = root / 'replaced-hook/Host.csproj'
        hook_project.write_text(hook_project.read_text(encoding='utf-8').replace('</PropertyGroup>', '<BeforeMicrosoftNETSdkTargets>../BeforeExisting.targets</BeforeMicrosoftNETSdkTargets></PropertyGroup>'), encoding='utf-8')
        run('reject-replaced-hook', 'restore', 'replaced-hook/Host.csproj', '--nologo', expected_error='CISTM009: The CIS.Core early SDK hook was replaced')
        passed('reject-replaced-sdk-hook')
        run('disabled-build', 'build', 'console/Host.csproj', '-c', 'Release', '-p:CisTypeManagerEnabled=false', '-m:1', '--nologo')
        disabled_directory = Path(property_value('console', 'TargetDir', '-p:CisTypeManagerEnabled=false'))
        if (disabled_directory / 'TypeManagerPackageHost.types.json').exists() or property_value('console', 'RuntimeIdentifier', '-p:CisTypeManagerEnabled=false'):
            raise RuntimeError('Disabled integration changed the portable host output')
        passed('disabled-integration-preserves-portable-build')
        report['complete'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


if __name__ == '__main__':
    main()
