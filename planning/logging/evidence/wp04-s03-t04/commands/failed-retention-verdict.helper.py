import datetime, hashlib, importlib.util, json, re, shutil, subprocess, sys
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path('C:/miscwork/portfolio/cislib')
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s03-t04'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t04'
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t04-windows-linux.json'
BASE='c8b897cdd9ea3af6f1ee0bc4d6f4397c350b7c4e'
COUNTS={'unit':2504,'integration':366,'architecture':397}
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
METADATA=['docs/modules/logging/operation-scopes.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
def require(value,message):
    if not value: raise AssertionError(message)
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def copy(source,target):
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists(): require(source.read_bytes()==target.read_bytes(),'Retained bytes differ: '+str(target))
    else: shutil.copyfile(source,target)
def tree(source,target,suffixes=None):
    require(source.is_dir(),'Missing evidence directory: '+str(source))
    for path in sorted(source.rglob('*')):
        if path.is_file() and 'obj' not in path.relative_to(source).parts and 'bin' not in path.relative_to(source).parts and (suffixes is None or path.suffix in suffixes): copy(path,target/path.relative_to(source))
def native(root):
    folder=root/'.artifacts/csharp/verification/Developer'
    summary=read(folder/'summary.json')
    require(summary['schema']=='cis.verification/v1' and summary['profile']=='Developer' and summary['lane']=='windows-x64' and summary['exitCode']==0 and summary['finishedUtc'] is not None,'Final Developer run not complete')
    inv=folder/'source-inputs.json'
    require(sha(inv)==summary['source']['inputInventory']['sha256'],'Developer inventory identity differs')
    for item in read(inv): require(sha(root/item['path'])==item['sha256'],'Final Developer source differs: '+item['path'])
    if root==ROOT: require(read(folder/'validation.json')==read(TASK/'check-developer.stdout.json'),'Developer gate not bound to executed output')
    groups=[row for row in read(root/'conformance/logging/case-inventory.json')['cases'] if '-SCOPE-T04-' in row['id']]
    require(len(groups)==6 and sum(row['minimumExecutions'] for row in groups)==40,'Task binding denominator differs')
    counts={}
    for family,expected in COUNTS.items():
        values=ET.parse(folder/f'families/{family}/report.trx').find('t:ResultSummary/t:Counters',NS).attrib
        counts[family]={key:int(values[key]) for key in ['total','passed','failed','notExecuted']}
        require(counts[family]=={'total':expected,'passed':expected,'failed':0,'notExecuted':0},'Native counts differ: '+family)
        discovery={row['id']:row['count'] for row in read(folder/f'families/{family}/discovery.json')['cases']}
        for group in groups:
            if group['family']==family: require(discovery[group['id']]==group['minimumExecutions'],'Task binding not executed: '+group['id'])
    for name,count in [('startup',21),('compatibility-samples',22),('log-value-consumers',12)]:
        controls=read(folder/f'contracts/{name}/summary.json')
        require(controls['exitCode']==0 and len(controls['controls'])==count and all(row['matched'] for row in controls['controls']),'Compiler controls differ')
    require(read(folder/'summary.json')['exitCode']==read(folder/'validation.json')['exitCode']==0,'Developer validation failed')
    return counts

def processes():
    driver=ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/verify-scope-qualification.py'
    spec=importlib.util.spec_from_file_location('scope_qualification_driver',driver)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.CATALOG=read(ROOT/'conformance/logging/scope-qualification-cases.json')
    module.ROWS={row['id']:row for row in module.CATALOG['cases']}
    require(len(module.controls())==6,'Comparator controls differ')
    host=TASK/'reviewed-process-inputs/host'
    pair=(sha(host/'CIS.Core.DiscoveryFixture.dll'),sha(host/'CIS.Core.dll'))
    for family in ['windows','linux']:
        folder=TASK/f'reviewed-process-{family}';summary=read(folder/'summary.json')
        require(read(TASK/f'reviewed-process-{family}.stdout.json')==summary,'Process command output not bound to summary')
        require(summary['schema']=='cis.operation-scope-qualification-process/v1' and summary['status']=='Passed' and summary['exitCode']==0 and summary['parentEnvironmentAndDirectoryUnchanged'],'Process verification failed')
        require(summary['runtimeVersion']=='10.0.12' and summary['rollForward']=='Disable' and summary['family']==family,'Runtime selection differs')
        require(summary['driverSha256']==sha(driver) and summary['inputsSha256']==sha(ROOT/'conformance/logging/scope-qualification-cases.json') and summary['comparatorControls']==module.controls(),'Final driver identity/controls differ')
        require([row['id'] for row in summary['cases']]==list(module.ROWS),'Child denominator differs')
        for item in summary['cases']:
            actual=read(folder/(item['id']+'.stdout.json'));module.verify(item['id'],actual)
            observed=actual['observed']
            require(item['status']=='Passed' and item['childExitCode']==0,'Child failed')
            require(observed['inputsSha256']==sha(ROOT/'conformance/logging/scope-qualification-cases.json') and observed['publicFacadeInitialized'] is False and observed['runtimeDescription']=='.NET 10.0.12' and observed['operatingSystem']==family,'Runtime identity differs')
            require((observed['fixtureSha256'],observed['coreSha256'])==pair and (item['fixtureSha256'],item['coreSha256'])==pair,'Managed identities differ')
        require(len(summary['reports'])==28 and {p.name for p in folder.iterdir()}=={'summary.json'}|{item['path'] for item in summary['reports']},'Process report/file denominator differs')
        for item in summary['reports']:
            path=folder/item['path'];require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Child report bytes differ')
    require(pair[1]==sha(ROOT/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'),'Process Core differs from native build')
    require(pair[0]==sha(ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll'),'Process fixture differs from native build')
    ubuntu=(TASK/'ubuntu-os-release.txt').read_text(encoding='utf-8-sig')
    require('ID=ubuntu' in ubuntu and 'VERSION_ID="24.04"' in ubuntu,'Ubuntu distribution differs')
    return {'childrenPerPlatform':14,'runtimeVersion':'10.0.12','rollForward':'Disable','fixtureSha256':pair[0],'coreSha256':pair[1],'comparatorControlsPerPlatform':6}

def artifacts(root,target):
    copy(root/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll',OUT/target/'CIS.Core.dll')
    copy(root/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg',OUT/target/'CIS.Core.0.1.0-dev.1.nupkg')
def index():
    return [{'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(OUT.rglob('*')) if p.is_file()]

def reviews():
    for name in ['claude-review','claude-final','claude-source-final','claude-evidence','claude-evidence-final']:
        result=read(TASK/(name+'.json'))
        require((TASK/(name+'.exit.txt')).read_text().strip()=='0' and not result.get('is_error'),'Claude review did not complete')
    verdict=read(TASK/'claude-evidence-final.json')['result']
    require(re.match(r'^(?:\*\*)?PASS',verdict.lstrip()) and re.match(r'^(?:\*\*)?PASS',read(TASK/'claude-source-final.json')['result'].lstrip()),'Corrected source/helper reviews did not pass')
    return verdict

phase=sys.argv[1]
if phase=='fresh':
    for name in ['retain-evidence.py','finalize-metadata.py','audit-commit.py']: require(sha(TASK/name)==sha(OUT/'commands'/name),'Frozen helper differs: '+name)
    require(sha(Path(__file__))==sha(OUT/'commands'/Path(__file__).name),'Running retention helper differs')
counts=native(ROOT);identity=processes()
release=ROOT/'.artifacts/csharp/verification/Release'
release_summary=read(release/'summary.json')
require(release_summary['profile']=='Release' and release_summary['exitCode']==2 and release_summary['finishedUtc'] is not None,'Release gate not complete')
require(sha(release/'source-inputs.json')==release_summary['source']['inputInventory']['sha256'],'Release inventory identity differs')
for item in read(release/'source-inputs.json'): require(sha(ROOT/item['path'])==item['sha256'],'Final Release source differs: '+item['path'])
require(read(release/'validation.json')==read(TASK/'check-release.stdout.json'),'Release gate not bound to executed output')
require(read(TASK/'verification-exits.json')=={'developer':0,'checkDeveloper':0,'release':2,'checkRelease':2},'Gate exits differ')
require(read(TASK/'reviewed-process-exits.json')=={'windows':0,'linux':0,'ubuntuIdentity':0},'Executed process exits differ')
require((TASK/'focused-source-final.exit.txt').read_text().strip()=='0' and (TASK/'focused-map-source-final.exit.txt').read_text().strip()=='0' and (TASK/'format-source-final.exit.txt').read_text().strip()=='0','Focused corrected checks or formatter differ')
review_verdict=reviews()
if phase=='direct':
    tree(ROOT/'.artifacts/csharp/verification/Developer',OUT/'developer',{'.json','.log','.trx'})
    tree(ROOT/'.artifacts/csharp/verification/Release',OUT/'release',{'.json','.log','.trx'})
    artifacts(ROOT,'development-artifacts')
    for name in ['focused','reviewed-process-windows','reviewed-process-linux','reviewed-process-inputs']:
        tree(TASK/name,OUT/name)
    for name in ['pre-cancellation-correction','pre-review-correction','pre-final-correction']:
        tree(TASK/name,OUT/'superseded-runs'/name)
    copy(TASK/'failed-mutation-correction.fixture.cs',OUT/'superseded-runs/failed-mutation-correction.fixture.cs')
    commands=['developer.stdout.log','developer.stderr.log','check-developer.stdout.json','check-developer.stderr.log','release.stdout.log','release.stderr.log','check-release.stdout.json','check-release.stderr.log','verification-exits.json','reviewed-process-windows.stdout.json','reviewed-process-windows.stderr.log','reviewed-process-linux.stdout.json','reviewed-process-linux.stderr.log','reviewed-process-exits.json','ubuntu-os-release.txt','ubuntu-os-release.stderr.log','cis-build-current.agent.txt','cis-build-current.stderr.log','cis-status-current.agent.txt','cis-status-current.stderr.log','cis-docs-current.agent.txt','cis-docs-current.stderr.log','cis-current-exits.json','retain-evidence.py','finalize-metadata.py','audit-commit.py','focused-final.exit.txt','focused-final.stdout.log','focused-final.stderr.log','focused-reviewed.stdout.log','focused-reviewed.stderr.log','focused-reviewed.exit.txt','focused-diagnostic.stdout.log','focused-diagnostic.stderr.log','focused-diagnostic.exit.txt','focused-architecture-reviewed.stdout.log','focused-architecture-reviewed.stderr.log','focused-architecture-reviewed.exit.txt','format-reviewed.stdout.log','format-reviewed.stderr.log','format-reviewed.exit.txt','format-source-final.stdout.log','format-source-final.stderr.log','format-source-final.exit.txt','focused-source-final.stdout.log','focused-source-final.stderr.log','focused-source-final.exit.txt','focused-map-source-final.stdout.log','focused-map-source-final.stderr.log','focused-map-source-final.exit.txt','focused-unit.stdout.log','focused-unit.stderr.log','focused-integration.stdout.log','focused-integration.stderr.log','focused-architecture.stdout.log','focused-architecture.stderr.log','focused-architecture.exit.txt','focused-corrected.stdout.log','focused-corrected.stderr.log','focused-corrected.exit.txt','focused-exits.json','format.stdout.log','format.stderr.log','format.exit.txt','process-build.stdout.log','process-build.stderr.log','process-build.exit.txt','process-windows.stdout.json','process-windows.stderr.log','process-linux.stdout.json','process-linux.stderr.log','process-exits.json']
    for stem in ['claude-review','claude-final','claude-source-final','claude-evidence','claude-evidence-final']:
        commands += [stem+suffix for suffix in ['.json','.stderr.log','.exit.txt','.prompt.txt']]
    for name in commands: copy(TASK/name,OUT/'commands'/name)
    sources=subprocess.run(['git','diff','--name-only','-z',BASE],cwd=ROOT,capture_output=True,check=True).stdout
    sources+=subprocess.run(['git','ls-files','--others','--exclude-standard','-z'],cwd=ROOT,capture_output=True,check=True).stdout
    paths={p.decode('utf-8') for p in sources.split(b'\0') if p}
    for path in paths:
        if not path.startswith('planning/logging/evidence/wp04-s03-t04'):
            copy(ROOT/path,OUT/'reviewed-source'/path)
    print(json.dumps({'phase':'direct','retainedFiles':len(index()),'native':counts,'processes':identity}))
    raise SystemExit(0)
require(phase=='fresh','Unknown phase')
replay=read(ROOT/'.artifacts/csharp/replay/latest.json')
require(replay['schema']=='cis.replay/v1' and replay['profile']=='Developer' and replay['lane']=='windows-x64','Fresh-copy profile/lane differs')
require(replay['exitCode']==0 and replay['inputVerifiedBefore'] and replay['inputVerifiedAfter'],'Fresh-copy source verification failed')
require((TASK/'fresh-copy.exit.txt').read_text().strip()=='0','Fresh-copy invocation failed')
run=Path(replay['runRoot']);inputs=read(run/'input-inventory.json')
for item in inputs:
    for root in [ROOT,run/'input',run/'work']: require(sha(root/item['path'])==item['sha256'],'Captured input changed: '+item['path'])
require(native(run/'work')==counts,'Fresh-copy native counts differ')
tree(run/'work/.artifacts/csharp/verification/Developer',OUT/'fresh-copy-developer',{'.json','.log','.trx'})
artifacts(run/'work','fresh-copy-artifacts')
for name in ['summary.json','input-inventory.json','verification.log']: copy(run/name,OUT/'fresh-copy'/name)
for name in ['fresh-copy.stdout.log','fresh-copy.stderr.log','fresh-copy.exit.txt']: copy(TASK/name,OUT/'commands'/name)
originals={'core-library-implementation-guide-v1.md':'01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675','core-library-backlog-v1.json':'4abb87f20bc85beaa145c82714bb5d34cb923ef7b9b7b857e678839110dcd549','core-library-implementation-plan-v1.md':'41f1568583c9a04c783dd6db5fe80cd24ba8a6142a78983f910c4973e72bf7a5'}
for name,expected in originals.items(): require(sha(ROOT/'planning/logging/sources'/name)==expected,'Original supplied source changed')
require(subprocess.run(['git','diff','--exit-code',BASE,'--','planning/logging/sources','planning/logging/evidence',':(exclude)planning/logging/evidence/wp04-s03-t04*'],cwd=ROOT,capture_output=True).returncode==0,'Historical evidence changed')
require(subprocess.run(['git','diff','--exit-code',BASE,'--','implementations/csharp/src/CIS.Core','contracts/logging'],cwd=ROOT,capture_output=True).returncode==0,'Shipping or contract source changed')
shipping=[]
record={'schemaVersion':1,'taskId':'WP04-S03-T04','recordedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'checkpointCommit':BASE,'taskChangesCommitted':False,
 'originalTaskDescription':'Add barrier-driven tests for nested scopes, child-task inheritance, two concurrent operations, independent runtimes, cancellation, input mutation, and invalid disposal order.',
 'selectedContract':'Human retained BeginScope and creator-disposes rule with documented detection limits on 11 October 2026. Universal cross-flow rejection in our later unpublished draft is withdrawn; prohibited transfer/disposal is unspecified beyond precise state guards.',
 'outcome':'Qualify complete internal scope-state scenarios with literal shared observations, deterministic barriers, explicit native bindings and unchanged shipping implementation.',
 'nativeChecks':{'newUnitCases':22,'newIntegrationCases':14,'newArchitectureCases':4,'newCases':40,'boundGroups':6,'unit':2504,'integration':366,'architecture':397,'total':3267,'failed':0,'skipped':0},
 'compilerControls':{'startup':21,'compatibilitySamples':22,'actualCoreValues':12,'total':55,'allMatched':True},
 'standaloneProcesses':identity,'ubuntuDistribution':{'report':'planning/logging/evidence/wp04-s03-t04/commands/ubuntu-os-release.txt','identityChecked':'ID=ubuntu;VERSION_ID=24.04'},
 'externalClaudeReview':{'reviewCompleted':True,'outsideSandbox':True,'readOnly':True,'reviews':['claude-review.json','claude-final.json','claude-source-final.json','claude-evidence.json','claude-evidence-final.json'],'resolvedFindings':['Completed worker failures and nonduplicated readiness errors preserved; timeout non-join limits stated.','Nested factory ownership observed before BeginScope; DTO/map copy separately qualified.','AC04 guidance content and precise criterion/prior native bindings validated.','Observer distinguishes macOS/unknown from Linux.','Worker active captures occur before readiness and release.','Initial fixture-assignment correction failure retained; final corrected unit checks pass.'],'finalVerdict':review_verdict,'actionableFindingsResolved':True,'productApproval':False},
 'verificationExits':{'directDeveloper':0,'checkDeveloper':0,'releaseVerify':2,'checkRelease':2,'freshCopyDeveloper':0,'windowsDriver':0,'ubuntuDriver':0},
 'freshCopyVerification':replay,'canonicalSourceInventory':'planning/logging/evidence/wp04-s03-t04/fresh-copy/input-inventory.json','verifiedCanonicalInputCount':len(inputs),
 'sourceIdentity':'Actual final implementation/test/contract inputs verified unchanged in main and isolated input/work copies. Direct and copied builds have independently retained DLL/package hashes; path-independent binary reproduction is not claimed.',
 'postCaptureInventoriedEdits':[],
 'changedShippingFiles':[{'path':p,'sha256':sha(ROOT/p)} for p in shipping],
 'supersededRuns':{'classification':'Preserved pre-correction/native/process investigations, not final candidate acceptance','reportsRoot':'planning/logging/evidence/wp04-s03-t04/superseded-runs','note':'Focused pre-format/cancellation/review reports remain in focused with distinct filenames; use corrected final native/fresh-copy summaries for acceptance.'},
 'taskComplete':True,'storyComplete':True,'storyCompletionScope':'Actual internal scope state under creator ownership; public runtime/provider/environment/event integration remains pending.',
 'limits':['Four original criteria qualified for internal scope state, with public runtime admission/provider/environment/event integration later.','Cross-flow ownership is a caller rule; guards do not reliably detect all prohibited misuse.','Unawaited child contexts may retain operation values after parent scope ends; helper-returned handles do not install a scope in the caller.','Windows Developer/fresh-copy suites and Ubuntu child processes are executed; full Linux SDK suite and release qualification remain pending.','CIS imported records remain Draft; graph freshness is not product approval.'],
 'reports':index()}
require(not RECEIPT.exists(),'Receipt already exists')
RECEIPT.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'phase':'fresh','retainedFiles':len(record['reports']),'capturedInputs':len(inputs),'taskComplete':True,'storyComplete':True}))
