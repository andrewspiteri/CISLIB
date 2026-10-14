"""Execute actual StopAsync children and independently validate captured observations."""
import argparse, copy, importlib.util, json, os, subprocess, tempfile
from datetime import datetime
from pathlib import Path

spec = importlib.util.spec_from_file_location('stop_start_records', Path(__file__).with_name('verify-flush-results.py'))
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
require, load, sha, digest, decoded, number = base.require, base.load, base.sha, base.digest, base.decoded, base.number
MODES = ('empty', 'normal', 'dual', 'configured-default', 'configured-min', 'configured-max', 'configured-below', 'configured-above', 'env', 'shared', 'pre-cancelled', 'blocked-write', 'blocked-flush', 'clock-correction', 'late-facts', 'closed-writes')


def check(mode, a, pid=None):
    require(a['mode'] == mode, 'mode')
    require(a['runtime'] == '.NET 10.0.12', 'actual runtime')
    if pid is not None:
        require(type(a['pid']) is int and a['pid'] == pid and pid > 0, 'actual child PID')
    if mode in ('configured-below', 'configured-above'):
        require(a['error'] == 'ArgumentOutOfRangeException' and a['acquired'] == 0, 'validation before acquisition')
        require(a['timers']['created'] == 0, 'no invalid-input timer')
        return
    empty, dual, flush, late = mode == 'empty', mode == 'dual', mode == 'blocked-flush', mode == 'late-facts'
    success = mode in ('empty', 'normal', 'dual', 'closed-writes')
    r = a['result']
    require(set(r) == {'completion', 'succeeded', 'loss', 'captured', 'observed', 'invalid', 'truncated', 'outputs'}, 'result shape')
    for key in ('captured', 'observed'):
        try:
            stamp = datetime.fromisoformat(r[key])
        except (TypeError, ValueError):
            raise AssertionError('UTC labels')
        require(stamp.utcoffset() is not None and stamp.utcoffset().total_seconds() == 0, 'UTC labels')
    require((r['completion'], r['succeeded'], r['loss']) == ('Completed' if success else 'TimedOut', success, late), 'summary flags')
    require((number(r['invalid']), number(r['truncated'])) == (1 if late else 0, 0), 'captured global facts')
    require(number(a['liveInvalid']) == (2 if late else 0), 'later live facts')
    outputs = r['outputs']
    require([o['name'] for o in outputs] == ([] if empty else ['console', 'file'] if dual else ['console']), 'enabled output order')
    for i, o in enumerate(outputs):
        require(set(o) == {'name', 'boundary', 'written', 'failed', 'abandoned', 'pending', 'flush', 'filtered', 'full', 'unavailable'}, 'output shape')
        target = 2 if dual and i == 0 else 1
        facts = tuple(number(o[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending'))
        require(facts[0] == sum(facts[1:]), 'boundary partition')
        require(facts == ((target, target, 0, 0, 0) if success or flush else (1, 0, 0, 0, 1)), 'covered dispositions')
        require(o['flush'] == ('Succeeded' if success else 'Pending'), 'captured writer flush')
        require(tuple(number(o[k]) for k in ('filtered', 'full', 'unavailable')) == (1 if dual and i == 1 else 0, 0, 0), 'captured rejection facts')
    require(a['cachedIdentity'] is True, 'cached result identity')
    require(a['immutableAfterIo'] is True, 'immutable after late I/O')
    require(a['joinedCallers'] == 64, 'repeated joining callers')
    require(a['concurrentStarters'] == (32 if mode == 'shared' else 0), 'concurrent first callers')
    require(a['lateFactories'] == 0, 'closed lazy capture')
    require(a['tokenPreserved'] is (mode == 'pre-cancelled'), 'caller cancellation token')
    require(a['finalState'] == 'Stopped' and a['sharedWorkersJoined'] is True, 'eventual worker join')
    expected_ms = {'configured-default': 5000, 'configured-min': 1, 'configured-max': 300000, 'env': 250}.get(mode, 10)
    require(a['configuredMilliseconds'] == expected_ms, 'frozen configured timeout')
    t = a['timers']
    require((t['created'], t['disposed'], t['active'], t['maximumActive']) == (0 if empty else 1, 0 if empty else 1, 0, 0 if empty else 1), 'one owned deadline')
    require((t['fires'], t['callbacks'], t['pendingJoins']) == (0 if success else 1, 0, 0), 'deadline and callback completion')
    for key, names in [('writer', [] if empty else ['one', 'both'] if dual else ['one']), ('file', ['both'] if dual else [])]:
        w = a[key]
        n = len(names)
        require((w['writes'], w['flushes'], w['maximumIo']) == (n, 1 if n else 0, 1 if n else 0), 'actual sequential I/O')
        require(w['order'] == ['write.' + str(i + 1) for i in range(n)] + (['flush.' + str(n)] if n else []), 'actual I/O order')
        raw = decoded(w['bytes'])
        require(not raw.startswith(b'\xef\xbb\xbf') and b'\r' not in raw and (not raw or raw.endswith(b'\n')), 'record framing')
        lines = raw.splitlines()
        require(all(len(line) + 1 <= 16384 for line in lines), 'bounded encoded record')
        rows = [load(line) for line in lines]
        require([row['event_name'] for row in rows] == names, 'actual selected records')
        require(all(row['attributes']['producer_pid'] == a['pid'] for row in rows), 'wire PID')
        require(all(row['message'] == '\u03a9 \U0001f642' and row['component'] == 'StopStart' and row['status'] == ('warn' if row['event_name'] == 'both' else 'info') for row in rows), 'actual event payload')
    diagnostics = a['diagnostics']
    require([o['name'] for o in diagnostics] == ([] if empty else ['console', 'file'] if dual else ['console']), 'live output order')
    for i, o in enumerate(diagnostics):
        target = 2 if dual and i == 0 else 1
        require(tuple(number(o[k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending', 'full', 'unavailable', 'filtered')) == (target, target, 0, 0, 0, 0, 0, 1 if dual and i == 1 else 0), 'eventual live dispositions')
        require(o['fault'] is None, 'healthy output fault absence')


def corruption_controls(cases):
    samples = {c['id']: c['report'] for c in cases}
    edits = [
        ('pid', 'normal', 'actual child PID', lambda a: a.update(pid=a['pid'] + 1)),
        ('runtime', 'normal', 'actual runtime', lambda a: a.update(runtime='.NET 9.0.0')),
        ('acquisition', 'configured-below', 'validation before acquisition', lambda a: a.update(acquired=1)),
        ('invalid-timer', 'configured-above', 'no invalid-input timer', lambda a: a['timers'].update(created=1)),
        ('shape', 'normal', 'result shape', lambda a: a['result'].update(extra=True)),
        ('utc', 'normal', 'UTC labels', lambda a: a['result'].update(captured='2026-10-13T19:20:21+01:00')),
        ('summary', 'blocked-write', 'summary flags', lambda a: a['result'].update(succeeded=True)),
        ('initial', 'late-facts', 'captured global facts', lambda a: a['result'].update(invalid='2')),
        ('live', 'late-facts', 'later live facts', lambda a: a.update(liveInvalid='1')),
        ('outputs', 'dual', 'enabled output order', lambda a: a['result']['outputs'].reverse()),
        ('partition', 'blocked-write', 'boundary partition', lambda a: a['result']['outputs'][0].update(pending='0')),
        ('disposition', 'blocked-write', 'covered dispositions', lambda a: a['result']['outputs'][0].update(pending='0', written='1')),
        ('flush', 'blocked-flush', 'captured writer flush', lambda a: a['result']['outputs'][0].update(flush='Succeeded')),
        ('filter', 'dual', 'captured rejection facts', lambda a: a['result']['outputs'][1].update(filtered='0')),
        ('cache', 'normal', 'cached result identity', lambda a: a.update(cachedIdentity=False)),
        ('immutable', 'blocked-write', 'immutable after late I/O', lambda a: a.update(immutableAfterIo=False)),
        ('join', 'shared', 'repeated joining callers', lambda a: a.update(joinedCallers=63)),
        ('race', 'shared', 'concurrent first callers', lambda a: a.update(concurrentStarters=31)),
        ('factory', 'closed-writes', 'closed lazy capture', lambda a: a.update(lateFactories=1)),
        ('token', 'pre-cancelled', 'caller cancellation token', lambda a: a.update(tokenPreserved=False)),
        ('worker', 'normal', 'eventual worker join', lambda a: a.update(sharedWorkersJoined=False)),
        ('budget', 'configured-max', 'frozen configured timeout', lambda a: a.update(configuredMilliseconds=1)),
        ('timer', 'shared', 'one owned deadline', lambda a: a['timers'].update(created=2)),
        ('callback', 'blocked-write', 'deadline and callback completion', lambda a: a['timers'].update(callbacks=1)),
        ('io', 'normal', 'actual sequential I/O', lambda a: a['writer'].update(maximumIo=2)),
        ('order', 'dual', 'actual I/O order', lambda a: a['writer']['order'].reverse()),
        ('framing', 'normal', 'record framing', lambda a: a['writer'].update(bytes=base.base64.b64encode(decoded(a['writer']['bytes'])[:-1]).decode('ascii'))),
        ('records', 'dual', 'actual selected records', lambda a: a['writer'].update(bytes=a['file']['bytes'])),
        ('live-disposition', 'normal', 'eventual live dispositions', lambda a: a['diagnostics'][0].update(pending='1')),
        ('fault', 'normal', 'healthy output fault absence', lambda a: a['diagnostics'][0].update(fault='WriteFailed')),
    ]
    result = []
    for identity, mode, reason, edit in edits:
        bad = copy.deepcopy(samples[mode])
        edit(bad)
        try:
            check(mode, bad, samples[mode]['pid'])
        except AssertionError as error:
            require(str(error) == reason, 'unexpected corruption ' + identity + ': ' + str(error))
            result.append(dict(id=identity, rejected=True, reason=reason))
        else:
            raise AssertionError('corruption accepted ' + identity)
    return result


def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'):
        parser.add_argument('--' + key, required=True)
    args = parser.parse_args()
    require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]
    mapping = load((repo / 'conformance/logging/stop-start-cases.json').read_bytes())
    require([c['id'] for c in mapping['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve()
    out.mkdir(parents=True, exist_ok=True)
    cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-stop-start-') as temp:
        for mode in MODES:
            work = Path(temp) / mode
            work.mkdir()
            env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            if mode == 'env':
                env['CORE_LOG_SHUTDOWN_TIMEOUT_MS'] = '250'
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'stop-start', mode, str(work)]
            process = subprocess.Popen(command, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            stdout = stderr = b''
            try:
                try:
                    stdout, stderr = process.communicate(timeout=60)
                except BaseException:
                    process.kill()
                    stdout, stderr = process.communicate(timeout=15)
                    raise
            finally:
                (out / (mode + '.stdout.bin')).write_bytes(stdout)
                (out / (mode + '.stderr.bin')).write_bytes(stderr)
            require(process.returncode == 0 and stderr == b'', 'child failure ' + mode)
            report = load(stdout)
            check(mode, report, process.pid)
            require((report['os'], report['coreSha256'], report['fixtureSha256']) == (args.family, sha(host.parent / 'CIS.Core.dll'), sha(host)), 'actual assemblies')
            cases.append(dict(id=mode, pid=process.pid, command=command, exitCode=0, report=report, stdoutSha256=digest(stdout), stderrSha256=digest(stderr)))
    report = dict(schema='cis.logging.stop-start-native/v1', task='WP05-S10-T01', family=args.family, runtime='.NET 10.0.12', children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(repo / 'conformance/logging/stop-start-cases.json'), cases=cases, controls=corruption_controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, children=len(cases), controls=len(report['controls']), passed=True)))


if __name__ == '__main__':
    main()
