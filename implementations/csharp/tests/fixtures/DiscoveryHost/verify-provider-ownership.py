import argparse, base64, copy, json, os, subprocess, tempfile
from pathlib import Path
from importlib.util import spec_from_file_location, module_from_spec
spec = spec_from_file_location('base', Path(__file__).with_name('verify-flush-results.py'))
base = module_from_spec(spec); spec.loader.exec_module(base)
load, sha, digest, decoded, require, number = base.load, base.sha, base.digest, base.decoded, base.require, base.number
MODES = ('shared', 'resources', 'scope-handles', 'in-flight')

def check(mode, a, pid):
    require(a['mode'] == mode and type(a['pid']) is int and a['pid'] == pid and pid > 0 and a['runtime'] == '.NET 10.0.12', 'child identity')
    require(a['runtimeLiveAfterProviderDispose'] is True and a['leaseHeldAfterProviderDispose'] is True and a['leaseReleasedAfterCoreStop'] is True, 'application runtime ownership')
    require(type(a['hostDisposals']) is int and a['hostDisposals'] == 0 and type(a['disposedGuards']) is int and a['disposedGuards'] == 3, 'borrowed source and disposed guards')
    require(number(a['invalid']) == number(a['truncated']) == 0, 'valid untruncated events')
    require(type(a['formatterCalls']) is int and a['formatterCalls'] == (2 if mode == 'shared' else 1 if mode == 'in-flight' else 0) and type(a['hostCaptureCalls']) is int and a['hostCaptureCalls'] == (1 if mode in ('shared', 'in-flight') else 0), 'disposal callback work')
    require(a['hostHandleSurvived'] is (mode in ('shared', 'in-flight')) and type(a['lifoErrors']) is int and a['lifoErrors'] == (1 if mode == 'scope-handles' else 0), 'caller scope lifetime')
    if mode == 'resources': require(a['sourceRetainedBefore'] is True and a['sourceReleasedAfter'] is True, 'source reference release')
    else: require(a['sourceRetainedBefore'] is None and a['sourceReleasedAfter'] is None, 'GC probe applicability')
    names = {'shared': ['before', 'direct.scoped', 'other'], 'resources': ['after.release'], 'scope-handles': ['inner', 'outer', 'after'], 'in-flight': ['in.flight', 'after.flight']}[mode]
    operations = {'shared': ['host.operation', 'owned.operation', None], 'resources': [None], 'scope-handles': ['inner', 'outer', None], 'in-flight': ['in.flight', None]}[mode]
    data = decoded(a['bytes']); require(not data.startswith(b'\xef\xbb\xbf') and data.endswith(b'\n') and b'\r' not in data, 'JSONL framing')
    rows = [load(line) for line in data.splitlines()]; require(len(rows) == len(names), 'actual output count')
    for row, name, operation in zip(rows, names, operations):
        adapted = name in ('before', 'other', 'in.flight')
        require(row['event_name'] == name and row.get('operation_id') == operation and row['message'] == ('rendered' if adapted else 'direct'), 'event ownership facts')
        require(row['component'] == ('Other.Ownership' if name == 'other' else 'Provider.Ownership' if adapted else 'Direct.Ownership') and row['status'] == 'info' and type(row['schema_version']) is int and row['schema_version'] == 1, 'wire mapping')
        require(type(row['resource']['process_id']) is int and row['resource']['process_id'] == pid and row['resource']['runtime_description'] == '.NET 10.0.12' and row['resource']['os_family'] == a['os'] and row['service'] == 'CIS.Core.DiscoveryFixture', 'automatic wire facts')
        require(row['attributes'] == ({'event_id': 17} if adapted else {}) and (not adapted or type(row['attributes']['event_id']) is int), 'exact attributes')
        require('truncated' not in row, 'untruncated wire')
    result = a['result']; require(result['completion'] == 'Completed' and result['succeeded'] is True and result['loss'] is False, 'actual Core stop')
    require(len(result['outputs']) == 1 and result['outputs'][0]['name'] == 'file', 'file composition')
    out = result['outputs'][0]; n = len(names)
    require(tuple(number(out[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (n, n, 0, 0, 0) and out['flush'] == 'Succeeded', 'exact Core delivery')

def controls(cases):
    good = {c['id']: c for c in cases}
    edits = [('runtime', 'shared', 'application runtime ownership', lambda a: a.update(runtimeLiveAfterProviderDispose=False)), ('lease', 'shared', 'application runtime ownership', lambda a: a.update(leaseHeldAfterProviderDispose=False)), ('source-disposal', 'resources', 'borrowed source and disposed guards', lambda a: a.update(hostDisposals=1)), ('guards', 'shared', 'borrowed source and disposed guards', lambda a: a.update(disposedGuards=2)), ('formatter', 'in-flight', 'disposal callback work', lambda a: a.update(formatterCalls=2)), ('handles', 'shared', 'caller scope lifetime', lambda a: a.update(hostHandleSurvived=False)), ('lifo', 'scope-handles', 'caller scope lifetime', lambda a: a.update(lifoErrors=0)), ('retention', 'resources', 'source reference release', lambda a: a.update(sourceReleasedAfter=False)), ('stop', 'resources', 'actual Core stop', lambda a: a['result'].update(succeeded=False)), ('ticket', 'in-flight', 'exact Core delivery', lambda a: a['result']['outputs'][0].update(boundary='1'))]
    results = []
    for identity, mode, reason, edit in edits:
        c = good[mode]; a = copy.deepcopy(c['report']); edit(a)
        try: check(mode, a, c['pid'])
        except AssertionError as error: require(str(error) == reason, 'control reason'); results.append(dict(id=identity, rejected=True, reason=reason))
        else: raise AssertionError('corruption accepted ' + identity)
    for mode, index, operation in [('shared', 1, 'host.operation'), ('scope-handles', 0, 'outer'), ('in-flight', 0, None)]:
        c = good[mode]; a = copy.deepcopy(c['report']); rows = [load(line) for line in decoded(a['bytes']).splitlines()]
        if operation is None: rows[index].pop('operation_id')
        else: rows[index]['operation_id'] = operation
        a['bytes'] = base64.b64encode(b''.join(json.dumps(x, separators=(',', ':')).encode() + b'\n' for x in rows)).decode()
        try: check(mode, a, c['pid'])
        except AssertionError as error: require(str(error) == 'event ownership facts', 'wire control reason'); results.append(dict(id='operation-' + mode, rejected=True, reason=str(error)))
        else: raise AssertionError('scope corruption accepted')
    return results

def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'): parser.add_argument('--' + key, required=True)
    args = parser.parse_args(); require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]; catalog = repo / 'conformance/logging/provider-ownership-cases.json'
    require([x['id'] for x in load(catalog.read_bytes())['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True); cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-provider-ownership-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir(); env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'provider-ownership', mode, str(work)]
            child = subprocess.Popen(command, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try: stdout, stderr = child.communicate(timeout=60)
            except BaseException: child.kill(); child.communicate(timeout=15); raise
            (out / (mode + '.stdout.bin')).write_bytes(stdout); (out / (mode + '.stderr.bin')).write_bytes(stderr)
            require(child.returncode == 0 and stderr == b'', 'child failure ' + mode); a = load(stdout); check(mode, a, child.pid)
            require((a['os'], a['coreSha256'], a['fixtureSha256']) == (args.family, sha(host.parent / 'CIS.Core.dll'), sha(host)), 'exact assemblies')
            data = (work / 'logs \u03a9' / 'active.log').read_bytes(); require(data == decoded(a['bytes']), 'actual file bytes'); (out / (mode + '.file.bin')).write_bytes(data)
            cases.append(dict(id=mode, pid=child.pid, exitCode=child.returncode, command=command, report=a, stdoutSha256=digest(stdout), stderrSha256=digest(stderr), fileSha256=digest(data)))
    report = dict(schema='cis.logging.provider-ownership-native/v1', task='WP06-S02-T04', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(catalog), cases=cases, controls=controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n'); print(json.dumps(dict(platform=args.family, children=len(cases), controls=len(report['controls']), passed=True)))

if __name__ == '__main__': main()
