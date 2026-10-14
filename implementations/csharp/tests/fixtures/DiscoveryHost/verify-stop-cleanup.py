"""Native actual-Core observations and independent corruption controls."""
import argparse, copy, importlib.util, json, os, subprocess, tempfile
from datetime import datetime
from pathlib import Path
spec = importlib.util.spec_from_file_location('cleanup_base', Path(__file__).with_name('verify-flush-results.py'))
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
require, load, sha, digest, decoded, number = base.require, base.load, base.sha, base.digest, base.decoded, base.number
MODES = ('normal', 'write-resource-failure', 'flush-resource-failure', 'write-timer-resource-failure', 'resource-failure', 'timer-failure', 'callback-write-failure', 'callback-held', 'ownership-held', 'deadline-cleanup-failure', 'deadline-cleanup-held', 'post-stop-flush', 'primitive-close')


def check(mode, a, pid=None):
    require(a['mode'] == mode and mode in MODES, 'mode')
    require(a['runtime'] == '.NET 10.0.12', 'actual runtime')
    if pid is not None:
        require(type(a['pid']) is int and a['pid'] == pid and pid > 0, 'actual child PID')
    write = mode in ('write-resource-failure', 'write-timer-resource-failure')
    early = write or mode == 'flush-resource-failure'
    deadline = mode in ('callback-write-failure', 'callback-held', 'ownership-held', 'deadline-cleanup-failure', 'deadline-cleanup-held')
    result = a['result']
    require(set(result) == {'completion', 'succeeded', 'loss', 'captured', 'observed', 'outputs'}, 'result shape')
    require((result['completion'], result['succeeded'], result['loss']) == ('TimedOut' if deadline else 'Completed', not deadline and not early and mode != 'primitive-close', early or mode == 'primitive-close'), 'summary flags')
    for k in ('captured', 'observed'):
        require(datetime.fromisoformat(result[k]).utcoffset().total_seconds() == 0, 'UTC labels')
    outputs = result['outputs']
    require([o['name'] for o in outputs] == ['console', 'file'], 'output order')
    require(tuple(number(outputs[0][k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (2 if early else 1, 2 if early else 1, 0, 0, 0), 'console dispositions')
    require(outputs[0]['flush'] == ('Failed' if mode == 'primitive-close' else 'Succeeded'), 'console flush')
    require(tuple(number(outputs[1][k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (1, 0 if deadline or write else 1, 1 if write else 0, 0, 1 if deadline else 0), 'file dispositions')
    require(outputs[1]['flush'] == ('Pending' if deadline else 'Failed' if early or mode == 'primitive-close' else 'Succeeded'), 'file flush')
    fault = 'OutputCleanupFailed' if mode in ('write-resource-failure', 'flush-resource-failure', 'write-timer-resource-failure', 'resource-failure') else 'FileFlushTimerCleanupFailed' if mode == 'timer-failure' else 'CancellationCallbacksFailed' if mode == 'callback-write-failure' else 'ShutdownTimerCleanupFailed' if mode in ('deadline-cleanup-failure', 'deadline-cleanup-held') else None
    live = a['live']
    require([o['name'] for o in live] == ['console', 'file'], 'live outputs')
    require(live[0]['fault'] == (fault if mode in ('deadline-cleanup-failure', 'deadline-cleanup-held') else None) and live[1]['fault'] == fault, 'latest safe faults')
    failed = write or mode == 'callback-write-failure'
    require(tuple(number(live[1][k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending')) == (1, 0 if failed else 1, 1 if failed else 0, 0, 0), 'eventual file disposition')
    owner = a['owner']
    require(owner['calls'] == 1 and owner['gateFree'] is True and owner['activeIo'] is False and owner['activeCallback'] is False, 'safe owned cleanup')
    require(a['borrowedConsoleOpen'] is True, 'borrowed console lifetime')
    require(a['heldOwned'] is deadline and a['heldCallback'] is (mode == 'callback-held'), 'held ownership')
    require(a['sameTask'] is True and a['immutable'] is True and a['disposeJoiners'] == 64, 'original cached operation')
    primary = early or mode == 'callback-write-failure'
    require(a['primaryPreserved'] is (True if primary else None) and a['primaryFirst'] is (True if primary else None) and a['cleanupPreserved'] is True, 'preserved failures')
    errors = 3 if mode == 'write-timer-resource-failure' else 2 if primary else 1 if mode in ('resource-failure', 'timer-failure') else 0
    require(a['workerErrors'] == errors, 'actual failure count')
    file = a['file']
    require((file['writes'], file['written'], file['flushes'], file['flushed'], file['closed']) == (1, 0 if failed else 1, 0 if deadline or write or mode == 'primitive-close' else 1, 0 if deadline or early or mode == 'primitive-close' else 1, 1), 'actual writer calls')
    rows = base.records(decoded(file['bytes']), a['pid']); console = base.records(decoded(a['consoleBytes']), a['pid'])
    require([x['event_name'] for x in rows] == ([] if failed else ['one']) and [x['event_name'] for x in console] == (['one', 'healthy'] if early else ['one']), 'actual records')
    require(all(len(line) + 1 <= 16384 for blob in (file['bytes'], a['consoleBytes']) for line in decoded(blob).splitlines()), 'bounded records')
    require(a['inactiveFlushResolved'] is (mode == 'deadline-cleanup-held'), 'inactive flush settled')
    require(a['lateFlushCompleted'] is (mode == 'deadline-cleanup-held'), 'late flush completed')
    timers = a['timers']; count = 6 if mode == 'post-stop-flush' else 3 if mode == 'deadline-cleanup-held' else 2
    require((timers['created'], timers['disposed'], timers['active'], timers['periodicFires'], timers['deadlineFires'], a['timerDisposeCalls'], a['deadlineDisposeCalls']) == (count, count, 0, 0, 1 if deadline else 0, 1, count - 1), 'single cleanup attempts')
    require(a['postFlushes'] == (4 if mode == 'post-stop-flush' else 1 if mode == 'deadline-cleanup-held' else 0) and a['postFlushSucceeded'] is (mode not in ('post-stop-flush', 'deadline-cleanup-held')), 'closed flush semantics')


def corruption_controls(cases):
    samples = {c['id']: c['report'] for c in cases}
    edits = [
        ('pid', 'normal', 'actual child PID', lambda a: a.update(pid=a['pid'] + 1)),
        ('runtime', 'normal', 'actual runtime', lambda a: a.update(runtime='.NET 10.0.13')),
        ('shape', 'normal', 'result shape', lambda a: a['result'].update(extra=True)),
        ('summary', 'ownership-held', 'summary flags', lambda a: a['result'].update(succeeded=True)),
        ('console', 'normal', 'console dispositions', lambda a: a['result']['outputs'][0].update(written='0')),
        ('pending', 'ownership-held', 'file dispositions', lambda a: a['result']['outputs'][1].update(pending='0')),
        ('flush', 'ownership-held', 'file flush', lambda a: a['result']['outputs'][1].update(flush='Succeeded')),
        ('fault', 'resource-failure', 'latest safe faults', lambda a: a['live'][1].update(fault=None)),
        ('late', 'callback-write-failure', 'eventual file disposition', lambda a: a['live'][1].update(failed='0')),
        ('owner', 'normal', 'safe owned cleanup', lambda a: a['owner'].update(calls=2)),
        ('gate', 'normal', 'safe owned cleanup', lambda a: a['owner'].update(gateFree=False)),
        ('io', 'normal', 'safe owned cleanup', lambda a: a['owner'].update(activeIo=True)),
        ('callback', 'normal', 'safe owned cleanup', lambda a: a['owner'].update(activeCallback=True)),
        ('borrowed', 'normal', 'borrowed console lifetime', lambda a: a.update(borrowedConsoleOpen=False)),
        ('held', 'ownership-held', 'held ownership', lambda a: a.update(heldOwned=False)),
        ('callback-held', 'callback-held', 'held ownership', lambda a: a.update(heldCallback=False)),
        ('identity', 'normal', 'original cached operation', lambda a: a.update(sameTask=False)),
        ('immutable', 'normal', 'original cached operation', lambda a: a.update(immutable=False)),
        ('joiners', 'normal', 'original cached operation', lambda a: a.update(disposeJoiners=63)),
        ('primary', 'write-resource-failure', 'preserved failures', lambda a: a.update(primaryPreserved=False)),
        ('first', 'write-resource-failure', 'preserved failures', lambda a: a.update(primaryFirst=False)),
        ('cleanup', 'write-resource-failure', 'preserved failures', lambda a: a.update(cleanupPreserved=False)),
        ('errors', 'write-timer-resource-failure', 'actual failure count', lambda a: a.update(workerErrors=2)),
        ('calls', 'ownership-held', 'actual writer calls', lambda a: a['file'].update(flushes=1)),
        ('records', 'normal', 'actual records', lambda a: a['file'].update(bytes='')),
        ('timer', 'normal', 'single cleanup attempts', lambda a: a.update(timerDisposeCalls=2)),
        ('inactive-flush', 'deadline-cleanup-held', 'inactive flush settled', lambda a: a.update(inactiveFlushResolved=False)),
        ('late-flush', 'deadline-cleanup-held', 'late flush completed', lambda a: a.update(lateFlushCompleted=False)),
        ('post-flush', 'post-stop-flush', 'closed flush semantics', lambda a: a.update(postFlushSucceeded=True)),
    ]
    result = []
    for identity, mode, reason, edit in edits:
        bad = copy.deepcopy(samples[mode]); edit(bad)
        try: check(mode, bad, samples[mode]['pid'])
        except AssertionError as error:
            require(str(error) == reason, 'unexpected corruption ' + identity + ': ' + str(error)); result.append(dict(id=identity, rejected=True, reason=reason))
        else: raise AssertionError('corruption accepted ' + identity)
    return result


def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'):
        parser.add_argument('--' + key, required=True)
    args = parser.parse_args()
    require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]
    mapping = load((repo / 'conformance/logging/stop-cleanup-cases.json').read_bytes())
    require([c['id'] for c in mapping['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True)
    cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-stop-cleanup-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir()
            env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'stop-cleanup', mode, str(work)]
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
    report = dict(schema='cis.logging.stop-cleanup-native/v1', task='WP05-S10-T04', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(repo / 'conformance/logging/stop-cleanup-cases.json'), cases=cases, controls=corruption_controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, children=len(cases), controls=len(report['controls']), passed=True)))


if __name__ == '__main__':
    main()
