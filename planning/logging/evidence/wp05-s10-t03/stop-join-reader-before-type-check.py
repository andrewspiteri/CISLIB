"""Actual caller-cancellation/disposal children and independent observation checks."""
import argparse, copy, importlib.util, json, os, subprocess, tempfile
from datetime import datetime
from pathlib import Path
spec = importlib.util.spec_from_file_location('join_base', Path(__file__).with_name('verify-flush-results.py'))
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
require, load, sha, digest, decoded, number = base.require, base.load, base.sha, base.digest, base.decoded, base.number
MODES = ('empty', 'dispose-first', 'stop-first', 'pre-cancelled', 'cancel-later', 'cancelled-joins', 'deadline-joins', 'after-completion-cancel')


def check(mode, a, pid=None):
    require(a['mode'] == mode and mode in MODES, 'mode')
    require(a['runtime'] == '.NET 10.0.12', 'actual runtime')
    if pid is not None:
        require(type(a['pid']) is int and a['pid'] == pid and pid > 0, 'actual child PID')
    started = a['preCancelledStart']
    require(started == {'admissionClosed': True, 'shutdownStarted': True, 'state': 'Stopping', 'writeOutcome': 'Stopped', 'deadlineCreated': 1} if mode == 'pre-cancelled' else started is None, 'pre-cancelled initiation')
    empty, deadline = mode == 'empty', mode == 'deadline-joins'
    result = a['result']
    require(set(result) == {'completion', 'succeeded', 'loss', 'captured', 'observed', 'outputs'}, 'result shape')
    require((result['completion'], result['succeeded'], result['loss']) == ('TimedOut' if deadline else 'Completed', not deadline, False), 'summary flags')
    for key in ('captured', 'observed'):
        require(datetime.fromisoformat(result[key]).utcoffset().total_seconds() == 0, 'UTC labels')
    require([o['name'] for o in result['outputs']] == ([] if empty else ['console']), 'output order')
    for o in result['outputs']:
        require(tuple(number(o[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == ((1, 0, 0, 0, 1) if deadline else (1, 1, 0, 0, 0)), 'covered dispositions')
        require(o['flush'] == ('Pending' if deadline else 'Succeeded'), 'writer flush')
    require(a['tokenIdentity'] is True, 'caller token identity')
    expected = 64 if mode in ('cancelled-joins', 'deadline-joins') else 1 if mode in ('empty', 'pre-cancelled', 'cancel-later', 'after-completion-cancel') else 0
    require(a['cancelledWaiters'] == expected and a['disposeJoiners'] == 64, 'actual joining callers')
    require(all(a[k] is True for k in ('sameTask', 'sameResult', 'immutable')), 'original operation identity')
    require(a['outputCancelledBeforeDeadline'] is False and a['outputCancelledAtReturn'] is deadline, 'caller-only cancellation')
    require(a['pendingAtReturn'] is deadline and a['finalState'] == 'Stopped', 'bounded wait and late completion')
    stream = a['stream']
    require(stream['disposed'] == 0, 'borrowed stream lifetime')
    require((stream['writes'], stream['written'], stream['flushes'], stream['flushed']) == ((0, 0, 0, 0) if empty else (1, 1, 0, 0) if deadline else (1, 1, 1, 1)), 'actual writer calls')
    rows = base.records(decoded(stream['bytes']), a['pid'])
    require([row['event_name'] for row in rows] == ([] if empty else ['one']), 'actual records')
    require(all(len(line) + 1 <= 16384 for line in decoded(stream['bytes']).splitlines()), 'bounded records')
    for o in a['live']:
        require(tuple(number(o[k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending')) == (1, 1, 0, 0, 0) and o['fault'] is None, 'eventual live disposition')
    require(len(a['live']) == (0 if empty else 1), 'live outputs')
    timers = a['timers']
    require((timers['created'], timers['disposed'], timers['active'], timers['periodicFires'], timers['deadlineFires']) == (0 if empty else 1, 0 if empty else 1, 0, 0, 1 if deadline else 0), 'original deadline only')


def corruption_controls(cases):
    samples = {c['id']: c['report'] for c in cases}
    edits = [
        ('pre-admission', 'pre-cancelled', 'pre-cancelled initiation', lambda a: a['preCancelledStart'].update(admissionClosed=False)),
        ('pre-start', 'pre-cancelled', 'pre-cancelled initiation', lambda a: a['preCancelledStart'].update(shutdownStarted=False)),
        ('pre-state', 'pre-cancelled', 'pre-cancelled initiation', lambda a: a['preCancelledStart'].update(state='Running')),
        ('pre-write', 'pre-cancelled', 'pre-cancelled initiation', lambda a: a['preCancelledStart'].update(writeOutcome='Accepted')),
        ('pre-timer', 'pre-cancelled', 'pre-cancelled initiation', lambda a: a['preCancelledStart'].update(deadlineCreated=0)),
        ('pid', 'stop-first', 'actual child PID', lambda a: a.update(pid=a['pid'] + 1)),
        ('runtime', 'stop-first', 'actual runtime', lambda a: a.update(runtime='.NET 10.0.13')),
        ('shape', 'stop-first', 'result shape', lambda a: a['result'].update(extra=True)),
        ('summary', 'deadline-joins', 'summary flags', lambda a: a['result'].update(succeeded=True)),
        ('pending', 'deadline-joins', 'covered dispositions', lambda a: a['result']['outputs'][0].update(pending='0')),
        ('flush', 'deadline-joins', 'writer flush', lambda a: a['result']['outputs'][0].update(flush='Succeeded')),
        ('token', 'pre-cancelled', 'caller token identity', lambda a: a.update(tokenIdentity=False)),
        ('caller', 'cancelled-joins', 'actual joining callers', lambda a: a.update(cancelledWaiters=63)),
        ('dispose', 'dispose-first', 'actual joining callers', lambda a: a.update(disposeJoiners=63)),
        ('identity', 'stop-first', 'original operation identity', lambda a: a.update(sameTask=False)),
        ('cancel', 'cancel-later', 'caller-only cancellation', lambda a: a.update(outputCancelledBeforeDeadline=True)),
        ('late', 'deadline-joins', 'bounded wait and late completion', lambda a: a.update(pendingAtReturn=False)),
        ('borrowed', 'stop-first', 'borrowed stream lifetime', lambda a: a['stream'].update(disposed=1)),
        ('calls', 'deadline-joins', 'actual writer calls', lambda a: a['stream'].update(flushes=1)),
        ('records', 'stop-first', 'actual records', lambda a: a['stream'].update(bytes='')),
        ('live', 'deadline-joins', 'eventual live disposition', lambda a: a['live'][0].update(pending='1')),
        ('timer', 'dispose-first', 'original deadline only', lambda a: a['timers'].update(created=2)),
    ]
    result = []
    for identity, mode, reason, edit in edits:
        bad = copy.deepcopy(samples[mode]); edit(bad)
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
    mapping = load((repo / 'conformance/logging/stop-join-cases.json').read_bytes())
    require([c['id'] for c in mapping['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True)
    cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-stop-join-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir()
            env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'stop-join', mode, str(work)]
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
    report = dict(schema='cis.logging.stop-join-native/v1', task='WP05-S10-T03', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(repo / 'conformance/logging/stop-join-cases.json'), cases=cases, controls=corruption_controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, children=len(cases), controls=len(report['controls']), passed=True)))


if __name__ == '__main__':
    main()
