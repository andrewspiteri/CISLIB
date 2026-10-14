"""Execute actual T02 children; independently check deadline/IO/record observations."""
import argparse, copy, importlib.util, json, os, subprocess, tempfile
from datetime import datetime
from pathlib import Path
spec = importlib.util.spec_from_file_location('drain_base', Path(__file__).with_name('verify-flush-results.py'))
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
require, load, sha, digest, decoded, number = base.require, base.load, base.sha, base.digest, base.decoded, base.number
MODES = ('normal-dual', 'queued-only', 'blocked-write', 'single-write', 'cancel-write', 'blocked-flush', 'cancel-flush', 'blocked-callback')


def check(mode, a, pid=None):
    require(a['mode'] == mode, 'mode')
    require(a['runtime'] == '.NET 10.0.12', 'actual runtime')
    if pid is not None:
        require(type(a['pid']) is int and a['pid'] == pid and pid > 0, 'actual child PID')
    normal, queued = mode == 'normal-dual', mode == 'queued-only'
    single = mode == 'single-write'
    flush, cooperative = mode in ('blocked-flush', 'cancel-flush'), mode in ('cancel-write', 'cancel-flush')
    result = a['result']
    require(set(result) == {'completion', 'succeeded', 'loss', 'captured', 'observed', 'invalid', 'truncated', 'outputs'}, 'result shape')
    require((result['completion'], result['succeeded'], result['loss']) == ('Completed' if normal or queued else 'TimedOut', normal, not normal and not flush and not single), 'summary flags')
    require(number(result['invalid']) == number(result['truncated']) == 0, 'initial capture facts')
    for key in ('captured', 'observed'):
        require(datetime.fromisoformat(result[key]).utcoffset().total_seconds() == 0, 'UTC labels')
    require([o['name'] for o in result['outputs']] == (['console', 'file'] if normal else ['console']), 'output order')
    for o in result['outputs']:
        values = tuple(number(o[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending'))
        require(values[0] == sum(values[1:]), 'boundary partition')
        require(values == ((3, 3, 0, 0, 0) if normal else (3, 0, 0, 3, 0) if queued else (1, 1, 0, 0, 0) if flush else (1, 0, 0, 0, 1) if single else (3, 0, 0, 2, 1)), 'covered dispositions')
        require(o['flush'] == ('Succeeded' if normal else 'Pending' if flush or single else 'Failed'), 'final flush resolution')
        require(all(number(o[k]) == 0 for k in ('filtered', 'full', 'unavailable')), 'initial rejection facts')
    require(all(a[k] is True for k in ('sameTask', 'sameResult', 'immutable', 'workersSettled')), 'immutable identity')
    require(a['cancellationRequested'] is (not normal and not queued), 'actual cancellation request')
    if not normal and not flush:
        require(a['registeredAfterDeadline'] == (1 if single else 0), 'unreachable flush removal')
    require(a['registeredAfterIo'] == 0, 'eventual flush removal')
    if mode in ('queued-only', 'blocked-write', 'single-write', 'blocked-flush', 'blocked-callback'):
        require(a['pendingBeforeRelease'] is True, 'actual held IO or worker')
    stream = a['stream']
    require(stream['disposed'] == 0, 'borrowed console lifetime')
    require((stream['writes'], stream['written'], stream['flushes'], stream['flushed']) == ((3, 3, 1, 1) if normal else (0, 0, 0, 0) if queued else (1, 0, 0, 0) if mode == 'cancel-write' else (1, 1, 1, 0 if mode == 'cancel-flush' else 1) if flush else (1, 1, 0, 0)), 'actual writer calls')
    if mode == 'blocked-callback':
        require(stream['callbacks'] == 1 and stream['callbackGateFree'] is True, 'callback outside admission gate')
    raw = decoded(stream['bytes'])
    records = base.records(raw, a['pid'])
    names = ['one', 'two', 'three'] if normal else [] if queued or mode == 'cancel-write' else ['one']
    require([x['event_name'] for x in records] == names, 'actual console records')
    require(all(len(line) + 1 <= 16384 for line in raw.splitlines()), 'bounded complete records')
    if normal:
        require(decoded(a['fileBytes']) == raw, 'actual physical file records')
    else:
        require(a['fileBytes'] is None, 'disabled file absence')
    require([o['name'] for o in a['live']] == (['console', 'file'] if normal else ['console']), 'live output order')
    for o in a['live']:
        expected = (3, 3, 0, 0, 0) if normal else (3, 0, 0, 3, 0) if queued else (3, 0, 1, 2, 0) if mode == 'cancel-write' else (1, 1, 0, 0, 0) if flush else (1, 1, 0, 0, 0) if single else (3, 1, 0, 2, 0)
        require(tuple(number(o[k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending')) == expected, 'later dispositions')
        require(o['fault'] == ('WriteFailed' if mode == 'cancel-write' else 'FlushFailed' if mode == 'cancel-flush' else None), 'safe later fault')
    timers = a['timers']
    require((timers['created'], timers['disposed'], timers['active'], timers['periodicFires'], timers['deadlineFires']) == (2 if normal else 1, 2 if normal else 1, 0, 0, 0 if normal else 1), 'single deadline and cancelled scheduling')


def corruption_controls(cases):
    samples = {c['id']: c['report'] for c in cases}
    edits = [
        ('pid', 'normal-dual', 'actual child PID', lambda a: a.update(pid=a['pid'] + 1)),
        ('late-flush', 'single-write', 'actual writer calls', lambda a: a['stream'].update(flushes=1)),
        ('late-control', 'single-write', 'eventual flush removal', lambda a: a.update(registeredAfterIo=1)),
        ('runtime', 'normal-dual', 'actual runtime', lambda a: a.update(runtime='.NET 10.0.13')),
        ('shape', 'normal-dual', 'result shape', lambda a: a['result'].update(extra=True)),
        ('loss', 'blocked-write', 'summary flags', lambda a: a['result'].update(loss=False)),
        ('partition', 'blocked-write', 'boundary partition', lambda a: a['result']['outputs'][0].update(pending='0')),
        ('disposition', 'blocked-write', 'covered dispositions', lambda a: a['result']['outputs'][0].update(written='1', pending='0')),
        ('flush', 'queued-only', 'final flush resolution', lambda a: a['result']['outputs'][0].update(flush='Pending')),
        ('cancel', 'blocked-write', 'actual cancellation request', lambda a: a.update(cancellationRequested=False)),
        ('registration', 'queued-only', 'unreachable flush removal', lambda a: a.update(registeredAfterDeadline=1)),
        ('pending', 'blocked-write', 'actual held IO or worker', lambda a: a.update(pendingBeforeRelease=False)),
        ('identity', 'blocked-write', 'immutable identity', lambda a: a.update(immutable=False)),
        ('borrowed', 'normal-dual', 'borrowed console lifetime', lambda a: a['stream'].update(disposed=1)),
        ('calls', 'blocked-flush', 'actual writer calls', lambda a: a['stream'].update(flushes=0)),
        ('callback', 'blocked-callback', 'callback outside admission gate', lambda a: a['stream'].update(callbackGateFree=False)),
        ('wire', 'normal-dual', 'actual console records', lambda a: a['stream'].update(bytes='')),
        ('file', 'normal-dual', 'actual physical file records', lambda a: a.update(fileBytes='')),
        ('live', 'cancel-write', 'later dispositions', lambda a: a['live'][0].update(failed='0')),
        ('fault', 'cancel-flush', 'safe later fault', lambda a: a['live'][0].update(fault=None)),
        ('timer', 'normal-dual', 'single deadline and cancelled scheduling', lambda a: a['timers'].update(periodicFires=1)),
    ]
    results = []
    for identity, mode, reason, edit in edits:
        bad = copy.deepcopy(samples[mode]); edit(bad)
        try:
            check(mode, bad, samples[mode]['pid'])
        except AssertionError as error:
            require(str(error) == reason, 'unexpected corruption ' + identity + ': ' + str(error))
            results.append(dict(id=identity, rejected=True, reason=reason))
        else:
            raise AssertionError('corruption accepted ' + identity)
    return results


def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'):
        parser.add_argument('--' + key, required=True)
    args = parser.parse_args()
    require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]
    mapping = load((repo / 'conformance/logging/stop-drain-cases.json').read_bytes())
    require([c['id'] for c in mapping['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True)
    cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-stop-drain-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir()
            env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'stop-drain', mode, str(work)]
            process = subprocess.Popen(command, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try:
                stdout, stderr = process.communicate(timeout=60)
            except BaseException:
                process.kill(); stdout, stderr = process.communicate(timeout=15); raise
            (out / (mode + '.stdout.bin')).write_bytes(stdout); (out / (mode + '.stderr.bin')).write_bytes(stderr)
            require(process.returncode == 0 and stderr == b'', 'child failure ' + mode)
            a = load(stdout); check(mode, a, process.pid)
            require((a['os'], a['coreSha256'], a['fixtureSha256']) == (args.family, sha(host.parent / 'CIS.Core.dll'), sha(host)), 'actual assemblies')
            cases.append(dict(id=mode, pid=process.pid, exitCode=0, command=command, report=a, stdoutSha256=digest(stdout), stderrSha256=digest(stderr)))
    report = dict(schema='cis.logging.stop-drain-native/v1', task='WP05-S10-T02', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(repo / 'conformance/logging/stop-drain-cases.json'), cases=cases, controls=corruption_controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, children=len(cases), controls=len(report['controls']), passed=True)))


if __name__ == '__main__':
    main()
