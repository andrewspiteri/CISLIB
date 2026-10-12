"""Qualify all S01 rendering criteria over original bytes, with no field normalization."""
import argparse
import base64
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(filename))
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module); return module


wire = load('story_wire', 'verify-jsonl-reuse.py')
human = load('story_human', 'verify-human-rendering.py')
require, same, sha = human.require, human.same, human.sha
FAULTS = {'timestamp', 'schema', 'type', 'exception', 'array', 'optional', 'unsafe', 'bom', 'boundary', 'copy', 'boolean-number'}


def verify(catalog, row, actual):
    wire.verify(catalog, row, actual)
    same([0, 1], row['presentation']['formats']); same([0, 1], row['presentation']['details'])
    same(actual['wires'][0], actual['afterRendering'])
    count = row['expected']['records']; cap = row['input']['maxEntryBytes']; frames = actual['renderFrames']
    require(len(frames) == 4 * count and {(f['recordIndex'], f['format'], f['detail']) for f in frames} == {(i, f, d) for i in range(count) for f in [0, 1] for d in [0, 1]}, 'Complete view matrix differs')
    for frame in frames:
        raw = base64.b64decode(actual['wires'][0][frame['recordIndex']], validate=True); line = base64.b64decode(frame['line'], validate=True)
        lost = row['expected']['truncated'] if frame['format'] == 1 else row['presentation']['normalTruncated' if frame['detail'] == 0 else 'verboseTruncated']
        same(lost, frame['truncated']); same(frame['format'] == 1, frame['jsonMemoryReused'])
        if frame['format'] == 1:
            require(raw == line, 'JSONL was changed by detail'); continue
        source = json.loads(raw); fields = human.read(line, cap)
        expected = {'timestamp', 'severity', 'component', 'event_name', 'message'}
        for name, key in [('timestamp', 'timestamp'), ('severity', 'status'), ('component', 'component'), ('event_name', 'event_name'), ('message', 'message')]:
            same(source[key], fields[name])
        if lost:
            expected.add('truncated'); same(True, fields['truncated'])
        if frame['detail'] == 1:
            for key in ['service', 'env', 'version', 'operation_id', 'trace_id', 'span_id', 'attributes', 'exception', 'message_template']:
                if key in source and key not in row['presentation']['verboseOmittedPayload']:
                    same(source[key], fields[key]); expected.add(key)
            for key, value in source['resource'].items():
                same(value, fields['resource.' + key]); expected.add('resource.' + key)
        require(set(fields) == expected, 'Human information fields differ')


def frame(actual, format, detail):
    return next(f for f in actual['renderFrames'] if f['recordIndex'] == 0 and f['format'] == format and f['detail'] == detail)


def replace_capture(actual, index, raw):
    value = base64.b64encode(raw).decode('ascii')
    actual['wires'][0][index] = value; actual['wires'][1][index] = value; actual['afterRendering'][index] = value
    for f in actual['renderFrames']:
        if f['recordIndex'] == index and f['format'] == 1:
            f['line'] = value
    actual['stream'] = base64.b64encode(b''.join(base64.b64decode(v, validate=True) for v in actual['wires'][0])).decode('ascii')


def reverse_maps(value):
    if isinstance(value, dict):
        return {k: reverse_maps(v) for k, v in reversed(list(value.items()))}
    if isinstance(value, list):
        return [reverse_maps(v) for v in value]
    return value


def human_line(fields, separator=' ', padded=False):
    return (('  ' if padded else '') + separator.join(k + '=' + ('  ' if padded else '') + json.dumps(v, ensure_ascii=True) for k, v in fields.items()) + ('  ' if padded else '') + '\n').encode('utf-8')


def damage(actual, fault):
    damaged = copy.deepcopy(actual)
    if fault in ['timestamp', 'schema']:
        source = json.loads(base64.b64decode(damaged['wires'][0][0], validate=True))
        source['timestamp' if fault == 'timestamp' else 'schema_version'] = '2026-10-12T10:11:13.1234567Z' if fault == 'timestamp' else 2
        replace_capture(damaged, 0, (json.dumps(source) + '\n').encode('utf-8'))
    elif fault == 'copy':
        frame(damaged, 1, 0)['jsonMemoryReused'] = False
    else:
        f = frame(damaged, 0, 1); raw = base64.b64decode(f['line'], validate=True)
        if fault == 'unsafe': raw = b'\x1b' + raw[1:]
        elif fault == 'bom': raw = b'\xef\xbb\xbf' + raw
        elif fault == 'boundary': raw += b'\n'
        else:
            fields = human.read(raw, 65536)
            if fault == 'type': fields['attributes']['integer'] = '9007199254740991'
            elif fault == 'boolean-number': fields['attributes']['true'] = 1
            elif fault == 'array': fields['attributes']['array'] = [1, 2, None]
            elif fault == 'exception': fields['exception']['inner_exceptions'][0]['message'] = 'changed'
            else: fields['operation_id'] = 'invented'
            raw = human_line(fields)
        f['line'] = base64.b64encode(raw).decode('ascii')
    return damaged


