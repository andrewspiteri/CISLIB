from pathlib import Path
import json,hashlib,shutil,xml.etree.ElementTree as E,datetime
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t02';e=r/'planning/logging/evidence/wp07-s01-t02';report=r/'.artifacts/csharp/verification/Developer'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
assert not (e/'native').exists(),'Use a fresh qualifying native evidence root; archive prior observations separately.'
s=load(report/'summary.json');assert s['finishedUtc'] and s['exitCode']==0,s.get('exitCode');assert (t/'developer-reviewed.exit').read_text(encoding='utf-8-sig').strip()=='0'
assert len(s['stages'])==15 and all(x['status']=='Passed' for x in s['stages'])
n={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};cases=[c for c in load(r/'conformance/logging/case-inventory.json')['cases'] if c['id'].startswith('INT-CANDIDATE-')];assert len(cases)==11 and sum(c['minimumExecutions'] for c in cases)==19
native=t/'linux-05-retained';lanes={};artifacts=[]
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def reference(p):return {'path':p.relative_to(r).as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)}
for lane,p in [('windows-x64',report/'families/integration/report.trx'),('linux-ubuntu2404-x64',native/'results/report.trx')]:
 tree=E.parse(p);lookup={}
 for d in tree.findall('.//t:UnitTest',n):
  m=d.find('t:TestMethod',n)
  if m is not None:lookup[d.get('id')]=m.get('className').split(',')[0]+'.'+m.get('name')
 methods={}
 for row in tree.findall('.//t:UnitTestResult',n):
  method=lookup[row.get('testId')]
  if method.startswith('CIS.Tests.IntegrationTests.CandidateLoggingProcessTests.'):
   assert row.get('outcome')=='Passed',row.get('testName');methods.setdefault(method,[]).append(row.get('testName'))
 bindings=[]
 for c in cases:
  assert len(methods.get(c['method'],[]))==c['minimumExecutions'],(lane,c['id']);bindings.append({'id':c['id'],'method':c['method'],'executions':len(methods[c['method']]),'outcome':'Passed','testNames':methods[c['method']]})
 assert sum(map(len,methods.values()))==19;lanes[lane]={'nativeReport':reference(p),'times':tree.find('t:Times',n).attrib,'caseBindings':bindings,'candidateExecutions':19,'failed':0,'skipped':0}
# Locate the actual private restore created within the full Windows run, using its test-context receipt.
text=(report/'families/integration/report.trx').read_text(encoding='utf-8-sig');import re
match=re.search(r'Exact package consumer evidence: ([^\r\n<]+)',text);assert match
win=Path(match.group(1).strip());assert (win/'candidate.json').is_file(),win
linux_receipts=list(native.rglob('candidate.json'));assert len(linux_receipts)==1
wp=load(win/'candidate.json');lp=load(linux_receipts[0]);assert wp['packageSha256']==lp['packageSha256'],(wp['packageSha256'],lp['packageSha256']);assert wp['assemblyHashes']==lp['assemblyHashes'];assert wp['projectSha256']==lp['projectSha256']==digest(r/'implementations/csharp/verification/consumers/logging-candidate/Consumer.csproj.template');assert wp['programSha256']==lp['programSha256']==digest(r/'implementations/csharp/verification/consumers/logging-candidate/Program.cs')
for lane,root in [('windows-x64',win),('linux-ubuntu2404-x64',linux_receipts[0].parent)]:
 origin=[]
 for p in root.glob('child-*/report.json'):
  data=load(p);assert data['os']==('windows' if lane=='windows-x64' else 'linux') and data['architecture']=='X64' and data['runtime']=='.NET 10.0.12'
  for name,value in wp['assemblyHashes'].items():assert data[name]['sha256']==value,(lane,p,name)
  origin.append({'report':p.relative_to(root).as_posix(),'mode':data['mode'],'stage':data['stage'],'pid':data['pid'],'instanceId':data['instanceId'],'loadedCoreSha256':data['core']['sha256'],'os':data['os'],'architecture':data['architecture'],'runtime':data['runtime']})
 assert len(origin)>=19;lanes[lane]['processOrigins']=origin;lanes[lane]['privateRestoreReceipt']=reference(root/'candidate.json')
 pressure=[]
 for process_report in root.glob('child-*/report.json'):
  observed=load(process_report)
  if observed['mode'] not in ['burst','deadline']:continue
  raw_path=process_report.parent/'stdout.bin';raw=raw_path.read_bytes();prefix=raw.rfind(b'\n')+1
  complete=[json.loads(line.decode('utf-8')) for line in raw[:prefix].splitlines()];tail=len(raw)-prefix
  output=next(o for o in observed['diagnostics']['outputs'] if o['outputName']=='console');uncertain=len(complete)-output['written']
  assert uncertain>=0 and uncertain+bool(tail)<=output['failed'] and tail<16384
  replies=load(process_report.parent/'replies.json');blocked=replies[0];held=next(o for o in blocked['diagnostics']['outputs'] if o['outputName']=='console')
  assert blocked['flush']['completion']=='TimedOut' and held['queueFull']>0 and 1<=held['pending']<=2
  original=next(row for row in replies if row['stage']=='stopped')
  assert original['stop']==observed['diagnostics']['lastStopResult']
  if observed['mode']=='burst':assert tail==0 and len(complete)==output['written'] and original['stop']['succeeded']
  else:assert original['stop']['completion']=='TimedOut' and not original['stop']['succeeded']
  pressure.append({'mode':observed['mode'],'rawBytes':len(raw),'rawSha256':digest(raw_path),'completeRecords':len(complete),'partialTailBytes':tail,'written':output['written'],'failed':output['failed'],'abandoned':output['abandoned'],'pending':output['pending'],'originalStopUnchanged':True})
 assert len(pressure)==2;lanes[lane]['physicalPressureObservations']=pressure

