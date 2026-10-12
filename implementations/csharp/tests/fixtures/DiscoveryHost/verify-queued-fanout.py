"""Independent qualification of actual Core -> ticketed Channels -> controlled memory writers."""
import argparse
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


def require(value, reason):
    if not value:
        raise AssertionError(reason)


def same(left, right):
    if type(left) in (int, float) and type(right) in (int, float):
        require(left == right, 'Numeric value differs'); return
    require(type(left) is type(right), 'Type differs')
    if isinstance(left, dict):
        require(set(left) == set(right), 'Object keys differ')
        for key in left:
            same(left[key], right[key])
    elif isinstance(left, list):
        require(len(left) == len(right), 'Array size differs')
        for a, b in zip(left, right):
            same(a, b)
    else:
        require(left == right, 'Value differs')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify(row, actual):
    same(row['id'], actual['caseId']); same(row['expected'], actual['semantic']); same(True, actual['independentProgress'])
    verify_transport(row, actual)


def verify_transport(row, actual):
    same(row['id'], actual['caseId']); same(True, actual['independentProgress'])
    bindings = actual['bindings']; steps = {step['label']: step for step in row['steps']}
    require(len(bindings) == len(steps) and {binding['caller'] for binding in bindings} == set(steps), 'Producer binding set differs')
    for binding, result in zip(bindings, actual['semantic']['results']):
        same({key: value for key, value in binding.items() if key != 'caller'}, result)
    expected_names = {name for name in ['console', 'file'] if row[name + 'Enabled']}
    require(set(actual['wires']) == expected_names, 'Enabled wire set differs')
    for name in expected_names:
        wire = actual['wires'][name]; output = actual['semantic']['outputs'][name]; same(0, wire['pendingAfterComplete'])
        accepted = {}
        for binding in bindings:
            observation = next(item for item in binding['outputs'] if item['name'] == name)
            ticket = observation['ticket']
            if observation['status'] == 'Accepted':
                require(type(ticket) is int and 0 < ticket <= 9223372036854775807 and ticket not in accepted, 'Successful ticket domain/uniqueness differs')
                accepted[ticket] = binding['caller']
            else:
                require(ticket is None, 'Rejection has invented ticket')
        originals = {}
        for item in wire['attempted']:
            ticket = item['ticket']; require(ticket in accepted and ticket not in originals, 'Attempted ticket absent/reused')
            raw = base64.b64decode(item['wire'], validate=True); after = base64.b64decode(item['after'], validate=True)
            require(raw == after, 'Captured bytes changed while held')
            require(0 < len(raw) <= 65536 and not raw.startswith(b'\xef\xbb\xbf') and raw.endswith(b'\n') and raw.count(b'\n') == 1 and b'\r' not in raw, 'Original framing/bounds differ')
            source = json.loads(raw.decode('utf-8', errors='strict')); caller = accepted[ticket]; step = steps[caller]
            for key, value in dict(schema_version=1, service='Fanout.App', component='Fanout', event_name='fanout.event', message='record-' + str(caller), timestamp='2026-10-12T10:11:12.1234567Z').items():
                same(value, source[key])
            same(caller, source['attributes']['input']); same({'Debug': 'debug', 'Warning': 'warn', 'Information': 'info'}[step['level']], source['status'])
            originals[ticket] = raw
        same(output['attemptedTickets'], [item['ticket'] for item in wire['attempted']])
        require(base64.b64decode(wire['stream'], validate=True) == b''.join(originals[ticket] for ticket in output['writtenTickets']), 'Confirmed stream bytes/order differ')
        require(output['lastAcceptedSequence'] == max(accepted, default=0) and sorted(accepted) == list(range(1, len(accepted) + 1)), 'Successful admission tickets have a gap or were not published')


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--dotnet', required=True); parser.add_argument('--host', required=True)
    parser.add_argument('--catalog', required=True); parser.add_argument('--output', required=True); parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    parser.add_argument('--task', choices=['WP05-S02-T02', 'WP05-S02-T03'], default='WP05-S02-T02')
    args = parser.parse_args(); directory = Path(args.output); directory.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    require(len(rows) == 12 and len({row['id'] for row in rows}) == 12, 'Fanout catalog differs')
    expected_modes = {'basic','union','saturation','independent','late-close','late-fault','early-fault','mutate','multi'} if args.task == 'WP05-S02-T02' else {'basic','file-fault-overflow','console-fault-overflow','independent-file','recover-console','recover-file'}
    require({row['mode'] for row in rows} == expected_modes, 'Actual runtime phase coverage differs')
    if args.task == 'WP05-S02-T03':
        require({row['id'] for row in rows} == {'OVERFLOW-FILE-ONLY', 'OVERFLOW-BOTH-FULL', 'OVERFLOW-FILE-RECOVERS', 'OVERFLOW-CONSOLE-FULL-FILE-FILTERED', 'OVERFLOW-NONE-WHILE-FULL', 'OVERFLOW-CONSOLE-FILTERED-FILE-FULL', 'OVERFLOW-CAPACITY-TWO', 'OVERFLOW-FILE-INDEPENDENT', 'OVERFLOW-CONSOLE-ONLY', 'OVERFLOW-CONSOLE-FAULT-FILE-FULL', 'OVERFLOW-FILE-FAULT-CONSOLE-FULL', 'OVERFLOW-CONSOLE-RECOVERS'}, 'Required overflow cases differ')
    anchor = 'FANOUT-BOTH' if args.task == 'WP05-S02-T02' else 'OVERFLOW-BOTH-FULL'
    report = dict(scope=args.task+' actual runtime capture, successful tickets, shared gate fanout/closure and sequential Channels with controlled MemoryStream writers/IOException faults. No physical destinations, durability, public creation/lifecycle, complete S02/S03 accounting/diagnostics or release claim.', family=args.family, cases=[], negativeControlsRejected=[])
    env = dict(os.environ, CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read')
    for row in rows:
        command = [args.dotnet,'exec','--fx-version','10.0.12','--roll-forward','Disable',args.host,'queued-fanout',args.catalog,row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-queued-fanout-') as owned:
            process = subprocess.run(command,cwd=owned,env=env,capture_output=True,timeout=35)
            (directory/(row['id']+'.stdout.json')).write_bytes(process.stdout); (directory/(row['id']+'.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id']+': exact-runtime child failed')
            require(not list(Path(owned).iterdir()), 'Fanout created a physical destination')
        actual = json.loads(process.stdout); verify(row, actual); observed = actual['observed']
        same(args.family,observed['operatingSystem']); same('.NET 10.0.12',observed['runtimeDescription']);same(sha(args.host),observed['fixtureSha256']);same(sha(Path(args.host).with_name('CIS.Core.dll')),observed['coreSha256']);same(sha(args.catalog),observed['inputsSha256'])
        if row['id'] == anchor:
            for fault in ['ticket','gap','factory','clock','gate','copy','stream','binding','fault','order','source','framing','unbound'] + (['eviction'] if args.task == 'WP05-S02-T03' else []):
                damaged = copy.deepcopy(actual); semantic = damaged['semantic']; wire = damaged['wires']['console']
                if fault == 'ticket': semantic['results'][0]['outputs'][0]['ticket'] = None
                elif fault == 'gap': semantic['outputs']['console']['lastAcceptedSequence'] += 1
                elif fault == 'factory': semantic['factoryCalls'] += 1
                elif fault == 'clock': semantic['clockCalls'] += 1
                elif fault == 'gate': semantic['callbackGateHeld'] = True
                elif fault == 'copy': semantic['sharedCaptures'] = False
                elif fault == 'stream': wire['stream'] = base64.b64encode(b'\n').decode('ascii')
                elif fault == 'binding': damaged['bindings'][0]['caller'] = 2
                elif fault == 'fault': semantic['outputs']['file']['faulted'] = True
                elif fault == 'unbound': wire['attempted'][0]['ticket'] = semantic['outputs']['console']['lastAcceptedSequence'] + 1
                elif fault == 'order': semantic['outputs']['console']['attemptedTickets'].reverse()
                else:
                    item = wire['attempted'][0]; raw = base64.b64decode(item['wire'],validate=True)
                    if fault in ('source', 'eviction'):
                        body = json.loads(raw); body['message'] = 'record-3' if fault == 'eviction' else 'CIS_CHANGED_AFTER_ADMISSION'
                        if fault == 'eviction': body['attributes']['input'] = 3
                        raw = (json.dumps(body)+'\n').encode('utf-8')
                    else:
                        raw += b'\n'
                    item['wire'] = item['after'] = base64.b64encode(raw).decode('ascii'); wire['stream'] = base64.b64encode(b''.join(base64.b64decode(item['wire']) for item in wire['attempted'])).decode('ascii')
                try:
                    verify(row,damaged)
                except (AssertionError,KeyError,ValueError):
                    report['negativeControlsRejected'].append(fault)
                else:
                    raise AssertionError('Corruption accepted: '+fault)
        report['cases'].append(dict(id=row['id'],passed=True,command=command,observed=observed))
    require(len(report['negativeControlsRejected']) == (13 if args.task == 'WP05-S02-T02' else 14), 'Fanout comparator controls did not execute')
    report.update(passed=True,count=len(rows),coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')),hostSha256=sha(args.host),inputsSha256=sha(args.catalog))
    (directory/'summary.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8');print(json.dumps(dict(family=args.family,count=len(rows),passed=True)))


if __name__ == '__main__':
    main()
