"""Independently compare explicit presentation to actual logger admission and original capture bytes."""
import argparse
import base64
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile

spec = importlib.util.spec_from_file_location('human_checks', Path(__file__).with_name('verify-human-rendering.py'))
human = importlib.util.module_from_spec(spec); spec.loader.exec_module(human)
require, same = human.require, human.same
MESSAGE = 'literal {user} {missing:format} unmatched { } quote " slash \\\n\u001b[2J caf\u00e9 \U0001f600\u202e'
TEMPLATE = 'raw {user} {missing:format} unmatched { {{ }}\n\u001b[31m'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify(row, actual):
    require(actual['caseId'] == row['id'], 'Case differs')
    expected = row['expected']
    for key in ['factoryCalls', 'clockCalls', 'consoleFiltered', 'fileFiltered', 'sharedCaptures', 'enabled', 'outcomes']:
        same(expected[key], actual[key])
    same(0, actual['invalidCount']); same(0, actual['truncatedCount'])
    require(actual['ambientOperation'] == 'after-operation' and actual['sampled']['spanId'] != actual['afterSampled']['spanId'], 'Ambient context not replaced')
    for output in ['console', 'file']:
        is_json = row[output + 'Format'] == 1
        frames, statuses = actual[output + 'Frames'], expected[output + 'Statuses']
        require(len(frames) == len(statuses), 'Admission count changed')
        for frame, status in zip(frames, statuses):
            raw = base64.b64decode(frame['source'], validate=True); line = base64.b64decode(frame['line'], validate=True)
            require(raw == base64.b64decode(frame['after'], validate=True), 'Capture bytes changed')
            require(0 < len(line) <= 65536 and line.endswith(b'\n') and line.count(b'\n') == 1, 'Entry bounds/framing differ')
            source = json.loads(raw.decode('utf-8', errors='strict'))
            for key, value in dict(status=status, service='Presentation.App', component='Presentation', event_name='presentation.event', message=MESSAGE, message_template=TEMPLATE, operation_id='before-operation', timestamp='2026-10-12T10:11:12.1234567Z').items():
                same(value, source[key])
            same('CIS_MUST_NOT_INTERPOLATE', source['attributes']['user']); same(42, source['attributes']['number'])
            same(actual['sampled']['traceId'], source['trace_id']); same(actual['sampled']['spanId'], source['span_id'])
            same(False, frame['truncated']); same(is_json, frame['jsonMemoryReused'])
            if is_json:
                require(line == raw, 'JSONL was changed by presentation')
            else:
                fields = human.read(line, 65536)
                for display, key in [('timestamp', 'timestamp'), ('severity', 'status'), ('component', 'component'), ('event_name', 'event_name'), ('message', 'message')]:
                    same(source[key], fields[display])
                if row['detail'] == 0:
                    require(len(fields) == 5, 'Normal detail differs')
                else:
                    for key in ['service', 'env', 'version', 'operation_id', 'trace_id', 'span_id', 'attributes', 'message_template']:
                        same(source[key], fields[key])
                    expected_fields = {'timestamp', 'severity', 'component', 'event_name', 'message', 'service', 'env', 'version', 'operation_id', 'trace_id', 'span_id', 'attributes', 'message_template'}
                    for key, value in source['resource'].items():
                        same(value, fields['resource.' + key]); expected_fields.add('resource.' + key)
                    require(set(fields) == expected_fields, 'Verbose presentation fields differ')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--dotnet', required=True); parser.add_argument('--host', required=True)
    parser.add_argument('--catalog', required=True); parser.add_argument('--output', required=True); parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args(); output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    keys = {(r['profile'], r['consoleFormat'], r['fileFormat'], r['detail'], r['stream']) for r in rows}
    require(keys == {(p, c, f, d, s) for p in ['both', 'console', 'file', 'disabled'] for c in [0, 1] for f in [0, 1] for d in [0, 1] for s in [0, 1]} and len(rows) == len(keys), 'Complete presentation matrix differs')
    for profile in ['both', 'console', 'file', 'disabled']:
        group = [r for r in rows if r['profile'] == profile]
        require(len(group) == 16, 'Profile matrix count differs')
        for row in group:
            same(group[0]['expected'], row['expected'])
    report = dict(scope='WP05-S01-T03 actual internal presentation/admission separation. No I/O/delivery, runtime format composition, runtime render-loss aggregation, attached debugger or interactive TTY execution, full S01 or release claim.', family=args.family, cases=[], negativeControlsRejected=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', TERM='dumb', NO_COLOR='1')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'presentation', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-presentation-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': process failed')
            require(not list(Path(temporary).iterdir()), 'Presentation created files')
        actual = json.loads(process.stdout); verify(row, actual)
        same(True, actual['outputRedirected']); same(True, actual['errorRedirected']); same(False, actual['debuggerAttached'])
        observed = actual['observed']; require(observed['operatingSystem'] == args.family and observed['runtimeDescription'] == '.NET 10.0.12', 'Native platform/runtime differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        if row['id'] == 'PRESENT-BOTH-C0F1D1S0':
            for fault in ['filter', 'recapture', 'capture', 'template', 'format', 'copy']:
                damaged = copy.deepcopy(actual)
                if fault == 'filter': damaged['enabled'][1] = False
                elif fault == 'recapture': damaged['clockCalls'] += 1
                elif fault == 'copy': damaged['fileFrames'][0]['jsonMemoryReused'] = False
                elif fault == 'format': damaged['consoleFrames'][0]['line'] = damaged['consoleFrames'][0]['source']
                else:
                    frame = damaged['fileFrames'][0]; source = json.loads(base64.b64decode(frame['source'], validate=True))
                    source['service' if fault == 'capture' else 'message_template'] = 'CIS_CHANGED_BY_RENDERING'
                    frame['after' if fault == 'capture' else 'line'] = base64.b64encode((json.dumps(source) + '\n').encode('utf-8')).decode('ascii')
                try:
                    verify(row, damaged)
                except (AssertionError, KeyError, ValueError):
                    report['negativeControlsRejected'].append(fault)
                else:
                    raise AssertionError('Corruption accepted: ' + fault)
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(set(report['negativeControlsRejected']) == {'filter', 'recapture', 'capture', 'template', 'format', 'copy'}, 'Comparator controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
