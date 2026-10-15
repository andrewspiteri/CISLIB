from pathlib import Path
import json,hashlib,xml.etree.ElementTree as ET,re,collections,sys
from datetime import datetime
root=Path.cwd();task=root/'.artifacts/csharp/tasks/wp07-s01-t01'; report=root/(sys.argv[1] if len(sys.argv)>1 else '.artifacts/csharp/verification/Developer')
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
x=load(root/'conformance/logging/functional-reconciliation.json'); inventory=load(root/'conformance/logging/case-inventory.json'); cases={c['id']:c for c in inventory['cases']}
summary=load(report/'summary.json'); assert summary['exitCode']==0 and summary['finishedUtc'],summary.get('exitCode')
assert len(summary['stages'])==15 and all(s['status']=='Passed' for s in summary['stages']),summary['stages']
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
methods={};family_counts={}
for family in ['unit','integration','architecture','business']:
 p=report/f'families/{family}/report.trx'; t=ET.parse(p)
 times=t.find('t:Times',ns);assert datetime.fromisoformat(summary['startedUtc']) <= datetime.fromisoformat(times.attrib['start']) <= datetime.fromisoformat(times.attrib['finish']) <= datetime.fromisoformat(summary['finishedUtc']),family
 c=t.find('.//t:Counters',ns);family_counts[family]=dict(c.attrib)
 assert int(c.attrib['failed'])==0 and int(c.attrib['notExecuted'])==0 and int(c.attrib['total'])>0
 for d in t.findall('.//t:UnitTest',ns):
  m=d.find('t:TestMethod',ns)
  if m is not None:methods[m.attrib['className'].split(',')[0]+'.'+m.attrib['name']]=family
 for result in t.findall('.//t:UnitTestResult',ns):assert result.attrib['outcome']=='Passed',result.attrib
source=(root/x['sourceGuide']).read_text(encoding='utf-8-sig');section=source.split('## 14 Full test suite')[1].split('## 15 Business case acceptance catalog')[0]
expected=dict(re.findall(r'^\| (Configuration|Identity|Immutability|Filtering|Deferred work|Serialization|Context|Delivery|File policy) \| (.+) \|$',section,re.M))
assert {a['area']:a['originalRequiredCoverage'] for a in x['unitAreas']}==expected
assert len(x['integrationScenarios'])==13 and {a['level'] for a in x['architectureInspectionLevels']}==set(range(1,6))
assert len(x['remainingFamilies'])==4 and len(x['compatibilityLayers'])==4 and len(x['architectureRules'])==6
bindings=[]
for area in x['unitAreas']+x['integrationScenarios']+x['architectureInspectionLevels']:
 for b in area['support']:
  assert b['method'] in methods,b['method']
  source=(root/b['source']).read_text(encoding='utf-8-sig');assert b['method'].split('.')[-1]+'(' in source,b
  assert all(cases[i]['method']==b['method'] and cases[i]['implementation']=='Implemented' for i in b['caseIds'])
  bindings.append(b['method'])
assert [b['id'] for b in x['businessCases']]==[f'BC-{i:03d}' for i in range(1,17)]
original=load(root/'planning/logging/sources/core-library-backlog-v1.json')
# The guide table is the authoritative literal catalog.
guide=(root/x['sourceGuide']).read_text(encoding='utf-8-sig')
source_cases={i:(s,r) for i,s,r in re.findall(r'^\| (BC-\d{3}) \| (.+?) \| (.+?) \|$',guide,re.M)}
for b in x['businessCases']:
 assert (b['title'],b['originalRequiredResult'])==source_cases[b['id']],b['id']
 assert all(m in methods for m in b.get('supportingTechnicalMethods',[]))
 c=cases[b['id']];assert all(b[k]==c[k] for k in ['title','requiredResult','implementation','method'])
 if b['implementation']=='Implemented':assert b['method'] in methods,b['id']
 else:assert b['id'] in ['BC-009','BC-010'] and b['pendingOwner']=='WP07-S04'
