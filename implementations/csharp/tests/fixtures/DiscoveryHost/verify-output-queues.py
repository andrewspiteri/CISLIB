"""Independent actual-Channel/MemoryStream probes; not runtime/destination/release qualification."""
import argparse
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def require(condition, reason):
    if not condition:
        raise AssertionError(reason)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def exact(left, right):
    require(type(left) is type(right) and left == right, 'Value or type differs: ' + repr((left, right)))


def verify(row, actual):
    exact(row['id'], actual['caseId']); expected = row['expected']
    exact(expected['workers'], actual['workers'])
    for flag in ['distinctWorkers', 'completed']:
        exact(True, actual[flag])
    independent = row['mode'] == 'independent'
    outputs = actual['outputs']
    expected_names = {name for name in ['console', 'file'] if row[name + 'Enabled']}
    require(set(outputs) == expected_names | ({'consoleProgressWhileFileBlocked'} if independent else set()), 'Enabled worker set differs')
    if independent:
        exact(True, outputs['consoleProgressWhileFileBlocked'])
    for name in expected_names:
        output = outputs[name]; accepted, rejected = expected['acceptedPerOutput'], expected['rejectedPerOutput']
        require(len(output['admissions']) == accepted + rejected, 'Admission count differs')
        for left, right in zip([True] * accepted + [False] * rejected, output['admissions']):
            exact(left, right)
        exact(0 if independent and name == 'console' else expected['pendingAtBarrier'], output['pendingAtBarrier'])
        exact(1, output['maximumActive'])
        for flag in ['admittedAfterComplete', 'ambientSeen', 'inlineSeen']:
            exact(False, output[flag])
        for flag in ['ownedReferences']:
            exact(True, output[flag])
        exact(0, output['pendingAfterComplete']); exact(0, output['activeAfterComplete'])
        raw = base64.b64decode(output['stream'], validate=True)
        require(raw == base64.b64decode(output['expectedStream'], validate=True), 'Original capture byte identity/order differs')
        require(not raw.startswith(b'\xef\xbb\xbf') and raw.endswith(b'\n'), 'BOM/terminal LF differs')
        lines = raw.split(b'\n'); require(len(lines) == accepted + 1 and lines[-1] == b'', 'Record framing/count differs')
        for index, line in enumerate(lines[:-1]):
            require(0 < len(line) + 1 <= 65536 and b'\r' not in line, 'Record byte bounds/raw newline differs')
            source = json.loads(line.decode('utf-8', errors='strict'))
            for key, value in {'schema_version': 1, 'component': 'Queue', 'status': 'info', 'event_name': 'queue.event', 'message': 'record-' + str(index)}.items():
                exact(value, source[key])
        require(output['messages'] == ['record-' + str(index) for index in range(accepted)], 'Observed owned capture order differs')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--dotnet', required=True); parser.add_argument('--host', required=True)
    parser.add_argument('--catalog', required=True); parser.add_argument('--output', required=True); parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args(); output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    require(len(rows) == 11 and len({row['id'] for row in rows}) == 11, 'Queue case matrix differs')
    matrix = {(r['capacity'], r['consoleEnabled'], r['fileEnabled']) for r in rows if r['mode'] == 'bounded' and r['expected']['workers'] > 0}
    require(matrix == {(c, console, file) for c in [1, 2, 8] for console, file in [(True, False), (False, True), (True, True)]}, 'Capacity/enabled matrix differs')
    report = dict(scope='WP05-S02-T01 actual internal bounded Channel workers with controlled MemoryStream I/O. No actual console/file destination ownership, runtime fanout/tickets, lifecycle deadlines, fault diagnostics/counters, complete S02, release or durability claim.', family=args.family, cases=[], negativeControlsRejected=[])
    env = dict(os.environ, CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'output-queue', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-output-queue-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': process failed')
            require(not list(Path(temporary).iterdir()), 'Queue fixture created a destination file')
        actual = json.loads(process.stdout); verify(row, actual); observed = actual['observed']
        exact(args.family, observed['operatingSystem']); exact('.NET 10.0.12', observed['runtimeDescription'])
        exact(sha(Path(args.host).with_name('CIS.Core.dll')), observed['coreSha256']); exact(sha(args.host), observed['fixtureSha256']); exact(sha(args.catalog), observed['inputsSha256'])
        if row['id'] == 'QUEUE-BOTH-2':
            for fault in ['admission', 'pending', 'parallel', 'inline', 'ambient', 'ownership', 'bytes', 'worker', 'closed', 'framing']:
                damaged = copy.deepcopy(actual); item = damaged['outputs']['file']
                if fault == 'admission': item['admissions'][-1] = True
                elif fault == 'pending': item['pendingAtBarrier'] += 1
                elif fault == 'parallel': item['maximumActive'] = 2
                elif fault == 'inline': item['inlineSeen'] = True
                elif fault == 'ambient': item['ambientSeen'] = True
                elif fault == 'ownership': item['ownedReferences'] = False
                elif fault == 'worker': damaged['workers'] = 1
                elif fault == 'closed': item['admittedAfterComplete'] = True
                elif fault == 'bytes': item['stream'] = base64.b64encode(b'\n').decode('ascii')
                else:
                    raw = base64.b64decode(item['stream'], validate=True) + b'\n'
                    item['stream'] = item['expectedStream'] = base64.b64encode(raw).decode('ascii')
                try:
                    verify(row, damaged)
                except (AssertionError, KeyError, ValueError):
                    report['negativeControlsRejected'].append(fault)
                else:
                    raise AssertionError('Comparator accepted corruption: ' + fault)
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(len(report['negativeControlsRejected']) == 10, 'Comparator controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