# Protect all shipping sources and the independently owned work.
base=load(t/'baseline.json')
for f in base['protected']+base['shippingSources']:assert digest(r/f['path'])==f['sha256'],f['path']
source_paths=['implementations/csharp/tests/CIS.Core.IntegrationTests/CandidatePackageFixture.cs','implementations/csharp/tests/CIS.Core.IntegrationTests/CandidateLoggingProcessTests.cs','implementations/csharp/verification/consumers/logging-candidate/Program.cs','implementations/csharp/verification/consumers/logging-candidate/Consumer.csproj.template','conformance/logging/case-inventory.json','conformance/logging/requirement-case-map.json','conformance/logging/candidate-consumer-qualification.json','docs/modules/logging/candidate-consumers.md']
run_inputs={i['path']:i['sha256'] for i in load(report/'source-inputs.json')}
for path in source_paths:assert digest(r/path)==run_inputs[path],path
# Native snapshot recorded the executed portable test assembly and current consumer source.
linux_inputs=load(t/'linux-inputs-05.json');recorded={i['path']:i['sha256'] for i in linux_inputs['files']}
assert recorded['implementations/csharp/verification/consumers/logging-candidate/Program.cs']==digest(r/source_paths[2])
test_dll='implementations/csharp/tests/CIS.Core.IntegrationTests/bin/Release/net10.0/CIS.Core.IntegrationTests.dll';assert recorded[test_dll]==digest(r/test_dll),'Native and current integration assembly differ'
staged_hash=(native/'test-assembly.sha256').read_text(encoding='utf-8').split()[0]
assert staged_hash==recorded[test_dll]==digest(r/test_dll)
expected_codebase=linux_inputs['root']+'/'+test_dll
for method in E.parse(native/'results/report.trx').findall('.//t:TestMethod',n):assert method.get('codeBase')==expected_codebase,method.attrib

e.mkdir(parents=True,exist_ok=True)
def copy(src,dst):
 p=e/dst;p.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,p)
for name in ['summary.json','validation.json','source-inputs.json']:copy(report/name,Path('native/Developer')/name)
family_counts={}
for family in ['unit','integration','architecture','business']:
 for name in ['report.trx','discovery.json','execution.json']:copy(report/f'families/{family}/{name}',Path('native/Developer/families')/family/name)
 counters=E.parse(report/f'families/{family}/report.trx').find('.//t:Counters',n).attrib;assert counters['failed']=='0' and counters['notExecuted']=='0';family_counts[family]=counters
for p in native.rglob('*'):
 if p.is_file():copy(p,Path('native/Ubuntu')/p.relative_to(native))
# Retain all control replies and raw redirected output from the exact full-run consumer, plus restore inputs/metadata.
for p in win.rglob('*'):
 if p.is_file() and p.suffix in ['.json','.txt','.log','.bin'] and not {'packages','http-cache','feed','bin'} & set(p.relative_to(win).parts):copy(p,Path('native/WindowsCandidate')/p.relative_to(win))
copy(Path(__file__).resolve(),Path('task/freeze-evidence.py'))
for name in ['baseline.json','graph-before.txt','graph-start.txt','graph-find.txt','graph-runtime-context.txt','developer-reviewed.log','developer-reviewed.exit','linux.log','linux.exit','linux-02.log','linux-02.exit','linux-05.log','linux-05.exit','linux-inputs-05.json','run-linux-05.sh','run-linux-05.py']:
 copy(t/name,Path('task')/name)
for folder in ['focused','focused-02','focused-03','focused-04','focused-05','focused-06','focused-reviewed']:
 if (t/folder/'report.trx').is_file():copy(t/folder/'report.trx',Path('prior-runs')/folder/'report.trx')
assert len(list((e/'native/Ubuntu').rglob('candidate.json')))==1
for qualifying_root in [e/'native/WindowsCandidate',next((e/'native/Ubuntu').rglob('candidate.json')).parent]:
 for report_path in qualifying_root.glob('child-*/report.json'):
  if load(report_path)['mode'] in ['burst','deadline']:
   assert not (report_path.parent/'stdout.txt').exists() and not (report_path.parent/'terminal-stream.txt').exists(),'Stale pressure evidence'
