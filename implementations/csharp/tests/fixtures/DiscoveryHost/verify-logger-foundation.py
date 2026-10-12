"""Execute the real encoder in exact-runtime children and independently inspect its JSONL bytes."""
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


def verify(row, actual):
    require(actual['caseId'] == row['id'], 'Case identity differs')
    expected, mode = row['expected'], row['input']['mode']
    results = actual['results']
    require([r['outcome'] for r in results] == expected['outcomes'], 'Outcomes differ')
    for key in ['clockCalls', 'factoryCalls']:
        require(type(actual[key]) is int and actual[key] == expected[key], key + ' differs')
    for index, output in enumerate(['console', 'file']):
        wires = actual[output]['wires']
        require(len(wires) == expected['accepted'][index], 'Accepted wire count differs')
        encoded = actual[output]['wireBase64']
        require(len(encoded) == len(wires), 'Raw byte record count differs')
        for wire, raw_base64 in zip(wires, encoded):
            raw = base64.b64decode(raw_base64, validate=True)
            require(len(raw) <= row['input'].get('maxEntryBytes', 65536) and raw.endswith(b'\n') and raw.count(b'\n') == 1 and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'), 'Actual raw byte size/framing differs')
            require(raw.decode('utf-8', errors='strict') == wire, 'Raw bytes differ from reported text')
            require(wire.endswith('\n') and wire.count('\n') == 1 and '\r' not in wire and not wire.startswith('\ufeff'), 'JSONL framing differs')
            require(len(wire.encode('utf-8', errors='strict')) <= row['input'].get('maxEntryBytes', 65536), 'Actual encoded cap differs')
            record = json.loads(wire)
            require(type(record['schema_version']) is int and record['schema_version'] == 1, 'Schema differs')
            require(record['status'] == expected['status'] and record['timestamp'] == '2026-10-12T10:11:12.1234567Z', 'Method severity/clock differs')
            require(record['service'] == 'Logger.App' and record['component'] == 'Orders' and record['event_name'] == 'logger.event' and record['message'] == 'original', 'Owned identity/data differs')
            require(record.get('truncated', False) is (mode == 'truncate') and (mode == 'truncate' or 'truncated' not in record), 'Loss marker differs')
            if mode == 'settings':
                require(record['attributes']['payload'] == 'x' * 30000 and len(wire.encode('utf-8')) > 16384, 'Mutable settings changed the frozen cap')
            elif mode == 'fanout':
                require(record['operation_id'] == 'scope-op' and record['attributes']['tenant'] == 'original', 'Owned scope changed')
                require(record['trace_id'] == actual['sampled']['traceId'] and record['span_id'] == actual['sampled']['spanId'], 'Current pair differs')
            elif mode == 'truncate':
                require(0 < len(record['attributes']['attachment']) < 50000 and set(record['attributes']['attachment']) == {'x'}, 'Truncation prefix differs')
            elif mode == 'isolation':
                require(record['operation_id'] == 'first', 'Scope isolation differs')
    require(actual['console']['filtered'] == (1 if mode == 'union' else 0) and actual['file']['filtered'] == 0 and actual['console']['unavailable'] == 0 and actual['file']['unavailable'] == 0, 'Output counters differ')
    for result in results:
        invalid = result['outcome'] == 'InvalidEvent'
        require(result['truncated'] is (mode == 'truncate') and invalid == (result['errorCode'] is not None), 'Result loss/error differs')
        if invalid:
            require(result['outputs'] == [] and result['errorCode'] == ('InvalidEventName' if mode == 'invalid' else 'InvalidFactoryResult'), 'Invalid capture reached fanout or wrong safe code')
        for output in result['outputs']:
            require(output['sequence'] == 1 and type(output['sequence']) is int if output['status'] == 'Accepted' else output['sequence'] is None, 'Ticket differs')
    if mode == 'union':
        require(actual['enabledBefore'] is True and results[0]['outputs'] == [dict(name='console', status='Filtered', sequence=None), dict(name='file', status='Accepted', sequence=1)], 'Union interest/order differs')
    elif mode == 'disabled':
        require(actual['enabledBefore'] is False and results[0]['outputs'] == [], 'Disabled output included')
    elif mode == 'full':
        require(actual['enabledWhenFull'] is True and results[1]['outputs'][0]['status'] == 'QueueFull' and actual['console']['attempts'] == 2, 'Capacity was treated as reservation')
    elif mode == 'fanout':
        require(actual['sameCapture'] is True and actual['storeEmpty'] is True, 'Fanout duplicated capture or scope leaked')
        require(actual['console']['wires'] == actual['file']['wires'], 'Fanout bytes differ')
    elif mode == 'stopped':
        require(results[0]['outputs'] == [dict(name='console', status='Stopped', sequence=None)], 'Stopped output differs')
        require(actual['enabledAfterClose'] is False and actual['loggerCreatedAfterClose'] is True and actual['console']['attempts'] == 0, 'Admission reopened')
    elif mode == 'caller-throws':
        require(actual['sameCallerException'] is True, 'Factory exception was concealed')
    elif mode == 'isolation':
        require(json.loads(actual['otherWire'])['operation_id'] == 'second', 'Second runtime scope leaked')
    require(type(actual['invalidCount']) is int and actual['invalidCount'] == (1 if mode in ['invalid', 'null-result'] else 0), 'Invalid count differs')
    require(type(actual['truncatedCount']) is int and actual['truncatedCount'] == (1 if mode == 'truncate' else 0), 'Truncation count differs')


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
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8-sig'))['cases']
    report = dict(scope='Actual Core logger/capture/encoding with controlled admission doubles; no workers/I/O/delivery; not full SDK/output/runtime/release qualification.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'logger-foundation', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-logger-foundation-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(row, actual)
        if row['id'] == 'LOGGER-DIRECT-INFORMATION':
            for fault in ['missing-ticket', 'wrong-ticket', 'invalid-utf8', 'invalid-lf']:
                corrupted = json.loads(json.dumps(actual))
                if fault == 'missing-ticket':
                    corrupted['results'][0]['outputs'][0]['sequence'] = None
                elif fault == 'wrong-ticket':
                    corrupted['results'][0]['outputs'][0]['sequence'] = 2
                else:
                    raw = base64.b64decode(corrupted['console']['wireBase64'][0], validate=True)
                    raw = b'\xff' + raw[1:] if fault == 'invalid-utf8' else raw[:-1] + b' '
                    corrupted['console']['wireBase64'][0] = base64.b64encode(raw).decode('ascii')
                reject_corruption(row, corrupted, fault)
                report.setdefault('negativeControlsRejected', []).append(fault)
        elif row['id'] == 'LOGGER-FULL':
            corrupted = json.loads(json.dumps(actual))
            corrupted['results'][1]['outputs'][0]['sequence'] = 1
            reject_corruption(row, corrupted, 'invented-rejection-ticket')
            report.setdefault('negativeControlsRejected', []).append('invented-rejection-ticket')
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(set(report.get('negativeControlsRejected', [])) == {'missing-ticket', 'wrong-ticket', 'invalid-utf8', 'invalid-lf', 'invented-rejection-ticket'}, 'Independent negative controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
