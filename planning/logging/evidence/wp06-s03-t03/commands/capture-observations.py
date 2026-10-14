from pathlib import Path
import json, hashlib, shutil, re, collections, xml.etree.ElementTree as ET
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp06-s03-t03';e=r/'planning/logging/evidence/wp06-s03-t03'
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def write(p,v):
 p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(v,indent=2)+'\n',encoding='utf-8')
def copy(src,dst):
 dst.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(src,dst)
paths=read(r/'implementations/csharp/verification/shipping-projects.json')['shippingProjects'][0]['moduleSlices'][0]['sources'];names={Path(p).name for p in paths}
coverage={};ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for family,expected in [('unit',65),('integration',79)]:
 root=t/f'coverage-current/{family}';trx=root/'report.trx';xml=next(root.rglob('*.cobertura.xml'));tree=ET.parse(xml)
 rows=ET.parse(trx).findall('.//t:UnitTestResult',ns);assert len(rows)==expected and all(n.get('outcome')=='Passed' for n in rows)
 selected=[c for c in tree.findall('.//class') if '/Logging/MicrosoftLogger/' in c.get('filename','').replace('\\','/')]
 seen={};branches={};files={}
 for c in selected:
  name=Path(c.get('filename').replace('\\','/')).name;assert name in names
  for line in c.findall('./lines/line'):
   key=(name,int(line.get('number')));seen[key]=max(seen.get(key,0),int(line.get('hits')))
   if line.get('branch','').lower()=='true':
    covered,total=map(int,re.search(r'\((\d+)/(\d+)\)',line.get('condition-coverage')).groups())
    if key in branches:assert branches[key][1]==total
    branches[key]=(max(branches.get(key,(0,total))[0],covered),total)
 assert {k[0] for k in seen}==names
 for name in sorted(names):
  hits=[v for k,v in seen.items() if k[0]==name];b=[v for k,v in branches.items() if k[0]==name]
  files[name]=dict(linesCovered=sum(v>0 for v in hits),linesTotal=len(hits),branchesCovered=sum(v[0] for v in b),branchesTotal=sum(v[1] for v in b))
 copy(trx,e/f'coverage/{family}/report.trx');copy(xml,e/f'coverage/{family}/{xml.relative_to(root).as_posix()}')
 coverage[family]=dict(tests=expected,trxSha256=sha(trx),coberturaSha256=sha(xml),files=files,linesCovered=sum(v>0 for v in seen.values()),linesTotal=len(seen),branchesCovered=sum(v[0] for v in branches.values()),branchesTotal=sum(v[1] for v in branches.values()),scope='Exact adapter source paths, duplicate source lines deduplicated within this report. Lanes are not combined.')
 assert (coverage[family]['linesCovered'],coverage[family]['linesTotal'])==(139,141)
 assert coverage[family]['branchesCovered']==(91 if family=='unit' else 89) and coverage[family]['branchesTotal']==100
write(e/'coverage-audit.json',coverage)
mutation={}
for label in ['current','final']:
 root=t/f'mutation-{label}';raw=root/'mutation/reports/mutation-report.json';j=read(raw)
 selected={k:v for k,v in j['files'].items() if '/Logging/MicrosoftLogger/' in k.replace('\\','/')};assert {Path(k.replace('\\','/')).name for k in selected}==names
 counts=collections.Counter(m['status'] for f in selected.values() for m in f['mutants']);denominator=sum(counts[s] for s in ['Killed','Survived','NoCoverage','Timeout'])
 assert denominator==130 and counts['CompileError']==23 and counts['Ignored']==37 and counts['Timeout']==0
 write(e/f'mutation/{label}/adapter-report.json',dict(schema='cis.logging.adapter-mutation-extract/v1',derivedAdapterOnly=True,nativeReportSha256=sha(raw),nativeReportLocalPath=str(raw.relative_to(r)),nativeSchemaVersion=j['schemaVersion'],files=selected,thresholds=j['thresholds'],scope='Exact native adapter file objects; other Core files and testFiles omitted. Not a complete native report.'))
 commands=read(root/'commands.json');native=next(c for c in commands if c['name']=='mutation');assert native['exitCode']==2
 copy(root/'commands.json',e/f'mutation/{label}/commands.json');copy(root/'logs/mutation.log',e/f'mutation/{label}/mutation.log');copy(root/'adapter-source-inputs.json',e/f'mutation/{label}/adapter-source-inputs.json')
 mutation[label]=dict(status='FailedThreshold',nativeExit=2,runnerExit=int((t/f'mutation-{label}.exit-code').read_text()),counts=dict(counts),testableDenominator=denominator,killed=counts['Killed'],scorePercent=100*counts['Killed']/denominator,breakThreshold=90,nativeReportSha256=sha(raw),nativeReportRetainedLocally=str(raw.relative_to(r)),noncompilingAndIgnoredReceiveNoKillCredit=True,childMutantInjectionClaimed=False)
 if label=='final':
  assert next(c for c in commands if c['name']=='unmutated-rebuild')['exitCode']==0
  snapshot=read(root/'snapshot-inputs.json');assert all(sha(root/'source'/f['path'])==f['sha256'] for f in snapshot)
  write(e/'mutation/final/snapshot-inputs.json',snapshot)
  copy(root/'logs/unmutated-rebuild.log',e/'mutation/final/unmutated-rebuild.log')
  mutation[label]['snapshotInputsCheckedUnchanged']=len(snapshot);mutation[label]['unmutatedRebuildExit']=0
assert mutation['current']['killed']==78 and mutation['final']['killed']==72
write(e/'mutation-audit.json',mutation)
for f in read(t/'baseline.json')['protected']:assert sha(r/f['path'])==f['sha256'],f['path']
copy(t/'baseline.json',e/'baseline.json');copy(t/'coverage-current/commands.json',e/'coverage/commands.json');copy(t/'coverage-current/adapter-source-inputs.json',e/'coverage/adapter-source-inputs.json')
for p in t.glob('graph-*-before.txt'):copy(p,e/'graph'/p.name)
write(e/'receipt.json',dict(schema='cis.logging.task-receipt/v1',taskId='WP06-S03-T03',date='2026-10-15',baseCommit=read(t/'baseline.json')['head'],status='Wiring implemented; native adapter observations retained; final Developer verification and Claude review in progress.',scope='Original T03 explicitly defers complete full matrix execution to WP07. Embedded Microsoft adapter is a slice of CIS.Core under the user packaging amendment.',coverage=coverage,mutation=mutation,shippingSourcesUnchanged=True,protectedInputs=len(read(t/'baseline.json')['protected']),releaseQualification='Pending WP07; failed mutation score is not a passing gate.',fullNativeMutationReports='Retained in ignored local task artifacts; adapter-only exact extracts are tracked, with full-report hashes. Whole Core report contains other module implementation and is excluded from Claude transfer.'))
print(json.dumps(dict(coverage=coverage,mutation=mutation,protectedUnchanged=True)))
