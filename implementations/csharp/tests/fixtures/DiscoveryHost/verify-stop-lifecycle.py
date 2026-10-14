"""Independent native lifecycle, actual kill/restart and raw-byte checks."""
import argparse, base64, copy, importlib.util, json, os, queue, subprocess, tempfile, threading
from datetime import datetime
from pathlib import Path
spec = importlib.util.spec_from_file_location('lifecycle_base', Path(__file__).with_name('verify-flush-results.py'))
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
require, load, sha, digest, decoded, number = base.require, base.load, base.sha, base.digest, base.decoded, base.number
MODES = ('normal', 'normal-dual', 'producer-stop-race', 'pre-cancelled', 'cancelled-joiners', 'held-write', 'held-flush', 'late-failure', 'kill-queued', 'kill-partial', 'kill-after-stop')


def identity(mode, a, pid=None):
    require(a['mode'] == mode and type(a['pid']) is int and a['pid'] > 0 and (pid is None or a['pid'] == pid), 'actual child PID')
    require(a['runtime'] == '.NET 10.0.12', 'actual runtime')


def records(blob, pids):
    data = decoded(blob); require(data and data[-1:] == b'\n' and not data.startswith(b'\xef\xbb\xbf'), 'whole physical records')
    rows = []
    for line in data.splitlines():
        require(len(line) + 1 <= 16384, 'bounded physical records'); a = load(line)
        require(type(a['attributes']['producer_pid']) is int and a['attributes']['producer_pid'] in pids, 'record producer PID')
        rows.extend(base.records(line + b'\n', a['attributes']['producer_pid']))
    return rows


def stop_shape(a, dual=False):
    require(set(a) == {'completion', 'succeeded', 'loss', 'captured', 'observed', 'outputs'} and type(a['succeeded']) is bool and type(a['loss']) is bool, 'stop shape')
    require(all(datetime.fromisoformat(a[k]).utcoffset().total_seconds() == 0 for k in ('captured', 'observed')), 'UTC labels')
    require([o['name'] for o in a['outputs']] == (['console', 'file'] if dual else ['file']), 'file output')
    return a['outputs'][-1]


