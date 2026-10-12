import datetime,hashlib,importlib.util,json,re,shutil,subprocess,sys
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path('C:/miscwork/portfolio/cislib');TASK=ROOT/'.artifacts/csharp/tasks/wp04-s04-t03';OUT=ROOT/'planning/logging/evidence/wp04-s04-t03';RECEIPT=ROOT/'planning/logging/evidence/wp04-s04-t03-windows-linux.json'
BASE='3761d664673205dd8e8c675d8f206a130886ffa9'
COUNTS={'unit':2689,'integration':440,'architecture':398}
METADATA=['docs/modules/logging/event-correlation.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
SHIP=['implementations/csharp/src/CIS.Core/Logging/Events/'+name for name in ['CapturedEventFacts.cs','EventProjection.cs']]
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def require(c,m):
    if not c: raise AssertionError(m)
def read(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def copy(source,target):
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists(): require(source.read_bytes()==target.read_bytes(),'Retained bytes changed: '+str(target))
    else: shutil.copyfile(source,target)
def tree(source,target,suffixes=None):
    require(source.is_dir(),'Missing tree '+str(source))
    for p in sorted(source.rglob('*')):
        if p.is_file() and not {'bin','obj'}.intersection(p.relative_to(source).parts) and (suffixes is None or p.suffix in suffixes): copy(p,target/p.relative_to(source))
def manifest(): return [{'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(OUT.rglob('*')) if p.is_file()]
def git(*args): return subprocess.run(['git',*args],cwd=ROOT,capture_output=True,check=True).stdout

def native(root):
    folder=root/'.artifacts/csharp/verification/Developer';s=read(folder/'summary.json')
    require(s['schema']=='cis.verification/v1' and s['profile']=='Developer' and s['lane']=='windows-x64' and s['exitCode']==0 and s['finishedUtc'],'Developer run incomplete')
    require(sha(folder/'source-inputs.json')==s['source']['inputInventory']['sha256'],'Inventory identity differs')
    for i in read(folder/'source-inputs.json'): require(sha(root/i['path'])==i['sha256'],'Executed source changed: '+i['path'])
    groups=[g for g in read(root/'conformance/logging/case-inventory.json')['cases'] if '-EVENT-T03-' in g['id']]
    require(len(groups)==10 and sum(g['minimumExecutions'] for g in groups)==54,'Task binding denominator')
    require({f:sum(g['minimumExecutions'] for g in groups if g['family']==f) for f in COUNTS}=={'unit':40,'integration':14,'architecture':0},'Task split differs')
    counts={}
    for family,total in COUNTS.items():
        c=ET.parse(folder/f'families/{family}/report.trx').find('t:ResultSummary/t:Counters',NS).attrib
        counts[family]={k:int(c[k]) for k in ['total','passed','failed','notExecuted']}
        require(counts[family]==dict(total=total,passed=total,failed=0,notExecuted=0),'Native counts differ: '+family)
        discovery={i['id']:i['count'] for i in read(folder/f'families/{family}/discovery.json')['cases']}
        for g in groups:
            if g['family']==family: require(discovery[g['id']]==g['minimumExecutions'],'Group not executed '+g['id'])
    for name,count in [('startup',21),('compatibility-samples',22),('log-value-consumers',12)]:
        c=read(folder/f'contracts/{name}/summary.json');require(c['exitCode']==0 and len(c['controls'])==count and all(i['matched'] for i in c['controls']),'Compiler controls differ')
    require(read(folder/'validation.json')['exitCode']==0,'Developer validation failed')
    if root==ROOT: require(read(folder/'validation.json')==read(TASK/'check-developer.stdout.json'),'Developer command binding differs')
    return counts

def processes():
    driver=ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/verify-event-correlation.py'
    spec=importlib.util.spec_from_file_location('correlation_driver',driver);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
    m.CATALOG=read(ROOT/'conformance/logging/event-correlation-cases.json');m.ROWS={r['id']:r for r in m.CATALOG['cases']}
    require(m.CATALOG['caseCount']==len(m.ROWS)==14 and len(m.controls())==8,'Comparator denominator differs')
    host=TASK/'reviewed-process-inputs/host';pair=(sha(host/'CIS.Core.DiscoveryFixture.dll'),sha(host/'CIS.Core.dll'))
    for family in ['windows','linux']:
        folder=TASK/f'reviewed-process-{family}';s=read(folder/'summary.json')
        require(read(TASK/f'reviewed-process-{family}.stdout.json')==s,'Process stdout differs')
        require(s['schema']=='cis.event-correlation-process/v1' and s['family']==family and s['exitCode']==0 and s['status']=='Passed' and s['parentEnvironmentAndDirectoryUnchanged'],'Process verification failed')
        require(s['runtimeVersion']=='10.0.12' and s['rollForward']=='Disable' and s['driverSha256']==sha(driver) and s['inputsSha256']==sha(ROOT/'conformance/logging/event-correlation-cases.json'),'Driver identity differs')
        require(s['comparatorControls']==m.controls() and [i['id'] for i in s['cases']]==list(m.ROWS),'Controls/children differ')
        for i in s['cases']:
            a=read(folder/(i['id']+'.stdout.json'));m.verify(i['id'],a);o=a['observed']
            require(i['status']=='Passed' and i['childExitCode']==0 and o['publicFacadeInitialized'] is False and o['runtimeDescription']=='.NET 10.0.12' and o['operatingSystem']==family,'Child identity differs')
            require((o['fixtureSha256'],o['coreSha256'])==pair and (i['fixtureSha256'],i['coreSha256'])==pair and o['inputsSha256']==s['inputsSha256'],'Managed inputs differ')
        require(len(s['reports'])==28 and {p.name for p in folder.iterdir()}=={'summary.json'}|{i['path'] for i in s['reports']},'Process files differ')
        for i in s['reports']: require(sha(folder/i['path'])==i['sha256'] and (folder/i['path']).stat().st_size==i['bytes'],'Process report changed')
    require(pair==(sha(ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll'),sha(ROOT/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll')),'Process/native artifacts differ')
    os_release=(TASK/'ubuntu-os-release.txt').read_text(encoding='utf-8-sig');require('ID=ubuntu' in os_release and 'VERSION_ID="24.04"' in os_release,'Ubuntu identity differs')
    return dict(childrenPerPlatform=14,runtimeVersion='10.0.12',rollForward='Disable',fixtureSha256=pair[0],coreSha256=pair[1],comparatorControlsPerPlatform=8)

phase=sys.argv[1];require(phase in ['direct','fresh'],'Unknown phase')
if phase=='fresh':
    for n in ['retain-evidence.py','finalize-metadata.py','audit-commit.py']: require(sha(TASK/n)==sha(OUT/'commands'/n),'Frozen helper changed')
    require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Running retention helper differs')
counts=native(ROOT);identity=processes()
release=ROOT/'.artifacts/csharp/verification/Release';s=read(release/'summary.json')
require(s['profile']=='Release' and s['exitCode']==2 and s['finishedUtc'],'Release gate not complete')
require(sha(release/'source-inputs.json')==s['source']['inputInventory']['sha256'],'Release inventory identity differs')
for i in read(release/'source-inputs.json'): require(sha(ROOT/i['path'])==i['sha256'],'Release source changed')
require(read(release/'validation.json')==read(TASK/'check-release.stdout.json'),'Release command binding differs')
require(read(TASK/'verification-exits.json')==dict(developer=0,checkDeveloper=0,release=2,checkRelease=2),'Gate exits differ')
require(read(TASK/'reviewed-process-exits.json')==dict(windows=0,linux=0,ubuntuIdentity=0),'Process invocation exits differ')
for n in ['format','focused-unit','focused-integration']: require((TASK/(n+'.exit.txt')).read_text().strip()=='0','Focused/format failed')
for n in ['claude-source','claude-evidence']:
    r=read(TASK/(n+'.json'));require((TASK/(n+'.exit.txt')).read_text().strip()=='0' and not r.get('is_error') and re.match(r'^(?:\*\*)?PASS\b',r['result'].lstrip()),'Review not completed/pass '+n)
if phase=='direct':
    for profile in ['Developer','Release']: tree(ROOT/f'.artifacts/csharp/verification/{profile}',OUT/profile.lower(),{'.json','.log','.trx'})
    for n in ['focused','focused-initial','activity-probe','reviewed-process-windows','reviewed-process-linux','reviewed-process-inputs']: tree(TASK/n,OUT/n)
    for p in sorted(TASK.iterdir()):
        if p.is_file() and p.suffix in ['.json','.log','.txt','.py'] and not p.name.startswith(('implement-','prepare-','fresh-copy')): copy(p,OUT/'commands'/p.name)
    for name in ['CIS.Core.dll']:
        copy(ROOT/'implementations/csharp/src/CIS.Core/bin/Release/net10.0'/name,OUT/'development-artifacts'/name)
    copy(ROOT/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg',OUT/'development-artifacts/CIS.Core.0.1.0-dev.1.nupkg')
    changed=git('diff','--name-only','-z',BASE)+git('ls-files','--others','--exclude-standard','-z')
    for p in sorted({p.decode('utf-8') for p in changed.split(b'\0') if p}):
        if not p.startswith('planning/logging/evidence/wp04-s04-t03'): copy(ROOT/p,OUT/'reviewed-source'/p)
    print(json.dumps(dict(phase=phase,retainedFiles=len(manifest()),native=counts,processes=identity)));raise SystemExit(0)

replay=read(ROOT/'.artifacts/csharp/replay/latest.json');require(replay['schema']=='cis.replay/v1' and replay['profile']=='Developer' and replay['lane']=='windows-x64' and replay['exitCode']==0 and replay['inputVerifiedBefore'] and replay['inputVerifiedAfter'],'Copied-source verification failed')
require((TASK/'fresh-copy.exit.txt').read_text().strip()=='0','Replay invocation failed')
run=Path(replay['runRoot']);inputs=read(run/'input-inventory.json');require(native(run/'work')==counts,'Copied-source native counts differ')
inventories=[read(ROOT/'.artifacts/csharp/verification/Developer/source-inputs.json'),read(run/'work/.artifacts/csharp/verification/Developer/source-inputs.json'),read(release/'source-inputs.json'),inputs]
direct={i['path']:i for i in inventories[0]};fresh={i['path']:i for i in inventories[1]}
require(inventories[0]==inputs and set(fresh)<=set(direct) and all(direct[p]==i for p,i in fresh.items()),'Inventory reconciliation differs')
omitted=sorted(set(direct)-set(fresh));require(len(omitted)==3101 and all(p.startswith('planning/logging/evidence/') and not p.startswith('planning/logging/evidence/wp04-s04-t03') and {'bin','obj'}.intersection(Path(p).parts) for p in omitted),'Unexpected native inventory omission')
for inventory in inventories:
    require(len({i['path'] for i in inventory})==len(inventory),'Duplicate inventory path')
    for i in inventory:
        for root in [ROOT,run/'input',run/'work']: require(sha(root/i['path'])==i['sha256'],'Executed source changed: '+i['path'])
difference=dict(directAndReplayEntries=len(inputs),freshNativeEntries=len(fresh),commonRowsExactlyMatch=True,freshAddsNoUncapturedSources=True,omittedFromFreshNativeInventory=[direct[p] for p in omitted],reason='Git-free native inventory excludes historical bin/obj segments; every omitted archived input remains physically copied and hashed in main/input/work. No current source/test/contract/conformance file is omitted.')
p=OUT/'fresh-copy/native-inventory-difference.json';require(not p.exists(),'Reconciliation already exists');p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(difference,indent=2)+'\n',encoding='utf-8',newline='\n')
tree(run/'work/.artifacts/csharp/verification/Developer',OUT/'fresh-copy-developer',{'.json','.log','.trx'})
copy(run/'work/implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll',OUT/'fresh-copy-artifacts/CIS.Core.dll')
copy(run/'work/.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg',OUT/'fresh-copy-artifacts/CIS.Core.0.1.0-dev.1.nupkg')
for n in ['summary.json','input-inventory.json','verification.log']: copy(run/n,OUT/'fresh-copy'/n)
for n in ['fresh-copy.stdout.log','fresh-copy.stderr.log','fresh-copy.exit.txt']: copy(TASK/n,OUT/'commands'/n)
for paths in [['planning/logging/sources'],['contracts/logging'],['planning/logging/evidence',':(exclude)planning/logging/evidence/wp04-s04-t03*']]: require(not git('diff','--name-only',BASE,'--',*paths),'Preserved source/history changed')
shipping=git('diff','--name-only',BASE,'--','implementations/csharp/src/CIS.Core').decode('utf-8').splitlines();require(set(shipping)==set(SHIP),'Unexpected shipping change')
record=dict(schemaVersion=1,taskId='WP04-S04-T03',recordedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),checkpointCommit=BASE,taskChangesCommitted=False,
 originalTaskDescription='Capture valid W3C Activity.Current IDs and UTC time before admission; omit both IDs for absent/invalid/non-W3C activity and preserve the separate operation ID.',
 outcome='Actual owned UTC instant and validated paired current W3C IDs before cooperative payload callbacks. Independent source observations bind generated CLR current spans; operation ID remains separate.',
 nativeChecks=dict(newUnitCases=40,newIntegrationCases=14,newArchitectureCases=0,newCases=54,boundGroups=10,unit=2689,integration=440,architecture=398,total=3527,failed=0,skipped=0),compilerControls=dict(startup=21,compatibilitySamples=22,actualCoreValues=12,total=55,allMatched=True),
 standaloneProcesses=identity,ubuntuDistribution=dict(report='planning/logging/evidence/wp04-s04-t03/commands/ubuntu-os-release.txt',identityChecked='ID=ubuntu;VERSION_ID=24.04'),
 externalClaudeReview=dict(reviewCompleted=True,outsideSandbox=True,readOnly=True,reviews=['claude-source.json','claude-evidence.json'],sourceRequiredFindings=[],resolvedFindings=[],finalVerdict=read(TASK/'claude-evidence.json')['result'],actionableFindingsResolved=True,productApproval=False),
 verificationExits=dict(directDeveloper=0,checkDeveloper=0,releaseVerify=2,checkRelease=2,freshCopyDeveloper=0,windowsDriver=0,ubuntuDriver=0,ubuntuIdentity=0),activeEvidenceHelpers=['retain-evidence.py','finalize-metadata.py','audit-commit.py'],
 nativeInventoryReconciliation='planning/logging/evidence/wp04-s04-t03/fresh-copy/native-inventory-difference.json',freshCopyVerification=replay,canonicalSourceInventory='planning/logging/evidence/wp04-s04-t03/fresh-copy/input-inventory.json',verifiedCanonicalInputCount=len(inputs),
 sourceIdentity='All four executed inventories are verified against main/input/work. Direct and replay inventories match. Fresh native common rows match exactly with 3101 explicitly retained historical bin/obj omissions; every omitted row is also hashed in every root. Path-independent binary reproducibility is not claimed.',postCaptureInventoriedEdits=[],changedShippingFiles=[dict(path=p,sha256=sha(ROOT/p)) for p in sorted(SHIP)],
 taskComplete=True,storyComplete=False,storyCompletionScope='T03 complete for owned time/current W3C facts before future admission. T04 bounded escaped encoder and T05 full structural/wire qualification remain required.',
 limits=['Development facts observer is not a shipping wire encoder or queue.','A supplied public CLR parent span is not its generated current span; original reserved vector uses an explicit native binding, full wire qualification remains T05.','Eight private CLR field-fault controls are native pinned-runtime tests, not portable or public-API cases.','Time/IDs are sampled sequentially; no atomic concurrent caller snapshot is promised.','Original creator-disposes contract and its detection limits are preserved.','Windows full native/copy verification and Ubuntu child processes execute; full Linux SDK/release qualification remains pending.','CIS graph coverage remains partial and does not approve products.'],reports=manifest())
require(not RECEIPT.exists(),'Receipt exists');RECEIPT.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n');print(json.dumps(dict(phase=phase,retainedFiles=len(record['reports']),capturedInputs=len(inputs),taskComplete=True,storyComplete=False)))
