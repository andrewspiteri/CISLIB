"""Follow-up against an already completed isolated native inventory matrix."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--workspace', type=Path, required=True)
parser.add_argument('--output-root', type=Path, required=True)
parser.add_argument('--dotnet', default='dotnet')
args = parser.parse_args()
workspace = args.workspace.resolve()
output = args.output_root.resolve()
output.mkdir(parents=True, exist_ok=True)
(output / 'results.json').write_text(json.dumps({'complete': False}), encoding='utf-8')

def run(name, *arguments, expected_error=None):
    result = subprocess.run([args.dotnet, *arguments], cwd=workspace,
                            text=True, capture_output=True)
    log = result.stdout + result.stderr
    (output / (name + '.log')).write_text(log, encoding='utf-8')
    if expected_error:
        if result.returncode == 0 or expected_error not in log:
            raise RuntimeError(name + ' did not fail for the expected reason')
    elif result.returncode:
        raise RuntimeError(name + ' failed; inspect its log')
    return result.stdout.strip()

host = 'host/Host.csproj'
run('native-build', 'build', host, '-c', 'Debug', '-m:1', '--nologo')
relative = run('publish-directory', 'msbuild', host, '-p:Configuration=Debug',
               '-getProperty:PublishDir', '-nologo')
# MSBuild property output may retain Windows separators even on Unix; MSBuild
# normalizes these when executing its copy targets and NormalizeDirectory.
publish = (workspace / 'host' / relative.replace('\\', '/')).resolve()
manifest_path = publish / 'InventoryMatrixHost.types.json'
manifest_path.unlink(missing_ok=True)
run('default-publish', 'publish', host, '-c', 'Debug', '--no-build', '--no-restore', '-m:1', '--nologo')
manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
expected = {'InventoryMatrixHost', 'CIS.TypeManager.RidAsset', 'CIS.TypeManager.Fixture.Contracts',
            'CIS.TypeManager.Fixture.Transitive', 'CIS.TypeManager.Fixture.Unused'}
observed = {item['assemblyName'].split(',')[0] for item in manifest['assemblies']}
if observed != expected or len(manifest['assemblies']) != 5:
    raise RuntimeError('Default publish inventory is incomplete')
process = json.loads(run('published-host', str(publish / 'InventoryMatrixHost.dll')))
if process != {'initiallyLoaded': False, 'variant': 'linux-x64'}:
    raise RuntimeError('Unexpected native Linux host result: ' + str(process))
run('package-copy-disabled', 'build', host, '-c', 'Debug', '--no-restore', '-m:1',
    '-p:CopyLocalLockFileAssemblies=false', '--nologo',
    expected_error='CISTM003: Type Manager requires')
report = {'complete': True, 'results': [
    {'case': 'default-relative-publish-no-build', 'passed': True, 'publishDir': relative,
     'selected': sorted(observed), 'runtime': process},
    {'case': 'package-copy-disabled', 'passed': True}]}
(output / 'results.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
