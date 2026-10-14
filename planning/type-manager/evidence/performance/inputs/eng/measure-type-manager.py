"""Build private synthetic catalogs, then measure the exact deployment in fresh processes.

No timing threshold is a correctness gate. The deferred control calls the same
factory/options at first use instead of at the bootstrap boundary.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import time
from xml.sax.saxutils import escape


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation', choices=['prepare', 'run'])
    parser.add_argument('--repo-root', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--candidate-core', type=Path)
    parser.add_argument('--candidate-report', type=Path)
    parser.add_argument('--expected-core-sha256', required=True)
    parser.add_argument('--fixtures', type=Path)
    parser.add_argument('--expected-fixtures-sha256')
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--samples', type=int, default=12)
    parser.add_argument('--warmups', type=int, default=2)
    parser.add_argument('--iterations', type=int, default=10000)
    args = parser.parse_args()
    if platform.system() not in ('Windows', 'Linux') or platform.machine().lower() not in ('amd64', 'x86_64'):
        parser.error('Only native Windows/Linux x64 lanes are supported')
    if args.samples < 12 or args.samples % 4 or args.warmups < 1 or args.iterations < 1000:
        parser.error('Use at least 12 samples in a multiple of four, one warmup round and 1000 query iterations')
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output directory')
    root.mkdir(parents=True, exist_ok=True)
    (root / 'logs').mkdir()
    language = args.repo_root.resolve() / 'implementations/csharp'
    policy = read(language / 'eng/toolchain.json')
    allowed = {'PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'TEMP', 'TMP', 'TMPDIR', 'HOME', 'USERPROFILE',
               'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMW6432',
               'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE'}
    env = {key: value for key, value in os.environ.items() if key.upper() in allowed}
    env.update(DOTNET_CLI_HOME=str(root / 'dotnet-home'), NUGET_PACKAGES=str(root / 'packages'),
               NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1',
               MSBUILDDISABLENODEREUSE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
               DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false', DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true',
               DOTNET_TieredCompilation='1', DOTNET_TieredPGO='1')
    report = dict(complete=False, schemaVersion=1, operation=args.operation, platform=platform.platform(), python=platform.python_version(),
                  harnessSha256=digest(__file__), runtimePolicySha256=digest(language / 'eng/toolchain.json'),
                  sdk=policy['sdk'], runtime=policy['targetRuntime'])

    def save():
        write(root / 'results.json', report)

    def invoke(name, *arguments):
        started = time.perf_counter()
        try:
            result = subprocess.run([args.dotnet, *map(str, arguments)], cwd=root, env=env,
                                    capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=180)
        except subprocess.TimeoutExpired as error:
            as_bytes = lambda value: value.encode('utf-8') if isinstance(value, str) else (value or b'')
            raw = as_bytes(error.stdout) + as_bytes(error.stderr)
            (root / 'logs' / (name + '.log')).write_bytes(raw + b'\nTIMEOUT\n')
            raise
        elapsed = (time.perf_counter() - started) * 1000
        (root / 'logs' / (name + '.log')).write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode:
            raise RuntimeError(name + ' failed; see log')
        return result.stdout, elapsed

    def project(name, source, references=(), executable=False):
        directory = root / 'build' / name
        directory.mkdir(parents=True)
        (directory / 'Program.cs').write_text(source, encoding='utf-8', newline='\n')
        refs = ''.join('<Reference Include="' + escape(path.stem, {'"': '&quot;'}) + '" HintPath="' +
                       escape(str(path), {'"': '&quot;'}) + '" Private="true" />' for path in references)
        text = ('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
                '<AssemblyName>' + name + '</AssemblyName><AssemblyVersion>1.0.0.0</AssemblyVersion>'
                '<OutputType>' + ('Exe' if executable else 'Library') + '</OutputType>'
                '<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>'
                '<ServerGarbageCollection>false</ServerGarbageCollection><IsPackable>false</IsPackable>'
                '</PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>')
        target = directory / 'Fixture.csproj'
        target.write_text(text, encoding='utf-8')
        invoke('build-' + name, 'build', target, '-c', 'Release', '--nologo', '-m:1')
        return directory / 'bin/Release/net10.0'

    try:
        shutil.copyfile(language / 'global.json', root / 'global.json')
        (root / 'Directory.Build.props').write_text('<Project><PropertyGroup><RestoreConfigFile>' +
            escape(str(root / 'NuGet.Config')) + '</RestoreConfigFile></PropertyGroup></Project>', encoding='utf-8')
        for name in ['Directory.Build.targets', 'Directory.Packages.props']:
            (root / name).write_text('<Project />', encoding='utf-8')
        (root / 'Directory.Build.rsp').write_text('', encoding='utf-8')
        (root / 'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>', encoding='utf-8')
        sdk, _ = invoke('sdk', '--version')
        if sdk.strip() != policy['sdk']:
            raise RuntimeError('Wrong SDK')
        if args.operation == 'prepare':
            if not args.candidate_core or not args.candidate_report:
                raise RuntimeError('Preparation needs a candidate Core and producer report')
            core = args.candidate_core.resolve()
            producer = read(args.candidate_report)
            if digest(core) != args.expected_core_sha256 or producer.get('complete') is not True or producer.get('coreSha256') != digest(core):
                raise RuntimeError('Candidate does not match completed producer and expected hash')
            sources = {path.relative_to(language / 'src/CIS.Core').as_posix(): digest(path)
                       for path in (language / 'src/CIS.Core/TypeManagement').rglob('*.cs')}
            if sources != {key: value for key, value in producer['coreSourceInputs'].items() if key.startswith('TypeManagement/')}:
                raise RuntimeError('Current module source differs from the candidate record')
            dependencies = ['Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll']
            for name in dependencies:
                if producer['fixtureFiles'].get('Host/bin/Release/net10.0/' + name) != digest(core.parent / name):
                    raise RuntimeError('Dependency differs from the candidate producer: ' + name)
            fixture_sources = language / 'tests/fixtures/TypeManager/Performance'
            contracts = project('CIS.TypeManager.PerformanceContracts', (fixture_sources / 'Contracts.cs').read_text(encoding='utf-8-sig'))
            contract_dll = contracts / 'CIS.TypeManager.PerformanceContracts.dll'
            host = project('CIS.TypeManager.PerformanceHost', (fixture_sources / 'Host.cs').read_text(encoding='utf-8-sig'),
                           [core, contract_dll, *[core.parent / name for name in dependencies]], executable=True)
            identity_output, _ = invoke('dependency-identities', 'exec', '--fx-version', policy['targetRuntime'],
                                        host / 'CIS.TypeManager.PerformanceHost.dll', '--identities')
            dependency_identities = json.loads(identity_output)
            catalogs = {}
            for size, libraries, classes in [('small', 2, 32), ('large', 8, 512)]:
                layout = root / 'deploy' / size
                layout.mkdir(parents=True)
                for filename in ['CIS.TypeManager.PerformanceHost.dll', 'CIS.TypeManager.PerformanceHost.deps.json', 'CIS.TypeManager.PerformanceHost.runtimeconfig.json']:
                    shutil.copyfile(host / filename, layout / filename)
                for path in [contract_dll, core, core.parent / 'Microsoft.Extensions.Logging.Abstractions.dll', core.parent / 'Microsoft.Extensions.DependencyInjection.Abstractions.dll']:
                    shutil.copyfile(path, layout / path.name)
                    if digest(path) != digest(layout / path.name):
                        raise RuntimeError('Deployment copy differs: ' + path.name)
                for name in dependencies:
                    if digest(layout / name) != producer['fixtureFiles']['Host/bin/Release/net10.0/' + name]:
                        raise RuntimeError('Deployed dependency changed: ' + name)
                names = ['CIS.TypeManager.PerformanceHost', 'CIS.TypeManager.PerformanceContracts', 'CIS.Core',
                         'Microsoft.Extensions.Logging.Abstractions', 'Microsoft.Extensions.DependencyInjection.Abstractions']
                for index in range(libraries):
                    name = 'Perf.' + size + '.Library' + str(index).zfill(2)
                    definitions = ['using PerformanceFixtures; namespace SyntheticCatalog;']
                    for number in range(classes):
                        interfaces = (['IPrepared'] if number % 2 == 0 else []) + (['ILazy'] if number % 3 == 0 else [])
                        class_name = 'Candidate' + str(number).zfill(4)
                        definitions.append('internal sealed class ' + class_name + (' : ' + ', '.join(interfaces) if interfaces else '') +
                                           ' { private ' + class_name + '() { Counters.Activated++; } }')
                    built = project(name, '\n'.join(definitions) + '\n', [contract_dll])
                    shutil.copyfile(built / (name + '.dll'), layout / (name + '.dll'))
                    names.append(name)
                identity = lambda name: dependency_identities.get(name, name + ', Version=1.0.0.0, Culture=neutral, PublicKeyToken=null')
                write(layout / 'CIS.TypeManager.PerformanceHost.types.json', dict(schemaVersion=1, hostAssembly=identity(names[0]),
                    assemblies=[dict(assemblyName=identity(name), relativePath=name + '.dll') for name in names]))
                catalogs[size] = dict(libraries=libraries, classesPerLibrary=classes, generatedClasses=libraries * classes)
            report.update(coreSha256=digest(core), candidateReportSha256=digest(args.candidate_report), moduleSourceInputs=sources,
                          dependencyIdentities=dependency_identities,
                          catalogs=catalogs, fixtureSourceInputs={path.name: digest(path) for path in fixture_sources.glob('*.cs')},
                          generatedSourceInputs={path.relative_to(root).as_posix(): digest(path) for path in sorted((root / 'build').glob('*/Program.cs'))},
                          deployment={path.relative_to(root).as_posix(): digest(path) for path in sorted((root / 'deploy').rglob('*')) if path.is_file()})
            if digest(core) != args.expected_core_sha256:
                raise RuntimeError('Producer Core changed during fixture build')
        else:
            if not args.fixtures or not args.expected_fixtures_sha256:
                raise RuntimeError('Run needs frozen fixtures and an independently selected producer-report hash')
            fixtures = args.fixtures.resolve()
            manifest = fixtures / 'results.json'
            if digest(manifest) != args.expected_fixtures_sha256:
                raise RuntimeError('Fixture producer report hash mismatch')
            inputs = read(manifest)
            if inputs.get('complete') is not True or inputs['operation'] != 'prepare' or inputs['coreSha256'] != args.expected_core_sha256 or inputs['harnessSha256'] != digest(__file__):
                raise RuntimeError('Fixture provenance mismatch')
            current_fixture_sources = {path.name: digest(path) for path in (language / 'tests/fixtures/TypeManager/Performance').glob('*.cs')}
            if inputs['runtimePolicySha256'] != report['runtimePolicySha256'] or inputs['fixtureSourceInputs'] != current_fixture_sources:
                raise RuntimeError('Fixture source or runtime policy differs from the producer')
            def verify():
                actual = {path.relative_to(fixtures).as_posix() for path in (fixtures / 'deploy').rglob('*') if path.is_file()}
                if actual != set(inputs['deployment']):
                    raise RuntimeError('Deployment file inventory mismatch')
                for relative, expected in inputs['deployment'].items():
                    path = (fixtures / relative).resolve()
                    if not path.is_relative_to(fixtures / 'deploy') or digest(path) != expected:
                        raise RuntimeError('Frozen deployment changed: ' + relative)
                for size in inputs['catalogs']:
                    if digest(fixtures / 'deploy' / size / 'CIS.Core.dll') != args.expected_core_sha256:
                        raise RuntimeError('Deployment Core mismatch')
            verify()
            report.update(coreSha256=args.expected_core_sha256, fixtureReportSha256=digest(manifest),
                          deployment=inputs['deployment'], samplesPerGroup=args.samples, warmupRounds=args.warmups,
                          queryIterations=args.iterations, observations=[])
            for round_number in range(args.warmups + args.samples):
                modes = ['eager', 'deferred'] if round_number % 2 == 0 else ['deferred', 'eager']
                catalogs = list(inputs['catalogs'].items())
                if (round_number // 2) % 2:
                    catalogs.reverse()
                for size, catalog in catalogs:
                    for mode in modes:
                        name = size + '-' + mode + '-' + str(round_number).zfill(2)
                        host = fixtures / 'deploy' / size / 'CIS.TypeManager.PerformanceHost.dll'
                        output, wall = invoke(name, 'exec', '--fx-version', policy['targetRuntime'], host,
                            mode, catalog['libraries'], catalog['classesPerLibrary'], args.iterations)
                        sample = json.loads(output)
                        if sample.get('valid') is not True or sample['mode'] != mode or sample['environment']['runtime'] != policy['targetRuntime'] or sample['environment']['processArchitecture'] != 'X64' or sample['environment']['osArchitecture'] != 'X64':
                            raise RuntimeError('Wrong or invalid measurement process')
                        sample.update(catalog=size, round=round_number, warmup=round_number < args.warmups, processWallMilliseconds=wall)
                        report['observations'].append(sample)
                        save()
            verify()
            report['summaries'] = {}
            def distribution(values):
                return dict(n=len(values), minimum=min(values), median=statistics.median(values), maximum=max(values))
            for size in inputs['catalogs']:
                for mode in ['eager', 'deferred']:
                    samples = [sample for sample in report['observations'] if sample['catalog'] == size and sample['mode'] == mode and not sample['warmup']]
                    if len(samples) != args.samples:
                        raise RuntimeError('Wrong measured sample count')
                    metrics = {name + '.' + key: distribution([sample[name][key] for sample in samples])
                               for name in ['bootstrapBoundary', 'firstUse', 'preparation', 'firstPrepared', 'firstUncached', 'preparedCached', 'lazyCached', 'loopControl', 'exactLookup']
                               for key in ['Milliseconds', 'AllocatedBytes']}
                    for name in ['retainedManagedBytes', 'retainedPrivateBytes', 'retainedWorkingSetBytes', 'processWallMilliseconds']:
                        metrics[name] = distribution([sample[name] for sample in samples])
                    metrics['bootstrapPlusFirstUseMilliseconds'] = distribution([sample['bootstrapBoundary']['Milliseconds'] + sample['firstUse']['Milliseconds'] for sample in samples])
                    report['summaries'][size + '-' + mode] = metrics
        report['complete'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        save()
    print(json.dumps(dict(complete=True, operation=args.operation, report=str(root / 'results.json'))))


if __name__ == '__main__':
    main()
