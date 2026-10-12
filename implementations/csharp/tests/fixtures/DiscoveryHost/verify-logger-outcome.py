"""Execute the complete real logger outcome table and independently compare observations/original bytes."""
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
    required = {'tryCalls', 'results', 'invalidCount', 'filtered', 'sharedCaptures', 'enabledAfter', 'clockCalls', 'truncatedCount', 'attempts', 'accepted', 'queueFull', 'exceptionSame', 'unavailable', 'factoryInvocations', 'payloadTrace', 'exceptions', 'enabledBefore'}
    if row['input']['scenario'] in ['capacity', 'sticky']:
        required.add('enabledAfterFirst')
    require(set(expected) == required, 'Required expected fields differ')
    for key, value in expected.items():
        require(key in actual, key + ': missing')
        same(value, actual[key], key)
    require(len(actual['wires']) == 2, 'Output groups differ')
    accepted_bytes = {}
    for index, name in enumerate(['console', 'file']):
        require(len(actual['wires'][index]) == expected['accepted'][index], 'Accepted bytes differ')
        wire_index = 0
        for call, result in enumerate(expected['results']):
            entry = next((o for o in result['outputs'] if o['name'] == name), None) if result else None
            if entry is None or entry['status'] != 'Accepted':
                continue
            raw = base64.b64decode(actual['wires'][index][wire_index], validate=True)
            wire_index += 1
            require(0 < len(raw) <= 16384 and raw.endswith(b'\n') and raw.count(b'\n') == 1 and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'), 'Raw size/framing differs')
            if call in accepted_bytes:
                require(accepted_bytes[call] == raw, 'Fanout bytes differ')
            else:
                accepted_bytes[call] = raw
            record = json.loads(raw.decode('utf-8', errors='strict'))
            severity = 'debug' if row['input']['level'] == 1 else 'info'
            require(type(record['schema_version']) is int and record['schema_version'] == 1 and record['status'] == severity and record['timestamp'] == '2026-10-12T10:11:12.1234567Z', 'Schema/severity/clock differs')
            require(record['component'] == 'Outcomes' and record['event_name'] == 'outcome.event' and record['message'] == 'original', 'Owned event differs')
            require(record['attributes']['probe'] == 'original' and record['exception']['message'] == 'exception-original', 'Owned payload differs')
            require('truncated' not in record and 'trace_id' not in record and 'span_id' not in record, 'Loss/ambient IDs differ')
            require(b'CIS_' not in raw, 'Private content leaked')


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
    require(len(rows) == 125 and len({row['id'] for row in rows}) == 125, 'Complete unique decision case set differs')
    modes = ['disabled', 'filtered', 'filtered-faulted', 'accepted', 'full', 'faulted', 'admission-fault']
    require(catalog['modes'] == modes, 'Output modes differ')
    expected_pairs = {'OUTCOME-TABLE-' + left.upper() + '-' + right.upper() + '-' + method.upper() for left in modes for right in modes for method in ['direct', 'lazy']}
    require({row['id'] for row in rows if row['id'].startswith('OUTCOME-TABLE-')} == expected_pairs, 'Reachable table pairs differ')
    report = dict(scope='Actual Core complete interest/admission outcome table; explicit non-I/O doubles. AC04 actual output I/O and full release qualification remain pending.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'logger-outcome', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-logger-outcome-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(row, actual)
        controls = []
        if row['id'] == 'OUTCOME-TABLE-ACCEPTED-FILTERED-FAULTED-LAZY':
            controls = ['wrong-outcome', 'factory-retry', 'invented-filtered-ticket', 'uninterested-fault', 'invalid-utf8', 'missing-expected-field']
        elif row['id'] == 'OUTCOME-STICKY-ACCEPTED-LAZY':
            controls = ['sticky-retry']
        elif row['id'] == 'OUTCOME-CAPACITY-LAZY':
            controls = ['capacity-reservation']
        elif row['id'] == 'OUTCOME-THROW-ACCEPTED-ACCEPTED':
            controls = ['replaced-factory-exception']
        for fault in controls:
            corrupted = json.loads(json.dumps(actual))
            control_row = json.loads(json.dumps(row))
            if fault == 'missing-expected-field':
                del control_row['expected']['factoryInvocations']
            elif fault == 'wrong-outcome':
                corrupted['results'][0]['outcome'] = 'PartiallyAccepted'
            elif fault == 'factory-retry':
                corrupted['factoryInvocations'][0] = 2
            elif fault == 'invented-filtered-ticket':
                corrupted['results'][0]['outputs'][1]['sequence'] = 101
            elif fault == 'uninterested-fault':
                corrupted['unavailable'][1] = 1
            elif fault == 'invalid-utf8':
                raw = base64.b64decode(corrupted['wires'][0][0], validate=True)
                corrupted['wires'][0][0] = base64.b64encode(b'\xff' + raw[1:]).decode('ascii')
            elif fault == 'sticky-retry':
                corrupted['tryCalls'][0] = 2
            elif fault == 'capacity-reservation':
                corrupted['enabledAfterFirst'] = False
            elif fault == 'replaced-factory-exception':
                corrupted['exceptionSame'][0] = False
            reject_corruption(control_row, corrupted, fault)
            report.setdefault('negativeControlsRejected', []).append(fault)
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(set(report.get('negativeControlsRejected', [])) == {'wrong-outcome', 'factory-retry', 'invented-filtered-ticket', 'uninterested-fault', 'invalid-utf8', 'sticky-retry', 'capacity-reservation', 'replaced-factory-exception', 'missing-expected-field'}, 'Independent negative controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
