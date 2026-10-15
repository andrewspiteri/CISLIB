from pathlib import Path
import json,hashlib,shutil,subprocess,datetime
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t01';e=r/'planning/logging/evidence/wp07-s01-t01';report=r/'.artifacts/csharp/verification/Developer'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
s=load(report/'summary.json');assert s['finishedUtc'] and s['exitCode']==0
assert (t/'developer-verified.exit').read_text(encoding='utf-8-sig').strip()=='0'
subprocess.run(['python',str(t/'audit-reconciliation.py')],check=True)
e.mkdir(parents=True,exist_ok=True)
def copy(src,dst):
 p=e/dst;p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(r/src,p)
for name in ['summary.json','validation.json','source-inputs.json']:
 copy(Path('.artifacts/csharp/verification/Developer')/name,Path('native/Developer')/name)
for family in ['unit','integration','architecture','business']:
 for name in ['discovery.json','execution.json','report.trx']:
  copy(Path('.artifacts/csharp/verification/Developer/families')/family/name,Path('native/Developer/families')/family/name)
for name in ['baseline.json','audit-reconciliation.py','reconciliation-audit.json','review-resolutions.json','claude-manifest.json','claude-prompt.txt','claude-review.json','claude-review.stderr.txt','claude-review.exit-code','claude-findings.json','run-claude.py','developer-verified.log','developer-verified.exit','boundaries.log','graph-after.txt','graph-scope-find.txt']:
 copy(Path('.artifacts/csharp/tasks/wp07-s01-t01')/name,Path('task')/name)
for folder in ['scopes','business-initial','business-corrected','business-reviewed','business-reviewed-02']:
 copy(Path('.artifacts/csharp/tasks/wp07-s01-t01')/folder/'report.trx',Path('focused')/folder/'report.trx')
for folder in ['developer-initial','developer-format-failure','developer-before-review']:
 for p in (t/folder).rglob('*'):
  if p.is_file():copy(p.relative_to(r),Path('prior-runs')/folder/p.relative_to(t/folder))
# Replay exact method binding and source-preservation checks against the frozen native reports.
subprocess.run(['python',str(t/'audit-reconciliation.py'),'planning/logging/evidence/wp07-s01-t01/native/Developer'],check=True)
copy(Path('.artifacts/csharp/tasks/wp07-s01-t01/reconciliation-audit.json'),Path('task/reconciliation-audit.json'))
owned=[p for p in subprocess.run(['git','diff','--name-only'],check=True,capture_output=True,text=True).stdout.splitlines() if not p.startswith('planning/logging/evidence/')]
owned+=['conformance/logging/functional-reconciliation.json','docs/modules/logging/functional-reconciliation.md','implementations/csharp/tests/CIS.Core.BusinessCases/LoggingBusinessStories.cs','implementations/csharp/tests/CIS.Core.UnitTests/PublicOperationScopeTests.cs','implementations/csharp/tests/fixtures/DiscoveryHost/LoggingBusinessFixture.cs']
inputs=[{'path':p,'bytes':(r/p).stat().st_size,'sha256':hashlib.sha256((r/p).read_bytes()).hexdigest()} for p in sorted(set(owned))]
# A non-executable applicability note was added after the native runner captured its inventory.
# Preserve the exact earlier map bytes and disclose that difference; compiled/test inputs must match.
source_inventory={i['path']:i['sha256'] for i in load(report/'source-inputs.json')}
for f in inputs:
 if f['path'].endswith(('.cs','.csproj','.props','.targets','.lock.json')) and f['path'] in source_inventory:
  assert f['sha256']==source_inventory[f['path']],f['path']
map_path='conformance/logging/functional-reconciliation.json'
map_current=load(r/map_path); map_before=json.loads(json.dumps(map_current));map_before['unitAreas'][0].pop('applicability',None)
raw=(json.dumps(map_before,indent=2,ensure_ascii=False)+'\n').replace('\n','\r\n').encode('utf-8')
assert hashlib.sha256(raw).hexdigest()==source_inventory[map_path],'Unexpected pre-run map differences'
(e/'task/reconciliation-at-run-start.json').write_bytes(raw)

receipt={'task':'WP07-S01-T01','recordedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'baseCommit':load(t/'baseline.json')['head'],'scope':'Section14/all16BC functional reconciliation,14 observable logging business cases,3 public scope cases, actual public scope wiring and actionable token/range diagnostics.','developer':{'runId':s['runId'],'exitCode':0,'stages':len(s['stages']),'nativeRoot':'native/Developer','audit':'task/reconciliation-audit.json'},'inputs':inputs,'nativeEvidenceNotes':['Only the final completed summary and per-family TRX/discovery/execution files represent the final Developer run. Reused empty-output stage logs can retain prior text and are not treated as final evidence.','The prior run that omitted BeginScope from an actual API expectation failed1architecturecase and did not execute business. A subsequent formatter failure was line-ending-only. The earlier complete pass precedes review fixes.','Focused initial BC006 failed because accepted levels were absent. The first review-tightened BC006 failed because an assertion incorrectly expected keys in the intentionally fixed safe exception Message; corrected tests assert Issues and separately inspect original exception text for leakage.','Claude raw result contains textual unavailable tool-invocation wrappers. claude-findings.json is extracted from the returned JSON code block; no tool capability was granted. Prompt bytes preserve the actual transfer; embedded file views normalize text line endings while headers identify original raw source hashes.'],'sourceInventoryDeltaAtFreeze':{'path':map_path,'change':'Added non-executable explanation that no boolean environment key exists, plus LF normalization; exact run-start map preserved at task/reconciliation-at-run-start.json. Compiled/test input hashes match the run inventory.'},'review':{'initial':'ChangesRequired,7findings','resolutions':'task/review-resolutions.json','final':'Pending follow-up'},'release':{'state':'Pending','remainingPlannedGroups':27,'pendingBusinessCases':['BC-009','BC-010'],'requiredWork':['WP07-S01-T02 exact candidate consumers','T03 concurrency','T04 coverage','T05 discovery/lanes','WP07-S02 architecture','WP07-S03 mutation','WP07-S04 compatibility','WP08 examples/instructions/skills','WP09 full release/bundle/publication']},'limits':'Windows development source-reference/public runtime or explicitly controlled seam evidence; no complete candidate-package/Ubuntu/full-release/historical/ingestion/publication claim. Type Manager implementation, original supplied sources and actual publication ledger preserved.'}
(e/'verification.json').write_bytes((json.dumps(receipt,indent=2,ensure_ascii=False)+'\n').encode('utf-8'))
print('Frozen final verification and task evidence',e)