def check(mode, a, pid=None, physical_console=None):
    identity(mode, a, pid); seed = decoded(a['seed']); require([x['event_name'] for x in records(a['seed'], {a['pid']})] == ['seed'], 'physical seed')
    require(all(a[k] is True for k in ('sameTask', 'immutable', 'leaseReleased', 'stoppedChecks')) and type(a['joiners']) is int and a['joiners'] == 64, 'shared operation')
    result = a['result']; dual = mode == 'normal-dual'; output = stop_shape(result, dual); require(decoded(a['bytes']).startswith(seed), 'preexisting bytes')
    if a['kind'] == 'public':
        receipts = a['receipts']; require(receipts and [number(x['ticket']) for x in receipts] == list(range(1, len(receipts) + 1)), 'accepted ticket order')
        require((result['completion'], result['succeeded'], result['loss']) == ('Completed', True, False), 'public stop summary')
        require(tuple(number(output[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (len(receipts), len(receipts), 0, 0, 0) and output['flush'] == 'Succeeded', 'public dispositions')
        require([x['event_name'] for x in records(a['bytes'], {a['pid']})] == ['seed'] + [x['event'] for x in receipts], 'physical receipt order')
        attempts = 120 if mode in ('normal', 'normal-dual') else 40 if mode == 'pre-cancelled' else 514
        require(type(a['attempts']) is int and a['attempts'] == attempts == len(receipts) + a['stopped'], 'producer accounting')
        require((a['cancelled'], a['racingStoppers'], a['capturingStopped']) == (1 if mode == 'pre-cancelled' else 0, 16 if mode == 'producer-stop-race' else 0, mode == 'producer-stop-race'), 'public mode proof')
        if mode == 'producer-stop-race': require(a['producerStopped'] + a['producerAccepted'] == 512 and a['producerAccepted'] == len(receipts) - 1, 'producer source accounting')
        require(a['stopped'] >= 1 and all(x['event'] != 'capturing' for x in receipts) if mode == 'producer-stop-race' else a['stopped'] == 0, 'capture race closure')
        if dual:
            console = result['outputs'][0]
            require(tuple(number(console[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (len(receipts), len(receipts), 0, 0, 0) and console['flush'] == 'Succeeded', 'console dispositions')
            marker = b'application-after-stop\n'; require(physical_console is not None and physical_console.endswith(marker), 'application stdout after stop')
            require(physical_console[:-len(marker)] == decoded(a['bytes'])[len(seed):], 'actual console and file bytes')
        return
    require(a['kind'] == 'kernel' and a['heldLease'] is True, 'held ownership'); flushing = mode == 'held-flush'; failure = mode == 'late-failure'
    require((result['completion'], result['succeeded'], result['loss']) == ('TimedOut', False, not flushing), 'deadline summary')
    require(tuple(number(output[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (3 if flushing else 6, 3 if flushing else 0, 0, 0 if flushing else 5, 0 if flushing else 1), 'deadline dispositions')
    require(output['flush'] == ('Pending' if flushing else 'Failed'), 'deadline flush')
    require(len(a['live']) == 1 and a['live'][0]['name'] == 'file', 'live output'); live = a['live'][0]
    require(tuple(number(live[k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending')) == (3 if flushing else 6, 3 if flushing else 0 if failure else 1, 1 if failure else 0, 0 if flushing else 5, 0) and live['fault'] == ('WriteFailed' if failure else None), 'eventual dispositions')
    w = a['writer']; require((w['writes'], w['flushes'], w['cancellations'], w['disposals'], w['unsafeDispose'], w['inFlight']) == (3 if flushing else 1, 1 if flushing else 0, 1, 1, False, False), 'physical cleanup')
    require(a['cancelled'] == (64 if mode == 'cancelled-joiners' else 0), 'caller cancellation only')
    require(type(a['outputCancellationsBeforeDeadline']) is list and len(a['outputCancellationsBeforeDeadline']) == 2 and all(type(n) is int and n == 0 for n in a['outputCancellationsBeforeDeadline']), 'caller-only output cancellation')
    require(type(a['outputCancellationRequestedBeforeDeadline']) is list and len(a['outputCancellationRequestedBeforeDeadline']) == 2 and all(n is False for n in a['outputCancellationRequestedBeforeDeadline']), 'caller-only output token')
    timer = a['timers']; require((timer['created'], timer['disposed'], timer['fires'], timer['active'], timer['maximumActive']) == (1, 1, 1, 0, 1), 'single deadline')
    line = decoded(a['line']); require([x['event_name'] for x in records(a['line'], {a['pid']})] == ['held.0'], 'actual first line')
    if flushing:
        require(decoded(a['pendingBytes']) == decoded(a['bytes']) and [x['event_name'] for x in records(a['bytes'], {a['pid']})] == ['seed', 'held.0', 'held.1', 'held.2'], 'held flush bytes')
    else:
        require(decoded(a['pendingBytes']) == seed + line[:32], 'pending physical prefix')
        require(decoded(a['bytes']) == seed + line[:32] if failure else decoded(a['bytes']) == seed + line, 'eventual physical bytes')


def checkpoint(mode, a, pid=None):
    identity(mode, a, pid); require(mode.startswith('kill-') and a['kind'] == 'checkpoint' and a['heldLease'] is True, 'crash ownership')
    seed, line = decoded(a['seed']), decoded(a['line'])
    require([x['event_name'] for x in records(a['seed'], {a['pid']})] == ['seed'] and [x['event_name'] for x in records(a['line'], {a['pid']})] == ['held.0'], 'crash source records')
    require(decoded(a['bytes']) == (seed if mode == 'kill-queued' else seed + line[:32]), 'physical crash boundary')
    w = a['writer']; require((w['writes'], w['flushes'], w['disposals'], w['unsafeDispose'], w['inFlight'], w['cancellations']) == (1, 0, 0, False, True, 1 if mode == 'kill-after-stop' else 0), 'crash writer state')
    require(len(a['live']) == 1 and a['live'][0]['name'] == 'file', 'crash live output'); live = a['live'][0]
    require(tuple(number(live[k]) for k in ('accepted', 'written', 'failed', 'abandoned', 'pending')) == (6, 0, 0, 5 if mode == 'kill-after-stop' else 0, 1 if mode == 'kill-after-stop' else 6), 'crash dispositions')
    if mode == 'kill-after-stop':
        o = stop_shape(a['result']); require((a['result']['completion'], a['result']['succeeded'], a['result']['loss']) == ('TimedOut', False, True) and tuple(number(o[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (6, 0, 0, 5, 1) and o['flush'] == 'Failed', 'crash stop result')
    else: require(a['result'] is None, 'crash stop result')


def restart(mode, before, after, pid=None):
    identity('restart', after, pid); require(after['pid'] != before['pid'], 'fresh restart PID'); partial = mode != 'kill-queued'
    require(after['kind'] == 'restart' and decoded(after['beforeBytes']) == decoded(before['bytes']), 'reopened physical bytes')
    require(after['recovered'] is partial and after['leaseReleased'] is True, 'restart recovery')
    o = stop_shape(after['result']); require((after['result']['completion'], after['result']['succeeded'], after['result']['loss']) == ('Completed', True, False) and tuple(number(o[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (1, 1, 0, 0, 0) and o['flush'] == 'Succeeded', 'restart stop result')
    require(decoded(after['damagedBytes']) == decoded(before['bytes']) if partial else after['damagedBytes'] is None and decoded(after['bytes']).startswith(decoded(before['bytes'])), 'damaged preservation')
    rows = records(after['bytes'], {before['pid'], after['pid']}); require(len(rows) == (1 if partial else 2) and rows[-1]['event_name'] == 'restarted' and rows[-1]['attributes']['producer_pid'] == after['pid'] and all(not x['event_name'].startswith('held.') for x in rows), 'restart record')


def corruption_controls(cases):
    samples = {c['id']: c.get('report', c.get('checkpoint')) for c in cases}
    edits = [
        ('pid', 'normal', 'actual child PID', lambda a: a.update(pid=a['pid'] + 1)),
        ('runtime', 'normal', 'actual runtime', lambda a: a.update(runtime='.NET 10.0.13')),
        ('shape', 'normal', 'stop shape', lambda a: a['result'].update(extra=True)),
        ('summary', 'normal', 'public stop summary', lambda a: a['result'].update(succeeded=False)),
        ('boundary', 'normal', 'public dispositions', lambda a: a['result']['outputs'][0].update(boundary='119')),
        ('ticket', 'normal', 'accepted ticket order', lambda a: a['receipts'][0].update(ticket='2')),
        ('receipt', 'normal', 'physical receipt order', lambda a: a['receipts'][0].update(event='other')),
        ('race', 'producer-stop-race', 'public mode proof', lambda a: a.update(capturingStopped=False)),
        ('producer', 'producer-stop-race', 'producer accounting', lambda a: a.update(attempts=513)),
        ('stoppers', 'producer-stop-race', 'public mode proof', lambda a: a.update(racingStoppers=15)),
        ('pre-cancelled', 'pre-cancelled', 'public mode proof', lambda a: a.update(cancelled=0)),
        ('pending', 'held-write', 'deadline dispositions', lambda a: a['result']['outputs'][0].update(pending='0')),
        ('abandoned', 'held-write', 'deadline dispositions', lambda a: a['result']['outputs'][0].update(abandoned='4')),
        ('flush', 'held-write', 'deadline flush', lambda a: a['result']['outputs'][0].update(flush='Succeeded')),
        ('held', 'held-write', 'held ownership', lambda a: a.update(heldLease=False)),
        ('immutable', 'held-write', 'shared operation', lambda a: a.update(immutable=False)),
        ('joiners', 'held-write', 'shared operation', lambda a: a.update(joiners=63)),
        ('timer', 'held-write', 'single deadline', lambda a: a['timers'].update(created=2)),
        ('io', 'held-write', 'physical cleanup', lambda a: a['writer'].update(writes=2)),
        ('dispose', 'held-write', 'physical cleanup', lambda a: a['writer'].update(unsafeDispose=True)),
        ('output-token', 'cancelled-joiners', 'caller-only output token', lambda a: a['outputCancellationRequestedBeforeDeadline'].__setitem__(0, True)),
        ('output-cancel', 'cancelled-joiners', 'caller-only output cancellation', lambda a: a['outputCancellationsBeforeDeadline'].__setitem__(0, 1)),
        ('cancel', 'cancelled-joiners', 'caller cancellation only', lambda a: a.update(cancelled=63)),
        ('late', 'late-failure', 'eventual dispositions', lambda a: a['live'][0].update(failed='0')),
        ('partial', 'late-failure', 'eventual physical bytes', lambda a: a.update(bytes=a['seed'])),
        ('flush-pending', 'held-flush', 'deadline dispositions', lambda a: a['result']['outputs'][0].update(pending='1')),
        ('crash-owner', 'kill-queued', 'crash ownership', lambda a: a.update(heldLease=False)),
        ('crash-dispose', 'kill-queued', 'crash writer state', lambda a: a['writer'].update(disposals=1)),
        ('crash-pending', 'kill-queued', 'crash dispositions', lambda a: a['live'][0].update(pending='5')),
        ('crash-bytes', 'kill-partial', 'physical crash boundary', lambda a: a.update(bytes=a['seed'])),
        ('crash-result', 'kill-queued', 'crash stop result', lambda a: a.update(result={})),
        ('crash-stop', 'kill-after-stop', 'crash stop result', lambda a: a['result']['outputs'][0].update(flush='Succeeded')),
    ]
    result = []
    def reject(identity, reason, run):
        try: run()
        except AssertionError as error:
            require(str(error) == reason, 'unexpected corruption ' + identity + ': ' + str(error)); result.append(dict(id=identity, rejected=True, reason=reason))
        else: raise AssertionError('corruption accepted ' + identity)
    for name, mode, reason, edit in edits:
        bad = copy.deepcopy(samples[mode]); edit(bad)
        reject(name, reason, lambda: (checkpoint if mode.startswith('kill-') else check)(mode, bad, samples[mode]['pid']))
    dual = next(c for c in cases if c['id'] == 'normal-dual'); actual = decoded(dual['physicalConsoleBase64'])
    bad = copy.deepcopy(dual['report']); bad['result']['outputs'][0]['written'] = '119'
    reject('console-dispositions', 'console dispositions', lambda: check('normal-dual', bad, dual['pid'], actual))
    reject('console-marker', 'application stdout after stop', lambda: check('normal-dual', dual['report'], dual['pid'], actual[:-len(b'application-after-stop\n')]))
    reject('console-bytes', 'actual console and file bytes', lambda: check('normal-dual', dual['report'], dual['pid'], b'\n'.join(actual.split(b'\n')[1:])))
    killed = next(c for c in cases if c['id'] == 'kill-partial'); before, good = killed['checkpoint'], killed['restart']['report']
    for name, reason, edit in [
        ('restart-pid', 'fresh restart PID', lambda a: a.update(pid=before['pid'])),
        ('restart-before', 'reopened physical bytes', lambda a: a.update(beforeBytes='')),
        ('restart-recovery', 'restart recovery', lambda a: a.update(recovered=False)),
        ('restart-damaged', 'damaged preservation', lambda a: a.update(damagedBytes='')),
        ('restart-stop', 'restart stop result', lambda a: a['result']['outputs'][0].update(pending='1')),
    ]:
        bad = copy.deepcopy(good); edit(bad); reject(name, reason, lambda: restart('kill-partial', before, bad))
    return result


def main():
    parser = argparse.ArgumentParser()
    for key in ('dotnet', 'host', 'output', 'family'): parser.add_argument('--' + key, required=True)
    args = parser.parse_args(); require(args.family in ('windows', 'linux') and (os.name == 'nt') == (args.family == 'windows'), 'native OS')
    repo = Path(__file__).resolve().parents[5]; require([c['id'] for c in load((repo / 'conformance/logging/stop-lifecycle-cases.json').read_bytes())['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True); cases = []
    def command(mode, work): return [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'stop-lifecycle', mode, str(work)]
    env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
    def save(key, stdout, stderr):
        (out / (key + '.stdout.bin')).write_bytes(stdout); (out / (key + '.stderr.bin')).write_bytes(stderr)
    def pin(a, pid):
        identity(a['mode'], a, pid); require((a['os'], a['coreSha256'], a['fixtureSha256']) == (args.family, sha(host.parent / 'CIS.Core.dll'), sha(host)), 'actual assemblies')
    with tempfile.TemporaryDirectory(prefix='cislib-stop-lifecycle-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir(); cmd = command(mode, work); process = subprocess.Popen(cmd, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try:
                if not mode.startswith('kill-'):
                    stdout, stderr = process.communicate(timeout=60); save(mode, stdout, stderr); require(process.returncode == 0 and (mode == 'normal-dual' or stderr == b''), 'child failure ' + mode)
                    a = load(stderr if mode == 'normal-dual' else stdout); pin(a, process.pid); check(mode, a, process.pid, stdout if mode == 'normal-dual' else None)
                    cases.append(dict(id=mode, pid=process.pid, exitCode=0, command=cmd, report=a, physicalConsoleBase64=base64.b64encode(stdout).decode() if mode == 'normal-dual' else None, stdoutSha256=digest(stdout), stderrSha256=digest(stderr))); continue
                received = queue.Queue()
                reader = threading.Thread(target=lambda: received.put(process.stdout.readline()), daemon=True); reader.start()
                line = received.get(timeout=30); reader.join(timeout=5); require(not reader.is_alive() and process.poll() is None, 'live crash checkpoint')
                a = load(line); pin(a, process.pid); checkpoint(mode, a, process.pid); process.kill(); tail, stderr = process.communicate(timeout=15); stdout = line + tail; save(mode, stdout, stderr)
                require(process.returncode != 0 and tail == b'' and stderr == b'', 'external kill')
                path = work / 'logs \u03a9/active.log'; physical = path.read_bytes(); require(physical == decoded(a['bytes']), 'killed physical bytes'); (out / (mode + '.before-restart.bin')).write_bytes(physical)
                restart_cmd = command('restart', work); second = subprocess.Popen(restart_cmd, cwd=work, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                try: restart_out, restart_err = second.communicate(timeout=60)
                finally:
                    if second.poll() is None: second.kill(); second.communicate(timeout=15)
                save(mode + '.restart', restart_out, restart_err); require(second.returncode == 0 and restart_err == b'', 'restart child failure')
                b = load(restart_out); pin(b, second.pid); restart(mode, a, b, second.pid); active = path.read_bytes(); require(active == decoded(b['bytes']), 'restart actual active bytes'); (out / (mode + '.active.bin')).write_bytes(active)
                damaged = [p for p in path.parent.iterdir() if '.damaged' in p.name]; require(len(damaged) == (0 if mode == 'kill-queued' else 1), 'restart damaged membership')
                if damaged:
                    data = damaged[0].read_bytes(); require(data == physical == decoded(b['damagedBytes']), 'restart actual damaged bytes'); (out / (mode + '.damaged.bin')).write_bytes(data)
                cases.append(dict(id=mode, pid=process.pid, exitCode=process.returncode, command=cmd, checkpoint=a, killIssued=True, stdoutSha256=digest(stdout), stderrSha256=digest(stderr), beforeRestartSha256=digest(physical), activeSha256=digest(active), restart=dict(pid=second.pid, exitCode=0, command=restart_cmd, report=b, stdoutSha256=digest(restart_out), stderrSha256=digest(restart_err))))
            finally:
                if process.poll() is None: process.kill(); process.communicate(timeout=15)
    report = dict(schema='cis.logging.stop-lifecycle-native/v1', task='WP05-S10-T05', family=args.family, cases=cases, children=len(cases) + sum('restart' in c for c in cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(repo / 'conformance/logging/stop-lifecycle-cases.json'), controls=corruption_controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n'); print(json.dumps(dict(family=args.family, cases=len(cases), children=report['children'], controls=len(report['controls']), passed=True)))


if __name__ == '__main__': main()