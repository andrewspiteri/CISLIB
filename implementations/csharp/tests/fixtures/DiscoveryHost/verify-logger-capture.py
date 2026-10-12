"""Execute real lazy/capture/output-owned admission and independently compare observations/original bytes."""
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
    expected = row['expected']
    for key, value in expected.items():
        if key != 'wireMessages':
            require(key in actual, key + ': missing')
            same(value, actual[key], key)
    require(len(actual['wires']) == 2, 'Output groups differ')
    shared = sum(result is not None and sum(o['status'] == 'Accepted' for o in result['outputs']) == 2 for result in expected['results'])
    require(actual['sharedCaptures'] == shared, 'Fanout capture identity differs')
    for index, name in enumerate(['console', 'file']):
        require(len(actual['wires'][index]) == expected['accepted'][index], 'Actual accepted bytes differ')
        wire_index = 0
        for call, result in enumerate(expected['results']):
            entry = next((o for o in result['outputs'] if o['name'] == name), None) if result else None
            if entry is None or entry['status'] != 'Accepted':
                continue
            raw = base64.b64decode(actual['wires'][index][wire_index], validate=True)
            message = expected['wireMessages'][call] if len(expected['wireMessages']) == len(expected['results']) else expected['wireMessages'][wire_index]
            wire_index += 1
            require(0 < len(raw) <= 16384 and raw.endswith(b'\n') and raw.count(b'\n') == 1 and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'), 'Raw size/framing differs')
            record = json.loads(raw.decode('utf-8', errors='strict'))
            require(type(record['schema_version']) is int and record['schema_version'] == 1 and record['status'] == 'info' and record['timestamp'] == '2026-10-12T10:11:12.1234567Z', 'Schema/severity/clock differs')
            require(record['component'] == 'Capture' and record['event_name'] == 'capture.event' and record['message'] == message, 'Owned event differs')
            lost = row['input']['mode'] == 'truncate'
            require(record.get('truncated', False) is lost and (lost or 'truncated' not in record), 'Wire loss differs')
            require('trace_id' not in record and 'span_id' not in record, 'Ambient IDs leaked')
    require('CIS_PRIVATE' not in json.dumps(actual) and 'CIS_FACTORY_PRIVATE' not in json.dumps(actual), 'Safe errors leaked caller content')


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
    require(len(rows) == 41 and len({row['id'] for row in rows}) == 41, 'Complete unique decision case set differs')
    report = dict(scope='Actual Core lazy/global capture rejection and output-owned outcomes/tickets; admission plans are controls, not workers/delivery or full S05/release qualification.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'logger-capture', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-logger-capture-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(row, actual)
        controls = []
        if row['id'] == 'CAPTURE-NAME-NULL-LAZY':
            controls = ['factory-retry', 'balanced-factory-retry', 'double-invalid', 'invented-invalid-ticket', 'wrong-owned-ticket', 'invalid-utf8']
        elif row['id'] == 'CAPTURE-TRUNCATED-REJECTED-LAZY':
            controls = ['lost-truncation']
        elif row['id'] == 'CAPTURE-THROW-IO':
            controls = ['replaced-factory-exception']
        for fault in controls:
            corrupted = json.loads(json.dumps(actual))
            if fault == 'factory-retry':
                corrupted['factoryCalls'] = 4
            elif fault == 'balanced-factory-retry':
                corrupted['factoryInvocations'][0] = 2
                corrupted['factoryInvocations'][1] = 0
            elif fault == 'double-invalid':
                corrupted['invalidCount'] = 2
            elif fault == 'invented-invalid-ticket':
                corrupted['results'][1]['outputs'] = [dict(name='console', status='Accepted', sequence=12)]
            elif fault == 'wrong-owned-ticket':
                corrupted['results'][2]['outputs'][0]['sequence'] = 13
            elif fault == 'invalid-utf8':
                raw = base64.b64decode(corrupted['wires'][0][0], validate=True)
                corrupted['wires'][0][0] = base64.b64encode(b'\xff' + raw[1:]).decode('ascii')
            elif fault == 'lost-truncation':
                corrupted['truncatedCount'] = 0
            elif fault == 'replaced-factory-exception':
                corrupted['exceptionSame'][1] = False
            reject_corruption(row, corrupted, fault)
            report.setdefault('negativeControlsRejected', []).append(fault)
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(set(report.get('negativeControlsRejected', [])) == {'factory-retry', 'balanced-factory-retry', 'double-invalid', 'invented-invalid-ticket', 'wrong-owned-ticket', 'invalid-utf8', 'lost-truncation', 'replaced-factory-exception'}, 'Independent negative controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