assert 'truncat' in cases['BC-015']['effectiveResult'].lower()
for f in load(task/'baseline.json')['files']:
 if f['path'].startswith('planning/logging/sources/') or f['path'].endswith(('ledger.json','TypeManagerStartupStories.cs')):
  assert hashlib.sha256((root/f['path']).read_bytes()).hexdigest()==f['sha256'],f['path']
family=next(f for f in load(root/'implementations/csharp/verification/families.json')['families'] if f['id']=='business');assert family['developerEnabled']
planned=[c['id'] for c in inventory['cases'] if c['implementation']=='Planned'];assert len(planned)==27,len(planned)
result={'task':'WP07-S01-T01','developerRunId':summary['runId'],'stageCount':len(summary['stages']),'nativeFamilies':family_counts,'reconciledUnitAreas':9,'reconciledIntegrationScenarios':13,'architectureInspectionLevels':5,'exactSupportingMethodsExecuted':len(set(bindings)),'implementedLoggingBusinessCases':14,'pendingBusinessCases':['BC-009','BC-010'],'remainingPlannedReleaseGroups':27,'originalSourcesLedgerAndTypeManagerStoriesPreserved':True,'result':'Passed','limits':'Native Windows development source-reference/controlled-seam evidence. Candidate package, complete coverage/mutation/architecture/compatibility and both release lanes remain required.'}
result['startedUtc']=summary['startedUtc']; result['finishedUtc']=summary['finishedUtc']
result['nativeArtifacts']=[]
for relative in ['summary.json','validation.json','source-inputs.json']+[f'families/{family}/{name}' for family in ['unit','integration','architecture','business'] for name in ['report.trx','discovery.json','execution.json']]:
 p=report/relative;result['nativeArtifacts'].append({'path':str(p.relative_to(root)).replace('\\','/'),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
method_rows={}
for family in ['unit','integration','architecture','business']:
 t=ET.parse(report/f'families/{family}/report.trx'); lookup={}
 for d in t.findall('.//t:UnitTest',ns):
  m=d.find('t:TestMethod',ns)
  if m is not None:lookup[d.attrib['id']]=m.attrib['className'].split(',')[0]+'.'+m.attrib['name']
 for row in t.findall('.//t:UnitTestResult',ns):
  method=lookup[row.attrib['testId']];entry=method_rows.setdefault(method,{'family':family,'method':method,'executed':0,'outcomes':set(),'nativeReport':f'native/Developer/families/{family}/report.trx'})
  entry['executed']+=1;entry['outcomes'].add(row.attrib['outcome'])
for entry in method_rows.values():entry['outcomes']=sorted(entry['outcomes'])
result['supportingMethods']=[method_rows[m] for m in sorted(set(bindings))]
result['loggingBusinessMethods']=[dict(method_rows[b['method']],caseId=b['id']) for b in x['businessCases'] if b['implementation']=='Implemented']
result['matchedRunStartInputs']=[]
run_inputs={i['path']:i['sha256'] for i in load(report/'source-inputs.json')}
for path in sorted({b['source'] for a in x['unitAreas']+x['integrationScenarios']+x['architectureInspectionLevels'] for b in a['support']} | {'implementations/csharp/tests/CIS.Core.BusinessCases/LoggingBusinessStories.cs','implementations/csharp/tests/fixtures/DiscoveryHost/LoggingBusinessFixture.cs','implementations/csharp/src/CIS.Core/Logging/CoreLogRuntime.cs','implementations/csharp/src/CIS.Core/Environment/EnvironmentOverrides.cs','implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs'}):
 current=hashlib.sha256((root/path).read_bytes()).hexdigest();assert current==run_inputs[path],path
 result['matchedRunStartInputs'].append({'path':path,'runStartSha256':run_inputs[path],'currentSha256':current,'equal':True})
(task/'reconciliation-audit.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8');print('Audit passed:',len(result['supportingMethods']),'itemized supporting methods;',len(result['loggingBusinessMethods']),'logging cases;',len(result['matchedRunStartInputs']),'source hashes;',result['startedUtc'],'to',result['finishedUtc'])
