from pathlib import Path
import json,shutil,hashlib,xml.etree.ElementTree as E,subprocess
r=Path.cwd();t=r/'.artifacts/csharp/tasks/wp07-s01-t04';e=r/'planning/logging/evidence/wp07-s01-t04';d=r/'.artifacts/csharp/verification/Developer';c=t/'coverage-qualified'
load=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
def write(path,data):path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes((json.dumps(data,indent=2)+'\n').encode())
assert not e.exists(),'Use a fresh immutable evidence root'
developer=load(d/'summary.json');coverage=load(c/'coverage.json');controls=load(t/'summary-controls.json')
assert developer['exitCode']==0 and developer['finishedUtc'] and all(x['status']=='Passed' for x in developer['stages'])
assert (t/'developer-pass.exit').read_text(encoding='utf-8-sig').strip()=='0'
assert (t/'coverage-qualified.exit').read_text(encoding='utf-8-sig').strip()=='2'
assert coverage['qualified'] is False and len(coverage['assemblies'])==2
assert all(x['passed'] for x in controls['controls'])
baseline=load(t/'baseline.json')
for name,expected in baseline['protected'].items():assert sha(r/name)==expected,name
code=load(t/'claude-code-manifest.json')
runinputs={x['path']:x['sha256'] for x in load(d/'source-inputs.json')}
for f in code['files']:
 if f['path']=='implementations/csharp/eng/verify-shipping-coverage.ps1':
  old=(t/'review-inputs'/f['path']).read_text(encoding='utf-8-sig')
  current=(r/f['path']).read_text(encoding='utf-8-sig')
  start=old.index('[ordered]@{ os = ');end=old.index('$previousReuse',start);block=old[start:end]
  expected=old[:start]+old[end:]
  anchor="    if ((& dotnet --version).Trim() -ne $toolchain.sdk) { throw 'Selected SDK differs from the pin.' }\n"
  expected=expected.replace(anchor,anchor+''.join('    '+line if line.strip() else line for line in block.splitlines(keepends=True)))
  assert current==expected and sha(t/'review-inputs'/f['path'])==f['sha256']==runinputs[f['path']]
 else:assert sha(r/f['path'])==f['sha256']==runinputs[f['path']],f['path']
# Keep raw native documents and original attachment paths, not reserialized reports.
for family in ['unit','integration','architecture','business']:
 for path in (c/family).rglob('*'):
  if path.is_file() and (path.name=='report.trx' or path.name.endswith('.cobertura.xml')):
   dest=e/'native/Coverage'/path.relative_to(c);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,dest)
 for path in [d/f'families/{family}/report.trx',d/f'families/{family}/discovery.json',d/f'families/{family}/execution.json']:
  dest=e/'native/Developer'/path.relative_to(d);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,dest)
for name in ['summary.json','source-inputs.json','validation.json']:
 dest=e/'native/Developer'/name;shutil.copyfile(d/name,dest)
for name in ['coverage.json','inputs.json','candidate.json','environment.json','commands.json','unit.log','integration.log','architecture.log','business.log']:
 dest=e/'native/Coverage'/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(c/name,dest)
for report in coverage['reports']:
 for name in ['trx','cobertura']:
  source=Path(report[name]);relative=source.relative_to(c);dest=e/'native/Coverage'/relative
  assert sha(dest)==report[name+'Sha256']
# Every report's test assembly must still be the pinned input used by this collection.
collection_inputs={x['path']:x['sha256'] for x in load(c/'inputs.json')}
for family,suffix in [('unit','UnitTests'),('integration','IntegrationTests'),('architecture','ArchitectureTests'),('business','BusinessCases')]:
 binary=f'tests/CIS.Core.{suffix}/bin/Release/net10.0/CIS.Core.{suffix}.dll'
 assert sha(r/'implementations/csharp'/binary)==collection_inputs[binary]
 trx=E.parse(c/family/'report.trx');ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 for m in trx.findall('.//t:TestMethod',ns):assert Path(m.get('codeBase')).resolve()==(r/'implementations/csharp'/binary).resolve()
