"""Execute actual logger decision ordering and independently compare literal observations/original bytes."""
import argparse
import base64
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


def same(expected, actual, path):
    require(type(expected) is type(actual), path + ': JSON type differs')
    if isinstance(expected, dict):
        require(set(expected) == set(actual), path + ': fields differ')
        for key in expected:
            same(expected[key], actual[key], path + '.' + key)
    elif isinstance(expected, list):
        require(len(expected) == len(actual), path + ': length differs')
        for index, (left, right) in enumerate(zip(expected, actual)):
            same(left, right, path + '[' + str(index) + ']')
    else:
        require(expected == actual, path + ': value differs')


def verify(row, actual):
    require(actual['caseId'] == row['id'], 'Case differs')
    for key, value in row['expected'].items():
        require(key in actual, key + ': missing')
        same(value, actual[key], key)
    require(len(actual['wires']) == 2, 'Output wire groups differ')
    results = row['expected']['result']
    for index, name in enumerate(['console', 'file']):
        accepted = results is not None and any(o['name'] == name and o['status'] == 'Accepted' for o in results['outputs'])
        require(len(actual['wires'][index]) == int(accepted), 'Accepted original bytes differ')
        for encoded in actual['wires'][index]:
            raw = base64.b64decode(encoded, validate=True)
            require(0 < len(raw) <= 65536 and raw.endswith(b'\n') and raw.count(b'\n') == 1 and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'), 'Raw cap/framing differs')
            record = json.loads(raw.decode('utf-8', errors='strict'))
            require(type(record['schema_version']) is int and record['schema_version'] == 1, 'Schema differs')
            require(record['status'] == ('debug' if row['input']['level'] == 1 else 'info') and record['timestamp'] == '2026-10-12T10:11:12.1234567Z', 'Method severity/clock differs')
            require(record['component'] == 'Decisions' and record['event_name'] == 'decision.event' and record['message'] == 'original', 'Data differs')
            require('trace_id' not in record and 'span_id' not in record and set(record['attributes']) == {'probe'}, 'Ambient correlation/attribute shape differs')
            require(record['attributes']['probe'] == 'original' and record['exception']['message'] == 'exception-original' and 'truncated' not in record, 'Payload differs')
    if all(len(wires) == 1 for wires in actual['wires']):
        require(actual['sameCapture'] is True and actual['wires'][0] == actual['wires'][1], 'Fanout captured twice')


def reject_corruption(row, corrupted, fault):
    try:
        verify(row, corrupted)
    except (AssertionError, UnicodeDecodeError):
        return
    raise AssertionError('Independent comparator accepted ' + fault)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--host', required=True)
    parser.add_argument('--catalog', required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    catalog = json.loads(Path(args.catalog).read_text(encoding='utf-8-sig'))
    require(catalog['schemaVersion'] == 1, 'Catalog schema differs')
    rows = catalog['cases']
    require(len(rows) == 67 and len({row['id'] for row in rows}) == 67, 'Complete unique decision case set differs')
    report = dict(scope='Actual Core argument/state/capture/admission ordering with cooperative callbacks and controlled admission doubles; not writer/lifecycle/provider/S05/release qualification.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'logger-decision', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-logger-decision-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(row, actual)
        controls = []
        if row['id'] == 'DECISION-CAPTURE-FANOUT-DIRECT':
            controls = ['wrong-order', 'wrong-ticket', 'invalid-utf8']
        elif row['id'] == 'DECISION-ALL-FAULTED-LAZY':
            controls = ['premature-factory', 'wrong-unavailable']
        elif row['id'] == 'DECISION-CLOSED-NONE-DIRECT':
            controls = ['closed-admission']
        for fault in controls:
            corrupted = json.loads(json.dumps(actual))
            if fault == 'wrong-order':
                corrupted['trace'][0], corrupted['trace'][1] = corrupted['trace'][1], corrupted['trace'][0]
            elif fault == 'wrong-ticket':
                corrupted['result']['outputs'][0]['sequence'] = 2
            elif fault == 'invalid-utf8':
                raw = base64.b64decode(corrupted['wires'][0][0], validate=True)
                corrupted['wires'][0][0] = base64.b64encode(b'\xff' + raw[1:]).decode('ascii')
            elif fault == 'premature-factory':
                corrupted['trace'].append('factory')
            elif fault == 'wrong-unavailable':
                corrupted['unavailable'][0] = 0
            elif fault == 'closed-admission':
                corrupted['attempts'][0] = 1
            reject_corruption(row, corrupted, fault)
            report.setdefault('negativeControlsRejected', []).append(fault)
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(set(report.get('negativeControlsRejected', [])) == {'wrong-order', 'wrong-ticket', 'invalid-utf8', 'premature-factory', 'wrong-unavailable', 'closed-admission'}, 'Independent negative controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
