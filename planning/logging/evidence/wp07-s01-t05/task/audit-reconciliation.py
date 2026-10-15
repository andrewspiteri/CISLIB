from pathlib import Path
import argparse,collections,hashlib,json,xml.etree.ElementTree as E
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--source-root',type=Path);p.add_argument('--inputs',type=Path,required=True);p.add_argument('--lane',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();root=a.root;d=root/'.artifacts/csharp/verification/Developer';load=lambda f:json.loads(f.read_text(encoding='utf-8-sig'));summary=load(d/'summary.json');report=load(d/'discovery-reconciliation.json');inventory=load((a.source_root or root)/'conformance/logging/case-inventory.json');byid={c['id']:c for c in inventory['cases']};ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
inputs=load(a.inputs);assert summary['source']['commit']==inputs['snapshotCommit'];source_path=d/summary['source']['inputInventory']['path'];assert hashlib.sha256(source_path.read_bytes()).hexdigest()==summary['source']['inputInventory']['sha256'];observed_inputs={v['path']:v['sha256'] for v in load(source_path)};expected_inputs={v['path']:v['sha256'] for v in inputs['files']};assert all(observed_inputs.get(p)==v for p,v in expected_inputs.items());supplementary=sorted(set(observed_inputs)-set(expected_inputs));assert supplementary==(['dotnet-info-root.txt','os-release.txt','snapshot-commit.txt'] if a.lane=='linux-ubuntu2404-x64' else [])
assert summary['exitCode']==0 and summary['finishedUtc'] and len(summary['stages'])==15 and all(v['status']=='Passed' for v in summary['stages'])
assert summary['lane']==report['lane']==a.lane
assert summary['runId']==report['runId'] and summary['source']==report['source']
assert summary['reconciliation']['status']=='Produced' and summary['reconciliation']['report']['sha256']==hashlib.sha256((d/'discovery-reconciliation.json').read_bytes()).hexdigest()
assert len(byid)==len(inventory['cases'])==490
assert report['plannedCaseIds']==[c['id'] for c in inventory['cases'] if c['implementation']=='Planned'] and len(report['plannedCaseIds'])==9
seen=[];families=[]
def legacy(text):
 result=''
 for ch in text:
  n=ord(ch)
  if n>0xffff:n-=0x10000;result+='\\u%04x\\u%04x'%(0xd800+(n>>10),0xdc00+(n&1023))
  elif n<0x20 and n not in (9,10,13) or 0xd800<=n<=0xdfff or n in (0xfffe,0xffff):result+='\\u%04x'%n
  else:result+=ch
 return result
for family in report['families']:
 cases=family['cases'];seen.extend(c['id'] for c in cases)
 if family['status']!='Executed':
  assert family['id'] in ['mutation','compatibility'] and not family['executedRows']
  continue
 fd=d/'families'/family['id'];native=load(fd/'discovery-native.json');trx=E.parse(fd/'report.trx');definitions={v.get('id'):v.find('t:TestMethod',ns).attrib for v in trx.findall('./t:TestDefinitions/t:UnitTest',ns)};rows=trx.findall('./t:Results/t:UnitTestResult',ns);discovered={v['Id']:v for v in native['tests']};assert len(discovered)==len(native['tests'])==native['total']==len(rows)==family['discoveredRows']==family['executedRows']
 run_info=' '.join(trx.find('./t:ResultSummary',ns).itertext());assert 'Workers: 4' in run_info
 counts=collections.Counter();codec=0
 for row in rows:
  assert row.get('outcome')=='Passed'
  test=discovered[row.get('testId')];definition=definitions[row.get('testId')];method=definition['className']+'.'+definition['name'];assert test['Method']==method
  if test['Name']!=row.get('testName'):assert legacy(test['Name'])==row.get('testName');codec+=1
  lft,rgt=test['Source'].replace('\\','/'),definition['codeBase'].replace('\\','/')
  if a.lane=='windows-x64':lft,rgt=lft.lower(),rgt.lower()
  assert lft==rgt;counts[method]+=1
 required=set()
 for case in cases:
  original=byid[case['id']];assert case['applicable']==(a.lane in original['lanes']) and case['implementation']==original['implementation']
  expected=original.get('bindings',[original]);assert [v['method'] for v in case['bindings']]==[v['method'] for v in expected if v.get('method')]
  if original['implementation']=='Implemented' and case['applicable']:
   for binding in case['bindings']:
    method=binding['method'];required.add(method);assert binding['discovered']==binding['executed']==binding['passed']==counts[method]>=binding['minimumExecutions']>0;assert binding['failed']==binding['skipped']==0
  elif original['implementation']=='Planned':assert case['pending'] and not case['bindings']
 supporting={v['method']:v for v in family['supportingMethods']};assert set(supporting)==set(counts)-required
 inapplicable={v['method'] for c in inventory['cases'] if c['family']==family['id'] and c['implementation']=='Implemented' and a.lane not in c['lanes'] for v in c.get('bindings',[c]) if v.get('method')}
 for method,v in supporting.items():assert v['classification']==('BoundOnlyToInapplicableCases' if method in inapplicable else 'UnboundTechnical');assert v['discovered']==v['executed']==v['passed']==counts[method] and v['failed']==v['skipped']==0
 families.append({'family':family['id'],'rowsPassed':len(rows),'requiredMethods':len(required),'supportingMethods':len(supporting),'legacyDisplayNameRows':codec,'mstestWorkers':4,'nativeDiscoverySha256':hashlib.sha256((fd/'discovery-native.json').read_bytes()).hexdigest(),'nativeTrxSha256':hashlib.sha256((fd/'report.trx').read_bytes()).hexdigest()})
assert set(seen)==set(byid) and len(seen)==490
output={'lane':a.lane,'runId':summary['runId'],'stagesPassed':15,'caseIdsReconciled':len(seen),'plannedCaseIds':report['plannedCaseIds'],'families':families,'testsPassed':sum(v['rowsPassed'] for v in families),'maintainedInputCount':len(expected_inputs),'supplementaryNativeInputPaths':supplementary};a.output.write_bytes((json.dumps(output,indent=2)+'\n').encode());print(json.dumps(output))
