"""Independently qualify information, original UTF-8 bytes and scalar-safe human rendering."""
import argparse
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unicodedata


def require(condition, reason):
    if not condition:
        raise AssertionError(reason)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def same(left, right):
    if type(left) in (int, float) and type(right) in (int, float):
        require(left == right, 'Display numeric value differs'); return
    require(type(left) is type(right), 'Display JSON type differs')
    if isinstance(left, dict):
        require(set(left) == set(right), 'Display map keys differ')
        for key in left:
            same(left[key], right[key])
    elif isinstance(left, list):
        require(len(left) == len(right), 'Display array length differs')
        for a, b in zip(left, right):
            same(a, b)
    else:
        require(left == right, 'Display value differs')


def read(raw, cap):
    require(0 < len(raw) <= cap and raw.endswith(b'\n'), 'Actual human byte cap/final LF differs')
    text = raw[:-1].decode('utf-8', errors='strict')
    require(all(unicodedata.category(c) not in ['Cc', 'Cf', 'Zl', 'Zp'] for c in text), 'Raw terminal control or record boundary')
    fields = {}; offset = 0; decoder = json.JSONDecoder()
    while offset < len(text):
        while offset < len(text) and text[offset] == ' ':
            offset += 1
        if offset == len(text):
            break
        equal = text.index('=', offset); name = text[offset:equal]
        value_start = equal + 1
        while value_start < len(text) and text[value_start] == ' ':
            value_start += 1
        value, end = decoder.raw_decode(text, value_start)
        require(name not in fields, 'Duplicate human field')
        fields[name] = value; offset = end
        require(offset == len(text) or text[offset] == ' ', 'Missing field separator')
    return fields