# Bind every reference to retained evidence, keeping source paths as provenance only.
for lane,relative in [('windows-x64','native/Developer/families/integration/report.trx'),('linux-ubuntu2404-x64','native/Ubuntu/results/report.trx')]:
 ref=lanes[lane]['nativeReport'];ref['sourcePath']=ref['path'];ref['path']=relative
 receipt=lanes[lane]['privateRestoreReceipt'];receipt['sourcePath']=receipt['path']
 receipt['path']='native/WindowsCandidate/candidate.json' if lane=='windows-x64' else 'native/Ubuntu/'+linux_receipts[0].relative_to(native).as_posix()
native_artifacts=[dict(reference(p),path=p.relative_to(e).as_posix()) for p in sorted((e/'native').rglob('*')) if p.is_file()]
release=load(r/'.artifacts/csharp/verification/Release/summary.json')
assert release['exitCode']==2
planned=sum(issue['code']=='MissingRequiredCase' for issue in release['issues'])
expected_planned=sum(c['implementation']=='Planned' for c in load(r/'conformance/logging/case-inventory.json')['cases'])
assert planned==expected_planned==27
missing_gates=sum(issue['code']=='MissingReleaseGate' for issue in release['issues'])
assert missing_gates==len(load(r/'implementations/csharp/verification/families.json')['releaseGates'])
missing_family_area=any(issue['code']=='MissingFamilyExecution' for issue in release['issues'])
for name in ['summary.json','validation.json']:copy(r/'.artifacts/csharp/verification/Release'/name,Path('pending/Release')/name)
release_receipt={'exitCode':2,'summary':dict(reference(e/'pending/Release/summary.json'),path='pending/Release/summary.json'),'remainingPlannedCaseGroups':planned,'missingReleaseGates':missing_gates,'missingFamilyArea':missing_family_area,'fullReleaseAreasPending':missing_gates+int(missing_family_area)}
copy(t/'retain-linux.py',Path('task/retain-linux.py'))
audit={'task':'WP07-S01-T02','runId':s['runId'],'startedUtc':s['startedUtc'],'finishedUtc':s['finishedUtc'],'stagesPassed':15,'familyCounts':family_counts,'candidate':{'packageVersion':wp['packageVersion'],'packageSha256':wp['packageSha256'],'assemblyHashes':wp['assemblyHashes'],'samePackageAndPortableTestAssemblyOnBothLanes':True,'sameConsumerProjectSha256':wp['projectSha256'],'nativeStagedTestAssemblySha256':staged_hash,'nativeTestCodebase':expected_codebase},'lanes':lanes,'sourceInputs':[dict(reference(r/p),runStartSha256=run_inputs[p],equal=True) for p in source_paths],'shippingSourcesUnchanged':len(base['shippingSources']),'protectedWorkPreserved':True,'nativeArtifacts':native_artifacts,'result':'Passed'}
(t/'audit.json').write_bytes((json.dumps(audit,indent=2)+'\n').encode());copy(t/'audit.json',Path('audit.json'))
receipt={'task':'WP07-S01-T02','baseCommit':base['baseCommit'],'scope':'Real exact-candidate public consumer qualification at process/stream/file boundaries.','audit':'audit.json','developer':{'runId':s['runId'],'startedUtc':s['startedUtc'],'finishedUtc':s['finishedUtc'],'exitCode':0,'stagesPassed':15,'totalTests':sum(int(c['total']) for c in family_counts.values())},'candidate':audit['candidate'],'nativeCandidateCases':{'windows-x64':19,'linux-ubuntu2404-x64':19},'inputs':audit['sourceInputs'],'controls':'Three changed loaded-assembly hashes and a changed runtime label are rejected after a real positive process. Pressure uses a private BCL control pipe and a real unread stdout pipe. Recovery intentionally prepares documented test states from candidate-produced inventory and bytes.','priorResults':'Retained focused failures exposed fixture cache-path formatting, normal/verbose and bounded payload assumptions, and a local UTF-8 edit error. Initial native console-control pressure cases timed out (17pass/2fail). A corrected private-pipe run passed19 but its shell wrapper returned2; saved-script invocation passes19 and exits0. The full Windows profile then repacked the archive (same three DLL hashes, different package SHA); final Ubuntu replay uses exactly that selected archive and passes19 with exit0. The first native TRX was not retained before WSL temporary state closed; its native console output is retained without inventing the missing report.','auditGenerator':{'path':'task/freeze-evidence.py','sha256':digest(Path(__file__))},'release':release_receipt,'review':{'state':'Pending final evidence verdict; record actual outcome after response'},'limits':'Both lanes qualify these19 candidate cases, not full platform/release support. Complete deterministic concurrency, coverage, discovery, architecture, mutation, cumulative compatibility, examples/skills and full release/publication remain pending. No shipping source/API/dependency, Type Manager implementation or publication ledger change.'}
(e/'verification.json').write_bytes((json.dumps(receipt,indent=2)+'\n').encode());print('Frozen task evidence:',receipt['developer']['totalTests'],'full Developer tests;19 candidate cases per lane')
