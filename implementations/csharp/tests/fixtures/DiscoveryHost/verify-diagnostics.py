"""Independent exact-runtime checks of public snapshots and their original controlled stream reports."""
import argparse
import copy
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import runpy
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
MAX = 9223372036854775807


def require(condition, message):
    if not condition: raise AssertionError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def utc(token):
    require(type(token) is str and re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}\+00:00', token), 'Noncanonical UTC')
    return datetime.fromisoformat(token)


def count(token):
    require(type(token) is str and re.fullmatch(r'0|[1-9][0-9]*', token) and int(token) <= MAX, 'Noncanonical Int64')
    return int(token)


def verify(row, actual):
    require(actual['caseId'] == row['id'] and actual['source']['caseId'] == row['sourceCaseId'], 'Public/source identity differs')
    source = actual['source']; points = source['diagnostics']; expected = row['expected']; faults = {}; previous = {}; shutdown = None
    require(len(points) == len(expected), 'Public boundary count differs')
    before, after = utc(source['diagnosticsBeforeUtc']), utc(source['diagnosticsAfterUtc']); invalid = truncated = 0
    for point, wanted in zip(points, expected):
        require({key: point[key] for key in ['phase', 'state', 'closed', 'invalid', 'truncated']} == {key: wanted[key] for key in ['phase', 'state', 'closed', 'invalid', 'truncated']}, 'Literal public runtime fields differ')
        require(type(point['closed']) is bool and before <= utc(point['observedAtUtc']) <= after, 'Closure type/observation time differs')
        if point['closed']:
            token = point['shutdownStartedAtUtc']; require(before <= utc(token) <= after, 'Closure time out of interval')
            if shutdown is not None: require(token == shutdown, 'Closure time reset')
            shutdown = token
        else: require(point['shutdownStartedAtUtc'] is None, 'Open shutdown timestamp appeared')
        next_invalid, next_truncated = count(point['invalid']), count(point['truncated']); require(next_invalid >= invalid and next_truncated >= truncated, 'Global count decreased'); invalid, truncated = next_invalid, next_truncated
        require(len(point['outputs']) == len(wanted['outputs']), 'Enabled public output set differs')
        for output, target in zip(point['outputs'], wanted['outputs']):
            require({key: value for key, value in output.items() if key != 'faultAtUtc'} == target, 'Literal public output fields differ')
            c = {key: count(output[key]) for key in ['sequence', 'filtered', 'attempts', 'accepted', 'queueFull', 'unavailable', 'written', 'failed', 'abandoned', 'pending']}
            require(c['sequence'] == c['accepted'] == c['written'] + c['failed'] + c['abandoned'] + c['pending'], 'Public ticket/accepted partition differs')
            require(c['attempts'] == min(MAX, c['accepted'] + c['queueFull'] + c['unavailable']), 'Public admission sum differs')
            name = output['name']
            if name in previous: require(all(c[key] >= previous[name][key] for key in c if key != 'pending'), 'Cumulative public counter decreased')
            previous[name] = c
            if output['fault'] is None: require(output['faultAtUtc'] is None, 'Faultless timestamp appeared')
            else:
                token = output['faultAtUtc']; require(before <= utc(token) <= after, 'Fault time differs')
                if name in faults: require(token == faults[name], 'Retained fault time changed')
                faults[name] = token


def damage(actual, fault):
    value = copy.deepcopy(actual); points = value['source']['diagnostics']; final = points[-1]; output = final['outputs'][0]
    if fault == 'runtime-state': final['state'] = 'Running'
    elif fault == 'closure': final['closed'] = False
    elif fault == 'observed-time': final['observedAtUtc'] = '2001-01-01T00:00:00.0000000+00:00'
    elif fault == 'shutdown-time': final['shutdownStartedAtUtc'] = '2001-01-01T00:00:00.0000000+00:00'
    elif fault == 'global': final['invalid'] = '99'
    elif fault == 'written': output['written'] = '99'
    elif fault == 'ticket': output['sequence'] = '99'
    elif fault == 'fault': output['fault'] = 'private/path/payload'
    elif fault == 'fault-time': output['faultAtUtc'] = '2001-01-01T00:00:00.0000000+00:00'
    elif fault == 'owned':
        for key in ['invalid', 'truncated', 'outputs']: points[0][key] = copy.deepcopy(final[key])
    elif fault == 'shutdown-retention': final['shutdownStartedAtUtc'] = value['source']['diagnosticsBeforeUtc']
    elif fault == 'fault-retention': points[3]['outputs'][0]['faultAtUtc'] = value['source']['diagnosticsBeforeUtc']
    else: raise ValueError('Unknown damage control')
    return value


