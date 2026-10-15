from pathlib import Path
from datetime import datetime
import json,hashlib,shutil,subprocess,xml.etree.ElementTree as E
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t03';e=r/'planning/logging/evidence/wp07-s01-t03';report=r/'.artifacts/csharp/verification/Developer';native=t/'native-retained'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assert not e.exists(),'Use an entirely fresh task evidence root; archive prior material outside it'
summary=load(report/'summary.json');assert summary['finishedUtc'] and summary['exitCode']==0
assert (t/'developer.exit').read_text(encoding='utf-8-sig').strip()=='0'
assert (t/'native.exit').read_text(encoding='utf-8-sig').strip()=='0'
assert len(summary['stages'])==15 and all(s['status']=='Passed' for s in summary['stages'])
mapx=load(r/'conformance/logging/concurrency-qualification.json');assert len(mapx['areas'])==7 and len(mapx['bindings'])==46
inputs=load(t/'native-inputs.json');source_hashes={i['path']:i['sha256'] for i in inputs['files']}
assert (native/'payload.sha256').read_text(encoding='utf-8').split()[0]==inputs['archiveSha256']==digest(t/'native-inputs.tar')
staged={line.split(maxsplit=1)[1].strip():line.split()[0] for line in (native/'test-assemblies.sha256').read_text(encoding='utf-8').splitlines()}
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};lanes={}
def read_trx(path):
 tree=E.parse(path);lookup={}
 for d in tree.findall('.//t:UnitTest',ns):
  m=d.find('t:TestMethod',ns)
  if m is not None:lookup[d.get('id')]=m.get('className').split(',')[0]+'.'+m.get('name')
 counts={}
 for row in tree.findall('.//t:UnitTestResult',ns):
  assert row.get('outcome')=='Passed',(path,row.get('testName'))
  name=lookup[row.get('testId')];counts[name]=counts.get(name,0)+1
 return tree,counts
for lane in ['windows-x64','linux-ubuntu2404-x64']:
 rows=[];facts=[]
 for family,project in [('unit','CIS.Core.UnitTests'),('integration','CIS.Core.IntegrationTests')]:
  path=report/f'families/{family}/report.trx' if lane=='windows-x64' else native/f'results/{family}/report.trx'
  tree,counts=read_trx(path);times=tree.find('t:Times',ns).attrib
  if lane=='windows-x64':assert datetime.fromisoformat(summary['startedUtc'])<=datetime.fromisoformat(times['start'])<=datetime.fromisoformat(times['finish'])<=datetime.fromisoformat(summary['finishedUtc'])
  else:
   relative=f'implementations/csharp/tests/{project}/bin/Release/net10.0/{project}.dll';codebase=inputs['root']+'/'+relative
   assert staged[codebase]==source_hashes[relative]==digest(r/relative)
   for m in tree.findall('.//t:TestMethod',ns):assert m.get('codeBase')==codebase,m.attrib
  facts.append({'family':family,'path':f'native/Developer/families/{family}/report.trx' if lane=='windows-x64' else f'native/Ubuntu/results/{family}/report.trx','sha256':digest(path),'bytes':path.stat().st_size,'times':times})
  required=[b for b in mapx['bindings'] if b['family']==family]
  for binding in required:
   assert counts.get(binding['method'])==binding['minimumExecutions'],(lane,binding,counts.get(binding['method']))
   rows.append({'method':binding['method'],'family':family,'executed':counts[binding['method']],'outcome':'Passed','caseIds':binding['caseIds']})
  if lane!='windows-x64':assert sum(counts.values())==sum(b['minimumExecutions'] for b in required),'Unexpected focused discovery/execution'
 lanes[lane]={'bindings':rows,'executed':sum(row['executed'] for row in rows),'reports':facts}
assert all(v['executed']==217 for v in lanes.values())
base=load(t/'baseline.json')
for item in base['protected']+base['shippingSources']:assert digest(r/item['path'])==item['sha256'],item['path']
current_paths=['implementations/csharp/tests/CIS.Core.UnitTests/ConcurrencyQualificationTests.cs','conformance/logging/case-inventory.json','conformance/logging/requirement-case-map.json','conformance/logging/concurrency-qualification.json','docs/modules/logging/concurrency-qualification.md']
run_inputs={i['path']:i['sha256'] for i in load(report/'source-inputs.json')}
for path in current_paths:
 if path!='conformance/logging/concurrency-qualification.json':assert digest(r/path)==run_inputs[path],path
original_map=load(t/'qualification-at-run-start.json')
assert digest(t/'qualification-at-run-start.json')==run_inputs['conformance/logging/concurrency-qualification.json']
expected_map=json.loads(json.dumps(original_map))
invalid_method='CIS.Tests.UnitTests.ConcurrencyQualificationTests.InvalidOnlyConcurrentCallsKeepZeroTargetsAndFreezeLossAtEachBoundary'
boundary_method='CIS.Tests.IntegrationTests.FlushBoundaryProcessTests.RegisteredBoundaryFixtureRunsActualCoreInASeparateExactRuntime'
for area in expected_map['areas']:
 if area['id'] in ['already-cancelled','stop-races']:area['supportingMethods'].remove(invalid_method)
 if area['id']=='blocked-io':area['supportingMethods'].append(boundary_method)
