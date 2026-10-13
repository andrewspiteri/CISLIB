"""Independent original-byte checks for real environment-driven console consumers."""
import argparse
import base64
import copy
import importlib.util
import itertools
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import uuid

spec = importlib.util.spec_from_file_location('consumer_byte_helpers', Path(__file__).with_name('verify-console-composition.py'))
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)
require, sha, read, same, human = shared.require, shared.sha, shared.read, shared.same, shared.human


def environment_for(row):
    env = {k: v for k, v in os.environ.items() if not k.upper().startswith('CORE_') and k.upper() not in ['DD_SERVICE', 'DD_ENV', 'DD_VERSION']}
    env.update(DD_SERVICE='unused-alias', DD_ENV='unused-alias', DD_VERSION='unused-alias', TERM='dumb', NO_COLOR='1')
    env.update(row['environment'])
    return env


def check_lines(catalog, row, token, destination, resource):
    raw = base64.b64decode(token, validate=True)
    cap = row['expected']['settings']['maxEntryBytes']
    require(not raw or raw.endswith(b'\n'), 'Consumer entry framing/cap differs.')
    lines = raw[:-1].split(b'\n') if raw else []
    events = row['expected'][destination + 'Events']
    require(len(lines) == len(events), 'Consumer record count differs.')
    for line, event in zip(lines, events):
        require(len(line) + 1 <= cap and not line.startswith(b'\xef\xbb\xbf') and b'\r' not in line and b'\x1b' not in line, 'Consumer entry framing/cap differs.')
        wanted = copy.deepcopy(catalog['baseRecord'])
        wanted.update(status=event['level'], message=f"consumer-{event['id']} caf\u00e9 \U0001f642\nnext", resource=resource, attributes={'n': event['id']})
        if row['mode'] == 'pressure': wanted['message'] += 'p' * 2048
        if destination == 'file' or row['expected']['settings']['format'] == 'JsonLines':
            require(same(read(line.decode('utf-8')), wanted), 'Consumer JSONL facts differ.')
        else:
            fields = human(line)
            display = {key: wanted[value] for key, value in [('timestamp', 'timestamp'), ('severity', 'status'), ('component', 'component'), ('event_name', 'event_name'), ('message', 'message')]}
            if row['expected']['settings']['detail'] == 'Verbose':
                display.update({key: wanted[key] for key in ['service', 'env', 'version', 'operation_id', 'message_template', 'attributes']})
                display.update({'resource.' + key: value for key, value in resource.items()})
            require(same(fields, display), 'Consumer human facts differ.')