def main():
    parser = argparse.ArgumentParser()
    for name in ['dotnet', 'host', 'catalog', 'presentation-catalog', 'human-catalog', 'output']:
        parser.add_argument('--' + name, required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args(); output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    catalog = json.loads(Path(args.catalog).read_text(encoding='utf-8')); rows = catalog['cases']
    require(catalog['schemaVersion'] == 1 and len(rows) == 21 and len({r['id'] for r in rows}) == 21, 'Story cases differ')
    require(set(catalog['criteria']) == {'WP05-S01-AC01', 'WP05-S01-AC02', 'WP05-S01-AC03', 'WP05-S01-AC04'}, 'Story criteria differ')
    report = dict(scope=catalog['scope'], family=args.family, cases=[], negativeControlsRejected=[], positiveControlsAccepted=[])
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'rendering-story', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-render-story-') as temporary:
            process = subprocess.run(command, cwd=temporary, env=dict(os.environ, CORE_LOG_LEVEL='invalid-must-not-be-read', TERM='dumb', NO_COLOR='1'), capture_output=True, timeout=30)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': process failed')
            require(not list(Path(temporary).iterdir()), 'Rendering performed file I/O')
        actual = json.loads(process.stdout); verify(catalog, row, actual); observed = actual['observed']
        require(observed['operatingSystem'] == args.family and observed['runtimeDescription'] == '.NET 10.0.12', 'Runtime/platform differs')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(args.catalog), 'Actual managed/input hashes differ')
        if row['id'] in ['RENDER-INFO', 'RENDER-ABSENT']:
            for fault in sorted(FAULTS - {'optional'} if row['id'] == 'RENDER-INFO' else {'optional'}):
                try:
                    verify(catalog, row, damage(actual, fault))
                except (AssertionError, KeyError, ValueError):
                    report['negativeControlsRejected'].append(fault)
                else:
                    raise AssertionError('Story corruption accepted: ' + fault)
        if row['id'] == 'RENDER-INFO':
            for control in ['json-layout', 'human-layout', 'integer-float', 'number-notation']:
                changed = copy.deepcopy(actual)
                if control in ['json-layout', 'integer-float', 'number-notation']:
                    for index in range(2):
                        source = json.loads(base64.b64decode(changed['wires'][0][index], validate=True))
                        text = ' ' + json.dumps(reverse_maps(source), ensure_ascii=True) + ' \n'
                        original_text = text
                        if control == 'integer-float': text = text.replace('"integer": 9007199254740991', '"integer": 9007199254740991.0')
                        elif control == 'number-notation': text = text.replace('"number": 1.25', '"number": 1.25e0')
                        if control in ['integer-float', 'number-notation']:
                            require(text != original_text, 'Numeric control did not change its token')
                        replace_capture(changed, index, text.encode('utf-8'))
                else:
                    for f in changed['renderFrames']:
                        if f['format'] == 0:
                            fields = human.read(base64.b64decode(f['line'], validate=True), 65536)
                            f['line'] = base64.b64encode(human_line(reverse_maps(fields), '   ', padded=True)).decode('ascii')
                verify(catalog, row, changed); report['positiveControlsAccepted'].append(control)
        report['cases'].append(dict(id=row['id'], passed=True, views=len(actual['renderFrames']), command=command, observed=observed))
    require(set(report['negativeControlsRejected']) == FAULTS and set(report['positiveControlsAccepted']) == {'json-layout', 'human-layout', 'integer-float', 'number-notation'}, 'Story controls did not execute')
    supplemental = {}
    for name, script, path in [('presentation', 'verify-presentation.py', args.presentation_catalog), ('human', 'verify-human-rendering.py', args.human_catalog)]:
        command = [sys.executable, str(Path(__file__).with_name(script)), '--dotnet', args.dotnet, '--host', args.host, '--catalog', path, '--output', str(output / name), '--family', args.family]
        result = subprocess.run(command, capture_output=True, timeout=240)
        (output / (name + '.stdout.log')).write_bytes(result.stdout); (output / (name + '.stderr.log')).write_bytes(result.stderr)
        require(result.returncode == 0, 'Supplemental ' + name + ' failed')
        summary = json.loads((output / name / 'summary.json').read_text(encoding='utf-8')); require(summary['passed'], 'Supplemental checks did not pass')
        require(summary['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and summary['hostSha256'] == sha(args.host) and summary['inputsSha256'] == sha(path), 'Supplemental current byte pins differ')
        supplemental[name] = dict(path=name + '/summary.json', count=summary['count'], inputsSha256=sha(path))
    report.update(passed=True, count=len(rows), views=sum(c['views'] for c in report['cases']), supplemental=supplemental, totalNativeProcesses=len(rows) + sum(v['count'] for v in supplemental.values()), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog), normalizedFields=[])
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(family=args.family, cases=len(rows), views=report['views'], processes=report['totalNativeProcesses'], passed=True)))


if __name__ == '__main__':
    main()
