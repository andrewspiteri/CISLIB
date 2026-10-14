"""Run the real examples from source and an exact package in isolated layouts.

Requires the pinned native SDK (including ASP.NET for the Web sample). Every
success/failure scenario starts a fresh process. Output roots must be empty.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import time
import urllib.request
import xml.etree.ElementTree as ET
from xml.sax.saxutils import quoteattr
import zipfile


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo-root', type=Path, required=True)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--dependency-cache', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    if platform.system() not in ('Windows', 'Linux') or platform.machine().lower() not in ('amd64', 'x86_64'):
        parser.error('Supported example lanes are Windows x64 and Linux x64')
    repo = args.repo_root.resolve()
    language = repo / 'implementations/csharp'
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output root')
    root.mkdir(parents=True, exist_ok=True)
    (root / 'logs').mkdir()
    report = {'complete': False, 'scope': 'Native source and exact-package example integration; not a release gate',
              'platform': platform.platform(), 'architecture': platform.machine(),
              'verifierSha256': digest(__file__), 'packageSha256': digest(args.package), 'results': []}
    # Keep OS process essentials and the selected native SDK. MSBuild imports
    # arbitrary environment properties, so use an allowlist rather than prefixes.
    allowed_environment = {'PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'TEMP', 'TMP', 'TMPDIR',
                           'HOME', 'USERPROFILE', 'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES',
                           'PROGRAMFILES(X86)', 'PROGRAMW6432', 'DOTNET_ROOT', 'DOTNET_ROOT_X64',
                           'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE'}
    environment = {key: value for key, value in os.environ.items()
                   if key.upper() in allowed_environment}
    environment.update(NUGET_PACKAGES=str(root / 'packages'),
                       NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), DOTNET_CLI_HOME=str(root / 'dotnet-home'),
                       DOTNET_CLI_TELEMETRY_OPTOUT='1', MSBUILDDISABLENODEREUSE='1',
                       DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true', DOTNET_NOLOGO='1')

    def save():
        (root / 'results.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    def run(name, *arguments, cwd=root, failure=None, failed_file=None):
        process = subprocess.run([args.dotnet, *map(str, arguments)], cwd=cwd, env=environment,
                                 text=True, encoding='utf-8', errors='replace', capture_output=True, timeout=240)
        output = process.stdout + process.stderr
        (root / 'logs' / (name + '.log')).write_text(output, encoding='utf-8')
        if failure:
            if process.returncode != 1 or failure not in process.stderr or failed_file not in process.stderr or 'Unhandled exception' in output or any(marker in output for marker in
                    ['PREPARATION_COMPLETE', 'BUILDER_CREATING', 'CONTAINER_BUILDING', 'CONTAINER_BUILT', 'APPLICATION_READY']):
                raise RuntimeError(name + ' did not fail before container/application startup for ' + failure)
        elif process.returncode:
            raise RuntimeError(name + ' failed; inspect its log')
        return process.stderr if failure else process.stdout

    def passed(name, **details):
        report['results'].append({'case': name, 'passed': True, **details})
        save()

    def copy_tree(source, destination):
        shutil.copytree(source, destination, ignore=shutil.ignore_patterns('bin', 'obj', '__pycache__'))

    def http_check(name, directory, host_name):
        log_path = root / 'logs' / (name + '.log')
        with log_path.open('w', encoding='utf-8') as log:
            process = subprocess.Popen([args.dotnet, str(directory / (host_name + '.dll')), '--urls', 'http://127.0.0.1:0'],
                                       cwd=directory, env=environment, stdout=log, stderr=subprocess.STDOUT)
            try:
                address = None
                deadline = time.monotonic() + 45
                while time.monotonic() < deadline:
                    output = log_path.read_text(encoding='utf-8', errors='replace')
                    match = re.search(r'Now listening on: (http://127\.0\.0\.1:\d+)', output)
                    if match:
                        address = match.group(1)
                        break
                    if process.poll() is not None:
                        raise RuntimeError(name + ' exited before listening')
                    time.sleep(0.1)
                if address is None:
                    raise RuntimeError(name + ' did not start listening')
                responses = []
                opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
                for _ in range(2):
                    with opener.open(address + '/catalog', timeout=10) as response:
                        observed = json.load(response)
                    if observed.get('samePreparedManager') is not True or observed.get('sameCachedQuery') is not True or observed.get('sameConsumerWithinRequest') is not True or not observed.get('consumerId') or {item['type'] for item in observed['candidates']} != expected_types:
                        raise RuntimeError(name + ' returned an incorrect request-scoped catalog')
                    responses.append(observed)
                if responses[0]['consumerId'] == responses[1]['consumerId']:
                    raise RuntimeError(name + ' reused a scoped consumer across HTTP requests')
                passed(name, responses=responses)
            finally:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=10)

    expected_types = {'CIS.TypeManagement.Examples.Unused.DirectExample', 'CIS.TypeManagement.Examples.Unused.InheritedExample'}
    save()
    try:
        with zipfile.ZipFile(args.package) as package:
            nuspec = ET.fromstring(package.read(next(name for name in package.namelist() if name.endswith('.nuspec'))))
            version = nuspec.find('.//{*}version').text
            core_hash = hashlib.sha256(package.read('lib/net10.0/CIS.Core.dll')).hexdigest()
            helper_hash = hashlib.sha256(package.read('tools/net10.0/CIS.TypeManager.Build.dll')).hexdigest()
        report.update(packageVersion=version, packageCoreSha256=core_hash, packageHelperSha256=helper_hash)
        shutil.copyfile(language / 'global.json', root / 'global.json')
        shutil.copyfile(repo / '.editorconfig', root / '.editorconfig')
        (root / 'Directory.Build.targets').write_text('<Project />\n', encoding='utf-8')
        report['sdk'] = run('sdk', '--version').strip()
        run('runtime-info', '--info')
        if report['sdk'] != json.loads((root / 'global.json').read_text(encoding='utf-8-sig'))['sdk']['version']:
            raise RuntimeError('The native SDK does not match the repository pin')
        feed = root / 'feed'
        feed.mkdir()
        shutil.copyfile(args.package, feed / ('CIS.Core.' + version + '.nupkg'))
        policy = language / 'eng/runtime-dependencies.json'
        report['runtimeDependencyPolicySha256'] = digest(policy)
        report['dependencyPackages'] = {}
        for dependency in json.loads(policy.read_text(encoding='utf-8-sig'))['packages']:
            name, dependency_version = dependency['id'].lower(), dependency['version']
            archive = args.dependency_cache / name / dependency_version / (name + '.' + dependency_version + '.nupkg')
            shutil.copyfile(archive, feed / archive.name)
            report['dependencyPackages'][archive.name] = digest(archive)
        report['sourceProvenanceScope'] = 'Recorded C# and example inputs plus copied build inputs; not a transitive build attestation'
        report['copiedBuildInputs'] = {}
        report['exampleInputs'] = {str(path.relative_to(language)).replace('\\', '/'): digest(path)
                                  for folder in ['examples/type-manager', 'tests/fixtures/TypeManager/ExampleLate']
                                  for path in sorted((language / folder).rglob('*')) if path.is_file()
                                  and not set(path.relative_to(language).parts).intersection({'bin', 'obj'})}
        report['sourceCoreInputs'] = {str(path.relative_to(language / 'src/CIS.Core')).replace('\\', '/'): digest(path)
                                    for path in sorted((language / 'src/CIS.Core').rglob('*.cs'))
                                    if not set(path.relative_to(language / 'src/CIS.Core').parts).intersection({'bin', 'obj'})}
        for lane in ['source', 'package']:
            tree = root / lane / 'implementations/csharp'
            tree.mkdir(parents=True)
            for file in ['global.json', 'Directory.Build.props', 'Directory.Packages.props']:
                shutil.copyfile(language / file, tree / file)
            if (language / 'Directory.Build.targets').is_file():
                shutil.copyfile(language / 'Directory.Build.targets', tree / 'Directory.Build.targets')
            (tree / 'NuGet.config').write_text('<configuration><packageSources><clear/><add key="local" value=' +
                                              quoteattr(str(feed)) + '/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>', encoding='utf-8')
            copy_tree(language / 'examples/type-manager', tree / 'examples/type-manager')
            copy_tree(language / 'tests/fixtures/TypeManager/ExampleLate', tree / 'tests/fixtures/TypeManager/ExampleLate')
            if lane == 'source':
                copy_tree(language / 'src/CIS.Core', tree / 'src/CIS.Core')
                copy_tree(language / 'eng/CIS.TypeManager.Build', tree / 'eng/CIS.TypeManager.Build')
                copy_tree(language / 'build/type-manager', tree / 'build/type-manager')
                shutil.copyfile(repo / 'README.md', root / lane / 'README.md')
            else:
                for host in ['Console', 'Web']:
                    project_path = tree / 'examples/type-manager' / host / (host + '.csproj')
                    project = ET.parse(project_path).getroot()
                    project.insert(0, ET.Element('Sdk', Name='CIS.Core', Version=version))
                    removed_reference = removed_import = 0
                    for group in project:
                        for item in list(group):
                            if item.tag == 'ProjectReference' and item.get('Include') == '../../../src/CIS.Core/CIS.Core.csproj':
                                group.remove(item)
                                removed_reference += 1
                    for item in list(project):
                        if item.tag == 'Import' and item.get('Project') == '../../../build/type-manager/CIS.TypeManager.targets':
                            project.remove(item)
                            removed_import += 1
                    if (removed_reference, removed_import) != (1, 1):
                        raise RuntimeError('Expected exactly one Core reference and source inventory import')
                    project_path.write_bytes(ET.tostring(project, encoding='utf-8'))
            report['copiedBuildInputs'][lane] = {str(path.relative_to(tree)).replace('\\', '/'): digest(path)
                                                for path in sorted(tree.rglob('*')) if path.is_file()}
            late_project = tree / 'tests/fixtures/TypeManager/ExampleLate/ExampleLate.csproj'
            run(lane + '-late-build', 'build', late_project, '-c', 'Release', '-m:1', '--nologo', cwd=tree)
            late = late_project.parent / 'bin/Release/net10.0/CIS.TypeManager.Examples.Late.dll'
            for host in ['Console', 'Web']:
                project = tree / 'examples/type-manager' / host / (host + '.csproj')
                host_name = 'CIS.TypeManager.Examples.' + host
                for layout in ['build-debug', 'build-release', 'publish', 'publish-no-build']:
                    name = lane + '-' + host.lower() + '-' + layout
                    configuration = 'Debug' if layout == 'build-debug' else 'Release'
                    if layout.startswith('build'):
                        run(name + '-build', 'build', project, '-c', configuration, '-m:1', '--nologo', cwd=tree)
                        target = run(name + '-target-path', 'msbuild', project, '-getProperty:TargetPath', '-p:Configuration=' + configuration,
                                     '-nologo', cwd=tree).strip()
                        directory = Path(target).parent
                    else:
                        directory = root / 'deployments' / name
                        extra = ['--no-build'] if layout == 'publish-no-build' else []
                        run(name + '-publish', 'publish', project, '-c', 'Release', '-m:1', '--nologo', '-o', directory, *extra, cwd=tree)
                    manifest = json.loads((directory / (host_name + '.types.json')).read_text(encoding='utf-8-sig'))
                    assets = json.loads((project.parent / 'obj/project.assets.json').read_text(encoding='utf-8-sig'))
                    if {Path(path).resolve() for path in assets['packageFolders']} != {(root / 'packages').resolve()} or {Path(path).resolve() for path in assets['project']['restore']['sources']} != {feed.resolve()}:
                        raise RuntimeError(name + ' used an unexpected NuGet package folder or source')
                    expected = {host_name, 'CIS.Core', 'CIS.TypeManager.Examples.Contracts', 'CIS.TypeManager.Examples.Unused'}
                    if host == 'Console':
                        expected |= {'Microsoft.Extensions.Logging.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions'}
                    entries = manifest['assemblies']
                    if len(entries) != len(expected) or {item['assemblyName'].split(',')[0] for item in entries} != expected:
                        raise RuntimeError(name + ' selected an incomplete or unexpected inventory')
                    actual_core = digest(directory / 'CIS.Core.dll')
                    if lane == 'package' and (actual_core != core_hash or digest(root / 'packages/cis.core' / version.lower() / 'tools/net10.0/CIS.TypeManager.Build.dll') != helper_hash):
                        raise RuntimeError(name + ' did not consume the exact Core/helper bytes')
                    output = run(name + '-run', directory / (host_name + '.dll'), '--verify', late)
                    observed = json.loads(output.strip().splitlines()[-1])
                    if observed['unusedInitiallyLoaded'] or not observed['fixedMembershipChecked'] or {item['type'] for item in observed['candidates']} != expected_types:
                        raise RuntimeError(name + ' did not discover the complete unused selection or check later membership')
                    if {item['assembly'] for item in observed['candidates']} != {item['assemblyName'] for item in entries if item['assemblyName'].startswith('CIS.TypeManager.Examples.Unused,')}:
                        raise RuntimeError(name + ' lost defining assembly identity')
                    facts = observed['report']
                    if set(observed['selected']) != {item['assemblyName'] for item in entries} or facts['SelectedAssemblyCount'] != len(entries) or facts['PreparedContractCount'] != 1 or facts['TypeCount'] < 2:
                        raise RuntimeError(name + ' returned an inconsistent preparation report')
                    if not any(identity.startswith('CIS.TypeManager.Examples.Unused,') for identity in facts['LoadedAssemblyIdentities']):
                        raise RuntimeError(name + ' did not record the initially unloaded library')
                    boundary = 'BUILDER_CREATING' if host == 'Web' else 'APPLICATION_READY'
                    if output.index('PREPARATION_COMPLETE') >= output.index(boundary):
                        raise RuntimeError(name + ' crossed the startup boundary before preparation')
                    if host == 'Web' and (not observed['rootAndScopesShareManager'] or not observed['queryIdentityStable']):
                        raise RuntimeError(name + ' did not retain the exact manager/query across scopes')
                    deps = json.loads((directory / (host_name + '.deps.json')).read_text(encoding='utf-8'))
                    if any('Microsoft.Build' in item or 'CIS.TypeManager.Build' in item for item in deps['libraries']) or (directory / 'CIS.TypeManager.Build.dll').exists():
                        raise RuntimeError(name + ' deployed build tooling')
                    runtime_options = json.loads((directory / (host_name + '.runtimeconfig.json')).read_text(encoding='utf-8'))['runtimeOptions']
                    frameworks = runtime_options.get('frameworks', [runtime_options.get('framework', {})])
                    expected_frameworks = {'Microsoft.NETCore.App', 'Microsoft.AspNetCore.App'} if host == 'Web' else {'Microsoft.NETCore.App'}
                    if {item.get('name') for item in frameworks} != expected_frameworks:
                        raise RuntimeError(name + ' has an unexpected shared framework requirement')
                    passed(name, coreSha256=actual_core, manifest=manifest, observed=observed)
                    for fault in ['missing-manifest', 'malformed-manifest', 'missing-library']:
                        broken = root / 'broken' / (name + '-' + fault)
                        shutil.copytree(directory, broken)
                        inventory = broken / (host_name + '.types.json')
                        if fault == 'missing-manifest':
                            inventory.unlink()
                        elif fault == 'malformed-manifest':
                            inventory.write_text('{', encoding='utf-8')
                        else:
                            (broken / 'CIS.TypeManager.Examples.Unused.dll').unlink()
                        failure = 'InvalidInventory'
                        failed_file = str(broken / 'CIS.TypeManager.Examples.Unused.dll') if fault == 'missing-library' else str(inventory)
                        rejected = run(name + '-' + fault, broken / (host_name + '.dll'), '--verify', late,
                                       failure='PREPARATION_FAILED: ' + failure, failed_file=failed_file)
                        detail = 'BytePositionInLine' if fault == 'malformed-manifest' else 'FileNotFoundException'
                        if detail not in rejected:
                            raise RuntimeError(name + '-' + fault + ' lost the expected original failure detail')
                        passed(name + '-' + fault, expectedFailure=failure, startupBoundaryReached=False)
                    if host == 'Web' and layout == 'build-release':
                        http_check(name + '-http', directory, host_name)
        if len(report['results']) != 66:
            raise RuntimeError('Unexpected check count')
        report['complete'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        save()
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
