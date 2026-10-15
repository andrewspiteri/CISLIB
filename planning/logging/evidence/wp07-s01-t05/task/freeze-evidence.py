from pathlib import Path
import json,hashlib,shutil,sys,subprocess
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t05';dest=r/'planning/logging/evidence/wp07-s01-t05'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
assert not dest.exists();dest.mkdir(parents=True)
def copy(src,rel):
 out=dest/rel;out.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,out)
lanes=[]
for label,work,audit in [('windows',t/'windows-verified',t/'windows-final-audit.json'),('ubuntu',t/'native-final',t/'native-final-audit.json')]:
 d=work/'.artifacts/csharp/verification/Developer';s=load(d/'summary.json');a=load(audit);assert s['exitCode']==0 and a['runId']==s['runId'];lanes.append(a)
 for p in sorted(d.rglob('*')):
  if p.is_file() and not {'bin','obj','packages','feed','app','http-cache'}.intersection(p.relative_to(d).parts) and p.suffix in {'.json','.trx','.log','.txt'}:copy(p,Path('native')/label/'Developer'/p.relative_to(d))
 copy(audit,Path('audits')/(label+'.json'))
 copy(t/(label+'-candidate-audit.json'),Path('audits')/(label+'-candidate-origins.json'))
for p in (t/'windows-final-candidates').rglob('*'):
 if p.is_file():copy(p,Path('native/windows/candidate-observations')/p.relative_to(t/'windows-final-candidates'))
for folder in ['Release','Developer']:
 for p in (t/'guard-qualified-work/.artifacts/csharp/verification'/folder).rglob('*'):
  if p.is_file():copy(p,Path('controls')/folder/p.relative_to(t/'guard-qualified-work/.artifacts/csharp/verification'/folder))
for name in ['baseline.json','windows-inputs.json','verified-native-inputs.json','guard-qualified-controls.json','guard-qualified-controls.py','required-field-controls.json','required-field-controls.ps1','audit-candidates.py','audit-reconciliation.py','collect-candidates.py','verified-run-native.sh','run-native-verified.py','windows-verified.log','verified-native.log','release-control.log','lane-mismatch-control.log']:
 copy(t/name,Path('task')/name)
for name in ['claude-manifest.json','claude-review.json','claude-result.txt','claude-followup-manifest.json','claude-followup-review.json','claude-followup-result.txt','claude-final-manifest.json','claude-final-review.json','claude-final-result.txt','review-resolution.json','claude-resolution-manifest.json','claude-resolution-review.json','claude-resolution-result.txt']:
 copy(t/name,Path('review')/name)
for name in ['os-release.txt','dotnet-info-pinned.txt','powershell-info.json','snapshot-commit.txt']:
 copy(t/'native-final'/name,Path('native/ubuntu')/name)
copy(t/'windows-final-work/.artifacts/csharp/verification/Developer/families/architecture/report.trx',Path('prior-failed-checker/windows-architecture.trx'))
copy(t/'windows-final-work/.artifacts/csharp/verification/Developer/summary.json',Path('prior-failed-checker/summary.json'))
copy(t/'before-final-corrections-inputs.json',Path('prior-failed-checker/inputs.json'))
for name in ['failed-native-arch.trx','failed-native-arch.log']:
 copy(t/name,Path('prior-runner-timeout')/name)
copy(t/'before-runner-bound-adjustment/verified-native-inputs.json',Path('prior-runner-timeout/inputs.json'))
copy(t/'checker-final/report.trx',Path('controls/focused-checker.trx'))
copy(t/'checker-final.log',Path('controls/focused-checker.log'))
for name in ['graph-status.log','graph-find-DiscoveryLogger.log','graph-find-Get-VerificationIssues.log','graph-context.json','graph-query-summary.json','graph-build-final.log']:
 copy(t/name,Path('graph')/name)
copy(Path(__file__),Path('task/freeze-evidence.py'))
b=load(t/'baseline.json');assert all(hashlib.sha256((r/p).read_bytes()).hexdigest()==v for p,v in b['protected'].items())
inv=load(r/'conformance/logging/case-inventory.json');bindings=[v for c in inv['cases'] for v in c.get('bindings',[])]
record={'task':'WP07-S01-T05','baseCommit':b['head'],'scope':'Required discovery/lane/outcome/supporting method reconciliation; not full release qualification.','lanes':lanes,'cases':{'preserved':len(inv['cases']),'implemented':sum(c['implementation']=='Implemented' for c in inv['cases']),'planned':[c['id'] for c in inv['cases'] if c['implementation']=='Planned'],'compositeGroups':sum(bool(c.get('bindings')) for c in inv['cases']),'bindingEntries':len(bindings),'distinctGroupMethods':len({v['method'] for v in bindings})},'controls':{'profileAndFreshness':load(t/'guard-qualified-controls.json'),'requiredFieldDiagnostics':load(t/'required-field-controls.json')},'review':{'initial':'FINDINGS','resolution':'review/review-resolution.json','followup':'review/claude-resolution-result.txt','readOnly':True,'scope':'Logging verification code and case metadata; no Type Manager implementation or credentials.'},'protectedInputs':{'count':len(b['protected']),'unchanged':True},'sourceNote':'Both final native runs use the same maintained-source snapshot. The isolated Git HEAD is supplemented by exact raw input hashes for the final runner/settings/document overrides; unchanged C# and shipping binaries retain that HEAD. Isolated verification copies avoid primary checkout SourceLink file locks. Final status/execution-matrix evidence links are documentation after the native snapshot.','nativeAttemptLimit':'An earlier cross-filesystem SDK run exceeded its 20-minute runner timeout before tests and is not a pass. Final Ubuntu run stages the same toolchain on Linux storage.','nativeInputMetadata':'All 1092 maintained files match both lane manifests. Native source inventory additionally includes three generated root diagnostics. OS and snapshot bytes are retained; the pre-pin root dotnet-info is hash-only. Exact pinned SDK and PowerShell reports are retained. This is not a complete offline retention kit.','nativeHost':'Ubuntu 24.04 x64 under WSL; SDK10.0.401/runtime10.0.12; actual process origins are retained.','nonshippingMetadata':'Three Type Manager fixture/example hosts declare both supported RIDs, generated lockfiles retain both, and six Microsoft SDK runtime/host download IDs are explicitly source-mapped. No shipping dependency or implementation changed.','graphContext':'graph/graph-context.json','remaining':['Nine planned obligations, eight release gates, full mutation/compatibility/coverage qualification and later roadmap work.','No publication, historical replay, offline kit or release support claim.']}
(dest/'verification.json').write_bytes((json.dumps(record,indent=2)+'\n').encode())
files=[{'path':p.relative_to(dest).as_posix(),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(dest.rglob('*')) if p.is_file()]
(dest/'hashes.json').write_bytes((json.dumps({'task':'WP07-S01-T05','excludes':'hashes.json itself','files':files},indent=2)+'\n').encode());print('Frozen evidence',len(files),'files',sum(f['bytes'] for f in files),'bytes')
