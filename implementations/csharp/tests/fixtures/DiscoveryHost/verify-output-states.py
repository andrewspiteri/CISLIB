"""Independent original-transport checks for actual WP05-S03-T03 workers."""
import argparse
import base64
import copy
from datetime import datetime, timedelta
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


def require(value, message):
    if not value:
        raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def utc(token):
    require(type(token) is str and re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}\+00:00', token), 'Not canonical UTC')
    value = datetime.fromisoformat(token)
    require(value.utcoffset() == timedelta(0), 'Non-UTC offset')
    return value


def verify(row, actual):
    require(actual['caseId'] == row['id'] and actual['result'] == row['expected']['result'], 'Case/admission differs')
    names = [name for name in ['console', 'file'] if row[name + 'Mode'] != 'disabled']
    points = actual['checkpoints']; expected = row['expected']['checkpoints']; faults = {}; previous = {}
    require(len(points) == len(expected), 'Boundary count differs')
    before, after = utc(actual['beforeUtc']), utc(actual['afterUtc'])
    for point, wanted in zip(points, expected):
        require(point['phase'] == wanted['phase'] and type(point['closed']) is bool and point['closed'] is wanted['closed'], 'Phase/closure differs')
        require(point['invalid'] == point['truncated'] == '0', 'Fault recursively logged')
        require(list(point['outputs']) == names, 'Output set/order differs')
        for name in names:
            counts = point['outputs'][name]
            literal = {key: value for key, value in counts.items() if key != 'faultAtUtc'}
            require(literal == wanted['outputs'][name], 'Literal state/fault/disposition differs')
            c = {}
            for key in ['accepted', 'written', 'failed', 'abandoned', 'pending', 'inFlight', 'unavailable']:
                token = counts[key]; require(type(token) is str and re.fullmatch(r'0|[1-9][0-9]*', token) and int(token) <= 9223372036854775807, 'Noncanonical Int64')
                c[key] = int(token)
            require(c['accepted'] == c['written'] + c['failed'] + c['abandoned'] + c['pending'] and c['inFlight'] <= min(1, c['pending']), 'Partition/gauge differs')
            if name in previous:
                require(all(c[key] >= previous[name][key] for key in c if key not in ['pending', 'inFlight']), 'Cumulative count decreased')
            previous[name] = c
            if counts['fault'] is None:
                require(counts['faultAtUtc'] is None, 'Faultless timestamp appeared')
            else:
                at = utc(counts['faultAtUtc']); require(before <= at <= after, 'Fault time out of capture interval')
                if name in faults: require(faults[name] == counts['faultAtUtc'], 'Fault time lost on stop')
                faults[name] = counts['faultAtUtc']
    require(list(actual['wires']) == names, 'Writer set/order differs')
    shared = {}; failures = 0
    for name in names:
        fails = row[name + 'Mode'] != 'success'; wire = actual['wires'][name]
        total = 3 if row['closeBeforeRelease'] or row['releaseFirst'] == name else 4
        tickets = [1] if fails else list(range(1, total + 1))
        require([item['ticket'] for item in wire['attempts']] == tickets and all(type(item['ticket']) is int for item in wire['attempts']), 'Attempted abandoned work/retry/order differs')
        require(wire['confirmed'] == ([] if fails else tickets) and all(type(item) is int for item in wire['confirmed']), 'Confirmation differs')
        originals = []
        for record in wire['attempts']:
            ticket = record['ticket']; raw = base64.b64decode(record['bytes'], validate=True); originals.append(raw)
            require(0 < len(raw) <= 65536 and raw.endswith(b'\n') and raw.count(b'\n') == 1 and b'\r' not in raw and not raw.startswith(b'\xef\xbb\xbf'), 'Complete packet bounds/framing differ')
            body = json.loads(raw.decode('utf-8', errors='strict'))
            require(body['component'] == 'States' and body['event_name'] == 'states.event' and body['message'] == 'record-' + str(ticket), 'Original caller identity differs')
            if ticket in shared: require(shared[ticket] == raw, 'Shared captured bytes changed')
            shared[ticket] = raw
        require(base64.b64decode(wire['bytes'], validate=True) == (originals[0][:len(originals[0]) // 2] if fails else b''.join(originals)), 'Actual partial/complete bytes differ')
        require(type(wire['privateExceptionAccesses']) is int and wire['privateExceptionAccesses'] == 0, 'Private exception was accessed')
        failures += int(fails)
    require(type(actual['originalErrors']) is int and actual['originalErrors'] == failures, 'Original errors replaced')


def damage(actual, fault):
    result = copy.deepcopy(actual); final = result['checkpoints'][-1]['outputs']['file']
    if fault == 'fault': final['fault'] = 'private/path/payload'
    elif fault == 'time': final['faultAtUtc'] = '2001-01-01T00:00:00.0000000+00:00'
    elif fault == 'state': final['state'] = 'Running'
    elif fault == 'abandon': final['abandoned'] = '0'
    elif fault == 'retry': result['wires']['file']['attempts'].append(copy.deepcopy(result['wires']['file']['attempts'][0]))
    elif fault == 'private': result['wires']['file']['privateExceptionAccesses'] = 1
    else: result['checkpoints'][0]['outputs'] = copy.deepcopy(result['checkpoints'][-1]['outputs'])
    return result


def main():
    parser = argparse.ArgumentParser()
    for name in ['dotnet', 'host', 'catalog', 'output']: parser.add_argument('--' + name, required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True); args = parser.parse_args()
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    ids = {'STATE-' + name for name in ['HEALTHY-CONSOLE', 'HEALTHY-FILE', 'HEALTHY-BOTH', 'DISABLED', 'FAIL-CONSOLE', 'FAIL-FILE', 'ISOLATE-CONSOLE', 'ISOLATE-FILE', 'FAIL-BOTH-CONSOLE-FIRST', 'FAIL-BOTH-FILE-FIRST', 'CLOSE-THEN-FAIL', 'CANCEL-FILE']}
    require(len(rows) == 12 and {row['id'] for row in rows} == ids, 'State matrix differs')
    report = dict(scope='WP05-S03-T03 actual internal queue lifecycle and safe fault isolation. Public diagnostics, physical output ownership and bounded shutdown remain later work.', family=args.family, cases=[], negativeControlsRejected=[])
    actuals = {}; env = dict(os.environ, CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read', TERM='dumb', NO_COLOR='1')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'output-states', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-output-states-') as owned:
            process = subprocess.run(command, cwd=owned, env=env, capture_output=True, timeout=35)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed'); require(not list(Path(owned).iterdir()), 'Created physical output')
        actual = json.loads(process.stdout); verify(row, actual); actuals[row['id']] = actual; observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(args.catalog), 'Executed byte identity differs')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    row = next(row for row in rows if row['id'] == 'STATE-ISOLATE-FILE')
    for fault in ['fault', 'time', 'state', 'abandon', 'retry', 'private', 'owned']:
        try: verify(row, damage(actuals[row['id']], fault))
        except (AssertionError, ValueError, KeyError): report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Comparator accepted corruption: ' + fault)
    require(len(report['negativeControlsRejected']) == 7, 'State controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