def main():
    parser = argparse.ArgumentParser()
    for name in ['dotnet', 'host', 'catalog', 'output']: parser.add_argument('--' + name, required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True); args = parser.parse_args()
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True); catalog = Path(args.catalog)
    rows = json.loads(catalog.read_text(encoding='utf-8'))['cases']
    expected_delivery = ['CONSOLE-PARTIAL-SUCCESS', 'FILE-PARTIAL-SUCCESS', 'BOTH-PARTIAL-SUCCESS', 'CONSOLE-PARTIAL-FAILURE', 'FILE-PARTIAL-FAILURE', 'FILE-COMPLETE-FAILURE', 'CONSOLE-ZERO-FAILURE', 'CONSOLE-PARTIAL-CANCEL', 'FILE-PARTIAL-CANCEL', 'CONSOLE-CANCEL-THEN-SUCCESS', 'DISABLED']
    expected_states = ['HEALTHY-CONSOLE', 'HEALTHY-FILE', 'HEALTHY-BOTH', 'DISABLED', 'FAIL-CONSOLE', 'FAIL-FILE', 'ISOLATE-CONSOLE', 'ISOLATE-FILE', 'FAIL-BOTH-CONSOLE-FIRST', 'FAIL-BOTH-FILE-FIRST', 'CLOSE-THEN-FAIL', 'CANCEL-FILE']
    expected_ids = {'DIAGNOSTICS-DELIVERY-' + value for value in expected_delivery} | {'DIAGNOSTICS-STATE-' + value for value in expected_states}
    require(len(rows) == 23 and {row['id'] for row in rows} == expected_ids, 'Full diagnostics matrix differs')
    helpers = {name: runpy.run_path(str(Path(__file__).with_name(filename)))['verify'] for name, filename in [('delivery', 'verify-delivery-observations.py'), ('states', 'verify-output-states.py')]}
    report = dict(scope='WP05-S03-T04 actual public owned diagnostic snapshots; all four S03 criteria at the real controlled stream/queue seam. No public factory, physical ownership, bounded Stop/LastStopResult, durability or release claim.', family=args.family, cases=[], negativeControlsRejected=[])
    actuals = {}; env = dict(os.environ, CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read', TERM='dumb', NO_COLOR='1')
    for row in rows:
        require(row['sourceCatalog'] == ('delivery-observation-cases.json' if row['profile'] == 'delivery' else 'output-state-cases.json') and row['id'] == 'DIAGNOSTICS-' + row['sourceCaseId'], 'Source case/profile binding differs')
        source_file = catalog.with_name(row['sourceCatalog']); source_row = next(item for item in json.loads(source_file.read_text(encoding='utf-8'))['cases'] if item['id'] == row['sourceCaseId'])
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'diagnostics', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-diagnostics-') as owned:
            process = subprocess.run(command, cwd=owned, env=env, capture_output=True, timeout=35)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed'); require(not list(Path(owned).iterdir()), 'Created physical output')
        actual = json.loads(process.stdout); verify(row, actual); helpers[row['profile']](source_row, actual['source']); actuals[row['id']] = actual; observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family, 'Runtime/platform differs')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(catalog) and observed['sourceInputsSha256'] == sha(source_file), 'Executed source/host/Core identity differs')
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    row = next(row for row in rows if row['id'] == 'DIAGNOSTICS-STATE-ISOLATE-CONSOLE')
    for fault in ['runtime-state', 'closure', 'observed-time', 'shutdown-time', 'global', 'written', 'ticket', 'fault', 'fault-time', 'owned', 'shutdown-retention', 'fault-retention']:
        try: verify(row, damage(actuals[row['id']], fault))
        except AssertionError: report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Public diagnostics comparator accepted corruption: ' + fault)
    require(len(report['negativeControlsRejected']) == 12, 'Public comparator controls did not run')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n'); print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
