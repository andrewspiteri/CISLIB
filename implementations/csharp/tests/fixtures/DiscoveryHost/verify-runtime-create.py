"""Native actual-Core observations and independent corruption controls."""
import argparse, copy, importlib.util, json, os, subprocess, tempfile
from datetime import datetime
from pathlib import Path
spec = importlib.util.spec_from_file_location('cleanup_base', Path(__file__).with_name('verify-flush-results.py'))
base = importlib.util.module_from_spec(spec); spec.loader.exec_module(base)
require, load, sha, digest, decoded, number = base.require, base.load, base.sha, base.digest, base.decoded, base.number
MODES = ('file', 'disabled', 'invalid-settings', 'invalid-metadata', 'contended')


def check(mode, a, pid=None):
    require(a['mode'] == mode and mode in MODES, 'mode')
    require(a['runtime'] == '.NET 10.0.12', 'actual runtime')
    if pid is not None: require(type(a['pid']) is int and a['pid'] == pid and pid > 0, 'actual child PID')
    if mode in ('invalid-settings', 'invalid-metadata'):
        require(a['error'] == 'CoreConfigurationException' and a['noFileArtifacts'] is True, 'pre-acquisition validation'); return
    result = a['result']
    require((result['completion'], result['succeeded'], result['loss']) == ('Completed', True, False), 'successful stop')
    require(a['sameTask'] is True and a['preserved'] is True and a['factPid'] == a['pid'], 'runtime facts and identity')
    disabled = mode == 'disabled'; contention = mode == 'contended'
    require(a['contention'] is contention and a['released'] is (not disabled) and a['frozen'] is (not disabled), 'physical ownership and frozen settings')
    require(a['error'] == ('FileOwnershipUnavailable' if contention else None), 'safe initialization error')
    raw = decoded(a['bytes']); rows = base.records(raw, a['pid'])
    require([row['event_name'] for row in rows] == ([] if disabled else ['factory']), 'actual public records')
    require(a['noFileArtifacts'] is disabled, 'disabled output isolation')
    require([o['name'] for o in result['outputs']] == ([] if disabled else ['file']), 'actual destinations')
    if not disabled:
        require(a['application'] == 'CIS.Core.DiscoveryFixture', 'automatic application fact')
        o = result['outputs'][0]; require(tuple(number(o[k]) for k in ('boundary', 'written', 'failed', 'abandoned', 'pending')) == (1, 1, 0, 0, 0) and o['flush'] == 'Succeeded', 'actual delivery')
        require(all(len(line) + 1 <= 16384 for line in raw.splitlines()), 'bounded records')


def corruption_controls(cases):
    samples = {c['id']: c['report'] for c in cases}
    edits = [
        ('pid', 'file', 'actual child PID', lambda a: a.update(pid=a['pid'] + 1)),
        ('runtime', 'file', 'actual runtime', lambda a: a.update(runtime='.NET 10.0.13')),
        ('validation', 'invalid-settings', 'pre-acquisition validation', lambda a: a.update(noFileArtifacts=False)),
        ('metadata', 'invalid-metadata', 'pre-acquisition validation', lambda a: a.update(error=None)),
        ('stop', 'file', 'successful stop', lambda a: a['result'].update(succeeded=False)),
        ('facts', 'file', 'runtime facts and identity', lambda a: a.update(factPid=a['pid'] + 1)),
        ('release', 'file', 'physical ownership and frozen settings', lambda a: a.update(released=False)),
        ('freeze', 'file', 'physical ownership and frozen settings', lambda a: a.update(frozen=False)),
        ('contention', 'contended', 'safe initialization error', lambda a: a.update(error='unsafe raw path')),
        ('record', 'file', 'actual public records', lambda a: a.update(bytes='')),
        ('disabled', 'disabled', 'disabled output isolation', lambda a: a.update(noFileArtifacts=False)),
        ('application', 'file', 'automatic application fact', lambda a: a.update(application='unknown')),
        ('delivery', 'file', 'actual delivery', lambda a: a['result']['outputs'][0].update(written='0')),
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
    mapping = load((repo / 'conformance/logging/runtime-create-cases.json').read_bytes())
    require([c['id'] for c in mapping['cases']] == list(MODES), 'shared case membership')
    host, out = Path(args.host).resolve(), Path(args.output).resolve(); out.mkdir(parents=True, exist_ok=True)
    cases = []
    with tempfile.TemporaryDirectory(prefix='cislib-runtime-create-') as temp:
        for mode in MODES:
            work = Path(temp) / mode; work.mkdir()
            env = {k: v for k, v in os.environ.items() if not k.startswith('CORE_')}
            command = [args.dotnet, 'exec', '--fx-version', '10.0.12', '--roll-forward', 'Disable', str(host), 'runtime-create', mode, str(work)]
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
    report = dict(schema='cis.logging.runtime-create-native/v1', task='WP05-S10-T04', family=args.family, children=len(cases), coreSha256=sha(host.parent / 'CIS.Core.dll'), fixtureSha256=sha(host), verifierSha256=sha(__file__), baseVerifierSha256=sha(Path(__file__).with_name('verify-flush-results.py')), caseMappingSha256=sha(repo / 'conformance/logging/runtime-create-cases.json'), cases=cases, controls=corruption_controls(cases))
    (out / 'report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8', newline='\n')
    print(json.dumps(dict(family=args.family, children=len(cases), controls=len(report['controls']), passed=True)))


if __name__ == '__main__':
    main()
