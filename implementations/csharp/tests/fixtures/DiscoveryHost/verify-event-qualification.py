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


def same_json(left, right):
    if type(left) is not type(right):
        return False
    if type(left) is dict:
        return left.keys() == right.keys() and all(same_json(value, right[key]) for key, value in left.items())
    if type(left) is list:
        return len(left) == len(right) and all(same_json(a, b) for a, b in zip(left, right))
    return left == right


def verify(catalog, row, actual):
    require(actual['caseId'] == row['id'], 'Case identity differs')
    mode, common = row['input']['mode'], catalog['common']
    records = actual['records']
    require(len(records) == row['expected']['records'], 'Record count differs')
    if mode == 'none':
        require(actual['noneSkipped'] is True and actual['clockCalls'] == 0, 'None read inputs/clock')
        return
    spans = set()
    optional = set('process_started_at host_name os_description os_architecture process_architecture runtime_description process_id commit_id build_id'.split())
    for index, item in enumerate(records):
        wire = item['wire']; encoded = wire.encode('utf-8', errors='strict')
        require(wire.endswith('\n') and wire.count('\n') == 1 and '\r' not in wire and not wire.startswith('\ufeff'), 'JSONL framing differs')
        require(len(encoded) == item['utf8Bytes'] and len(encoded) <= row['input']['maxEntryBytes'], 'Actual bytes exceed cap')
        record = json.loads(wire)
        require(type(record['schema_version']) is int and record['schema_version'] == 1, 'Schema type differs')
        for key in ['timestamp', 'env', 'version', 'component', 'event_name']:
            require(record[key] == common[key], key + ' changed')
        require(record['status'] == row['expected']['status'], 'Severity mapping differs')
        require(record['service'] == common['service'] + ('.Store' + str(index) if mode == 'stores' else ''), 'Service changed')
        lost = row['expected']['truncated']
        require(item['truncated'] is lost and record.get('truncated', False) is lost and (lost or 'truncated' not in record), 'Loss state differs')
        expected_resource = dict(common['resource'])
        if 'environmentKind' in row['expected']:
            expected_resource['environment_kind'] = row['expected']['environmentKind']
        for name in (optional if mode in ['absent', 'empty'] else ['os_description', 'runtime_description'] if mode in ['aggregate', 'sibling'] else []):
            expected_resource.pop(name)
        require(record['resource'] == expected_resource and type(record['resource'].get('process_id', 0)) is int, 'Resource allowlist/type differs')
        sampled = item['sampled']
        if sampled:
            require(record['trace_id'] == sampled['traceId'] and record['span_id'] == sampled['spanId'], 'Sampled pair changed')
            for key, length in [('trace_id', 32), ('span_id', 16)]:
                value = record[key]
                require(len(value) == length and set(value) <= set('0123456789abcdef') and value != '0' * length, 'Invalid W3C pair')
            require(record['span_id'] != '2' * 16, 'Parent span emitted')
            if mode in ['concurrent', 'stores']:
                require(record['span_id'] not in spans, 'Worker spans shared'); spans.add(record['span_id'])
        else:
            require('trace_id' not in record and 'span_id' not in record, 'Unavailable pair emitted')
        if mode in ['fields', 'delayed', 'child']:
            expected_attrs = dict(common['attributes'])
            if mode == 'delayed':
                expected_attrs['scopeOnly'] = 'original-scope'
            require(same_json(record['attributes'], expected_attrs), 'Typed attributes differ')
            attrs = record['attributes']
            require(attrs['null'] is None and type(attrs['true']) is bool and type(attrs['false']) is bool and type(attrs['integer']) is int and type(attrs['number']) is float, 'Scalar wire types changed')
            require(type(attrs['integerString']) is str and type(attrs['decimal']) is str and list(attrs['map']) == ['a', 'z'] and attrs['array'] == [2, 1, None], 'String/collection kinds differ')
            require(record['message'] == common['attributes']['text'] and record['message_template'] == 'template {value}', 'Text changed')
            require(record['operation_id'] == ('child' if index == 1 else 'outer') if mode == 'child' else record['operation_id'] == ('inner' if mode == 'delayed' else 'event'), 'Precedence differs')
            error = record['exception']
            require(error == dict(type='System.InvalidOperationException', message='captured failure', stack_trace=actual['originalStack'], inner_exceptions=[dict(type='System.ArgumentException', message='inner')]), 'Exception tree/stack changed')
            require(set(record) == set('schema_version timestamp status event_name message service env version component resource attributes operation_id trace_id span_id exception message_template'.split()), 'Field set differs')
        elif mode in ['absent', 'empty']:
            require(record['message'] == '' and record['attributes'] == {}, 'Empty required values differ')
            require(set(record) == set('schema_version timestamp status event_name message service env version component resource attributes'.split()) | ({'message_template'} if mode == 'empty' else set()), 'Absent fields emitted')
            require(mode != 'empty' or record['message_template'] == '', 'Empty template lost')
        elif mode in ['concurrent', 'stores']:
            suffix = str(index)
            require(record['message'] == 'worker-' + suffix and record['operation_id'] == ('operation-' if index % 2 == 0 else 'event-') + suffix, 'Worker operation leaked')
            require(record['attributes'] == dict(worker=index, shared='event-' + suffix, scopeOnly='outer') and type(record['attributes']['worker']) is int, 'Worker attributes leaked')
            require(all(item[key] is True for key in ['scopeRestored', 'activityRestored', 'callerChanged']), 'Worker restoration/mutation not executed')
        elif mode == 'aggregate':
            require(record['message'] == common['attributes']['text'], 'Higher priority message changed')
            retained = record['attributes']['a']
            require(retained and ('\U0001f600\u0001' * row['input']['maxEntryBytes']).startswith(retained), 'Scalar prefix differs')
            require('z' not in record['attributes'] and 'exception' not in record and 'message_template' not in record, 'Lower priority won budget')
            larger = json.loads(actual['largerWire'])
            require(actual['largerTruncated'] is False and 'truncated' not in larger and len(larger['attributes']['a']) > len(retained) and larger['attributes']['z'] == '1.0000', 'Owned facts mutated')
        elif mode == 'sibling':
            error = record['exception']
            require(error['message'] == 'root' and error['truncated'] is True and error['inner_exceptions'] == [dict(type='System.Exception', message='small')], 'Later fitting sibling/parent loss differs')
        require('CIS_EXCLUDED_EXCEPTION_DATA' not in wire and 'CIS_CHANGED_EXCEPTION_DATA' not in wire and 'CIS_NEVER_CAPTURE_COLLECTOR' not in wire, 'Excluded content leaked')
    if mode in ['child', 'fields', 'delayed']:
        require(actual['storeEmpty'] is True, 'Scope owner cleanup failed')
    if mode == 'delayed':
        later = actual['laterState']
        require(later['callerChanged'] is True and later['exceptionDataChanged'] is True and later['scopeOnly'] == 'replacement-scope' and later['operationId'] == 'replacement' and later['traceId'] != records[0]['sampled']['traceId'] and later['spanId'] != records[0]['sampled']['spanId'], 'Later state not changed')
    if mode == 'child':
        require(records[0]['sampled'] == records[2]['sampled'] and records[0]['sampled']['traceId'] == records[1]['sampled']['traceId'] and records[0]['sampled']['spanId'] != records[1]['sampled']['spanId'], 'Child/restored activity differs')
    if mode in ['concurrent', 'stores']:
        require(all(actual[key] is True for key in ['parentHeadsUnchanged', 'parentActivityUnchanged', 'storesEmpty']), 'Parent/owner state changed')
        require(all(parent == dict(operationId='parent', shared='parent') for parent in actual['parentWhileBlocked']), 'Worker altered parent')


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
    rows = catalog['cases']
    report = dict(scope='Actual exact-runtime Core complete-event children; not full SDK/output/runtime/release qualification.', family=args.family, cases=[])
    env = dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', DD_AGENT_HOST='CIS_NEVER_CAPTURE_COLLECTOR')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'event-qualification', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-event-qualification-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=env, capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout)
            (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Encoder created files')
        actual = json.loads(process.stdout)
        verify(catalog, row, actual)
        if row['id'] == 'EVENT-QUAL-INFORMATION':
            corrupt = json.loads(json.dumps(actual))
            record = json.loads(corrupt['records'][0]['wire'])
            record['attributes']['null'] = 'null'
            wire = json.dumps(record, separators=(',', ':')) + '\n'
            corrupt['records'][0].update(wire=wire, utf8Bytes=len(wire.encode('utf-8')))
            try:
                verify(catalog, row, corrupt)
            except AssertionError:
                report['negativeControlRejected'] = True
            else:
                raise AssertionError('Independent comparator accepted corrupted null')
        observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['fixtureSha256'] == sha(args.host) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input hash differs')
        require(b'CIS_NEVER_CAPTURE_COLLECTOR' not in process.stdout, 'Collector metadata leaked')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(report.get('negativeControlRejected') is True, 'Independent negative control did not run')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, passed=True, count=len(rows))))


if __name__ == '__main__':
    main()
