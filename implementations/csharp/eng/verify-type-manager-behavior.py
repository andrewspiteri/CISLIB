"""Check unpublished frozen consumers, schema fixtures and compatibility controls.

The original API/consumer baseline is never regenerated. Controls compile only
the TypeManagement module in private projects; these are not shipping assemblies.
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
from xml.sax.saxutils import escape


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def same(left, right):
    # Python otherwise equates JSON true and 1.
    return json.dumps(left, sort_keys=True) == json.dumps(right, sort_keys=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo-root', type=Path, required=True)
    parser.add_argument('--candidate-core', type=Path, required=True)
    parser.add_argument('--candidate-report', type=Path, required=True)
    parser.add_argument('--expected-core-sha256', required=True)
    parser.add_argument('--expected-baseline-sha256', required=True)
    parser.add_argument('--api-tool', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--api-dotnet', help='Optional separate dotnet host containing the pinned .NET 8 tool runtime')
    args = parser.parse_args()
    if platform.system() not in ('Windows', 'Linux') or platform.machine().lower() not in ('amd64', 'x86_64'):
        parser.error('Supported qualification lanes are Windows x64 and Linux x64')
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output root')
    root.mkdir(parents=True, exist_ok=True)
    (root / 'logs').mkdir()
    (root / 'cwd').mkdir()
    language = args.repo_root.resolve() / 'implementations/csharp'
    baseline = language / 'verification/contracts/type-manager/v1-behavior-development'
    original = language / 'verification/contracts/type-manager/v1-development'
    core = args.candidate_core.resolve()
    dependencies = ['Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll']
    api_tool = args.api_tool.resolve()
    runtime_policy = read(language / 'eng/toolchain.json')
    report = dict(complete=False, scope='Unpublished development compatibility, not released historical behavior',
                  platform=platform.platform(), verifierSha256=digest(__file__), coreSha256=digest(core),
                  candidateReportSha256=digest(args.candidate_report), apiToolSha256=digest(api_tool),
                  runtimePolicySha256=digest(language / 'eng/toolchain.json'),
                  consumerRuntime=runtime_policy['targetRuntime'], apiRuntime=runtime_policy['toolHostRuntime'], results=[])
    report['dependencies'] = {name: digest(core.parent / name) for name in dependencies}
    allowed = {'PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'TEMP', 'TMP', 'TMPDIR', 'HOME', 'USERPROFILE',
               'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMW6432',
               'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE'}
    environment = {key: value for key, value in os.environ.items() if key.upper() in allowed}
    environment.update(DOTNET_CLI_HOME=str(root / 'dotnet-home'), NUGET_PACKAGES=str(root / 'packages'),
                       NUGET_HTTP_CACHE_PATH=str(root / 'http-cache'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
                       DOTNET_NOLOGO='1', MSBUILDDISABLENODEREUSE='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false',
                       DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false', DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')

    def save():
        (root / 'results.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    def passed(name, **details):
        report['results'].append(dict(case=name, passed=True, **details))
        save()

    def run(name, *arguments, error=None):
        prefix = [args.dotnet]
        if str(arguments[0]) == str(api_tool):
            prefix = [args.api_dotnet or args.dotnet, 'exec', '--fx-version', runtime_policy['toolHostRuntime']]
        elif str(arguments[0]).endswith('.dll'):
            prefix += ['exec', '--fx-version', runtime_policy['targetRuntime']]
        try:
            process = subprocess.run([*prefix, *map(str, arguments)], cwd=root / 'cwd', env=environment,
                                     text=True, encoding='utf-8', errors='replace', capture_output=True, timeout=180)
        except subprocess.TimeoutExpired as error:
            partial = (error.stdout or b'') + (error.stderr or b'')
            if isinstance(partial, bytes):
                partial = partial.decode('utf-8', errors='replace')
            (root / 'logs' / (name + '.log')).write_text(partial + '\nTIMEOUT\n', encoding='utf-8')
            raise
        output = process.stdout + process.stderr
        (root / 'logs' / (name + '.log')).write_text(output, encoding='utf-8')
        if error is not None:
            if process.returncode == 0 or not all(text in output for text in error):
                raise RuntimeError(name + ' did not fail for the specified compatibility defect')
        elif process.returncode:
            raise RuntimeError(name + ' failed; inspect its log')
        return process.stdout

    def verify_baseline(directory, expected_hash):
        if digest(directory / 'baseline-hashes.json') != expected_hash:
            raise RuntimeError('Baseline manifest differs from its independently selected hash')
        hashes = read(directory / 'baseline-hashes.json')
        listed = [item['path'] for item in hashes['files']]
        actual = {path.relative_to(directory).as_posix() for path in directory.rglob('*') if path.is_file() and path != directory / 'baseline-hashes.json'}
        if len(listed) != len(set(listed)) or set(listed) != actual:
            raise RuntimeError('Baseline file inventory differs')
        for item in hashes['files']:
            path = (directory / item['path']).resolve()
            if not path.is_relative_to(directory) or digest(path) != item['sha256']:
                raise RuntimeError('Frozen baseline changed: ' + item['path'])
        return {item['path']: item['sha256'] for item in hashes['files']}

    def runtime(directory, candidate):
        directory.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(candidate, directory / 'CIS.Core.dll')
        for name in dependencies:
            shutil.copyfile(core.parent / name, directory / name)
            if digest(directory / name) != report['dependencies'][name]:
                raise RuntimeError('Dependency copy differs: ' + name)
        if digest(directory / 'CIS.Core.dll') != digest(candidate):
            raise RuntimeError('Candidate copy differs')

    def source_consumer(name, source, candidate, historical=False, error=None):
        directory = root / name
        directory.mkdir()
        shutil.copyfile(source / 'Program.cs', directory / 'Program.cs')
        template = (source / 'Consumer.csproj.template').read_text(encoding='utf-8-sig')
        token = '../reference/CIS.Core.dll' if historical else '@@CORE@@'
        if template.count(token) != 1:
            raise RuntimeError('Unexpected consumer reference template')
        project = directory / 'Consumer.csproj'
        template = template.replace(token, escape(str(candidate), {'"': '&quot;'}))
        if not historical:
            if template.count('@@EXTRA@@') != 1:
                raise RuntimeError('Unexpected extra reference template')
            template = template.replace('@@EXTRA@@', escape(str(baseline / 'compiled/CIS.TypeManager.BehaviorExtra.dll'), {'"': '&quot;'}))
        project.write_text(template, encoding='utf-8')
        run(name + '-build', 'build', project, '-c', 'Release', '-m:1', '--nologo', error=error)
        return directory / 'bin/Release/net10.0'

    def original_consumer(name, directory, candidate):
        runtime(directory, candidate)
        consumer = directory / 'CIS.TypeManager.ContractConsumer.dll'
        manifest = directory / 'consumer.types.json'
        identities = [('CIS.TypeManager.ContractConsumer', consumer.name), ('CIS.Core', 'CIS.Core.dll')]
        manifest.write_text(json.dumps(dict(schemaVersion=1,
            hostAssembly='CIS.TypeManager.ContractConsumer, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null',
            assemblies=[dict(assemblyName=identity + ', Version=1.0.0.0, Culture=neutral, PublicKeyToken=null', relativePath=filename)
                        for identity, filename in identities])), encoding='utf-8')
        observed = json.loads(run(name, consumer, manifest))
        if observed.get('contractConsumer') != 'passed' or observed.get('selectedAssemblyCount') != 2:
            raise RuntimeError('Original consumer did not pass')
        passed(name, consumerSha256=digest(consumer), observed=observed)

    def behavior(name, binaries, candidate, cases, negative_field=None, negative_value=None):
        for case in cases:
            case_name = name + '-' + case['id']
            directory = root / 'runs' / case_name
            directory.mkdir(parents=True)
            for filename in ['CIS.TypeManager.BehaviorConsumer.dll', 'CIS.TypeManager.BehaviorConsumer.deps.json', 'CIS.TypeManager.BehaviorConsumer.runtimeconfig.json', 'CIS.TypeManager.BehaviorExtra.dll']:
                shutil.copyfile(binaries / filename, directory / filename)
            if case.get('assetMode') == 'bad-image':
                (directory / 'CIS.TypeManager.BehaviorExtra.dll').write_bytes(b'Not a managed assembly')
            if case['id'] == 'path-traversal':
                shutil.copyfile(directory / 'CIS.TypeManager.BehaviorConsumer.dll', directory.parent / 'Outside.dll')
            runtime(directory, candidate)
            filename = 'CIS.TypeManager.BehaviorConsumer.types.json' if case['mode'] == 'default' else 'input.types.json'
            manifest = directory / filename
            if case['input'] is not None:
                shutil.copyfile(baseline / 'cases' / case['input'], manifest)
            consumer = directory / 'CIS.TypeManager.BehaviorConsumer.dll'
            observed = json.loads(run(case_name, consumer, case['mode'], manifest))
            (directory / 'observed.json').write_text(json.dumps(observed, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
            equal = same(observed, case['expected'])
            if negative_field is None and not equal:
                raise RuntimeError(case_name + ' differs from the frozen behavior expectation')
            if negative_field is not None:
                actual, expected = observed, case['expected']
                for key in negative_field.split('.'):
                    actual, expected = actual[key], expected[key]
                if equal or not same(actual, negative_value) or same(actual, expected):
                    raise RuntimeError(case_name + ' missed its specified deliberate regression')
            passed(case_name, consumerSha256=digest(consumer), candidateSha256=digest(candidate),
                   observedSha256=digest(directory / 'observed.json'), regressionDetected=negative_field is not None,
                   regressionField=negative_field, regressionValue=negative_value)

    def module(name, changes):
        directory = root / 'controls' / name
        directory.mkdir(parents=True)
        hits = [0] * len(changes)
        module_root = language / 'src/CIS.Core/TypeManagement'
        for path in sorted(module_root.rglob('*.cs')):
            text = path.read_text(encoding='utf-8-sig')
            for index, (filename, before, after) in enumerate(changes):
                if path.name == filename or filename == '*':
                    if filename != '*' and text.count(before) != 1:
                        raise RuntimeError('Mutation anchor is not unique: ' + filename)
                    hits[index] += text.count(before)
                    if before == after:
                        raise RuntimeError('No-op mutation')
                    text = text.replace(before, after)
            destination = directory / path.relative_to(module_root)
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(text, encoding='utf-8')
        if any(count == 0 for count in hits):
            raise RuntimeError('Mutation anchor was not found')
        (directory / 'Core.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>CIS.Core</AssemblyName><AssemblyVersion>1.0.0.0</AssemblyVersion><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>', encoding='utf-8')
        run(name + '-build', 'build', directory / 'Core.csproj', '-c', 'Release', '-m:1', '--nologo')
        binary = directory / 'bin/Release/net10.0/CIS.Core.dll'
        report.setdefault('controls', {})[name] = dict(binarySha256=digest(binary),
            mutations=[dict(file=change[0], before=change[1], after=change[2], occurrences=count) for change, count in zip(changes, hits)])
        return binary

    save()
    try:
        original_hash = '4fd1124533971344d0ca362056450553c5eb0cea2c46e28780cc6301d1ed49d1'
        report['originalBaseline'] = verify_baseline(original, original_hash)
        report['behaviorBaseline'] = verify_baseline(baseline, args.expected_baseline_sha256)
        report['originalBaselineManifestSha256'] = digest(original / 'baseline-hashes.json')
        report['behaviorBaselineManifestSha256'] = digest(baseline / 'baseline-hashes.json')
        producer = read(args.candidate_report)
        if digest(core) != args.expected_core_sha256 or producer.get('complete') is not True or producer.get('coreSha256') != digest(core):
            raise RuntimeError('Candidate provenance does not identify this completed runtime candidate')
        report['moduleSourceInputs'] = {}
        for path in sorted((language / 'src/CIS.Core/TypeManagement').rglob('*.cs')):
            relative = path.relative_to(language / 'src/CIS.Core').as_posix()
            if producer['coreSourceInputs'].get(relative) != digest(path):
                raise RuntimeError('Control source differs from recorded candidate source: ' + relative)
            report['moduleSourceInputs'][relative] = digest(path)
        if set(report['moduleSourceInputs']) != {path for path in producer['coreSourceInputs'] if path.startswith('TypeManagement/')}:
            raise RuntimeError('The module source file set differs from the candidate record')
        shutil.copyfile(language / 'global.json', root / 'global.json')
        for filename in ['Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props']:
            (root / filename).write_text('<Project />', encoding='utf-8')
        (root / 'Directory.Build.props').write_text('<Project><PropertyGroup><RestoreConfigFile>' +
            escape(str(root / 'NuGet.Config')) + '</RestoreConfigFile></PropertyGroup></Project>', encoding='utf-8')
        (root / 'Directory.Build.rsp').write_text('', encoding='utf-8')
        (root / 'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>', encoding='utf-8')
        report['sdk'] = run('sdk', '--version').strip()
        if report['sdk'] != read(root / 'global.json')['sdk']['version']:
            raise RuntimeError('Wrong SDK')
        report['apiToolVersion'] = run('api-tool-version', api_tool, '--version').strip()
        if not report['apiToolVersion'].startswith('10.0.401+'):
            raise RuntimeError('Wrong API compatibility tool')
        api_arguments = [api_tool, '-l', original / 'reference/CIS.Core.dll', '-r', core,
                         '--enable-rule-cannot-change-parameter-name', '--enable-rule-attributes-must-match']
        run('api', *api_arguments)
        passed('api')
        frozen = root / 'historical-frozen'
        shutil.copytree(original / 'compiled', frozen)
        original_consumer('historical-frozen', frozen, core)
        if digest(frozen / 'CIS.TypeManager.ContractConsumer.dll') != report['originalBaseline']['compiled/CIS.TypeManager.ContractConsumer.dll']:
            raise RuntimeError('Historical consumer changed')
        historical_source = source_consumer('historical-source', original / 'source', core, historical=True)
        original_consumer('historical-source', historical_source, core)
        cases = read(baseline / 'cases.json')['cases']
        if len(cases) != 22 or len({case['id'] for case in cases}) != 22:
            raise RuntimeError('Unexpected behavior case inventory')
        for case in cases:
            if not re.fullmatch('[a-z0-9-]+', case['id']) or case['mode'] not in {'manifest', 'default', 'relative', 'prepared', 'arguments', 'cancel', 'selected'}:
                raise RuntimeError('Invalid behavior case')
            if case['input'] is not None and not (baseline / 'cases' / case['input']).resolve().is_relative_to(baseline / 'cases'):
                raise RuntimeError('Fixture input escapes baseline')
            if case.get('assetMode') not in (None, 'bad-image'):
                raise RuntimeError('Unknown fixture asset mode')
        original_api_output = source_consumer('behavior-original-api', baseline / 'source', original / 'reference/CIS.Core.dll')
        if digest(original_api_output / 'CIS.TypeManager.BehaviorConsumer.dll') != report['behaviorBaseline']['compiled/CIS.TypeManager.BehaviorConsumer.dll']:
            raise RuntimeError('Original API source rebuild differs from the frozen consumer binary')
        passed('behavior-original-api-build', exactFrozenBinary=True)
        behavior('frozen', baseline / 'compiled', core, cases)
        source_output = source_consumer('behavior-source', baseline / 'source', core)
        behavior('source', source_output, core, cases)
        positive = module('positive', [])
        behavior('module-positive', baseline / 'compiled', positive, cases)
        run('module-api-positive', api_tool, '-l', original / 'reference/CIS.Core.dll', '-r', positive,
            '--enable-rule-cannot-change-parameter-name', '--enable-rule-attributes-must-match')
        passed('module-api-positive')
        module_frozen = root / 'module-historical-frozen'
        shutil.copytree(original / 'compiled', module_frozen)
        original_consumer('module-binary-positive', module_frozen, positive)
        module_source = source_consumer('module-historical-source', original / 'source', positive, historical=True)
        original_consumer('module-source-positive', module_source, positive)
        mutations = [
            ('failure-value', [('TypeManagerFailureKind.cs', 'InvalidInventory = 1', 'InvalidInventory = 99')], 'schema-1', 'failureValues.InvalidInventory', 99),
            ('accept-new-schema', [('AssemblyInventory.cs', 'if (number != 1)', 'if (number != 1 && number != 2)')], 'schema-new', 'outcome.status', 'success'),
            ('public-only', [('TypeManager.cs', 'candidate.IsClass && !candidate.IsAbstract', 'candidate.IsClass && candidate.IsPublic && !candidate.IsAbstract')], 'schema-1', 'outcome.markers', []),
            ('mutable-query', [('TypeManager.cs', 'result = Array.AsReadOnly(matches.ToArray());', 'result = matches.ToArray();')], 'schema-1', 'outcome.collectionsProtected', False),
            ('lookup-loads', [('TypeManager.cs', 'return exactNames.TryGetValue(assemblyQualifiedTypeName, out type);', 'type = Type.GetType(assemblyQualifiedTypeName); return type is not null;')], 'schema-1', 'outcome.lookupDoesNotLoad', False),
            ('culture-order', [('TypeManager.cs', 'int comparison = StringComparer.Ordinal.Compare(left!.AssemblyQualifiedName, right!.AssemblyQualifiedName);', 'int comparison = StringComparer.CurrentCulture.Compare(left!.AssemblyQualifiedName, right!.AssemblyQualifiedName);')], 'schema-1', 'outcome.markers', ['CompatibilityFixtures.ÉCandidate', 'CompatibilityFixtures.ZCandidate']),
            ('cwd-relative', [('PreparationInputs.cs', 'AppContext.BaseDirectory', 'Environment.CurrentDirectory')], 'relative-path', 'outcome.status', 'failure'),
            ('duplicate-property', [('AssemblyInventory.cs', 'if (!names.Add(property.Name))', 'if (!names.Add(property.Name) && property.Name.Length < 0)')], 'duplicate-root-property', 'outcome.status', 'success'),
            ('path-traversal', [('AssemblyInventory.cs', 'segment is "." or ".."', 'segment == "."'), ('AssemblyInventory.cs', "segment.EndsWith('.')", "(segment != \"..\" && segment.EndsWith('.'))"), ('AssemblyInventory.cs', 'if (Path.IsPathRooted(check) || check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))', 'if (Path.IsPathRooted(check))')], 'path-traversal', 'outcome.status', 'success')]
        for name, changes, case_id, field, value in mutations:
            mutant = module(name, changes)
            behavior(name, baseline / 'compiled', mutant, [case for case in cases if case['id'] == case_id], negative_field=field, negative_value=value)
            if name == 'failure-value':
                behavior('failure-value-thrown', baseline / 'compiled', mutant, [case for case in cases if case['id'] == 'malformed'], negative_field='outcome.value', negative_value=99)
        broken = module('renamed-public-property', [('*', 'AssemblyManifestPath', 'RenamedManifestPath')])
        run('api-negative', api_tool, '-l', original / 'reference/CIS.Core.dll', '-r', broken,
            '--enable-rule-cannot-change-parameter-name', '--enable-rule-attributes-must-match', error=['AssemblyManifestPath'])
        passed('api-negative')
        binary_negative = root / 'binary-negative'
        shutil.copytree(frozen, binary_negative)
        runtime(binary_negative, broken)
        run('binary-negative', binary_negative / 'CIS.TypeManager.ContractConsumer.dll', binary_negative / 'consumer.types.json',
            error=['MissingMethodException', 'AssemblyManifestPath'])
        passed('binary-negative')
        source_consumer('source-negative', original / 'source', broken, historical=True, error=['CS0117', 'AssemblyManifestPath'])
        passed('source-negative')
        # Recheck frozen inputs and candidate bytes after every build/control.
        if report['behaviorBaseline'] != verify_baseline(baseline, args.expected_baseline_sha256) or report['originalBaseline'] != verify_baseline(original, original_hash) or report['coreSha256'] != digest(core) or report['dependencies'] != {name: digest(core.parent / name) for name in dependencies}:
            raise RuntimeError('Qualification modified a frozen input')
        report['complete'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        save()
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
