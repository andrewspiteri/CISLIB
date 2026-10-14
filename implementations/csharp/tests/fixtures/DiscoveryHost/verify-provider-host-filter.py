import argparse, base64, copy, json, os, subprocess, tempfile
from pathlib import Path
from importlib.util import spec_from_file_location, module_from_spec
spec = spec_from_file_location('base', Path(__file__).with_name('verify-flush-results.py'))
base = module_from_spec(spec); spec.loader.exec_module(base)
load, sha, digest, decoded, require, number = base.load, base.sha, base.digest, base.decoded, base.require, base.number
MODES = ('broad', 'provider-rule', 'category-rule', 'narrow-host', 'narrow-category', 'trace-file', 'core-off')

def check(mode, a, stderr, pid):
    require(a['mode'] == mode and a['pid'] == pid and pid > 0 and a['runtime'] == '.NET 10.0.12', 'child identity')
    off, narrow = mode == 'core-off', mode in ('narrow-host', 'narrow-category')
    count = 0 if off else 3 if narrow else 6 if mode == 'trace-file' else 5
    require(type(a['formatterCalls']) is int and type(a['stateEnumerations']) is int and a['formatterCalls'] == a['stateEnumerations'] == count, 'skipped callback work')
    require(a['coreDebug'] is (not off) and a['hostDebug'] is (not off and not narrow), 'effective host interest')
    require(a['fileExists'] is (not off) and a['leaseReleased'] is (not off), 'physical lifetime')
    require(a['consoleLevel'] == ('None' if off else 'Warning') and a['fileLevel'] == ('None' if off else 'Trace' if mode == 'trace-file' else 'Debug'), 'core thresholds')
    require(number(a['invalid']) == 0, 'invalid accounting')
    file_names = [] if off else ['warning', 'error', 'critical', 'direct.debug'] if narrow else ['trace', 'debug', 'information', 'warning', 'error', 'critical'] if mode == 'trace-file' else ['debug', 'information', 'warning', 'error', 'critical']
    console_names = [] if off else ['warning', 'error', 'critical']
    for data, names in ((decoded(a['fileBytes']), file_names), (stderr, console_names)):
        require(not data.startswith(b'\xef\xbb\xbf') and (not data or data.endswith(b'\n')) and b'\r' not in data, 'JSONL framing')
        rows = [load(line) for line in data.splitlines()]
        require([x['event_name'] for x in rows] == names, 'destination severity records')
        for row in rows:
            name = row['event_name']; direct = name == 'direct.debug'
            require(type(row['schema_version']) is int and row['schema_version'] == 1 and row['status'] == {'information': 'info', 'warning': 'warn', 'direct.debug': 'debug'}.get(name, name), 'wire level')
            require(row['message'] == ('direct' if direct else 'rendered') and row['component'] == ('Direct.Host.Filter' if direct else 'Provider.Host.Filters'), 'mapped message and category')
            require(type(row['resource']['process_id']) is int and row['resource']['process_id'] == pid and row['resource']['runtime_description'] == '.NET 10.0.12' and row['resource']['os_family'] == a['os'] and row['service'] == 'CIS.Core.DiscoveryFixture', 'automatic facts')
            require('truncated' not in row and 'operation_id' not in row, 'untruncated ordinary event')
            if not direct:
                attrs = row['attributes']; require(set(attrs) == {'event_id', 'value'} and type(attrs['event_id']) is int and attrs['event_id'] == 17 and type(attrs['value']) is int and attrs['value'] == 7, 'structured state')
    result = a['result']
    require(result['completion'] == 'Completed' and result['succeeded'] is True and result['loss'] is False, 'actual stop result')
    require([x['name'] for x in result['outputs']] == ([] if off else ['console', 'file']), 'output composition')
    for out in result['outputs']:
        n = len(console_names if out['name'] == 'console' else file_names)
        require(tuple(number(out[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (n, n, 0, 0, 0) and out['flush'] == 'Succeeded', 'separate core delivery')
    require([x['name'] for x in a['outputs']] == ([] if off else ['console', 'file']), 'live output composition')
    for out in a['outputs']:
        n = len(console_names if out['name'] == 'console' else file_names)
        require(tuple(number(out[k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending', 'full', 'unavailable')) == (n, n, 0, 0, 0, 0, 0) and out['fault'] is None, 'live delivery accounting')

def controls(cases):
    good = {c['id']: c for c in cases}
    edits = [('formatter', 'broad', 'skipped callback work', lambda a: a.update(formatterCalls=6)), ('enumeration', 'core-off', 'skipped callback work', lambda a: a.update(stateEnumerations=1)), ('narrow-interest', 'narrow-host', 'effective host interest', lambda a: a.update(hostDebug=True)), ('lease', 'provider-rule', 'physical lifetime', lambda a: a.update(leaseReleased=False)), ('threshold', 'trace-file', 'core thresholds', lambda a: a.update(fileLevel='Debug')), ('stop', 'broad', 'actual stop result', lambda a: a['result'].update(loss=True)), ('ticket', 'broad', 'separate core delivery', lambda a: a['result']['outputs'][1].update(boundary='4')), ('live', 'category-rule', 'live delivery accounting', lambda a: a['outputs'][1].update(accepted='4'))]
    result = []
    for identity, mode, reason, edit in edits:
        c = good[mode]; a = copy.deepcopy(c['report']); edit(a)
        try: check(mode, a, decoded(c['stderr']), c['pid'])
        except AssertionError as error: require(str(error) == reason, 'control reason'); result.append(dict(id=identity, rejected=True, reason=reason))
        else: raise AssertionError('corruption accepted ' + identity)
    # Remove Debug from the otherwise valid broad physical file report.
    c = good['broad']; a = copy.deepcopy(c['report']); lines = decoded(a['fileBytes']).splitlines(keepends=True); a['fileBytes'] = base64.b64encode(b''.join(lines[1:])).decode()
    try: check('broad', a, decoded(c['stderr']), c['pid'])
    except AssertionError as error: require(str(error) == 'destination severity records', 'missing Debug control'); result.append(dict(id='missing-debug', rejected=True, reason=str(error)))
    else: raise AssertionError('missing Debug accepted')
    return result

def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'): parser.add_argument('--' + key, required=True)
    args = parser.parse_args(); require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]; catalog = repo / 'conformance/logging/provider-host-filter-cases.json'
    require([x['id'] for x in load(catalog.read_bytes())['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True); cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-host-filter-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir(); env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'provider-host-filter', mode, str(work)]
            child = subprocess.Popen(command, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try: stdout, stderr = child.communicate(timeout=60)
            except BaseException: child.kill(); child.communicate(timeout=15); raise
            (out / (mode + '.stdout.bin')).write_bytes(stdout); (out / (mode + '.stderr.bin')).write_bytes(stderr)
            require(child.returncode == 0, 'child failure ' + mode); a = load(stdout); check(mode, a, stderr, child.pid)
            require((a['os'], a['coreSha256'], a['fixtureSha256'], a['factorySha256']) == (args.family, sha(host.parent / 'CIS.Core.dll'), sha(host), sha(host.parent / 'Microsoft.Extensions.Logging.dll')), 'exact assemblies')
            path = work / 'logs \u03a9' / 'active.log'; data = path.read_bytes() if path.exists() else b''
            require(path.exists() is a['fileExists'] and data == decoded(a['fileBytes']), 'actual file bytes')
            (out / (mode + '.file.bin')).write_bytes(data)
            cases.append(dict(id=mode, pid=child.pid, exitCode=child.returncode, command=command, report=a, stderr=base64.b64encode(stderr).decode(), stdoutSha256=digest(stdout), stderrSha256=digest(stderr), fileSha256=digest(data)))
    report = dict(schema='cis.logging.provider-host-filter-native/v1', task='WP06-S02-T03', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), factorySha256=sha(host.parent / 'Microsoft.Extensions.Logging.dll'), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(catalog), cases=cases, controls=controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n'); print(json.dumps(dict(platform=args.family, children=len(cases), controls=len(report['controls']), passed=True)))

if __name__ == '__main__': main()