assert expected_map==mapx,'Only the three reviewed non-executable attribution fixes may differ'
assert {b['method'] for b in mapx['bindings']}=={m for area in mapx['areas'] for m in area['supportingMethods']}
verified_executable_sources={b['source'] for b in mapx['bindings']}|{f['path'] for f in base['shippingSources']}
for source_path in verified_executable_sources:assert digest(r/source_path)==run_inputs[source_path],source_path
baseline_inventory=json.loads(subprocess.run(['git','show',base['baseCommit']+':conformance/logging/case-inventory.json'],check=True,capture_output=True).stdout)
new_ids={c['id'] for c in load(r/'conformance/logging/case-inventory.json')['cases']}-{c['id'] for c in baseline_inventory['cases']}
assert new_ids==set(mapx['newCases']) and len(new_ids)==2
new_executions=sum(b['minimumExecutions'] for b in mapx['bindings'] if set(b['caseIds'])&new_ids)
assert new_executions==3


family_counts={}
for family in ['unit','integration','architecture','business']:
 tree,_=read_trx(report/f'families/{family}/report.trx');family_counts[family]=tree.find('.//t:Counters',ns).attrib
release=load(r/'.artifacts/csharp/verification/Release/summary.json');assert release['exitCode']==2
planned=sum(i['code']=='MissingRequiredCase' for i in release['issues']);assert planned==sum(c['implementation']=='Planned' for c in load(r/'conformance/logging/case-inventory.json')['cases'])==27
missing_gates=sum(i['code']=='MissingReleaseGate' for i in release['issues']);assert missing_gates==len(load(r/'implementations/csharp/verification/families.json')['releaseGates'])
def copy(source,relative):
 target=e/relative;target.parent.mkdir(parents=True,exist_ok=True)
 if source.resolve()!=target.resolve():shutil.copy2(source,target)
for name in ['summary.json','validation.json','source-inputs.json']:copy(report/name,Path('native/Developer')/name)
for family in ['unit','integration','architecture','business']:
 for name in ['report.trx','discovery.json','execution.json']:copy(report/f'families/{family}/{name}',Path('native/Developer/families')/family/name)
for source in native.rglob('*'):
 if source.is_file():copy(source,Path('native/Ubuntu')/source.relative_to(native))
for name in ['summary.json','validation.json']:copy(r/'.artifacts/csharp/verification/Release'/name,Path('pending/Release')/name)
for name in ['baseline.json','graph-before.txt','graph-start.txt','graph-find.txt','graph-context.txt','developer.log','developer.exit','native.log','native.exit','native-inputs.json','run-native.sh','run-native.py','retain-native.py','release.log','release.exit']:
 copy(t/name,Path('task')/name)
copy(Path(__file__).resolve(),Path('task/freeze-evidence.py'))
copy(t/'qualification-at-run-start.json',Path('task/qualification-at-run-start.json'))
for source in t.glob('claude*'):
 if source.is_file():copy(source,Path('task')/source.name)
for folder in ['focused','focused-02']:
 if (t/folder/'report.trx').is_file():copy(t/folder/'report.trx',Path('prior-runs')/folder/'report.trx')
artifacts=[{'path':p.relative_to(e).as_posix(),'sha256':digest(p),'bytes':p.stat().st_size} for p in sorted((e/'native').rglob('*')) if p.is_file()]
audit={'task':'WP07-S01-T03','developerRunId':summary['runId'],'startedUtc':summary['startedUtc'],'finishedUtc':summary['finishedUtc'],'stagesPassed':15,'familyCounts':family_counts,'lanes':lanes,'payloadSha256':inputs['archiveSha256'],'stagedAssemblies':staged,'sourceInputs':[{'path':path,'sha256':digest(r/path),'runStartSha256':run_inputs[path],'equal':digest(r/path)==run_inputs[path]} for path in current_paths],'nativeArtifacts':artifacts,'verifiedExecutableSourceCount':len(verified_executable_sources),'newCaseIds':sorted(new_ids),'newExecutions':new_executions,'shippingSourcesUnchanged':len(base['shippingSources']),'protectedSourcesLedgerAndTypeManagerPreserved':True,'result':'Passed'}
(e/'audit.json').write_bytes((json.dumps(audit,indent=2)+'\n').encode())
release_path=e/'pending/Release/summary.json';receipt={'task':'WP07-S01-T03','baseCommit':base['baseCommit'],'scope':f'Seven deterministic concurrency areas via46 exact methods/217 mapped executions per lane; {len(new_ids)} new case bindings / {new_executions} new executions.','developer':{'runId':summary['runId'],'startedUtc':summary['startedUtc'],'finishedUtc':summary['finishedUtc'],'exitCode':0,'stagesPassed':15,'totalTests':sum(int(c['total']) for c in family_counts.values())},'lanes':{lane:v['executed'] for lane,v in lanes.items()},'audit':'audit.json','auditGenerator':{'path':'task/freeze-evidence.py','sha256':digest(Path(__file__))},'inputs':audit['sourceInputs'],'release':{'exitCode':2,'summary':{'path':'pending/Release/summary.json','sha256':digest(release_path),'bytes':release_path.stat().st_size},'remainingPlannedCaseGroups':planned,'missingReleaseGates':missing_gates,'missingFamilyArea':any(i['code']=='MissingFamilyExecution' for i in release['issues'])},'review':{'state':'Pending read-only review; update actual verdict after response'},'sourceInventoryDelta':'Only the three reviewed documentation attribution fixes in concurrency-qualification.json differ; exact original map retained at task/qualification-at-run-start.json. All listed modified/bound test sources and77shipping sources match run start; no blanket assertion about unlisted executable inputs.','limits':'Controlled callback/clock and source-reference technical tests; private storage probes pinned.NET10.0.12 and seeded near-exhaustion distinguished from actual emitted history. Candidate/physical public boundary evidence is previousWP07-S01-T02. No new shipping API/dependency/framework, full coverage/mutation/architecture/cumulative compatibility/platform-release or ingestion claim.'}
(e/'verification.json').write_bytes((json.dumps(receipt,indent=2)+'\n').encode());print('Frozen',receipt['developer']['totalTests'],'Developer tests;217 mapped executions per lane')
