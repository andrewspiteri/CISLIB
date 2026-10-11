import hashlib
import json
from pathlib import Path
import subprocess

ROOT=Path('C:/miscwork/portfolio/cislib')
def require(value,message):
    if not value: raise AssertionError(message)
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
record=read(ROOT/'planning/logging/evidence/wp04-s03-t01-windows-linux.json')
require(record['taskComplete'] and not record['storyComplete'],'Completion scope differs')
require(record['externalClaudeReview']['actionableFindingsResolved'],'Review findings remain')
for item in record['reports']:
    path=ROOT/item['path']
    require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Retained report differs: '+item['path'])
inputs=read(ROOT/record['canonicalSourceInventory'])
changes={item['path']:item for item in record['postCaptureInventoriedEdits']}
allowed={'docs/modules/logging/operation-scopes.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md'}
require(set(changes)==allowed,'Post-capture changes differ')
run=Path(record['freshCopyVerification']['runRoot'])
for item in inputs:
    for copy in ['input','work']:
        require(sha(run/copy/item['path'])==item['sha256'],'Captured copy changed: '+item['path'])
    expected=changes[item['path']]['finalSha256'] if item['path'] in changes else item['sha256']
    require(sha(ROOT/item['path'])==expected,'Canonical input differs: '+item['path'])
for item in record['changedShippingFiles']: require(sha(ROOT/item['path'])==item['sha256'],'Reviewed shipping source differs')
changed=subprocess.run(['git','diff','--cached','--name-only','1024d40','--','implementations/csharp/src/CIS.Core'],cwd=ROOT,capture_output=True,check=True).stdout.decode('utf-8').splitlines()
require(set(changed)=={item['path'] for item in record['changedShippingFiles']},'Unexpected shipping diff')
require(not subprocess.run(['git','diff','--name-only'],cwd=ROOT,capture_output=True,check=True).stdout,'Unstaged tracked changes remain')
paths=[path.decode('utf-8') for path in subprocess.run(['git','diff','--cached','--name-only','-z','--diff-filter=ACM'],cwd=ROOT,capture_output=True,check=True).stdout.split(b'\0') if path]
request=''.join(':'+path+'\n' for path in paths).encode('utf-8')
raw=subprocess.run(['git','cat-file','--batch'],cwd=ROOT,input=request,capture_output=True,check=True).stdout
position=0
for path in paths:
    end=raw.index(b'\n',position);header=raw[position:end].split()
    require(len(header)==3 and header[1]==b'blob','Missing staged blob')
    size=int(header[2]);data=raw[end+1:end+1+size];position=end+size+2
    require(hashlib.sha256(data).hexdigest()==sha(ROOT/path),'Staged bytes differ: '+path)
print(json.dumps({'retainedReportsVerified':len(record['reports']),'capturedInputsVerified':len(inputs),'stagedBlobsVerified':len(paths),'stagedEvidenceAndReceiptBlobsVerified':sum(path.startswith('planning/logging/evidence/wp04-s03-t01') for path in paths),'completionOnlyMetadataChanges':len(changes),'reviewedShippingFiles':len(changed)}))
