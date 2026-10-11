import hashlib,json
from pathlib import Path
ROOT=Path('C:/miscwork/portfolio/cislib')
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s03-t01'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t01'
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t01-windows-linux.json'
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def require(value,message):
    if not value: raise AssertionError(message)
record=read(RECEIPT)
for item in record['reports']:
    path=ROOT/item['path']
    require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Existing report changed')
gates=read(TASK/'verification-exits.json');process=read(TASK/'process-exits.json')
expected={'directDeveloper':read(TASK/'developer-exit.json')['exitCode'],'checkDeveloper':gates['checkDeveloper'],'releaseVerify':gates['releaseVerify'],'checkRelease':gates['checkRelease'],'freshCopyDeveloper':read(TASK/'fresh-copy-exit.json')['exitCode'],'windowsScopeDriver':process['windows'],'ubuntuScopeDriver':process['linux']}
require(record['verificationExits']==expected,'Receipt exit values differ from actual reports')
review=read(TASK/'claude-evidence-final.json')
require(not review.get('is_error') and 'All four earlier findings' in review['result'],'Final review differs')
for name in ['append-report-supplements.py','finalize-receipt.py','claude-evidence-final-prompt.txt','claude-evidence-final.json','claude-evidence-final.stderr.log','claude-evidence-final-exit.json']:
    target=OUT/'commands'/name
    require(not target.exists(),'Final supplement already retained')
    target.write_bytes((TASK/name).read_bytes())
record['externalClaudeReview'].update(evidenceSupplementFinalVerdict='Previous medium/low/info findings confirmed fixed; one final low provenance finding resolved with retained append/finalization scripts and a complete final-state audit outside the report hash set',evidenceSupplementFinalFindingResolved=True,reviews=['claude-review.json','claude-final.json','claude-evidence.json','claude-evidence-final.json'])
record['finalEvidencePreparation']={'appendStepScript':'planning/logging/evidence/wp04-s03-t01/commands/append-report-supplements.py','finalizerScript':'planning/logging/evidence/wp04-s03-t01/commands/finalize-receipt.py','record':'Manifest updated after retaining correction outputs and evidence reviews. Earlier retained audits describe their intermediate manifests. A final complete-state audit runs after this finalizer and staging; its output is intentionally outside reports to avoid a self-reference. Its actual output SHA256 and final receipt SHA256 are recorded in the task commit message. All actual verification exits are compared with the receipt.'}
record['reports']=[{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]
RECEIPT.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'retainedReports':len(record['reports']),'evidenceFindingsResolved':True,'finalIndexAudit':'Runs next, outside hashed report set; actual SHA256 is recorded in commit message.'}))
