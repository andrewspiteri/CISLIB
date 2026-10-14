"""Reject mismatched candidate bytes and source records before instrumentation builds."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser()
parser.add_argument('--repo-root', type=Path, required=True)
parser.add_argument('--candidate-core', type=Path, required=True)
parser.add_argument('--candidate-report', type=Path, required=True)
parser.add_argument('--output-root', type=Path, required=True)
args = parser.parse_args()
root = args.output_root.resolve()
if root.exists() and any(root.iterdir()):
    parser.error('Use an empty output root')
root.mkdir(parents=True, exist_ok=True)
verifier = args.repo_root.resolve() / 'implementations/csharp/eng/verify-type-manager-instrumentation.py'
candidate = args.candidate_core.resolve()
producer = args.candidate_report.resolve()
changed_core = root / 'changed-core.dll'
changed_core.write_bytes(candidate.read_bytes() + b'\0')
source_report = json.loads(producer.read_text(encoding='utf-8-sig'))
source_report['coreSourceInputs']['TypeManagement/TypeManager.cs'] = '0' * 64
changed_source = root / 'changed-source.json'
changed_source.write_text(json.dumps(source_report), encoding='utf-8')
report = {'complete': False, 'verifierSha256': hashlib.sha256(verifier.read_bytes()).hexdigest(), 'results': []}
report_path = root / 'results.json'
report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
for name, core, record, message in [
    ('changed-core', changed_core, producer, 'Candidate producer report does not match the frozen Core bytes'),
    ('changed-source', candidate, changed_source, 'Candidate source provenance differs from the source selected for instrumentation')
]:
    output = root / name
    result = subprocess.run([sys.executable, str(verifier), '--repo-root', str(args.repo_root.resolve()),
                             '--candidate-core', str(core), '--candidate-report', str(record),
                             '--output-root', str(output)], text=True, encoding='utf-8', errors='replace',
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (root / (name + '.log')).write_text(result.stdout, encoding='utf-8')
    if result.returncode == 0 or message not in result.stdout or (output / 'binaries').exists():
        raise RuntimeError(name + ' was not rejected before instrumentation')
    report['results'].append({'case': name, 'passed': True, 'rejectedFor': message})
    report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
report['complete'] = True
report_path.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
