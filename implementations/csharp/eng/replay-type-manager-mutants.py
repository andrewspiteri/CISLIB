"""Replay native Stryker mutations individually without instrumentation.

Native reports remain immutable. This diagnoses exclusions/coverage; completing
the experiment does not qualify the mutation gate or approve any disposition.
"""
import argparse
import collections
import hashlib
import json
import os
from pathlib import Path, PureWindowsPath
import platform
import re
import shutil
import signal
import subprocess
import time
import xml.etree.ElementTree as ET


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pinned_json(path, expected):
    data = path.read_bytes()
    if hashlib.sha256(data).hexdigest() != expected:
        raise ValueError('Pinned input changed: ' + str(path))
    return json.loads(data.decode('utf-8-sig'))


def native_cases(path):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    tree = ET.parse(path)
    if tree.getroot().tag != '{' + ns['t'] + '}TestRun':
        raise ValueError('Unexpected native test report schema')
    defined_ids = [item.attrib['id'] for item in tree.findall('./t:TestDefinitions/t:UnitTest', ns)]
    definitions = set(defined_ids)
    if len(defined_ids) != len(definitions):
        raise ValueError('Duplicate native test definitions')
    rows = tree.findall('./t:Results/t:UnitTestResult', ns)
    ids = [row.attrib['testId'] for row in rows]
    if len(ids) != len(set(ids)) or set(ids) != definitions:
        raise ValueError('Missing or duplicate native test identities')
    counts = collections.Counter(row.attrib['outcome'] for row in rows)
    if set(counts) - {'Passed', 'Failed', 'NotExecuted'}:
        raise ValueError('Unexpected native test outcome')
    counters = tree.find('./t:ResultSummary/t:Counters', ns)
    summary = tree.find('./t:ResultSummary', ns)
    if summary is None or summary.attrib.get('outcome') not in ('Completed', 'Failed'):
        raise ValueError('Native test run did not complete')
    for field, expected in [('total', len(rows)), ('passed', counts['Passed']),
                            ('failed', counts['Failed']), ('notExecuted', counts['NotExecuted']),
                            ('executed', counts['Passed'] + counts['Failed']), ('error', 0),
                            ('timeout', 0), ('aborted', 0), ('passedButRunAborted', 0)]:
        if counters is None or int(counters.attrib[field]) != expected:
            raise ValueError('Native counters do not match case outcomes')
    return [dict(id=row.attrib['testId'], name=row.attrib['testName'], outcome=row.attrib['outcome'],
                 output=row.findtext('./t:Output/t:StdOut', default='', namespaces=ns),
                 error=row.findtext('./t:Output/t:ErrorInfo/t:Message', default='', namespaces=ns)) for row in rows]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prepared-root', type=Path, required=True)
    parser.add_argument('--expected-preparation-sha256', required=True)
    parser.add_argument('--native-report', type=Path, required=True)
    parser.add_argument('--expected-native-sha256', required=True)
    parser.add_argument('--output-root', type=Path, required=True)
    parser.add_argument('--syntax-root', type=Path, required=True)
    parser.add_argument('--expected-syntax-sha256', required=True)
    parser.add_argument('--ids', help='Optional comma-separated explicit IDs for controls; default replays every CompileError')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    if platform.system() not in ('Windows', 'Linux'):
        parser.error('Only Windows and Linux qualification are supported')
    prepared = args.prepared_root.resolve()
    preparation = pinned_json(prepared / 'preparation.json', args.expected_preparation_sha256)
    native = pinned_json(args.native_report, args.expected_native_sha256)
    syntax = pinned_json(args.syntax_root / 'manifest.json', args.expected_syntax_sha256)
    if syntax['nativeReportSha256'] != args.expected_native_sha256:
        raise ValueError('Syntax reconstruction belongs to a different native report')
    original = prepared / 'source'
    files = {p.relative_to(original).as_posix(): digest(p) for p in original.rglob('*') if p.is_file()}
    if files != preparation['generatedFiles'] or preparation.get('prepared') is not True:
        raise ValueError('Prepared source inventory differs')
    mutations = {}
    for file, entry in native['files'].items():
        name = PureWindowsPath(file).name
        path = original / 'Core/TypeManagement' / name
        if path.read_bytes().decode('utf-8-sig') != entry['source'] or digest(path) != syntax['sourceFiles'][name]:
            raise ValueError('Native report source differs: ' + name)
        for mutant in entry['mutants']:
            key = str(mutant['id'])
            if key in mutations:
                raise ValueError('Duplicate native mutant ID')
            mutations[key] = (name, entry['source'], mutant)
    for file, entry in native['testFiles'].items():
        if (original / 'Tests' / PureWindowsPath(file).name).read_bytes().decode('utf-8-sig') != entry['source']:
            raise ValueError('Native report test source differs: ' + file)
    selected = args.ids.split(',') if args.ids else [key for key, (_, _, m) in mutations.items() if m['status'] == 'CompileError']
    if not selected or len(selected) != len(set(selected)) or any(key not in mutations for key in selected):
        raise ValueError('Invalid/empty mutant selection')
    if len(mutations) != 483 or sum(m['status'] == 'CompileError' for _, _, m in mutations.values()) != 238:
        raise ValueError('Unexpected native mutation scope')
    if set(syntax['mutants']) != set(mutations) or set(syntax['sourceFiles']) != {name for name, _, _ in mutations.values()} | {'TypeManagerFailureKind.cs'}:
        raise ValueError('Syntax/native source or mutant coverage differs')
    if {p.name: digest(p) for p in (original / 'Core/TypeManagement').glob('*.cs')} != syntax['sourceFiles']:
        raise ValueError('Complete syntax source inventory differs')
    if set(syntax['controls']) != set(syntax['sourceFiles']):
        raise ValueError('Missing normalized source controls')
    for name, expected in syntax['controls'].items():
        if digest(args.syntax_root / 'controls' / name) != expected:
            raise ValueError('Normalized control changed: ' + name)
    for key in selected:
        if syntax['mutants'][key]['file'] != mutations[key][0] or syntax['mutants'][key]['mutator'] != mutations[key][2]['mutatorName']:
            raise ValueError('Syntax mutation identity differs: ' + key)
        if digest(args.syntax_root / (key + '.cs')) != syntax['mutants'][key]['sha256']:
            raise ValueError('Regenerated syntax changed: ' + key)
    root = args.output_root.resolve()
    if root.is_relative_to(prepared) or prepared.is_relative_to(root):
        raise ValueError('Output must not overlap prepared inputs')
    if root.exists() and any(root.iterdir()):
        parser.error('Use an empty output directory')
    root.mkdir(parents=True, exist_ok=True)
    source = root / 'source'
    shutil.copytree(original, source)
    (root / 'logs').mkdir()
    policy = json.loads((source / 'toolchain.json').read_text(encoding='utf-8-sig'))
    allowed = {'PATH', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'TEMP', 'TMP', 'TMPDIR', 'HOME', 'USERPROFILE',
               'APPDATA', 'LOCALAPPDATA', 'PROGRAMFILES', 'PROGRAMFILES(X86)', 'PROGRAMW6432',
               'DOTNET_ROOT', 'DOTNET_ROOT_X64', 'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE'}
    environment = {key: value for key, value in os.environ.items() if key.upper() in allowed}
    environment.update(MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_TELEMETRY_OPTOUT='1',
                       DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true')
    report = dict(complete=False, qualified=False, scope='Individual source-module replay; native statuses are not rewritten',
                  runnerSha256=digest(Path(__file__)), preparationSha256=args.expected_preparation_sha256,
                  nativeReportSha256=args.expected_native_sha256, syntaxManifestSha256=args.expected_syntax_sha256,
                  selectedIds=selected, results=[])

    def save():
        (root / 'results.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')

    def run(name, arguments, timeout=None):
        if timeout is None:
            timeout = report.get('buildTimeoutSeconds', 90)
        log_path = root / 'logs' / (name + '.log')
        log = log_path.open('wb')
        options = dict(cwd=source, env=environment, stdout=log, stderr=subprocess.STDOUT)
        if os.name == 'nt':
            options['creationflags'] = subprocess.CREATE_NO_WINDOW
        else:
            options['start_new_session'] = True
        try:
            child = subprocess.Popen([args.dotnet, *map(str, arguments)], **options)
        except BaseException:
            log.close()
            raise
        timed_out = False
        def stop():
            if os.name == 'nt':
                if child.poll() is None:
                    subprocess.run(['taskkill', '/PID', str(child.pid), '/T', '/F'], capture_output=True, check=False, timeout=15)
            else:
                try:
                    os.killpg(child.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
        try:
            child.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            stop()
            child.wait(timeout=15)
        except BaseException:
            stop()
            raise
        finally:
            log.close()
        text = log_path.read_bytes().decode('utf-8', errors='replace')
        return child.returncode, timed_out, text

    def tests(name):
        directory = root / 'tests' / name
        deployment = source / 'Tests/bin/Release/net10.0'
        if 'testDeploymentFiles' in report and report['testDeploymentFiles'] != {
                p.relative_to(deployment).as_posix(): digest(p) for p in deployment.rglob('*')
                if p.is_file() and p.relative_to(deployment).as_posix() not in
                ('CIS.Core.dll', 'CIS.Core.pdb', 'CIS.Core.UnitTests.dll', 'CIS.Core.UnitTests.pdb')}:
            raise RuntimeError('Test dependency deployment changed: ' + name)
        expected_core = digest(deployment / 'CIS.Core.dll')
        code, timed_out, text = run(name, ['vstest', source / 'Tests/bin/Release/net10.0/CIS.Core.UnitTests.dll',
                                          '--logger:trx', '--ResultsDirectory:' + str(directory)], timeout=report.get('testTimeoutSeconds', 60))
        if timed_out:
            return 'Timeout', [], text
        reports = list(directory.glob('*.trx'))
        if len(reports) != 1:
            if name.startswith('mutant-'):
                return 'IncompleteTestRun', [], text
            raise RuntimeError('Expected exactly one native test report: ' + name)
        try:
            cases = native_cases(reports[0])
        except (ValueError, KeyError, ET.ParseError):
            if name.startswith('mutant-'):
                return 'IncompleteTestRun', [], text
            raise
        if len(cases) != preparation['expectedBaselineCases'] or any(c['outcome'] == 'NotExecuted' for c in cases):
            if name.startswith('mutant-'):
                return 'IncompleteTestRun', cases, text
            raise RuntimeError('Incomplete or skipped native test cases: ' + name)
        if 'baselineCases' in report and {(c['id'], c['name']) for c in cases} != {(c['id'], c['name']) for c in report['baselineCases']}:
            raise RuntimeError('Native test identity set changed: ' + name)
        failed = any(c['outcome'] == 'Failed' for c in cases)
        if (code != 0) != failed:
            raise RuntimeError('Test exit code contradicts native cases: ' + name)
        identities = [json.loads(line) for c in cases for line in c['output'].splitlines() if line.strip().startswith('{"runtime":')]
        if len(identities) != 10 or any(i['runtime'] != '.NET ' + policy['targetRuntime'] or i['architecture'] != 'X64' or
                                      i['os'] != platform.system().lower() or i['coreSha256'] != expected_core for i in identities):
            raise RuntimeError('Executed runtime/OS/architecture/Core differs: ' + name)
        return ('Killed' if failed else 'Survived'), cases, text

    save()
    try:
        code, timeout, version = run('sdk', ['--version'])
        if code or timeout or version.strip() != policy['sdk']:
            raise RuntimeError('SDK differs from pin')
        report['sdk'] = version.strip()
        build = ['build', 'Tests/Tests.csproj', '-c', 'Release', '-m:1', '--no-incremental', '-p:UseSharedCompilation=false']
        relaxed_flags = ['-p:TreatWarningsAsErrors=false', '-p:WarningsAsErrors=', '-p:EnableNETAnalyzers=false',
                         '-p:EnforceCodeStyleInBuild=false', '-p:RunAnalyzers=false', '-p:DiscoverEditorConfigFiles=false',
                         '-p:DiscoverGlobalAnalyzerConfigFiles=false', '-p:GenerateMSBuildEditorConfigFile=false',
                         '-p:MSTestAnalysisMode=']
        started = time.monotonic()
        code, timeout, _ = run('baseline-build', build)
        report['baselineBuildSeconds'] = time.monotonic() - started
        report['buildTimeoutSeconds'] = max(90, report['baselineBuildSeconds'] * 5 + 20)
        if code or timeout:
            raise RuntimeError('Baseline build failed')
        started = time.monotonic()
        outcome, cases, _ = tests('baseline')
        report['baselineSeconds'] = time.monotonic() - started
        report['testTimeoutSeconds'] = max(45, report['baselineSeconds'] * 5 + 20)
        if outcome != 'Survived':
            raise RuntimeError('Unmutated baseline did not pass')
        report['baselineCases'] = cases
        identities = [json.loads(line) for c in cases for line in c['output'].splitlines() if line.strip().startswith('{"runtime":')]
        if len(identities) != 10 or any(i['runtime'] != '.NET ' + policy['targetRuntime'] or i['architecture'] != 'X64' or i['os'] != platform.system().lower() for i in identities):
            raise RuntimeError('Baseline runtime/OS/architecture differs')
        report['runtimeIdentities'] = identities
        deployment = source / 'Tests/bin/Release/net10.0'
        baseline_files = {p.relative_to(deployment).as_posix(): digest(p) for p in deployment.rglob('*') if p.is_file()}
        report['baselineDeploymentFiles'] = baseline_files
        fixed = {p.relative_to(deployment).as_posix(): digest(p) for p in deployment.rglob('*')
                 if p.is_file() and p.relative_to(deployment).as_posix() not in ('CIS.Core.dll', 'CIS.Core.pdb', 'CIS.Core.UnitTests.dll', 'CIS.Core.UnitTests.pdb')}
        report['testDeploymentFiles'] = fixed
        def relaxed_control(name):
            expected = {file: digest(deployment / file) for file in ('CIS.Core.dll', 'CIS.Core.UnitTests.dll')}
            code, timeout, _ = run('relaxed-control-' + name, build + ['--no-restore', *relaxed_flags])
            if code or timeout:
                raise RuntimeError('Relaxed unmutated control did not build: ' + name)
            if expected != {file: digest(deployment / file) for file in expected}:
                raise RuntimeError('Relaxed configuration changed unmutated control binaries: ' + name)
            return expected

        report['relaxedBaselineFiles'] = relaxed_control('baseline')
        report['relaxedConfigurationAudit'] = {}
        for project in ('Core', 'Tests'):
            code, timeout, output = run('relaxed-config-' + project, ['msbuild', project + '/' + project + '.csproj',
                '-t:Compile', '-p:Configuration=Release', '-p:UseSharedCompilation=false',
                '-getItem:EditorConfigFiles,GlobalAnalyzerConfigFiles', *relaxed_flags])
            if code or timeout:
                raise RuntimeError('Relaxed compiler configuration audit failed: ' + project)
            items = json.loads(output)['Items']
            if items != {'EditorConfigFiles': [], 'GlobalAnalyzerConfigFiles': []}:
                raise RuntimeError('Relaxed compilation retains analyzer configuration: ' + project)
            report['relaxedConfigurationAudit'][project] = items
        report['normalizedControls'] = []
        for name, expected in syntax['controls'].items():
            path = source / 'Core/TypeManagement' / name
            baseline_bytes = path.read_bytes()
            try:
                path.write_bytes((args.syntax_root / 'controls' / name).read_bytes())
                code, timeout, _ = run('control-build-' + name, build + ['--no-restore'])
                if code or timeout:
                    raise RuntimeError('Normalized control must build strict-clean: ' + name)
                outcome, control_cases, _ = tests('control-' + name)
                if outcome != 'Survived':
                    raise RuntimeError('Normalized control did not pass: ' + name)
                report['normalizedControls'].append(dict(file=name, sourceSha256=expected, passed=len(control_cases),
                                                       binarySha256=digest(deployment / 'CIS.Core.dll'),
                                                       relaxedFiles=relaxed_control(name)))
                save()
                print(json.dumps(dict(control=name, passed=len(control_cases))), flush=True)
            finally:
                path.write_bytes(baseline_bytes)
                if digest(path) != files['Core/TypeManagement/' + name]:
                    raise RuntimeError('Control source restore failed: ' + name)
        for key in selected:
            name, _, mutant = mutations[key]
            changed = (args.syntax_root / (key + '.cs')).read_bytes()
            path = source / 'Core/TypeManagement' / name
            baseline_bytes = path.read_bytes()
            evidence = root / 'mutants' / key
            evidence.mkdir(parents=True)
            (evidence / name).write_bytes(changed)
            item = dict(id=key, file=name, nativeStatus=mutant['status'], location=mutant['location'],
                        originalText=syntax['mutants'][key]['originalText'], replacement=mutant['replacement'], sourceSha256=digest(evidence / name),
                        mutatorName=mutant['mutatorName'], nativeReason=mutant.get('statusReason'), syntax=syntax['mutants'][key])
            try:
                path.write_bytes((evidence / name).read_bytes())
                code, timeout, output = run('build-' + key, build + ['--no-restore'])
                if timeout:
                    raise RuntimeError('Compiler timeout; not a mutant outcome')
                if code:
                    codes = sorted(set(re.findall(r'error ((?:CS|CA|IDE)\d+)', output)))
                    if not codes:
                        raise RuntimeError('Build infrastructure failed for mutant ' + key)
                    item.update(strictBuildDiagnostics=codes)
                    relaxed, expired, relaxed_output = run('relaxed-build-' + key, build + ['--no-restore', *relaxed_flags])
                    if expired:
                        raise RuntimeError('Relaxed compiler timeout')
                    if relaxed:
                        language_codes = sorted(set(re.findall(r'error (CS\d+)', relaxed_output)))
                        if not language_codes:
                            raise RuntimeError('Relaxed build infrastructure failed for mutant ' + key)
                        item.update(outcome='CompilationRejected', diagnostics=language_codes, rejection='Persists with analyzers and warnings-as-errors disabled')
                    else:
                        item.update(strictPolicyRejected=True)
                        code = 0
                if code == 0:
                    for extension in ('dll', 'pdb'):
                        built = source / ('Core/bin/Release/net10.0/CIS.Core.' + extension)
                        shutil.copyfile(built, deployment / built.name)
                        shutil.copyfile(built, evidence / built.name)
                        if digest(built) != digest(deployment / built.name):
                            raise RuntimeError('Deployed mutant differs from built bytes')
                    item['binarySha256'] = digest(evidence / 'CIS.Core.dll')
                    item['normalizedControlBinarySha256'] = next(c['binarySha256'] for c in report['normalizedControls'] if c['file'] == name)
                    item['testAssemblySha256'] = digest(deployment / 'CIS.Core.UnitTests.dll')
                    item['testAssemblyChanged'] = item['testAssemblySha256'] != baseline_files['CIS.Core.UnitTests.dll']
                    outcome, cases, _ = tests('mutant-' + key)
                    item.update(outcome=outcome, failedTests=[c for c in cases if c['outcome'] == 'Failed'])
                    if outcome == 'Killed':
                        repeated, repeated_cases, _ = tests('mutant-' + key + '-repeat')
                        if repeated != 'Killed' or {c['name'] for c in cases if c['outcome'] == 'Failed'} != {c['name'] for c in repeated_cases if c['outcome'] == 'Failed'}:
                            item['outcome'] = 'UnstableTestOutcome'
                        item['repeatOutcome'] = repeated
            finally:
                path.write_bytes(baseline_bytes)
                if digest(path) != files['Core/TypeManagement/' + name]:
                    raise RuntimeError('Source restore failed: ' + name)
            report['results'].append(item)
            save()
            print(json.dumps(dict(done=len(report['results']), total=len(selected), id=key, outcome=item['outcome'])), flush=True)
        for relative, expected in files.items():
            if digest(source / relative) != expected:
                raise RuntimeError('Source input changed: ' + relative)
        code, timeout, _ = run('final-build', build + ['--no-restore'])
        if code or timeout or tests('final')[0] != 'Survived':
            raise RuntimeError('Final unmutated build/test failed')
        if baseline_files != {p.relative_to(deployment).as_posix(): digest(p) for p in deployment.rglob('*') if p.is_file()}:
            raise RuntimeError('Final unmutated deployment differs from baseline')
        report['summary'] = dict(collections.Counter(item['outcome'] for item in report['results']))
        report['complete'] = True
    except Exception as error:
        report['error'] = str(error)
        raise
    finally:
        save()


if __name__ == '__main__':
    main()