# Historical unsuccessful collection is evidence of diagnosis, never qualifying native evidence.
for name in ['developer-stalled-summary.json','developer-second-stalled-summary.json','developer-wrapper-stalled-summary.json','developer-one-node-stalled-summary.json','build-solution-no-servers.log','build-architecture-serial.log','windows-architecture-final.log']:
 source=t/name
 if source.exists():dest=e/'prior'/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
for name in ['report.trx']:
 source=t/'windows-architecture-final'/name;dest=e/'prior/stale-package'/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
for name in ['environment.json','commands.json','coverage.json']:
 source=t/'coverage-final'/name;dest=e/'prior/caller-sdk-metadata'/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
for name in ['baseline.json','summary-controls.json','check-summary-controls.py','claude-code-manifest.json','claude-transfer-status.json','check-protected.py','freeze-evidence.py','dotnet-info.txt','graph-before.txt','graph-start.txt']:
 source=t/name;dest=e/'task'/name;dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,dest)
for f in code['files']:
 dest=e/'inputs'/f['path'];dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(r/f['path'],dest)
total=sum(v['testsPassed'] for v in coverage['reports'])
assert total==sum(load(d/f'families/{family}/execution.json')['passed'] for family in ['unit','integration','architecture','business'])==5304
release=load(r/'.artifacts/csharp/verification/Release/summary.json');assert release['exitCode']==2 and release['finishedUtc']
release_dest=e/'pending/Release/summary.json';release_dest.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(r/'.artifacts/csharp/verification/Release/summary.json',release_dest)
receipt={'task':'WP07-S01-T04','baseCommit':baseline['head'],'scope':'Windows native Microsoft coverage over all four currently active families; measurement complete, release thresholds and full Ubuntu coverage pending.','developer':{'runId':developer['runId'],'exitCode':0,'stagesPassed':len(developer['stages']),'testsPassed':total},'coverage':{'exitCode':2,'report':'native/Coverage/coverage.json','qualified':False,'assemblies':[{k:v for k,v in a.items() if k!='sources'} for a in coverage['assemblies']],'aggregate':coverage['aggregate'],'sourceFiles':sum(len(a['sources']) for a in coverage['assemblies']),'nonExecutableFiles':sum(bool(s['nonExecutableReason']) for a in coverage['assemblies'] for s in a['sources'])},'release':{'exitCode':2,'summary':'pending/Release/summary.json','issues':len(release['issues'])},'localControls':{'passed':controls['passed'],'report':'task/summary-controls.json'},'protectedInputs':{'count':len(baseline['protected']),'unchanged':True},'hostBuildSettings':{'MaxBuildNodes':1,'MSBUILDDISABLENODEREUSE':'1','UseSharedCompilation':'false','DOTNET_CLI_USE_MSBUILD_SERVER':'0'},'verificationSourceNote':'Between the Developer run and final coverage, only the coverage runner SDK metadata block moved into the pinned language directory; the exact relocation is asserted by this generator. All shipping and test sources/binaries remain pinned.', 'review':load(t/'claude-transfer-status.json'),'remaining':['Helper coverage below required per-assembly threshold; retain every uncovered source/branch.','Full Ubuntu coverage and six-family release discovery/qualification.','Claude review unavailable after automatic rejection; no source transfer performed or independent review claimed.'],'evidenceGenerator':{'path':'task/freeze-evidence.py','sha256':sha(t/'freeze-evidence.py')}}
write(e/'verification.json',receipt)
write(e/'files.json',{'files':[{'path':p.relative_to(e).as_posix(),'bytes':p.stat().st_size,'sha256':sha(p)} for p in sorted(e.rglob('*')) if p.is_file()]})
print(json.dumps({'files':len(load(e/'files.json')['files']),'developerTests':total,'coverage':receipt['coverage']},indent=2))
