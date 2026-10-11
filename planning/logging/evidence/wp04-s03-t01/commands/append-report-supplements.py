# Exact Python-stdin script from the successful six-output append step; retained afterward.
import hashlib,json
from pathlib import Path
root=Path('C:/miscwork/portfolio/cislib');task=root/'.artifacts/csharp/tasks/wp04-s03-t01';out=root/'planning/logging/evidence/wp04-s03-t01'
receipt=root/'planning/logging/evidence/wp04-s03-t01-windows-linux.json'
for name in ['complete-evidence-record','audit-commit-corrected']:
    result=json.loads((task/(name+'-exit.json')).read_text(encoding='utf-8-sig'))
    if result['exitCode']!=0: raise AssertionError('Evidence correction did not complete')
for name in ['complete-evidence-record.log','complete-evidence-record.stderr.log','complete-evidence-record-exit.json','audit-commit-corrected.json','audit-commit-corrected.stderr.log','audit-commit-corrected-exit.json']:
    target=out/'commands'/name
    if target.exists(): raise AssertionError('Supplement output already retained')
    target.write_bytes((task/name).read_bytes())
record=json.loads(receipt.read_text(encoding='utf-8'))
record['evidenceGenerationRecord']['correctionSupplementOutputsRetained']=['planning/logging/evidence/wp04-s03-t01/commands/'+name for name in ['complete-evidence-record.log','complete-evidence-record-exit.json','audit-commit-corrected.json','audit-commit-corrected-exit.json']]
record['reports']=[{'path':p.relative_to(root).as_posix(),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'bytes':p.stat().st_size} for p in sorted(out.rglob('*')) if p.is_file()]
receipt.write_text(json.dumps(record,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'retainedReports':len(record['reports']),'correctionAndStrengthenedAuditExit':0}))
