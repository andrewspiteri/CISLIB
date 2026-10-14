import json
from pathlib import Path
import subprocess
import sys

repo = Path(__file__).resolve().parents[3]
work = Path(__file__).resolve().parent
producer = repo / '.artifacts/type-manager/runtime-acceptance/process-windows-qualified2/results.json'
wrong = json.loads(producer.read_text())
del wrong['coreSourceInputs']['TypeManagement/TypeManager.cs']
(work / 'wrong-producer.json').write_text(json.dumps(wrong))
base = [sys.executable, str(repo / 'implementations/csharp/eng/verify-type-manager-behavior.py'), '--repo-root', str(repo),
        '--candidate-core', str(producer.parent / 'Host/bin/Release/net10.0/CIS.Core.dll'),
        '--candidate-report', str(producer), '--expected-core-sha256', 'd70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09',
        '--expected-baseline-sha256', 'd5d26e9f528b3306b8437b9564da6797d660a191dc1f04162d5475ac283e4228',
        '--api-tool', str(repo / '.artifacts/csharp/wp01-s03-discovery/packages/microsoft.dotnet.apicompat.tool/10.0.401/tools/net8.0/any/Microsoft.DotNet.ApiCompat.Tool.dll')]
checks = [
    ('wrong-core-pin', '--expected-core-sha256', '0' * 64, 'Candidate provenance does not identify'),
    ('wrong-baseline-pin', '--expected-baseline-sha256', '0' * 64, 'Baseline manifest differs'),
    ('missing-source-record', '--candidate-report', str(work / 'wrong-producer.json'), 'Control source differs')]
results = []
for name, flag, value, diagnostic in checks:
    arguments = base.copy()
    arguments[arguments.index(flag) + 1] = value
    output = work / 'guards-final' / name
    process = subprocess.run([*arguments, '--output-root', str(output)], cwd=repo, capture_output=True, text=True, timeout=30)
    (work / (name + '.log')).write_text(process.stdout + process.stderr, encoding='utf-8')
    report = json.loads((output / 'results.json').read_text())
    assert process.returncode != 0 and report['complete'] is False and report['results'] == [] and diagnostic in report['error'], name
    results.append(dict(case=name, passed=True, diagnostic=report['error'], exitCode=process.returncode, checksBeforeRejection=0))
(work / 'guard-results.json').write_text(json.dumps(dict(complete=True, results=results), indent=2) + '\n', encoding='utf-8')
print(json.dumps(results, indent=2))