def verify(row, actual):
    require(actual['caseId'] == row['id'] and actual['clockCalls'] == 1 and actual['captureUnchanged'] is True, 'Capture identity/ownership differs')
    source = json.loads(base64.b64decode(actual['json'], validate=True))
    for key in ['captureTruncated', 'normalTruncated', 'verboseTruncated']:
        same(row['expected'][key], actual[key])
    require(source['status'] == ['trace', 'debug', 'info', 'warn', 'error', 'critical'][row['level']], 'Captured severity differs')
    for detail in ['normal', 'verbose']:
        fields = read(base64.b64decode(actual[detail], validate=True), row['maxEntryBytes'])
        lost = actual[detail + 'Truncated']
        require(('truncated' in fields) == lost and (not lost or fields['truncated'] is True), 'Display loss marker differs')
        for name, source_name in [('timestamp', 'timestamp'), ('severity', 'status'), ('component', 'component'), ('event_name', 'event_name')]:
            same(source[source_name], fields[name])
        require(type(fields['message']) is str and source['message'].startswith(fields['message']), 'Message prefix differs')
        if len(fields['message']) < len(source['message']):
            scalar = source['message'][len(fields['message'])]; category = unicodedata.category(scalar)
            cost = 2 if scalar in ['"', '\\', '\n', '\r', '\t', '\b', '\f'] else (12 if ord(scalar) > 0xffff else 6) if category in ['Cc', 'Cf', 'Zl', 'Zp'] else len(scalar.encode('utf-8'))
            require(len(base64.b64decode(actual[detail], validate=True)) + cost > row['maxEntryBytes'], 'Message underfills its scalar budget')
        if not lost:
            same(source['message'], fields['message'])
        if detail == 'normal':
            require(set(fields) == {'timestamp', 'severity', 'component', 'event_name', 'message'} | ({'truncated'} if lost else set()), 'Normal fields differ')
            continue
        for name in ['service', 'env', 'version', 'operation_id', 'trace_id', 'span_id']:
            require((name in fields) == (name in source), 'Optional context presence differs')
            if name in source:
                same(source[name], fields[name])
        expected_fields = {'timestamp', 'severity', 'component', 'event_name', 'message', 'service', 'env', 'version'} | ({'truncated'} if lost else set())
        expected_fields |= {k for k in ['operation_id', 'trace_id', 'span_id'] if k in source}
        for name, value in source['resource'].items():
            omitted = name in row['expected']['verboseOmittedResources']
            require(('resource.' + name in fields) == (not omitted), 'Descriptive resource availability differs')
            if not omitted:
                same(value, fields['resource.' + name]); expected_fields.add('resource.' + name)
        for name in ['attributes', 'exception', 'message_template']:
            omitted = name in row['expected']['verboseOmittedPayload']
            require((name in fields) == (name in source and not omitted), 'Payload availability differs')
            if name in fields:
                same(source[name], fields[name]); expected_fields.add(name)
        require(set(fields) == expected_fields, 'Verbose fields differ')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--dotnet', required=True); parser.add_argument('--host', required=True)
    parser.add_argument('--catalog', required=True); parser.add_argument('--output', required=True); parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args(); output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    require(len(rows) == 12 and len({r['id'] for r in rows}) == 12, 'Complete case set differs')
    report = dict(scope='WP05-S01-T02 actual internal human rendering. No workers, destination selection, runtime rendering-loss counters, I/O, delivery or full S01/release qualification.', family=args.family, cases=[], negativeControlsRejected=[], negativeControlDetails=[])
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'human-rendering', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-human-render-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read'), capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(temporary).iterdir()), 'Renderer created files')
        actual = json.loads(process.stdout); verify(row, actual)
        observed = actual['observed']; require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(args.catalog), 'Managed/input bytes differ')
        if row['id'] == 'HUMAN-INFO':
            for fault in ['missing', 'control', 'utf8', 'unexpected-loss', 'type', 'exception', 'split', 'extra']:
                damaged = copy.deepcopy(actual); detail = 'verbose' if fault in ['type', 'exception', 'split', 'extra'] else 'normal'; raw = base64.b64decode(damaged[detail], validate=True)
                if fault == 'missing': raw = raw.replace(b'event_name=', b'other=')
                elif fault == 'control': raw = b'\x1b' + raw[1:]
                elif fault == 'utf8': raw = b'\xff' + raw[1:]
                elif fault == 'unexpected-loss':
                    damaged['normalTruncated'] = True; raw = raw[:-1] + b' truncated=true\n'
                else:
                    fields = read(raw, row['maxEntryBytes'])
                    if fault == 'type': fields['attributes']['integer'] = '9007199254740991'
                    elif fault == 'exception': del fields['exception']['type']
                    elif fault == 'split': fields['attributes']['text'] = fields['attributes']['text'][:-1]
                    else: fields['invented'] = 'changed'
                    raw = (' '.join(k + '=' + json.dumps(v, ensure_ascii=True) for k, v in fields.items()) + '\n').encode('utf-8')
                damaged[detail] = base64.b64encode(raw).decode('ascii')
                try:
                    verify(row, damaged)
                except (AssertionError, KeyError, ValueError) as failure:
                    report['negativeControlsRejected'].append(fault); report['negativeControlDetails'].append(dict(fault=fault, reason=str(failure)))
                else:
                    raise AssertionError('Corruption accepted: ' + fault)
            spaced = copy.deepcopy(actual); fields = read(base64.b64decode(actual['normal']), row['maxEntryBytes'])
            raw = ('  '.join(k + '=' + json.dumps(v, ensure_ascii=True) for k, v in reversed(list(fields.items()))) + '\n').encode('utf-8')
            spaced['normal'] = base64.b64encode(raw).decode('ascii'); verify(row, spaced); report['orderSpacingControlAccepted'] = True
        if row['id'] in ['HUMAN-CAP-16384', 'HUMAN-CAP-65536']:
            for fault in (['missing-loss', 'underfill'] if row['id'] == 'HUMAN-CAP-16384' else ['resource']):
                damaged = copy.deepcopy(actual); detail = 'verbose' if fault == 'resource' else 'normal'
                raw = base64.b64decode(damaged[detail], validate=True)
                if fault == 'missing-loss': raw = raw.replace(b' truncated=true', b'')
                else:
                    fields = read(raw, row['maxEntryBytes']); fields['resource.os_description' if fault == 'resource' else 'message'] = 'changed' if fault == 'resource' else ''
                    raw = (' '.join(k + '=' + json.dumps(v, ensure_ascii=True) for k, v in fields.items()) + '\n').encode('utf-8')
                damaged[detail] = base64.b64encode(raw).decode('ascii')
                try:
                    verify(row, damaged)
                except (AssertionError, KeyError, ValueError) as failure:
                    report['negativeControlsRejected'].append(fault); report['negativeControlDetails'].append(dict(fault=fault, reason=str(failure)))
                else:
                    raise AssertionError('Corruption accepted: ' + fault)
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed, captureTruncated=actual['captureTruncated'], normalTruncated=actual['normalTruncated'], verboseTruncated=actual['verboseTruncated']))
    require(set(report['negativeControlsRejected']) == {'missing', 'control', 'utf8', 'unexpected-loss', 'type', 'exception', 'split', 'extra', 'missing-loss', 'underfill', 'resource'} and report['orderSpacingControlAccepted'], 'Comparator controls missing')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
