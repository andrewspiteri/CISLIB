import hashlib
import json
from pathlib import Path
import subprocess

ROOT=Path('C:/miscwork/portfolio/cislib')
def require(value,message):
    if not value: raise AssertionError(message)
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
record=read(ROOT/'planning/logging/evidence/wp04-s04-t01-windows-linux.json')
require(record['taskComplete'] and not record['storyComplete'],'Completion scope differs')
require(record['externalClaudeReview']['actionableFindingsResolved'],'Review findings remain')
OUT=ROOT/'planning/logging/evidence/wp04-s04-t01'
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s04-t01'
listed={item['path'] for item in record['reports']}
actual={path.relative_to(ROOT).as_posix() for path in OUT.rglob('*') if path.is_file()}
require(actual==listed,'Evidence folder and receipt manifest differ')
for name in ['retain-evidence.py','finalize-metadata.py','audit-commit.py']:
    require(sha(TASK/name)==sha(OUT/'commands'/name),'Executable helper differs from retained source: '+name)
require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Running audit differs from retained source')
for item in record['reports']:
    path=ROOT/item['path']
    require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Retained report differs: '+item['path'])
inputs=read(ROOT/record['canonicalSourceInventory'])
changes={item['path']:item for item in record['postCaptureInventoriedEdits']}
allowed={'docs/modules/logging/event-projection.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md'}
require(set(changes)==allowed,'Post-capture changes differ')
index={item['path']:item for item in inputs}
for path,change in changes.items():
    require(change['capturedSha256']==index[path]['sha256'],'Completion source identity differs')
    require(sha(OUT/'completion-metadata/input'/path)==change['capturedSha256'],'Completion snapshot differs')
run=Path(record['freshCopyVerification']['runRoot'])
for item in inputs:
    for copy in ['input','work']:
        require(sha(run/copy/item['path'])==item['sha256'],'Captured copy changed: '+item['path'])
    expected=changes[item['path']]['finalSha256'] if item['path'] in changes else item['sha256']
    require(sha(ROOT/item['path'])==expected,'Canonical input differs: '+item['path'])
for item in record['changedShippingFiles']: require(sha(ROOT/item['path'])==item['sha256'],'Reviewed shipping source differs')
changed=subprocess.run(['git','diff','--cached','--name-only','369607e','--','implementations/csharp/src/CIS.Core','contracts/logging'],cwd=ROOT,capture_output=True,check=True).stdout.decode('utf-8').splitlines()
require(set(changed)=={item['path'] for item in record['changedShippingFiles']},'Unexpected shipping diff')
require(not subprocess.run(['git','diff','--name-only'],cwd=ROOT,capture_output=True,check=True).stdout,'Unstaged tracked changes remain')
paths=[path.decode('utf-8') for path in subprocess.run(['git','diff','--cached','--name-only','-z','--diff-filter=ACM','--no-renames'],cwd=ROOT,capture_output=True,check=True).stdout.split(b'\0') if path]
status=subprocess.run(['git','diff','--cached','--name-status','--no-renames','--diff-filter=ACMDTUXB'],cwd=ROOT,capture_output=True,check=True).stdout.decode('utf-8').splitlines()
require(all(line.split(chr(9),1)[0] in {'A','C','M'} for line in status),'Unexpected staged deletion/type-change/unmerged/broken state')
deleted=subprocess.run(['git','diff','--cached','--name-only','--no-renames','--diff-filter=D'],cwd=ROOT,capture_output=True,check=True).stdout
require(not deleted,'Unexpected staged deletion/rename in T01')
for path in paths:
    if path=='planning/logging/evidence/wp04-s04-t01-windows-linux.json' or path.startswith('planning/logging/evidence/wp04-s04-t01/'):
        continue
    require(path in index,'Staged source absent from captured inventory: '+path)
    expected=changes[path]['finalSha256'] if path in changes else index[path]['sha256']
    require(sha(ROOT/path)==expected,'Staged source differs from captured/final metadata: '+path)
required={item['path'] for item in record['reports']} | {'planning/logging/evidence/wp04-s04-t01-windows-linux.json','planning/logging/evidence/wp04-s04-t01/commands/retain-evidence.py','planning/logging/evidence/wp04-s04-t01/commands/finalize-metadata.py'}
require(required.issubset(set(paths)),'Required evidence/receipt is absent from the staged diff: '+str(sorted(required-set(paths))))
untracked=subprocess.run(['git','ls-files','--others','--exclude-standard','-z'],cwd=ROOT,capture_output=True,check=True).stdout
require(not untracked,'Untracked nonignored task files remain')
request=''.join(':'+path+'\n' for path in paths).encode('utf-8')
raw=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=request,capture_output=True,check=True).stdout
position=0
for path in paths:
    end=raw.index(b'\n',position);header=raw[position:end].split()
    require(len(header)==3 and header[1]==b'blob','Missing staged blob')
    size=int(header[2]);data=raw[end+1:end+1+size];position=end+size+2
    require(hashlib.sha256(data).hexdigest()==sha(ROOT/path),'Staged bytes differ: '+path)
print(json.dumps({'retainedReportsVerified':len(record['reports']),'capturedInputsVerified':len(inputs),'stagedBlobsVerified':len(paths),'stagedEvidenceAndReceiptBlobsVerified':sum(path.startswith('planning/logging/evidence/wp04-s04-t01') for path in paths),'completionOnlyMetadataChanges':len(changes),'reviewedShippingFiles':len(changed)}))
