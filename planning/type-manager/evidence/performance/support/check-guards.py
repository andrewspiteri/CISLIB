import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
from unittest.mock import patch

repo = Path(__file__).resolve().parents[3]
work = Path(__file__).resolve().parent
tool = repo / 'implementations/csharp/eng/measure-type-manager.py'
fixtures = work / 'fixtures-native'
pin = 'eb6ea41970b95955fdd783f6b85bed88076f60ee587a6182ec1b9bdb13142e7c'
core = 'd70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09'
base = [str(tool), 'run', '--repo-root', str(repo), '--fixtures', str(fixtures),
        '--expected-fixtures-sha256', pin, '--expected-core-sha256', core]
clone = work / 'guard-fixtures-native'
clone.mkdir()
shutil.copyfile(fixtures / 'results.json', clone / 'results.json')
shutil.copytree(fixtures / 'deploy', clone / 'deploy')
asset = clone / 'deploy/small/Perf.small.Library00.dll'
asset.write_bytes(asset.read_bytes() + b'controlled mismatch')
cases = [('wrong-fixture-pin', '--expected-fixtures-sha256', '0' * 64, 'Fixture producer report hash mismatch'),
         ('wrong-core-pin', '--expected-core-sha256', '0' * 64, 'Fixture provenance mismatch'),
         ('altered-asset', '--fixtures', str(clone), 'Frozen deployment changed')]
results = []
for name, flag, value, expected in cases:
    arguments = base.copy()
    arguments[arguments.index(flag) + 1] = value
    output = work / 'guards-native' / name
    result = subprocess.run([sys.executable, *arguments, '--output-root', str(output)], capture_output=True, text=True, timeout=30)
    report = json.loads((output / 'results.json').read_text())
    (output / 'command.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    assert result.returncode != 0 and report['complete'] is False and not report.get('observations') and expected in report['error'], name
    results.append(dict(case=name, passed=True, diagnostic=report['error'], observationsBeforeRejection=0))
spec = importlib.util.spec_from_file_location('measurement_tool', tool)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
for variant in ('text', 'bytes'):
    output = work / 'guards-native' / ('timeout-' + variant)
    partial = 'partial output' if variant == 'text' else b'partial output'
    error = subprocess.TimeoutExpired('controlled-command', 180, output=partial, stderr=partial)
    with patch.object(sys, 'argv', [*base, '--output-root', str(output)]), patch.object(module.subprocess, 'run', side_effect=error):
        try:
            module.main()
            raise AssertionError('Timeout was not surfaced')
        except subprocess.TimeoutExpired:
            pass
    report = json.loads((output / 'results.json').read_text())
    log = (output / 'logs/sdk.log').read_text()
    assert report['complete'] is False and 'partial output' in log and 'TIMEOUT' in log
    results.append(dict(case='timeout-' + variant, passed=True, originalExceptionPreserved=True, partialOutputRetained=True))
(work / 'guard-results.json').write_text(json.dumps(dict(complete=True, results=results), indent=2) + '\n', encoding='utf-8')
print(json.dumps(results, indent=2))
