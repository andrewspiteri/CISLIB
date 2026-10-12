"""Independent counter identities and literal checkpoints for actual Core/Channel workers."""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


def require(value, reason):
    if not value:
        raise AssertionError(reason)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


FIELDS = ['filtered', 'attempts', 'accepted', 'queueFull', 'unavailable', 'rejected', 'written', 'failed', 'abandoned', 'pending', 'inFlight']
MAX = 2**63 - 1


def verify(row, actual):
    require(actual['caseId'] == row['id'], 'Case identity differs')
    names = [name for name in ['console', 'file'] if row[name + 'Enabled']]
    previous = {}
    for point in actual['checkpoints']:
        require(list(point['outputs']) == names, 'Enabled output set/order differs')
        for name, source in point['outputs'].items():
            require(set(source) == set(FIELDS), 'Counter shape differs')
            require(all(type(v) is str and re.fullmatch(r'0|[1-9][0-9]*', v) and int(v) <= MAX for v in source.values()), 'Not canonical nonnegative Int64')
            c = {key: int(value) for key, value in source.items()}
            require(c['accepted'] == c['written'] + c['failed'] + c['abandoned'] + c['pending'], 'Accepted disposition identity differs')
            require(c['rejected'] == min(MAX, c['queueFull'] + c['unavailable']), 'Rejection aggregate differs')
            require(c['attempts'] == min(MAX, c['accepted'] + c['queueFull'] + c['unavailable']), 'Admission aggregate differs')
            require(c['inFlight'] <= 1 and c['inFlight'] <= c['pending'] <= 3, 'Pending/in-flight bounds differ')
            if name in previous:
                require(all(c[key] >= previous[name][key] for key in FIELDS[:9]), 'Cumulative counter decreased')
            previous[name] = c
    projection = {key: actual[key] for key in row['expected']}
    # JSON bools must not be accepted as numeric 0/1 by Python equality.
    require(json.dumps(projection, sort_keys=True) == json.dumps(row['expected'], sort_keys=True), 'Literal checkpoint/results/writer expectations differ')


def damage(actual, fault):
    value = copy.deepcopy(actual); c = value['checkpoints'][1]['outputs']['console']
    if fault == 'negative': c['filtered'] = '-1'
    elif fault == 'disposition': c['accepted'] = '4'
    elif fault == 'attempts': c['attempts'] = '3'
    elif fault == 'rejected': c['rejected'] = '0'
    elif fault == 'inflight': c['inFlight'] = '2'
    elif fault == 'dequeued': c['written'] = '1'; c['pending'] = '2'
    elif fault == 'snapshot': value['checkpoints'][0]['outputs']['console'] = copy.deepcopy(c)
    elif fault == 'invalid': value['checkpoints'][1]['invalid'] = '4'
    else: value['writers']['console']['attempted'].append(1)
    return value


def main():
    parser = argparse.ArgumentParser()
    for arg in ['dotnet', 'host', 'catalog', 'output']: parser.add_argument('--' + arg, required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args(); output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    rows = json.loads(Path(args.catalog).read_text(encoding='utf-8'))['cases']
    expected = {'ACCOUNT-' + name for name in ['CONSOLE', 'FILE', 'BOTH', 'MIXED', 'LOW-INTEREST', 'CONSOLE-FAULT', 'FILE-FAULT', 'ONLY-CONSOLE-FAULT', 'ONLY-FILE-FAULT', 'BOTH-FAULT', 'DISABLED']}
    require(len(rows) == 11 and {row['id'] for row in rows} == expected, 'Accounting case matrix differs')
    report = dict(scope='WP05-S03-T01 actual internal cumulative output accounting and pending/in-flight checkpoints. Controlled writer callbacks; no physical destinations, partial-write/flush/public diagnostics/fault-state/lifecycle/release claim.', family=args.family, cases=[], negativeControlsRejected=[])
    env = dict(os.environ, CORE_LOG_QUEUE_CAPACITY='invalid-must-not-be-read', TERM='dumb', NO_COLOR='1')
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'output-accounting', args.catalog, row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-output-accounting-') as owned:
            process = subprocess.run(command, cwd=owned, env=env, capture_output=True, timeout=35)
            (output / (row['id'] + '.stdout.json')).write_bytes(process.stdout); (output / (row['id'] + '.stderr.log')).write_bytes(process.stderr)
            require(process.returncode == 0 and process.stderr == b'', row['id'] + ': child failed')
            require(not list(Path(owned).iterdir()), 'Created a physical destination')
        actual = json.loads(process.stdout); verify(row, actual); observed = actual['observed']
        require(observed['operatingSystem'] == args.family and observed['runtimeDescription'] == '.NET 10.0.12', 'Runtime/platform differs')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(args.catalog), 'Executed byte identity differs')
        if row['id'] == 'ACCOUNT-BOTH':
            for fault in ['negative', 'disposition', 'attempts', 'rejected', 'inflight', 'dequeued', 'snapshot', 'invalid', 'retry']:
                try: verify(row, damage(actual, fault))
                except (AssertionError, KeyError, ValueError): report['negativeControlsRejected'].append(fault)
                else: raise AssertionError('Comparator accepted corruption: ' + fault)
        report['cases'].append(dict(id=row['id'], passed=True, command=command, observed=observed))
    require(len(report['negativeControlsRejected']) == 9, 'Accounting controls did not execute')
    report.update(passed=True, count=len(rows), coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, count=len(rows), passed=True)))


if __name__ == '__main__':
    main()
