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
    allowed_environment = {'PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'TEMP', 'TMP', 'TMPDIR',
                           'HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES',
                           'PROGRAMFILES(X86)', 'PROGRAMW6432', 'DOTNET_ROOT', 'DOTNET_ROOT_X64',
                           'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE'}
    environment = {key: value for key, value in os.environ.items() if key.upper() in allowed_environment}
    environment.update(NUGET_PACKAGES=str(root / 'packages'),
                       NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), DOTNET_CLI_HOME=str(root / 'dotnet-home'),
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', MSBUILDDISABLENODEREUSE='1',
                       DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', DOTNET_NOLOGO='1')

    def write(relative, content):
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')

    def run(name, *arguments, expected_error=None):
        result = subprocess.run([args.dotnet, *arguments], cwd=root, env=environment,
                                text=True, encoding='utf-8', errors='replace', capture_output=True, timeout=240)
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
    write('Directory.Build.targets', '<Project />')
    write('Directory.Packages.props', '<Project />')
    write('Directory.Solution.props', '<Project />')
    write('Directory.Solution.targets', '<Project />')
    write('Directory.Build.rsp', '')
    write('Directory.Build.props', '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup></Project>')
    write('BeforeExisting.targets', '<Project><PropertyGroup><ExistingHookSeen>true</ExistingHookSeen></PropertyGroup></Project>')
    props_path = root / 'Directory.Build.props'
    props = ET.parse(props_path).getroot()
    ET.SubElement(ET.SubElement(props, 'PropertyGroup'), 'BeforeMicrosoftNETSdkTargets').text = str(root / 'BeforeExisting.targets')
    ET.SubElement(ET.SubElement(props, 'PropertyGroup'), 'RestoreConfigFile').text = str(root / 'NuGet.Config')
    props_path.write_bytes(ET.tostring(props, encoding='utf-8'))
    report['fixtureInputs'] = {}
    for fixture in ['Contracts', 'Transitive', 'Unused']:
        for path in (language / 'tests/fixtures/TypeManager' / fixture).iterdir():
            if path.is_file() and path.suffix in ['.cs', '.csproj', '.resx']:
                write('fixtures/' + fixture + '/' + path.name, path.read_text(encoding='utf-8'))
                report['fixtureInputs'][fixture + '/' + path.name] = digest(path)
    unused = root / 'fixtures/Unused/Unused.csproj'
    unused.write_text(unused.read_text(encoding='utf-8').replace('</Project>', f'<ItemGroup><PackageReference Include="CIS.Core" Version="[{version}]" /></ItemGroup></Project>'), encoding='utf-8')
    feed = root / 'feed'
    feed.mkdir()
    shutil.copyfile(args.package, feed / ('CIS.Core.' + version + '.nupkg'))
    shutil.copyfile(args.rid_package, feed / 'CIS.TypeManager.RidFixture.1.0.0.nupkg')
    policy = language / 'eng/runtime-dependencies.json'
    report['runtimeDependencyPolicySha256'] = digest(policy)
    report['dependencyPackages'] = {}
    for dependency in json.loads(policy.read_text(encoding='utf-8-sig'))['packages']:
        name, dependency_version = dependency['id'].lower(), dependency['version']
        source = args.dependency_cache / name / dependency_version / (name + '.' + dependency_version + '.nupkg')
        shutil.copyfile(source, feed / source.name)
        report['dependencyPackages'][source.name] = digest(source)
    write('NuGet.Config', '<configuration><packageSources><clear/><add key="local" value=' +
          xml.quoteattr(str(feed)) + '/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>')

    program = '''using CIS.TypeManagement;
using CIS.TypeManagement.Fixtures.Contracts;
using System.Text.Json;
if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "CIS.TypeManager.Fixture.Unused"))
{
    throw new InvalidOperationException("The unused discovery library was preloaded.");
}
var manager = await TypeManager.CreateAsync(new TypeManagerOptions(typeof(Program).Assembly)
    { PrecomputedContracts = [typeof(IDiscoveryMarker)] });
var withoutUnused = args.Contains("--without-unused");
var implementation = manager.GetImplementations<IDiscoveryMarker>().SingleOrDefault();
if (implementation?.FullName != (withoutUnused ? null : "CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings"))
{
    throw new InvalidOperationException("Incorrect discovered implementation.");
}
if (withoutUnused && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is
    "CIS.TypeManager.Fixture.Unused" or "CIS.TypeManager.Fixture.Transitive"))
{
    throw new InvalidOperationException("A removed dependency was loaded from stale output.");
}
var variant = manager.Types.Single(t => t.FullName == "InventoryFixture.Variant");
Console.WriteLine(JsonSerializer.Serialize(new { selected = manager.Assemblies.Count,
    implementation = implementation?.FullName, variant = (string)variant.GetProperty("Value")!.GetValue(null)! }));
'''

    def host(name, sdk='Microsoft.NET.Sdk', central=False, ordinary=False, duplicate=False):
        sdk_reference = '' if ordinary else f'<Sdk Name="CIS.Core" Version="{version}" />'
        core_reference = f'<PackageReference Include="CIS.Core" Version="{version}" />' if ordinary or duplicate else ''
        fixture_version = '' if central else ' Version="1.0.0"'
        write(name + '/Host.csproj', f'''<Project Sdk="{sdk}">
  {sdk_reference}
  <PropertyGroup><OutputType>Exe</OutputType><AssemblyName>TypeManagerPackageHost</AssemblyName><UseAppHost>false</UseAppHost><CisTypeManagerEnabled>true</CisTypeManagerEnabled></PropertyGroup>
  <ItemGroup><ProjectReference Include="../fixtures/Unused/Unused.csproj" />
    <ProjectReference Include="../fixtures/Contracts/Contracts.csproj" />
    <PackageReference Include="CIS.TypeManager.RidFixture"{fixture_version} />{core_reference}
  </ItemGroup>
</Project>''')
        write(name + '/Program.cs', program)
        if central:
            write(name + '/Directory.Packages.props', '<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include="CIS.TypeManager.RidFixture" Version="1.0.0" /></ItemGroup></Project>')

    def property_value(name, key, *extra):
        return run(name + '-property-' + key, 'msbuild', name + '/Host.csproj', '-p:Configuration=Release',
                   '-getProperty:' + key, '-nologo', *extra)

    def validate(name, mode, directory, rid, execute=True, without_unused=False, host_name='TypeManagerPackageHost', record=True):
        manifest_path = directory / (host_name + '.types.json')
        if not manifest_path.is_file():
            raise RuntimeError(name + '/' + mode + ' did not produce a current manifest')
        manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
        entries = {item['assemblyName'].split(',')[0]: item for item in manifest['assemblies']}
        expected = {host_name, 'CIS.Core', 'Microsoft.Extensions.Logging.Abstractions',
                    'Microsoft.Extensions.DependencyInjection.Abstractions', 'CIS.TypeManager.Fixture.Contracts',
                    'CIS.TypeManager.Fixture.Transitive', 'CIS.TypeManager.Fixture.Unused', 'CIS.TypeManager.RidAsset'}
        # ASP.NET supplies these from its shared framework, outside the deployed
        # application inventory; console consumers deploy them from the package.
        if name == 'web':
            expected -= {'Microsoft.Extensions.Logging.Abstractions',
                         'Microsoft.Extensions.DependencyInjection.Abstractions'}
        if without_unused:
            expected -= {'CIS.TypeManager.Fixture.Unused', 'CIS.TypeManager.Fixture.Transitive'}
        if set(entries) != expected or len(manifest['assemblies']) != len(expected) or manifest['schemaVersion'] != 1:
            raise RuntimeError('Incorrect package consumer inventory: ' + str(entries))
        if manifest['hostAssembly'] != entries[host_name]['assemblyName']:
            raise RuntimeError('Incorrect manifest host identity')
        if digest(directory / 'CIS.Core.dll') != core_hash:
            raise RuntimeError('Consumer is not using the exact packed Core DLL')
        if digest(root / 'packages/cis.core' / version.lower() / 'tools/net10.0/CIS.TypeManager.Build.dll') != helper_hash:
            raise RuntimeError('Consumer is not using the exact packed inventory helper')
        if digest(directory / entries['CIS.TypeManager.RidAsset']['relativePath']) != variant_hashes[rid]:
            raise RuntimeError('Wrong RID-selected managed asset')
        if not (directory / 'cistm-native.dll').is_file():
            raise RuntimeError('Native exclusion fixture was not deployed')
        if not without_unused and not (directory / 'fr/CIS.TypeManager.Fixture.Unused.resources.dll').is_file():
            raise RuntimeError('Satellite exclusion fixture was not deployed')
        deps = json.loads((directory / (host_name + '.deps.json')).read_text(encoding='utf-8'))
        if any('CIS.TypeManager.Build' in key or 'Microsoft.Build' in key for key in deps['libraries']):
            raise RuntimeError('Build tooling leaked into runtime dependencies')
        if (directory / 'CIS.TypeManager.Build.dll').exists():
            raise RuntimeError('Build helper leaked into deployment')
        runtime = None
        if execute:
            extra = ['--without-unused'] if without_unused else []
            runtime = json.loads(run(name + '-' + mode + '-run', str(directory / (host_name + '.dll')), *extra))
            implementation = None if without_unused else 'CIS.TypeManagement.Fixtures.Unused.DiscoveredSettings'
            if runtime != {'selected': len(expected), 'implementation': implementation, 'variant': rid}:
                raise RuntimeError('Unexpected prepared runtime catalog')
        shutil.copyfile(manifest_path, root / (name + '-' + mode + '.types.json'))
        assets = json.loads((root / name / 'obj/project.assets.json').read_text(encoding='utf-8-sig'))
        if {Path(path).resolve() for path in assets['packageFolders']} != {root / 'packages'} or \
                {Path(path).resolve() for path in assets['project']['restore']['sources']} != {feed}:
            raise RuntimeError('Consumer restore escaped its private package cache or feed')
        if record:
            passed(name + '-' + mode, selected=sorted(entries), rid=rid, runtime=runtime,
                   manifestSha256=digest(manifest_path))

    def lifecycle(name):
        project = root / name / 'Host.csproj'
        original = project.read_bytes()
        directory = Path(property_value(name, 'TargetDir'))
        manifest = directory / 'TypeManagerPackageHost.types.json'
        initial_manifest = manifest.read_bytes()
        stale = {filename: (directory / filename).read_bytes() for filename in
                 ['CIS.TypeManager.Fixture.Unused.dll', 'CIS.TypeManager.Fixture.Transitive.dll',
                  'fr/CIS.TypeManager.Fixture.Unused.resources.dll']}
        parsed = ET.fromstring(original)
        removed = 0
        for group in parsed.findall('ItemGroup'):
            for reference in list(group):
                if reference.tag == 'ProjectReference' and reference.get('Include') == '../fixtures/Unused/Unused.csproj':
                    group.remove(reference)
                    removed += 1
        if removed != 1:
            raise RuntimeError('The lifecycle control must remove exactly one project reference')
        # A test-only hook guarantees stale files exist immediately before the
        # real generator, regardless of SDK incremental-clean ordering.
        inject = ET.SubElement(parsed, 'Target', Name='PlantLifecycleStaleAssets',
                               BeforeTargets='CisWriteBuildTypeInventory;CisWritePublishTypeInventory',
                               Condition="'$(LifecycleStaleAssets)' == 'true'")
        for filename, content in stale.items():
            seed = root / 'stale-seeds' / name / filename
            seed.parent.mkdir(parents=True, exist_ok=True)
            seed.write_bytes(content)
            destination = '$(LifecycleDestination)/' + filename
            ET.SubElement(inject, 'Copy', SourceFiles=str(seed), DestinationFiles=destination)
            ET.SubElement(inject, 'Error', Condition="!Exists('" + destination + "')", Text='Stale fixture was not planted')
        ET.SubElement(inject, 'Message', Importance='High', Text='LIFECYCLE_STALE_FILES_PRESENT')
        try:
            project.write_bytes(ET.tostring(parsed, encoding='utf-8'))
            output = run(name + '-remove-reference', 'build', str(project), '-c', 'Release', '-m:1', '--nologo',
                         '-p:LifecycleStaleAssets=true', '-p:LifecycleDestination=' + str(directory))
            if 'LIFECYCLE_STALE_FILES_PRESENT' not in output:
                raise RuntimeError('The build stale-output generation probe did not execute')
            # Stale DLLs must not regain membership through a directory scan.
            for filename, content in stale.items():
                (directory / filename).parent.mkdir(parents=True, exist_ok=True)
                (directory / filename).write_bytes(content)
            validate(name, 'removed-reference-build', directory, native_rid, without_unused=True)
            current_manifest = manifest.read_bytes()
            manifest.write_bytes(initial_manifest)
            try:
                try:
                    validate(name, 'stale-manifest-negative-control', directory, native_rid, without_unused=True, record=False)
                except RuntimeError as error:
                    if not str(error).startswith('Incorrect package consumer inventory:'):
                        raise
                else:
                    raise RuntimeError('The selection assertion accepted the old manifest after reference removal')
            finally:
                manifest.write_bytes(current_manifest)
            passed(name + '-selection-assertion-rejects-stale-manifest')
            published = (root / name / property_value(name, 'PublishDir').replace('\\', '/')).resolve()
            output = run(name + '-removed-reference-publish', 'publish', str(project), '-c', 'Release', '--no-build', '--no-restore', '-m:1', '--nologo',
                         '-p:LifecycleStaleAssets=true', '-p:LifecycleDestination=' + str(published))
            if 'LIFECYCLE_STALE_FILES_PRESENT' not in output:
                raise RuntimeError('The publish stale-output generation probe did not execute')
            for filename, content in stale.items():
                (published / filename).parent.mkdir(parents=True, exist_ok=True)
                (published / filename).write_bytes(content)
            validate(name, 'removed-reference-publish', published, native_rid, without_unused=True)
            project.write_bytes(original)
            run(name + '-readd-reference', 'build', str(project), '-c', 'Release', '-m:1', '--nologo')
            validate(name, 'readded-reference-build', directory, native_rid)

            renamed = 'TypeManagerRenamedHost'
            parsed = ET.fromstring(original)
            parsed.find('PropertyGroup/AssemblyName').text = renamed
            project.write_bytes(ET.tostring(parsed, encoding='utf-8'))
            run(name + '-rename-host', 'build', str(project), '-c', 'Release', '-m:1', '--nologo')
            validate(name, 'renamed-host-build', directory, native_rid, host_name=renamed)
            run(name + '-renamed-host-publish', 'publish', str(project), '-c', 'Release', '--no-build', '--no-restore', '-m:1', '--nologo')
            validate(name, 'renamed-host-publish', published, native_rid, host_name=renamed)
            renamed_manifest = directory / (renamed + '.types.json')
            writes = list((root / name / 'obj').rglob('*.FileListAbsolute.txt'))
            if not any(str(renamed_manifest) in path.read_text(encoding='utf-8-sig') for path in writes):
                raise RuntimeError('The generated manifest is not registered for clean')
            run(name + '-clean', 'clean', str(project), '-c', 'Release', '-m:1', '--nologo')
            if renamed_manifest.exists() or manifest.exists():
                raise RuntimeError('Clean left a generated build manifest behind')
            passed(name + '-clean-removes-registered-manifest',
                   obsoletePublishedManifestRetained=(published / manifest.name).exists())
        finally:
            project.write_bytes(original)
        if manifest.exists():
            raise RuntimeError('Rebuild must start without its output manifest')
        run(name + '-rebuild-after-clean', 'build', str(project), '-c', 'Release', '-m:1', '--nologo')
        validate(name, 'rebuild-after-clean', directory, native_rid)

        helper = root / 'packages/cis.core' / version.lower() / 'tools/net10.0/CIS.TypeManager.Build.dll'
        helper_bytes = helper.read_bytes()
        before = (digest(manifest), manifest.stat().st_mtime_ns)
        helper.unlink()
        try:
            run(name + '-design-time', 'msbuild', str(project),
                '-t:Compile', '-p:Configuration=Release', '-p:DesignTimeBuild=true',
                '-p:SkipCompilerExecution=true', '-p:ProvideCommandLineArgs=true', '-p:BuildingProject=false', '-nologo')
            if before != (digest(manifest), manifest.stat().st_mtime_ns):
                raise RuntimeError('Design-time compile changed the manifest')
            passed(name + '-design-time-evaluation-and-compile-without-helper')
            for target in ['CisWriteBuildTypeInventory', 'CisWritePublishTypeInventory']:
                run(name + '-design-time-' + target, 'msbuild', str(project), '-t:' + target,
                    '-p:Configuration=Release', '-p:DesignTimeBuild=true', '-nologo')
                if before != (digest(manifest), manifest.stat().st_mtime_ns):
                    raise RuntimeError('Explicit design-time inventory target changed the manifest')
                run(name + '-missing-helper-control-' + target, 'msbuild', str(project), '-t:' + target,
                    '-p:Configuration=Release', '-nologo', expected_error='CISTM005: The Type Manager build helper is missing')
                passed(name + '-design-time-skip-and-missing-helper-control-' + target)
        finally:
            helper.write_bytes(helper_bytes)

    try:
        report['sdk'] = run('sdk-version', '--version')
        if report['sdk'] != json.loads((root / 'global.json').read_text(encoding='utf-8-sig'))['sdk']['version']:
            raise RuntimeError('Consumer SDK differs from the pinned version')
        host('console')
        host('web', 'Microsoft.NET.Sdk.Web')
        write('Consumers.slnx', '<Solution><Folder Name="/console/"><Project Path="console/Host.csproj" /></Folder><Folder Name="/web/"><Project Path="web/Host.csproj" /></Folder><Folder Name="/fixtures/"><Project Path="fixtures/Contracts/Contracts.csproj" /><Project Path="fixtures/Transitive/Transitive.csproj" /><Project Path="fixtures/Unused/Unused.csproj" /></Folder></Solution>')
        run('parallel-clean-restore-build', 'build', 'Consumers.slnx', '-c', 'Release', '-m:2', '--nologo')
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

        for name in ['console', 'web']:
            lifecycle(name)

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