def verify(catalog, row, actual):
    require(actual['ok'] is True and actual['caseId'] == row['id'], 'Consumer command result differs.')
    expected = row['expected']
    for key, message in [('settings', 'Environment-selected settings differ.'), ('results', 'Consumer outcomes/tickets differ.'), ('factoryCalls', 'Consumer factory calls differ.'), ('clockCalls', 'Consumer clock calls differ.'), ('final', 'Consumer final accounting differs.'), ('checkpoints', 'Independent file progress differs.'), ('selectedStreamArgument', 'Consumer stream selector argument differs.'), ('borrowedDisposes', 'Borrowed console disposal differs.'), ('afterApplication', 'Borrowed application bytes differ.')]:
        require(same(actual[key], expected[key]), message)
    resource = actual['resource']
    require(resource['build_version'] == catalog['buildVersion'] and resource['environment_kind'] == 'console' and resource['os_family'] in ['windows', 'linux'] and type(resource['process_id']) is int and resource['process_id'] > 0 and resource['runtime_description'] == '.NET 10.0.12', 'Consumer discovered facts differ.')
    instance = uuid.UUID(resource['instance_id'])
    require(instance.int != 0 and str(instance) == resource['instance_id'], 'Consumer instance identity differs.')
    for key in ['initialized_at', 'process_started_at']:
        if key not in resource and key == 'process_started_at':
            continue
        require(re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z', resource[key]) is not None, 'Consumer discovery timestamp differs.')
    require(set(resource) <= {'build_version', 'instance_id', 'initialized_at', 'environment_kind', 'os_family', 'process_id', 'process_started_at', 'host_name', 'os_description', 'os_architecture', 'process_architecture', 'runtime_description'}, 'Consumer resource includes non-facts.')
    selected = 'stderr' if expected['settings']['stream'] == 'StandardError' else 'stdout'
    other = 'stdout' if selected == 'stderr' else 'stderr'
    require(actual[other] == '', 'Unselected consumer stream has records.')
    check_lines(catalog, row, actual[selected], 'console', resource)
    check_lines(catalog, row, actual['file'], 'file', resource)


CONTROLS = {
    'settings': 'Environment-selected settings differ.', 'outcome': 'Consumer outcomes/tickets differ.',
    'factory': 'Consumer factory calls differ.', 'clock': 'Consumer clock calls differ.',
    'final': 'Consumer final accounting differs.', 'checkpoint': 'Independent file progress differs.',
    'dispose': 'Borrowed console disposal differs.', 'application': 'Borrowed application bytes differ.',
    'route': 'Unselected consumer stream has records.', 'identity': 'Consumer JSONL facts differ.',
    'resource': 'Consumer JSONL facts differ.', 'bom': 'Consumer entry framing/cap differs.',
    'records': 'Consumer record count differs.'
}


def damage(actual, fault):
    value = copy.deepcopy(actual)
    if fault == 'settings': value['settings']['format'] = 'Human'
    elif fault == 'outcome': value['results'][0]['outcome'] = 'Filtered'
    elif fault == 'factory': value['factoryCalls'][0] = 0
    elif fault == 'clock': value['clockCalls'] = 99
    elif fault == 'final': value['final']['outputs'][0]['abandoned'] = '99'
    elif fault == 'checkpoint': value['checkpoints'][0]['consoleWorkerCompleted'] = True
    elif fault == 'dispose': value['borrowedDisposes'] = 1
    elif fault == 'application': value['afterApplication'] = 'lost'
    elif fault == 'route': value['stdout'], value['stderr'] = value['stderr'], value['stdout']
    else:
        raw = base64.b64decode(value['stderr'], validate=True)
        if fault == 'identity': raw = raw.replace(b'Consumer.App', b'Wrong.App')
        elif fault == 'resource': raw = raw.replace(value['resource']['instance_id'].encode(), b'22222222-2222-2222-2222-222222222222')
        elif fault == 'bom': raw = b'\xef\xbb\xbf' + raw
        elif fault == 'records': raw = raw[raw.index(b'\n') + 1:]
        else: raise ValueError('Unknown damage')
        value['stderr'] = base64.b64encode(raw).decode('ascii')
    return value


def main():
    parser = argparse.ArgumentParser()
    for name in ['dotnet', 'host', 'catalog', 'output']: parser.add_argument('--' + name, required=True)
    parser.add_argument('--family', choices=['windows', 'linux'], required=True)
    args = parser.parse_args()
    catalog = read(Path(args.catalog).read_text(encoding='utf-8'))
    rows = catalog['cases']
    ids = {f'CONSUMER-{profile}-{presentation}-{stream}' for profile, presentation, stream in itertools.product(['CONSOLE', 'BOTH', 'FILE', 'DISABLED', 'BLOCK', 'FAIL'], ['JSONL', 'NORMAL', 'VERBOSE'], ['STDERR', 'STDOUT'])}
    ids |= {'CONSUMER-PRESSURE-JSONL-STDERR', 'CONSUMER-PRESSURE-JSONL-STDOUT'}
    require(len(rows) == 38 and {row['id'] for row in rows} == ids, 'Exact consumer case set differs.')
    output = Path(args.output); output.mkdir(parents=True, exist_ok=True)
    report = dict(scope='WP05-S04-T04 actual static environment -> internal Core console/queue consumers and original physical bytes; controlled independent file callbacks. No public factory, physical file ownership, bounded lifecycle/provider/release claim.', family=args.family, cases=[], negativeControlsRejected=[])
    actuals = {}; instances = set()
    for row in rows:
        command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', args.host, 'console-consumer', str(Path(args.catalog).resolve()), row['id']]
        with tempfile.TemporaryDirectory(prefix='cis-consumer-') as cwd:
            process = subprocess.Popen(command, cwd=cwd, env=environment_for(row), stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            try: stdout, stderr = process.communicate(timeout=30)
            except subprocess.TimeoutExpired:
                process.kill(); stdout, stderr = process.communicate(timeout=5)
                (output / (row['id'] + '.stdout.bin')).write_bytes(stdout); (output / (row['id'] + '.stderr.bin')).write_bytes(stderr)
                raise
            require(not list(Path(cwd).iterdir()), 'Consumer created unexpected physical files.')
        (output / (row['id'] + '.stdout.bin')).write_bytes(stdout); (output / (row['id'] + '.stderr.bin')).write_bytes(stderr)
        require(process.returncode == 0, 'Consumer child failure.')
        if row['mode'] == 'pressure':
            require(len(stdout) >= row['minimumStdoutBytes'] and len(stderr) >= row['minimumStderrBytes'], 'Consumer pipe pressure was not exercised.')
        raw = stdout.rstrip(b'\r\n'); start = raw.rfind(b'\n') + 1
        actual = read(raw[start:].decode('utf-8'))
        if row['physical']:
            actual['stdout'] = base64.b64encode(raw[:start]).decode('ascii'); actual['stderr'] = base64.b64encode(stderr).decode('ascii')
        else: require(start == 0 and stderr == b'', 'Controlled child emitted extra stdout/stderr.')
        verify(catalog, row, actual); observed = actual['observed']
        require(observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == args.family == actual['resource']['os_family'], 'Actual runtime/platform differs.')
        require(observed['fixtureSha256'] == sha(args.host) and observed['coreSha256'] == sha(Path(args.host).with_name('CIS.Core.dll')) and observed['inputsSha256'] == sha(args.catalog), 'Actual binary/input identity differs.')
        require(observed['processId'] == actual['resource']['process_id'] == process.pid, 'Actual child process identity differs.')
        instance = actual['resource']['instance_id']; require(instance not in instances, 'Fresh consumer reused an instance identity.'); instances.add(instance)
        actuals[row['id']] = actual
        report['cases'].append(dict(id=row['id'], passed=True, command=command, environment=row['environment'], observed=observed, instanceId=instance, processId=process.pid))
    row = next(row for row in rows if row['id'] == 'CONSUMER-BLOCK-JSONL-STDERR')
    for fault, reason in CONTROLS.items():
        try: verify(catalog, row, damage(actuals[row['id']], fault))
        except AssertionError as error:
            require(str(error) == reason, 'Consumer damage failed for another reason: ' + fault)
            report['negativeControlsRejected'].append(fault)
        else: raise AssertionError('Consumer accepted damage: ' + fault)
    require(len(report['negativeControlsRejected']) == 13, 'Consumer controls missing.')
    report.update(passed=True, count=38, coreSha256=sha(Path(args.host).with_name('CIS.Core.dll')), hostSha256=sha(args.host), inputsSha256=sha(args.catalog))
    (output / 'summary.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, count=38, passed=True)))


if __name__ == '__main__': main()
