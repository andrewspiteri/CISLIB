import argparse,copy,importlib.util,json,re,shutil,tempfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('timer_acceptance_bytes',Path(__file__).with_name('verify-flush-results.py'))
base=importlib.util.module_from_spec(spec);spec.loader.exec_module(base)
require,load,sha,digest=base.require,base.load,base.sha,base.digest
LANES=('interval','timer','lifecycle','qualification')
PLATFORMS=('windows','linux')

def check(root,native,host,mapping,reports):
 plan=(root/'planning/logging/sources/core-library-implementation-plan-v1.md').read_text(encoding='utf-8-sig')
 original=dict(re.findall(r'^- \*\*(WP05-S09-AC0[1-4])\*\* — (.+)$',plan,re.M))
 require(len(mapping['criteria'])==4 and {c['id'] for c in mapping['criteria']}==set(original),'original criteria')
 require(set(mapping['lanes'])==set(LANES),'lane membership')
 for c in mapping['criteria']:
  require(c['original']==original[c['id']].strip(),'original wording')
  require(c['platforms']==list(PLATFORMS),'criterion platforms')
 inventories={}
 for lane in LANES:
  descriptor=mapping['lanes'][lane]
  laneSpec=importlib.util.spec_from_file_location('timer_acceptance_'+lane,root/descriptor['verifier']);reader=importlib.util.module_from_spec(laneSpec);laneSpec.loader.exec_module(reader)
  inventory=load((root/descriptor['caseInventory']).read_bytes());ids=[c['id'] for c in inventory['cases']]
  require(len(ids)==len(set(ids)),'inventory duplicate cases');inventories[lane]=set(ids)
  primary={x for c in mapping['criteria'] for x in c['nativeLanes'].get(lane,[])}
  supplemental=set(mapping['supplementalCases'].get(lane,[]))
  require(not primary&supplemental and primary|supplemental==set(ids),'complete case classification')
  for platform in PLATFORMS:
   require((platform,lane) in reports,'native report missing');report=reports[(platform,lane)]
   require(report['schema']==descriptor['reportSchema'] and report['family']==platform,'native report identity')
   require(report['task']==inventory['task'],'native task identity')
   require(report['verifierSha256']==sha(root/descriptor['verifier']),'verifier bytes')
   require(report['baseVerifierSha256']==sha(root/'implementations/csharp/tests/fixtures/DiscoveryHost/verify-flush-results.py'),'base verifier bytes')
   require(report['caseMappingSha256']==sha(root/descriptor['caseInventory']),'case inventory bytes')
   require(report['coreSha256']==sha(host/'CIS.Core.dll') and report['fixtureSha256']==sha(host/'CIS.Core.DiscoveryFixture.dll'),'assembly bytes')
   cases=report['cases'];actual=[c['id'] for c in cases]
   require(len(actual)==len(set(actual)),'duplicate native cases')
   require(set(actual)==set(ids) and report['children']==len(ids),'native case membership')
   for case in cases:
    require(case['exitCode']==0 and case['report']['pid']==case['pid'] and case['pid']>0,'native child failed')
    folder=native/(platform+'-'+lane)
    stdout=(folder/(case['id']+'.stdout.bin')).read_bytes();stderr=(folder/(case['id']+'.stderr.bin')).read_bytes()
    require(digest(stdout)==case['stdoutSha256'] and load(stdout)==case['report'],'original stdout bytes')
    require(stderr==b'' and digest(stderr)==case['stderrSha256'],'original stderr bytes')
    require((case['report']['os'],case['report']['runtime'],case['report']['coreSha256'],case['report']['fixtureSha256'])==(platform,'.NET 10.0.12',report['coreSha256'],report['fixtureSha256']),'native child binding')
    reader.check(case['id'],load(stdout),case['pid'])
   expectedControls=reader.corruption_controls(cases)
   require(bool(expectedControls) and all(c['rejected'] is True for c in expectedControls) and report.get('controls')==expectedControls,'complete lane controls')
 for c in mapping['criteria']:
  for lane,ids in c['nativeLanes'].items():require(lane in inventories and set(ids)<=inventories[lane],'criterion case binding')
 contract=next(c for c in mapping['criteria'] if c['id']=='WP05-S09-AC03').get('documentedContract')
 require(contract is not None and contract['path']=='implementations/csharp/src/CIS.Core/Logging/LoggingOptions.cs' and contract['property']=='FlushIntervalMilliseconds' and contract['remarksPhrase']=='scheduling target, not a completion or durability guarantee','scheduling contract binding')
 source=(root/contract['path']).read_text(encoding='utf-8-sig');remarks=re.search(r'/// <remarks>(?P<body>(?:(?!/// </remarks>).)*)/// </remarks>\s*public int FlushIntervalMilliseconds\b',source,re.S)
 require(remarks is not None and contract['remarksPhrase'] in remarks.group('body'),'documented scheduling contract')

