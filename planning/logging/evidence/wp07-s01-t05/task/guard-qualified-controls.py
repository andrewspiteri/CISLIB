from pathlib import Path
import json,shutil,subprocess,hashlib
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t05';w=t/'guard-qualified-work';assert not w.exists()
for name in ['implementations/csharp/eng/verify.ps1','implementations/csharp/eng/Evidence.psm1','implementations/csharp/eng/Verification.psm1','implementations/csharp/verification/families.json','conformance/logging/case-inventory.json']:
 p=w/name;p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(r/name,p)
script=w/'implementations/csharp/eng/verify.ps1';results=[]
x=subprocess.run(['pwsh','-NoProfile','-File',str(script),'-Profile','Release','-Lane','windows-x64'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=30)
(t/'release-control.log').write_bytes(x.stdout);d=w/'.artifacts/csharp/verification/Release';s=json.loads((d/'summary.json').read_text(encoding='utf-8-sig'));c=json.loads((d/'discovery-reconciliation.json').read_text(encoding='utf-8-sig'))
assert x.returncode==s['exitCode']==2;assert len(c['plannedCaseIds'])==9;assert len(c['remainingReleaseGates'])==8;assert s['runId']==c['runId'] and s['source']==c['source'];assert s['reconciliation']['report']['sha256']==hashlib.sha256((d/'discovery-reconciliation.json').read_bytes()).hexdigest()
results.append({'control':'ReleaseRequiresPendingWork','exitCode':x.returncode,'runId':s['runId'],'plannedCases':9,'remainingReleaseGates':8,'issues':len(s['issues'])})
d=w/'.artifacts/csharp/verification/Developer';d.mkdir(parents=True);(d/'discovery-reconciliation.json').write_bytes(b'{"stale":true}')
x=subprocess.run(['pwsh','-NoProfile','-File',str(script),'-Profile','Developer','-Lane','linux-ubuntu2404-x64'],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=30);(t/'lane-mismatch-control.log').write_bytes(x.stdout);s=json.loads((d/'summary.json').read_text(encoding='utf-8-sig'))
assert x.returncode==s['exitCode']==1 and not (d/'discovery-reconciliation.json').exists() and s['reconciliation']['status']=='NotProduced';assert s['stages']==[];assert any(v['code']=='StageFailure' and 'actual execution host' in v['detail'] for v in s['issues'])
results.append({'control':'WrongLaneRemovesStaleReconciliation','exitCode':x.returncode,'runId':s['runId'],'status':s['reconciliation']['status'],'noStagesExecuted':True})
(t/'guard-qualified-controls.json').write_bytes((json.dumps({'controls':results,'passed':len(results)},indent=2)+'\n').encode());print(json.dumps(results))
