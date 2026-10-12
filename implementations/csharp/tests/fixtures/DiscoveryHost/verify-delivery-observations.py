"""Independent live boundary and partial/complete/unresolved Stream byte accounting checks."""
import argparse
import base64
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

MAX = 2**63 - 1


def require(value, message):
    if not value: raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def raw(value):
    return base64.b64decode(value, validate=True)


def count(value):
    require(type(value) is str and re.fullmatch(r'0|[1-9][0-9]*', value) and int(value) <= MAX, 'Noncanonical/negative Int64 observation')
    return int(value)


def verify(row, actual):
    require(actual['caseId'] == row['id'], 'Case identity differs')
    names = [name for name in ['console', 'file'] if row[name + 'Enabled']]
    projection = {key: actual[key] for key in row['expected']}
    require(json.dumps(projection, sort_keys=True) == json.dumps(row['expected'], sort_keys=True), 'Literal live boundaries/results/terminal errors differ')
    invalid = truncated = 0; previous = {}
    for point in actual['checkpoints']:
        obs = point['observation']; next_invalid = count(obs['invalid']); next_truncated = count(obs['truncated'])
        require(next_invalid >= invalid and next_truncated >= truncated, 'Global counter decreased'); invalid, truncated = next_invalid, next_truncated
        require(list(obs['outputs']) == names, 'Output boundary order/set differs')
        for name, values in obs['outputs'].items():
            c = {key: count(value) for key, value in values.items()}
            require(c['sequence'] == c['accepted'] == c['written'] + c['failed'] + c['abandoned'] + c['pending'], 'Accepted ticket/disposition boundary differs')
            require(c['rejected'] == min(MAX, c['queueFull'] + c['unavailable']) and c['attempts'] == min(MAX, c['accepted'] + c['queueFull'] + c['unavailable']), 'Admission aggregate differs')
            require(c['inFlight'] <= 1 and c['inFlight'] <= c['pending'] <= 3, 'Unresolved gauges differ')
            if name in previous: require(all(c[key] >= previous[name][key] for key in c if key not in ['pending', 'inFlight']), 'Cumulative counter decreased')
            previous[name] = c
    for phase in ['held', 'cancellationRequested', 'wires']: require(list(actual[phase]) == names, 'Stream output set/order differs')
    originals_by_ticket = {}
    for name in names:
        mode = row[name + 'Mode']; fails = mode in ['partial-failure', 'complete-failure', 'zero-failure', 'partial-cancel']
        wire = actual['wires'][name]; tickets = [1] if fails else [1, 2, 3]
        require([item['ticket'] for item in wire['attempted']] == tickets and wire['confirmed'] == ([] if fails else tickets), 'Attempted/confirmed tickets differ or write retried')
        require(all(type(item['ticket']) is int for item in wire['attempted']) and all(type(value) is int for value in wire['confirmed']), 'Ticket transport type differs')
        originals = []
        for item in wire['attempted']:
            before, after = raw(item['before']), raw(item['after']); ticket = item['ticket']; require(before == after, 'Owned captured bytes mutated'); originals.append(before)
            require(0 < len(before) <= 16384 and before.endswith(b'\n') and before.count(b'\n') == 1 and b'\r' not in before and not before.startswith(b'\xef\xbb\xbf'), 'Complete captured bounds/framing differ')
            body = json.loads(before.decode('utf-8', errors='strict'))
            require(body['component'] == 'Delivery' and body['event_name'] == 'delivery.event' and body['status'] == 'warn' and type(body['schema_version']) is int and body['schema_version'] == 1, 'Captured event identity differs')
            if ticket <= 2: require(body['message'] == ('seed \U0001F642\n\u0001quote"' if ticket == 1 else 'queued-one'), 'Captured caller message differs')
            else: require(body['truncated'] is True, 'Oversized record lost truncation provenance')
            if ticket in originals_by_ticket: require(before == originals_by_ticket[ticket], 'Shared capture changed across outputs')
            else: originals_by_ticket[ticket] = before
        first = originals[0]; prefix = len(first) if mode.startswith('complete') else 0 if mode == 'zero-failure' else len(first) // 2
        for phase in ['held', 'cancellationRequested']:
            state = actual[phase][name]; require(raw(state['bytes']) == first[:prefix], 'Unresolved stream bytes differ')
            require(state['cancellationRequested'] is (phase == 'cancellationRequested' and row[name + 'Cancel']), 'Cancellation request differs')
        require(raw(wire['stream']) == (first[:prefix] if fails else b''.join(originals)), 'Terminal stream bytes differ or duplicated')


