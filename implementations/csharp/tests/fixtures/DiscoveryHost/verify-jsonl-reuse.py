"""Qualify actual runtime-owned JSONL reuse and original-byte framing and independently compare observations/original bytes."""
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
    if type(expected) in (int, float) and type(actual) in (int, float):
        require(expected == actual, path + ': numeric value differs'); return
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


def expected_record(catalog, row, index):
    result = json.loads(json.dumps(catalog['common']))
    result.update(schema_version=1, status=row['expected']['status'], message=row['input']['messages'][index], message_template=row['input']['messages'][index], operation_id='event')
    result['attributes']['text'] = row['input']['messages'][index]
    mode = row['input']['mode']
    if mode in ['absent', 'empty']:
        for key in ['process_started_at', 'host_name', 'os_description', 'os_architecture', 'process_architecture', 'runtime_description', 'process_id', 'commit_id', 'build_id']:
            del result['resource'][key]
        del result['operation_id']; del result['exception']
        result['attributes'] = {'null': None} if mode == 'empty' else {}
        if mode == 'empty':
            result['message_template'] = ''
        else:
            del result['message_template']
    for field in row['expected']['omittedFields']:
        parts = field.split('.')
        if len(parts) == 2:
            del result[parts[0]][parts[1]]
        else:
            del result[parts[0]]
    if row['expected']['truncated']:
        result['truncated'] = True
    return result


def verify(catalog, row, actual):
    require(actual['caseId'] == row['id'], 'Case differs')
    require(set(row['input']) == {'level', 'messages', 'mode', 'maxEntryBytes'}, 'Input fields differ')
    require(set(row['expected']) == {'status', 'records', 'truncated', 'omittedFields'}, 'Expected fields differ')
    count = row['expected']['records']; lost = row['expected']['truncated']
    for key, value in dict(factoryCalls=count, clockCalls=count, invalidCount=0, truncatedCount=count if lost else 0, sharedCaptures=count, reusedMemory=True).items():
        same(value, actual[key], key)
    require(len(actual['wires']) == 2 and len(actual['wires'][0]) == count and actual['wires'][0] == actual['wires'][1] and len(actual['results']) == count, 'Fanout/record count differs')
    joined = b''
    for index in range(count):
        expected = dict(outcome='Accepted', truncated=lost, errorCode=None, outputs=[dict(name=name, status='Accepted', sequence=index+1) for name in ['console', 'file']])
        same(expected, actual['results'][index], 'result')
        raw = base64.b64decode(actual['wires'][0][index], validate=True)
        require(0 < len(raw) <= row['input']['maxEntryBytes'] and raw.endswith(b'\n') and raw.count(b'\n') == 1 and all(byte >= 32 for byte in raw[:-1]) and not raw.startswith(b'\xef\xbb\xbf'), 'Framing/cap/control differs')
        if lost:
            require(len(raw) >= row['input']['maxEntryBytes'] - 5, 'Greedy capture ignored configured cap')
        text = raw.decode('utf-8', errors='strict')
        require(not any(127 <= ord(char) <= 159 or ord(char) in [0x2028, 0x2029] for char in text), 'Raw Unicode controls/newlines differ')
        joined += raw
        record = json.loads(raw.decode('utf-8', errors='strict'))
        if lost:
            text = record['attributes'].pop('zPayload')
            require(type(text) is str and 0 < len(text) < 30000 and all(char == '\u0001' for char in text), 'Bounded escaped prefix differs')
        same(expected_record(catalog, row, index), record, 'record')
    stream = base64.b64decode(actual['stream'], validate=True)
    require(stream == joined and stream.count(b'\n') == count, 'Concatenated record stream differs')
    lines = stream.split(b'\n')
    require(lines[-1] == b'' and len(lines) == count + 1, 'Physical lines differ')
    for line in lines[:-1]:
        json.loads(line.decode('utf-8', errors='strict'))


def reject_corruption(catalog, row, corrupted, fault):
    try:
        verify(catalog, row, corrupted)
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
    require(len(rows) == 20 and len({row['id'] for row in rows}) == 20, 'Complete unique decision case set differs')
    report = dict(scope='WP05-S01-T01 actual runtime-owned JSONL reuse, original bytes/framing/types/caps; no destination/worker/I/O/delivery or full S01/release qualification.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'jsonl-reuse', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-jsonl-reuse-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(catalog, row, actual)
        if row['id'] == 'JSONL-INFO':
            for fault in ['bom', 'crlf', 'banner', 'ansi', 'utf8', 'type', 'stream']:
                corrupted = json.loads(json.dumps(actual))
                raw = base64.b64decode(corrupted['wires'][0][0], validate=True)
                if fault == 'bom': raw = b'\xef\xbb\xbf' + raw
                elif fault == 'crlf': raw = raw[:-1] + b'\r\n'
                elif fault == 'banner': raw = b'banner\n' + raw
                elif fault == 'ansi': raw = b'\x1b' + raw[1:]
                elif fault == 'utf8': raw = b'\xff' + raw[1:]
                elif fault == 'type':
                    record = json.loads(raw); record['attributes']['integer'] = '9007199254740991'
                    raw = (json.dumps(record) + '\n').encode('utf-8')
                elif fault == 'stream': corrupted['stream'] = base64.b64encode(raw).decode('ascii')
                if fault != 'stream':
                    for output_index in range(2): corrupted['wires'][output_index][0] = base64.b64encode(raw).decode('ascii')
                reject_corruption(catalog, row, corrupted, fault)
                report.setdefault('negativeControlsRejected', []).append(fault)
            reordered = json.loads(json.dumps(actual)); joined = b''
            for index in range(2):
                record = json.loads(base64.b64decode(actual['wires'][0][index], validate=True))
                raw = (' ' + json.dumps(dict(reversed(list(record.items())))) + ' \n').encode('utf-8'); joined += raw
                for output_index in range(2): reordered['wires'][output_index][index] = base64.b64encode(raw).decode('ascii')
            reordered['stream'] = base64.b64encode(joined).decode('ascii')
            verify(catalog, row, reordered); report['memberOrderWhitespaceControlAccepted'] = True
        if row['id'] in ['JSONL-NEWLINE-LS', 'JSONL-CAP-DEFAULT']:
            fault = 'raw-ls' if row['id'] == 'JSONL-NEWLINE-LS' else 'underfill'
            corrupted = json.loads(json.dumps(actual))
            raw = base64.b64decode(corrupted['wires'][0][0], validate=True)
            if fault == 'raw-ls':
                require(br'\u2028' in raw, 'Original LS escaping absent')
                raw = raw.replace(br'\u2028', bytes([0xE2, 0x80, 0xA8]))
            else:
                record = json.loads(raw); record['attributes']['zPayload'] = '\u0001' * 10
                raw = (json.dumps(record) + '\n').encode('utf-8')
            for output_index in range(2): corrupted['wires'][output_index][0] = base64.b64encode(raw).decode('ascii')
            corrupted['stream'] = base64.b64encode(b''.join(base64.b64decode(item, validate=True) for item in corrupted['wires'][0])).decode('ascii')
            reject_corruption(catalog, row, corrupted, fault)
            report.setdefault('negativeControlsRejected', []).append(fault)
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(set(report.get('negativeControlsRejected', [])) == {'bom', 'crlf', 'banner', 'ansi', 'utf8', 'type', 'stream', 'raw-ls', 'underfill'} and report.get('memberOrderWhitespaceControlAccepted') is True, 'Comparator controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