def controls(root,native,host,mapping,reports):
 result=[];key=('windows','interval')
 edits=[
 ('missing-report','native report missing',lambda d:d.pop(key)),
 ('missing-case','native case membership',lambda d:d[key]['cases'].pop(0)),
 ('duplicate-case','duplicate native cases',lambda d:d[key]['cases'].append(copy.deepcopy(d[key]['cases'][0]))),
 ('failed','native child failed',lambda d:d[key]['cases'][0].update(exitCode=1)),
 ('assembly','assembly bytes',lambda d:d[key].update(coreSha256='0'*64)),
 ('task','native task identity',lambda d:d[key].update(task='WP05-S10-T01')),
 ('base','base verifier bytes',lambda d:d[key].update(baseVerifierSha256='0'*64)),
 ('controls','complete lane controls',lambda d:d[key].update(controls=[])),
 ('verifier','verifier bytes',lambda d:d[key].update(verifierSha256='0'*64)),
 ('inventory','case inventory bytes',lambda d:d[key].update(caseMappingSha256='0'*64)),
 ('platform','native report identity',lambda d:d[key].update(family='linux')),
 ('stdout','original stdout bytes',lambda d:d[key]['cases'][0].update(stdoutSha256='0'*64))]
 for identity,reason,edit in edits:
  bad=copy.deepcopy(reports);edit(bad)
  try:check(root,native,host,mapping,bad)
  except AssertionError as error:
   require(str(error)==reason,'unexpected acceptance corruption '+identity+': '+str(error));result.append(dict(id=identity,rejected=True,reason=reason))
  else:raise AssertionError('corrupt acceptance evidence accepted '+identity)
 # Mutate only an owned temporary copy; original evidence bytes are never changed.
 with tempfile.TemporaryDirectory(prefix='cislib-timer-acceptance-control-') as temp:
  snapshot=Path(temp)/'native';shutil.copytree(native,snapshot)
  bad=copy.deepcopy(reports);case=bad[('windows','qualification')]['cases'][0]
  require(case['id']=='stopped-rearm','semantic control case')
  case['report']['sameDisposal']=False
  raw=(json.dumps(case['report'],separators=(',',':'))+'\n').encode('utf-8')
  (snapshot/'windows-qualification/stopped-rearm.stdout.bin').write_bytes(raw);case['stdoutSha256']=digest(raw)
  try:check(root,snapshot,host,mapping,bad)
  except AssertionError as error:
   require(str(error)=='original task identities','semantic control reason');result.append(dict(id='semantic-consistent-bytes',rejected=True,reason=str(error)))
  else:raise AssertionError('semantically corrupt consistent bytes accepted')
 return result

def main():
 p=argparse.ArgumentParser()
 for k in ('native','host','output'):p.add_argument('--'+k,required=True)
 a=p.parse_args();root=Path(__file__).resolve().parents[5];native=Path(a.native).resolve();host=Path(a.host).resolve().parent
 path=root/'conformance/logging/file-timer-qualification-map.json';mapping=load(path.read_bytes())
 reports={(platform,lane):load((native/(platform+'-'+lane)/'report.json').read_bytes()) for platform in PLATFORMS for lane in LANES}
 check(root,native,host,mapping,reports);rejected=controls(root,native,host,mapping,reports)
 out=Path(a.output);out.parent.mkdir(parents=True,exist_ok=True)
 report=dict(schema='cis.logging.file-timer-acceptance-execution/v1',task='WP05-S09-T04',sourceMapSha256=sha(path),sourcePlanSha256=sha(root/'planning/logging/sources/core-library-implementation-plan-v1.md'),verifierSha256=sha(__file__),coreSha256=sha(host/'CIS.Core.dll'),fixtureSha256=sha(host/'CIS.Core.DiscoveryFixture.dll'),criteria=[dict(id=c['id'],original=c['original'],bindings=c['nativeLanes'],platforms=c['platforms'],status='Executed native case bindings',documentedContract=c.get('documentedContract'),documentedContractStatus='Documented scheduling-target and guarantee limits checked separately from execution' if c.get('documentedContract') else None) for c in mapping['criteria']],supplemental=mapping['supplementalCases'],rejectionControls=rejected,scope=mapping['scope'])
 out.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8',newline='\n')
 print(json.dumps(dict(criteria=4,platforms=2,classifiedNativeChildren=sum(r['children'] for r in reports.values()),evidenceRejectionControls=len(rejected),passed=True)))
if __name__=='__main__':main()