def damage(actual, fault, name):
    value = copy.deepcopy(actual); held = value['checkpoints'][1]['observation']['outputs'][name]; final = value['checkpoints'][5]['observation']['outputs'][name]; wire = value['wires'][name]
    if fault == 'early-written': held['written'] = '1'; held['pending'] = '2'
    elif fault == 'lost-inflight': held['inFlight'] = '0'
    elif fault in ['partial-as-written', 'full-as-written']: final['written'] = '1'; final['failed'] = '0'; wire['confirmed'] = [1]
    elif fault == 'cancel-as-failed': value['checkpoints'][2]['observation']['outputs'][name]['failed'] = '1'
    elif fault == 'global-drift': value['checkpoints'][0]['observation']['invalid'] = '1'
    elif fault == 'ticket-drift': held['sequence'] = '4'
    elif fault == 'retry': wire['attempted'].append(copy.deepcopy(wire['attempted'][0]))
    elif fault == 'bytes': wire['stream'] = base64.b64encode(raw(wire['stream'])[:-1]).decode()
    else: wire['attempted'][0]['after'] = base64.b64encode(b'\n').decode()
    return value


def main():
    parser = argparse.ArgumentParser()
    for arg in ['dotnet', 'host', 'catalog', 'output']: parser.add_argument('--' + arg, required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args(); output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    expected_ids = {'DELIVERY-' + name for name in ['CONSOLE-PARTIAL-SUCCESS', 'FILE-PARTIAL-SUCCESS', 'BOTH-PARTIAL-SUCCESS', 'CONSOLE-PARTIAL-FAILURE', 'FILE-PARTIAL-FAILURE', 'FILE-COMPLETE-FAILURE', 'CONSOLE-ZERO-FAILURE', 'CONSOLE-PARTIAL-CANCEL', 'FILE-PARTIAL-CANCEL', 'CONSOLE-CANCEL-THEN-SUCCESS', 'DISABLED']}
    require(len(rows) == 11 and {row['id'] for row in rows} == expected_ids, 'Delivery case matrix differs')
    report = dict(scope='WP05-S03-T02 actual live Core/Channel counters/tickets and controlled asynchronous Stream writes. No public diagnostics/fault-state/safe-fault, registered flush/stop target, physical destination ownership, durability or release claim.', family=args.family, cases=[], negativeControlsRejected=[])
    actuals = {}; env = dict(os.environ, CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read', TERM='dumb', NO_COLOR='1')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'delivery-observation', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-delivery-observation-') as owned:
            process = subprocess.run(command, cwd=owned, env=env, capture_output=True, timeout=35)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': exact-runtime child failed'); require(not list(Path(owned).iterdir()), 'Created a physical destination')
        actual = json.loads(process.stdout); verify(row, actual); actuals[row['id']] = actual; observed = actual['observed']
        require(observed['operatingSystem'] == args.family and observed['runtimeDescription'] == '.NET 10.0.12', 'Runtime/platform differs')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(args.catalog), 'Executed byte identity differs')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    for fault in ['early-written', 'lost-inflight', 'partial-as-written', 'full-as-written', 'cancel-as-failed', 'global-drift', 'ticket-drift', 'retry', 'bytes', 'owned-bytes']:
        suffix = 'FILE-COMPLETE-FAILURE' if fault == 'full-as-written' else 'CONSOLE-PARTIAL-FAILURE' if fault == 'partial-as-written' else 'CONSOLE-PARTIAL-CANCEL' if fault == 'cancel-as-failed' else 'BOTH-PARTIAL-SUCCESS'; id = 'DELIVERY-' + suffix; row = next(row for row in rows if row['id'] == id)
        name = 'file' if fault == 'full-as-written' else 'console'
        try: verify(row, damage(actuals[id], fault, name))
        except (AssertionError, KeyError, ValueError): report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Comparator accepted corruption: ' + fault)
    require(len(report['negativeControlsRejected']) == 10, 'Delivery controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n'); print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
