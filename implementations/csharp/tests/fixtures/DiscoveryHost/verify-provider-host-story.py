import argparse, base64, copy, json, os, re, subprocess, tempfile
from pathlib import Path
from importlib.util import spec_from_file_location, module_from_spec
spec = spec_from_file_location('base', Path(__file__).with_name('verify-flush-results.py'))
base = module_from_spec(spec); spec.loader.exec_module(base)
load, sha, digest, decoded, require, number = base.load, base.sha, base.digest, base.decoded, base.require, base.number
MODES = ('async-owned', 'async-external', 'factory-precedence', 'factory-lifetime', 'shutdown', 'shutdown-loss', 'factory-formatter')
CATEGORY = 'CIS.Tests.DiscoveryFixture.ProviderHostStoryFixture.Worker'
def same(a, b):
    if type(a) is not type(b): return False
    if isinstance(a, dict): return a.keys() == b.keys() and all(same(a[k], b[k]) for k in a)
    if isinstance(a, list): return len(a) == len(b) and all(same(x, y) for x, y in zip(a, b))
    return a == b

def check(mode, a, pid):
    require(a['mode'] == mode and type(a['pid']) is int and a['pid'] == pid and pid > 0 and a['runtime'] == '.NET 10.0.12', 'child identity')
    concurrent, shutdown, loss = mode.startswith('async-'), mode in ('shutdown', 'shutdown-loss'), mode == 'shutdown-loss'
    count = 9 if concurrent else 4 if mode == 'factory-precedence' else 1 if mode == 'factory-formatter' else 3
    formats = 9 if concurrent else 2 if mode in ('factory-lifetime', 'shutdown', 'factory-formatter') else 3
    require(a['formatterExceptionPreserved'] is (mode == 'factory-formatter'), 'formatter exception identity')
    require(type(a['formatterCalls']) is int and a['formatterCalls'] == formats and type(a['arrivals']) is int and a['arrivals'] == (2 if concurrent else 0), 'callbacks and cohort')
    require(a['sameStopTask'] is True and a['sameStopResult'] is True and a['workFinishedAtStop'] is shutdown and a['ownerAttention'] is loss, 'observed stop and attention')
    require(a['sharedLiveAfterFactoryDispose'] is (mode == 'factory-lifetime') and type(a['disposedGuards']) is int and a['disposedGuards'] == (2 if mode == 'factory-lifetime' else 0), 'factory and Core ownership')
    require(number(a['invalid']) == (1 if loss else 0) and number(a['truncated']) == 0 and a['leaseReleased'] is True, 'actual accounting and lease')
    require(a['phases'] == (['work.started', 'shutdown.requested', 'work.finished', 'runtime.stop', 'factory.dispose'] if shutdown else []), 'shutdown ordering')
    data = decoded(a['bytes']); require(not data.startswith(b'\xef\xbb\xbf') and data.endswith(b'\n') and b'\r' not in data, 'JSONL framing')
    rows = [load(line) for line in data.splitlines()]; require(len(rows) == count, 'record count')
    for row in rows:
        require(type(row['schema_version']) is int and row['schema_version'] == 1 and row['status'] == 'info' and 'truncated' not in row, 'wire schema/status/truncation')
        require(row['service'] == 'CIS.Core.DiscoveryFixture' and type(row['resource']['process_id']) is int and row['resource']['process_id'] == pid and row['resource']['runtime_description'] == '.NET 10.0.12' and row['resource']['os_family'] == a['os'], 'automatic identity')
    def event(row, name, operation, category, attributes, message='rendered'):
        require((row['event_name'], row.get('operation_id'), row['component'], row['message']) == (name, operation, category, message), 'operation category message')
        require(same(row['attributes'], attributes), 'exact event attributes')
    if concurrent:
        category = 'Owned.Host.Story' if mode == 'async-owned' else CATEGORY
        for owner in (1, 2):
            selected = [row for row in rows if row['event_name'].startswith('owner.' + str(owner) + '.')]
            names = ['owner.' + str(owner) + '.' + x for x in ('outer', 'inner', 'event', 'restored')]
            require([row['event_name'] for row in selected] == names, 'per-operation order')
            for i, row in enumerate(selected):
                attrs = dict(owner=owner, phase='initial' if mode == 'async-owned' else 'changed', common='event' if i == 2 else 'inner' if i == 1 else 'outer', event_id=17)
                if i == 2: attrs['operation_id'] = 'ordinary-event-field'
                event(row, names[i], ('nested.' if i in (1, 2) else 'operation.') + str(owner), category, attrs)
        event(rows[-1], 'after', None, category, {'event_id': 17})
    elif mode == 'factory-precedence':
        common = dict(core=True, service='fake', trace_id='fake', span_id='fake', event_id=17)
        event(rows[0], 'outer', 'host.outer', CATEGORY, dict(common, common='host'))
        event(rows[1], 'inner', 'host.inner', CATEGORY, dict(common, common='event', operation_id='ordinary-event-field', env='fake', version='fake', component='fake', resource='fake'))
        event(rows[2], 'explicit', 'event.explicit', 'Direct.Host.Story', dict(core=True, common='core'), 'direct')
        event(rows[3], 'after', None, CATEGORY, {'event_id': 17})
        require(a['trace'] == '1' * 32 and re.fullmatch('[0-9a-f]{16}', a['span']) and a['span'] != '0' * 16, 'actual Activity')
        for row in rows[:3]: require(row['trace_id'] == a['trace'] and row['span_id'] == a['span'] and row['env'] != 'fake' and row['version'] != 'fake', 'protected Activity and identity')
        require('trace_id' not in rows[3] and 'span_id' not in rows[3], 'Activity does not leak')
    elif mode == 'factory-lifetime':
        event(rows[0], 'before', 'core.owner', CATEGORY, dict(owner='application', event_id=17))
        event(rows[1], 'direct.live', 'core.owner', 'Direct.Host.Story', dict(owner='application'), 'direct')
        event(rows[2], 'other.live', 'core.owner', 'Other.Host.Story', dict(owner='application', event_id=17))
    elif mode == 'factory-formatter':
        event(rows[0], 'after.formatter', None, CATEGORY, {'event_id': 17})
    else:
        event(rows[0], 'work.started', 'background', CATEGORY, dict(owner='worker', event_id=17))
        event(rows[1], 'work.finished', 'background', CATEGORY, dict(owner='worker', event_id=17))
        event(rows[2], 'owner.finishing', None, 'Direct.Host.Story', {}, 'direct')
    result = a['result']; require(result['completion'] == 'Completed' and result['succeeded'] is True and result['loss'] is loss, 'actual Core stop result')
    require(len(result['outputs']) == 1 and result['outputs'][0]['name'] == 'file', 'file composition')
    out = result['outputs'][0]; require(tuple(number(out[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (count, count, 0, 0, 0) and out['flush'] == 'Succeeded', 'exact drain delivery')

def controls(cases):
    good = {c['id']: c for c in cases}; result = []
    edits = [('formatter-identity', 'factory-formatter', 'formatter exception identity', lambda a: a.update(formatterExceptionPreserved=False)), ('cohort', 'async-owned', 'callbacks and cohort', lambda a: a.update(arrivals=1)), ('formatter', 'async-external', 'callbacks and cohort', lambda a: a.update(formatterCalls=10)), ('shared-task', 'shutdown', 'observed stop and attention', lambda a: a.update(sameStopTask=False)), ('shared-result', 'shutdown', 'observed stop and attention', lambda a: a.update(sameStopResult=False)), ('finish', 'shutdown', 'observed stop and attention', lambda a: a.update(workFinishedAtStop=False)), ('loss-attention', 'shutdown-loss', 'observed stop and attention', lambda a: a.update(ownerAttention=False)), ('runtime', 'factory-lifetime', 'factory and Core ownership', lambda a: a.update(sharedLiveAfterFactoryDispose=False)), ('guards', 'factory-lifetime', 'factory and Core ownership', lambda a: a.update(disposedGuards=0)), ('invalid', 'shutdown-loss', 'actual accounting and lease', lambda a: a.update(invalid='0')), ('lease', 'shutdown', 'actual accounting and lease', lambda a: a.update(leaseReleased=False)), ('ordering', 'shutdown', 'shutdown ordering', lambda a: a['phases'].reverse()), ('loss-result', 'shutdown-loss', 'actual Core stop result', lambda a: a['result'].update(loss=False)), ('ticket', 'async-owned', 'exact drain delivery', lambda a: a['result']['outputs'][0].update(boundary='8'))]
    for identity, mode, reason, edit in edits:
        c = good[mode]; a = copy.deepcopy(c['report']); edit(a)
        try: check(mode, a, c['pid'])
        except AssertionError as error: require(str(error) == reason, 'control reason'); result.append(dict(id=identity, rejected=True, reason=reason))
        else: raise AssertionError('corruption accepted ' + identity)
    wire_edits = [('owned-copy', 'async-owned', 'exact event attributes', lambda rows: rows[0]['attributes'].update(phase='changed')), ('external-copy', 'async-external', 'exact event attributes', lambda rows: rows[0]['attributes'].update(phase='initial')), ('identity', 'factory-precedence', 'automatic identity', lambda rows: rows[0].update(service='fake')), ('Activity', 'factory-precedence', 'protected Activity and identity', lambda rows: rows[0].update(trace_id='fake')), ('factory-scope', 'factory-lifetime', 'operation category message', lambda rows: rows[1].update(operation_id='host.fake'))]
    for identity, mode, reason, edit in wire_edits:
        c = good[mode]; a = copy.deepcopy(c['report']); rows = [load(line) for line in decoded(a['bytes']).splitlines()]; edit(rows); a['bytes'] = base64.b64encode(b''.join(json.dumps(row, separators=(',', ':')).encode() + b'\n' for row in rows)).decode()
        try: check(mode, a, c['pid'])
        except AssertionError as error: require(str(error) == reason, 'wire control reason'); result.append(dict(id=identity, rejected=True, reason=reason))
        else: raise AssertionError('wire corruption accepted ' + identity)
    return result

def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'): parser.add_argument('--' + key, required=True)
    args = parser.parse_args(); require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]; catalog = repo / 'conformance/logging/provider-host-story-cases.json'
    require([x['id'] for x in load(catalog.read_bytes())['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True); cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-provider-host-story-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir(); env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'provider-host-story', mode, str(work)]
            child = subprocess.Popen(command, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try: stdout, stderr = child.communicate(timeout=60)
            except BaseException: child.kill(); child.communicate(timeout=15); raise
            (out / (mode + '.stdout.bin')).write_bytes(stdout); (out / (mode + '.stderr.bin')).write_bytes(stderr)
            require(child.returncode == 0 and stderr == b'', 'child failure ' + mode); a = load(stdout); check(mode, a, child.pid)
            require((a['os'], a['coreSha256'], a['fixtureSha256']) == (args.family, sha(host.parent / 'CIS.Core.dll'), sha(host)), 'exact assemblies')
            data = (work / 'logs \u03a9' / 'active.log').read_bytes(); require(data == decoded(a['bytes']), 'actual file bytes'); (out / (mode + '.file.bin')).write_bytes(data)
            cases.append(dict(id=mode, pid=child.pid, exitCode=child.returncode, command=command, report=a, stdoutSha256=digest(stdout), stderrSha256=digest(stderr), fileSha256=digest(data)))
    report = dict(schema='cis.logging.provider-host-story-native/v1', task='WP06-S02-T05', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(catalog), cases=cases, controls=controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n'); print(json.dumps(dict(platform=args.family, children=len(cases), controls=len(report['controls']), passed=True)))

if __name__ == '__main__': main()
