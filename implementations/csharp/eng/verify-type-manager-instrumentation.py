"""Execute test-only instrumentation and three controls; never modify shipping Core.

An isolated source copy wraps the existing GetTypes call and observes the final
publication boundary. Counter/cancellation claims concern that instrumented copy;
the unmodified candidate remains a separate, hashed behavioral/architecture lane.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError('Instrumentation anchor is absent or ambiguous: ' + before)
    return source.replace(before, after)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo-root', type=Path, required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--candidate-core', type=Path, required=True)
    parser.add_argument('--candidate-report', type=Path, required=True,
                        help='Completed runtime-process producer report recording Core bytes and selected source inputs')
    parser.add_argument('--replay-root', type=Path)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    root = args.output_root.resolve()
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output root')
    root.mkdir(parents=True, exist_ok=True)
    (root / 'logs').mkdir()
    language = args.repo_root.resolve() / 'implementations/csharp'
    templates = language / 'tests/fixtures/TypeManager/Instrumentation'
    source_core = language / 'src/CIS.Core'
    factory = source_core / 'TypeManagement/TypeManager.cs'
    report = {'complete': False, 'scope': 'Test-only source instrumentation, not a shipping binary',
              'candidateCoreSha256': digest(args.candidate_core), 'factorySourceSha256': digest(factory),
              'candidateReportSha256': digest(args.candidate_report),
              'verifierSha256': digest(__file__),
              'templateInputs': {path.name: digest(path) for path in sorted(templates.glob('*.cs'))},
              'results': []}
    report_path = root / 'results.json'
    environment = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', MSBUILDDISABLENODEREUSE='1')

    def save():
        report_path.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')

    def run(name, *arguments, expected_error=None):
        result = subprocess.run([args.dotnet, *map(str, arguments)], cwd=root, env=environment,
                                text=True, encoding='utf-8', errors='replace', stdout=subprocess.PIPE,
                                stderr=subprocess.STDOUT)
        (root / 'logs' / (name + '.log')).write_text(result.stdout, encoding='utf-8')
        if expected_error:
            if result.returncode == 0 or expected_error not in result.stdout:
                raise RuntimeError(name + ' did not reject the control for ' + expected_error)
        elif result.returncode:
            raise RuntimeError(name + ' failed; inspect its log')
        return result.stdout

    save()
    try:
        producer = json.loads(args.candidate_report.read_text(encoding='utf-8-sig'))
        if not producer.get('complete') or producer.get('coreSha256') != report['candidateCoreSha256']:
            raise RuntimeError('Candidate producer report does not match the frozen Core bytes')
        if producer.get('mode') != 'build-and-run' or producer.get('verifierSha256') != digest(language / 'tests/fixtures/TypeManager/verify-runtime-processes.py'):
            raise RuntimeError('Candidate report must come from the current build-and-run producer')
        report['sourceProvenanceScope'] = 'Core C# sources and five named project/config files; not all transitive MSBuild inputs; producer report is trusted'
        source_inputs = {str(path.relative_to(source_core)).replace('\\', '/'): digest(path)
                         for path in sorted(source_core.rglob('*.cs'))
                         if not set(path.relative_to(source_core).parts).intersection({'bin', 'obj'})}
        project_inputs = {name: digest(language / name) for name in
                          ['src/CIS.Core/CIS.Core.csproj', 'global.json', 'Directory.Build.props', 'Directory.Packages.props', 'NuGet.config']}
        if source_inputs != producer.get('coreSourceInputs') or project_inputs != producer.get('coreProjectInputs'):
            raise RuntimeError('Candidate source provenance differs from the source selected for instrumentation')
        if report['factorySourceSha256'] != source_inputs['TypeManagement/TypeManager.cs']:
            raise RuntimeError('Factory source changed while capturing provenance')
        report['sourceGitHead'] = producer.get('sourceGitHead')
        for name in ['global.json', 'Directory.Build.props', 'Directory.Packages.props', 'NuGet.config']:
            shutil.copyfile(language / name, root / name)
            if digest(root / name) != project_inputs[name]:
                raise RuntimeError('Project input changed while copying: ' + name)
        report['sdk'] = run('sdk', '--version').strip()
        if report['sdk'] != json.loads((root / 'global.json').read_text(encoding='utf-8-sig'))['sdk']['version']:
            raise RuntimeError('Instrumentation SDK differs from the pin')
        variants = ['instrumented', 'enumerate-twice', 'query-enumeration', 'skip-final-cancellation']
        if args.replay_root:
            original = json.loads((args.replay_root / 'results.json').read_text(encoding='utf-8-sig'))
            if not original['complete'] or any(report[key] != original[key] for key in ['candidateCoreSha256', 'candidateReportSha256', 'factorySourceSha256', 'verifierSha256', 'templateInputs']):
                raise RuntimeError('Replay does not match the completed source/candidate/verifier')
            shutil.copytree(args.replay_root / 'binaries', root / 'binaries')
            report['sourceInputs'] = original['sourceInputs']
            report['instrumentedFactoryHashes'] = original['instrumentedFactoryHashes']
        else:
            report['sourceInputs'] = source_inputs
            report['instrumentedFactoryHashes'] = {}
            for variant in variants:
                variant_root = root / 'generated' / variant
                core = variant_root / 'Core'
                core.mkdir(parents=True)
                for relative in report['sourceInputs']:
                    target = core / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(source_core / relative, target)
                    if digest(target) != source_inputs[relative]:
                        raise RuntimeError('Core source changed while copying: ' + relative)
                project_bytes = (source_core / 'CIS.Core.csproj').read_bytes()
                if hashlib.sha256(project_bytes).hexdigest() != project_inputs['src/CIS.Core/CIS.Core.csproj']:
                    raise RuntimeError('Core project changed while copying')
                project = ET.fromstring(project_bytes)
                for group in project:
                    for item in list(group):
                        if item.tag in ['ProjectReference', 'None']:
                            group.remove(item)
                for item in list(project):
                    if item.tag == 'Import':
                        project.remove(item)
                project.find('./PropertyGroup/IsPackable').text = 'false'
                ET.SubElement(ET.SubElement(project, 'ItemGroup'), 'InternalsVisibleTo', Include='CIS.TypeManager.InstrumentationHost')
                (core / 'Core.csproj').write_bytes(ET.tostring(project, encoding='utf-8'))
                text = (core / 'TypeManagement/TypeManager.cs').read_text(encoding='utf-8')
                enumeration = 'var types = assembly.GetTypes();'
                wrapped = 'var types = TypeManagerTestProbe.Enumerate(assembly);'
                text = replace_once(text, enumeration, wrapped)
                final = 'cancellationToken.ThrowIfCancellationRequested();\n        return manager;'
                text = replace_once(text, final, 'TypeManagerTestProbe.BeforePublication?.Invoke();\n        ' + final)
                if variant == 'enumerate-twice':
                    text = replace_once(text, wrapped, '_ = TypeManagerTestProbe.Enumerate(assembly);\n                ' + wrapped)
                elif variant == 'query-enumeration':
                    anchor = 'PreparationInputs.ValidateContract(contractType);'
                    text = replace_once(text, anchor, anchor + '\n        _ = TypeManagerTestProbe.Enumerate(Assemblies[0]);')
                elif variant == 'skip-final-cancellation':
                    text = replace_once(text, final, 'return manager;')
                (core / 'TypeManagement/TypeManager.cs').write_text(text, encoding='utf-8')
                report['instrumentedFactoryHashes'][variant] = digest(core / 'TypeManagement/TypeManager.cs')
                shutil.copyfile(templates / 'TypeManagerTestProbe.cs', core / 'TypeManagerTestProbe.cs')
                if digest(core / 'TypeManagerTestProbe.cs') != report['templateInputs']['TypeManagerTestProbe.cs']:
                    raise RuntimeError('Probe template changed while copying')
                library = variant_root / 'Library'
                library.mkdir()
                (library / 'Library.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>CIS.TypeManager.ProbeLibrary</AssemblyName></PropertyGroup></Project>', encoding='utf-8')
                shutil.copyfile(templates / 'ProbeLibrary.cs', library / 'ProbeLibrary.cs')
                if digest(library / 'ProbeLibrary.cs') != report['templateInputs']['ProbeLibrary.cs']:
                    raise RuntimeError('Library template changed while copying')
                host = variant_root / 'Host'
                host.mkdir()
                (host / 'Host.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><AssemblyName>CIS.TypeManager.InstrumentationHost</AssemblyName></PropertyGroup><ItemGroup><ProjectReference Include="../Core/Core.csproj"/><ProjectReference Include="../Library/Library.csproj"/></ItemGroup></Project>', encoding='utf-8')
                shutil.copyfile(templates / 'Program.cs', host / 'Program.cs')
                if digest(host / 'Program.cs') != report['templateInputs']['Program.cs']:
                    raise RuntimeError('Host template changed while copying')
                run('build-' + variant, 'build', host / 'Host.csproj', '-c', 'Release', '-m:1', '--nologo')
                shutil.copytree(host / 'bin/Release/net10.0', root / 'binaries' / variant)
        report['binaryFiles'] = {str(path.relative_to(root / 'binaries')).replace('\\', '/'): digest(path)
                                 for path in sorted((root / 'binaries').rglob('*')) if path.is_file()}
        if args.replay_root and report['binaryFiles'] != original['binaryFiles']:
            raise RuntimeError('Instrumented/control replay bytes differ from the completed run')
        for variant, mode, error in [('instrumented', 'enumeration', None),
                                      ('instrumented', 'publication-cancellation', None),
                                      ('enumerate-twice', 'enumeration', 'ENUMERATION_COUNT:'),
                                      ('query-enumeration', 'enumeration', 'QUERY_ENUMERATION:'),
                                      ('skip-final-cancellation', 'publication-cancellation', 'PUBLICATION_CANCELLATION:')]:
            name = variant + '-' + mode
            observed = run(name, root / 'binaries' / variant / 'CIS.TypeManager.InstrumentationHost.dll',
                           mode, root / 'deployments' / name, expected_error=error)
            if error:
                report['results'].append({'case': name, 'passed': True, 'rejectedFor': error})
            else:
                result = json.loads(observed)
                if result.get('success') is not True or result.get('mode') != mode:
                    raise RuntimeError('Invalid instrumentation report: ' + observed)
                report['results'].append({'case': name, 'passed': True, 'observed': result})
            save()
        report['complete'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        save()
    print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
