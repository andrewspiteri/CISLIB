"""Execute the real encoder in exact-runtime children and independently inspect its JSONL bytes."""
import argparse
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
    expected = row['expected']
    if 'errorType' in expected:
        require(actual.get('errorType') == expected['errorType'] and 'wire' not in actual, 'Envelope failure differs')
        return
    wire = actual['wire']
    encoded = wire.encode('utf-8', errors='strict')
    cap = row['input']['maxEntryBytes']
    require(wire.endswith('\n') and wire.count('\n') == 1 and '\r' not in wire and not wire.startswith('\ufeff'), 'JSONL framing differs')
    require(len(encoded) == actual['utf8Bytes'] and len(encoded) <= cap, 'Actual encoded size exceeds limit')
    require(not expected['fillsLimit'] or len(encoded) == cap, 'Exact LF-inclusive cap differs')
    record = json.loads(wire)
    require(type(record['schema_version']) is int and record['schema_version'] == 1, 'Schema version differs')
    require(record['timestamp'] == '2026-10-12T10:11:12.1234567Z' and record['status'] == 'info', 'Time/severity differs')
    require(record['service'] == 'Encoding.App' and record['env'] == 'tests' and record['version'] == 'release-4', 'Protected identity changed')
    require(record['event_name'] == 'encoding.event' and record['component'] == 'component', 'Names changed')
    require(actual['truncated'] is expected['truncated'] and record.get('truncated', False) is expected['truncated'], 'Loss marker differs')
    require(type(record['attributes']) is dict and type(record['resource']) is dict, 'Required maps lost')
    require(record['resource']['build_version'] == '1.2.3', 'Protected build identity changed')
    mode, attrs = row['input']['mode'], record['attributes']
    if mode == 'complete':
        require(record['message'] == 'quote " slash \\ newline\n café 😀' and record['message_template'] == '', 'Captured text changed')
        require(record['operation_id'] == 'scope-operation' and record['trace_id'] == '1' * 32, 'Correlation changed')
        span = record['span_id']
        require(len(span) == 16 and set(span) <= set('0123456789abcdef') and span not in ['0' * 16, '2' * 16], 'Current span invalid')
        require(attrs['null'] is None and type(attrs['integer']) is int and attrs['integer'] == 9007199254740991, 'Null/integer types changed')
        require(type(attrs['integerString']) is str and attrs['integerString'] == '90071992547409920000' and attrs['decimal'] == '12.3400', 'Numeric strings changed')
        require(attrs['array'] == [True, 1.25] and list(attrs['map']) == ['a', 'z'], 'Array order or ordinal map output changed')
    elif mode == 'unicode':
        require(attrs['payload'] and ('😀\u0001' * 10000).startswith(attrs['payload']), 'Unicode scalar prefix split')
    elif mode == 'atomic':
        require('a' not in attrs and type(attrs['b']) is int and attrs['b'] == 0, 'Indivisible value corrupted or later scalar lost')
    elif mode == 'exception':
        require(record['exception']['truncated'] is True and record['exception']['message'] == 'captured failure', 'Exception marker changed')
    elif mode == 'prior':
        require(record['message'] == 'x' * 8192, 'Earlier scalar projection marker/text changed')


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
    report = dict(scope='Actual exact-runtime Core encoding children; not full SDK/output/runtime/release qualification.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'event-encoding', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-encoding-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(row, actual)
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
