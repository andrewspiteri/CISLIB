import datetime,hashlib,importlib.util,json,shutil,subprocess,sys
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path('C:/miscwork/portfolio/cislib')
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s03-t02'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t02'
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t02-windows-linux.json'
COUNTS={'unit':2470,'integration':348,'architecture':393}
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
def require(value,message):
    if not value: raise AssertionError(message)
def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def copy(source,target):
    target.parent.mkdir(parents=True,exist_ok=True)
    if target.exists(): require(source.read_bytes()==target.read_bytes(),'Retained bytes differ: '+str(target))
    else: shutil.copyfile(source,target)
def tree(source,target,suffixes={'.json','.log','.trx'}):
    for path in sorted(source.rglob('*')):
        if path.is_file() and path.suffix in suffixes and 'obj' not in path.relative_to(source).parts:
            copy(path,target/path.relative_to(source))
def native(root):
    folder=root/'.artifacts/csharp/verification/Developer'
    groups=[row for row in read(root/'conformance/logging/case-inventory.json')['cases'] if '-SCOPE-T02-' in row['id']]
    require(len(groups)==17 and sum(row['minimumExecutions'] for row in groups)==46,'Task bindings differ')
    counts={}
    for family,expected in COUNTS.items():
        values=ET.parse(folder/f'families/{family}/report.trx').find('t:ResultSummary/t:Counters',NS).attrib
        counts[family]={key:int(values[key]) for key in ['total','passed','failed','notExecuted']}
        require(counts[family]=={'total':expected,'passed':expected,'failed':0,'notExecuted':0},'Native counts differ')
        discovery={row['id']:row['count'] for row in read(folder/f'families/{family}/discovery.json')['cases']}
        for group in groups:
            if group['family']==family: require(discovery[group['id']]==group['minimumExecutions'],'Native task binding not executed')
    for name,count in [('startup',21),('compatibility-samples',22),('log-value-consumers',12)]:
        control=read(folder/f'contracts/{name}/summary.json')
        require(control['exitCode']==0 and len(control['controls'])==count and all(row['matched'] for row in control['controls']),'Compiler controls differ')
    require(read(folder/'summary.json')['exitCode']==read(folder/'validation.json')['exitCode']==0,'Developer validation failed')
    return counts

def processes():
    driver=ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/verify-scope-merges.py'
    spec=importlib.util.spec_from_file_location('scope_merge_driver',driver)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    require(len(module.controls())==3,'Comparator controls differ')
    host=TASK/'reviewed-process-inputs/host'
    pair=(sha(host/'CIS.Core.DiscoveryFixture.dll'),sha(host/'CIS.Core.dll'))
    for family in ['windows','linux']:
        folder=TASK/f'reviewed-process-{family}';summary=read(folder/'summary.json')
        require(summary['status']=='Passed' and summary['exitCode']==0 and summary['parentEnvironmentAndDirectoryUnchanged'],'Process failed')
        require(summary['driverSha256']==sha(driver) and len(summary['comparatorControls'])==3 and all(row['rejected'] for row in summary['comparatorControls']),'Final driver identity/controls differ')
        require([row['id'] for row in summary['cases']]==list(module.EXPECTED),'Child denominator differs')
        for item in summary['cases']:
            actual=read(folder/(item['id']+'.stdout.json'));module.verify(item['id'],actual)
            observed=actual['observed']
            require(item['status']=='Passed' and item['childExitCode']==0,'Child failed')
            require(observed['publicFacadeInitialized'] is False and observed['runtimeDescription']=='.NET 10.0.12' and observed['operatingSystem']==family,'Runtime identity differs')
            require((observed['fixtureSha256'],observed['coreSha256'])==pair,'Managed identities differ')
        for item in summary['reports']:
            path=folder/item['path'];require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Child report bytes differ')
    require(pair[1]==sha(ROOT/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'),'Process Core differs from native build')
    return {'childrenPerPlatform':4,'runtimeVersion':'10.0.12','rollForward':'Disable','fixtureSha256':pair[0],'coreSha256':pair[1],'comparatorControlsPerPlatform':3,'pythonOptimizationEnabled':True}

def artifacts(root,name):
    copy(root/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll',OUT/name/'CIS.Core.dll')
    copy(root/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg',OUT/name/'CIS.Core.0.1.0-dev.1.nupkg')

counts=native(ROOT);identity=processes()
exits=read(TASK/'verification-exits.json')
require(exits=={'checkDeveloper':0,'releaseVerify':2,'checkRelease':2},'Gate disposition differs')
require(read(TASK/'developer-reviewed-exit.json')['exitCode']==0 and read(TASK/'reviewed-process-exits.json')=={'windows':0,'linux':0},'Actual executed exits differ')
initial=read(TASK/'claude-review.json');final=read(TASK/'claude-final.json')
require(not initial.get('is_error') and not final.get('is_error') and final['result'].startswith('**Result: PASS.'),'Source review did not pass')
if sys.argv[1]=='direct':
    tree(ROOT/'.artifacts/csharp/verification/Developer',OUT/'developer')
    tree(ROOT/'.artifacts/csharp/verification/Release',OUT/'release')
    artifacts(ROOT,'development-artifacts')
    for path in TASK.iterdir():
        if path.is_file() and path.suffix in ['.json','.log','.txt']: copy(path,OUT/'commands'/path.name)
    for name in ['focused','focused-process','process-windows','process-linux','reviewed-process-windows','reviewed-process-linux','pre-review-verification']:
        tree(TASK/name,OUT/name)
    for name in ['process-inputs','reviewed-process-inputs']:
        tree(TASK/name,OUT/name,{'.json','.dll','.pdb','.exe'})
    for name in ['retain-evidence.py','finalize-metadata.py','audit-commit.py']:
        copy(TASK/name,OUT/'commands'/name)
    print(json.dumps({'retainedReports':sum(path.is_file() for path in OUT.rglob('*')),'native':counts,'processes':identity}))
    raise SystemExit(0)

replay=read(ROOT/'.artifacts/csharp/replay/latest.json')
require(replay['exitCode']==0 and replay['inputVerifiedBefore'] and replay['inputVerifiedAfter'],'Fresh-copy source verification failed')
require(read(TASK/'fresh-copy-exit.json')['exitCode']==0,'Fresh-copy invocation failed')
run=Path(replay['runRoot']);inputs=read(run/'input-inventory.json')
for item in inputs:
    for root in [ROOT,run/'input',run/'work']: require(sha(root/item['path'])==item['sha256'],'Captured input changed: '+item['path'])
require(native(run/'work')==counts,'Fresh-copy native counts differ')
tree(run/'work/.artifacts/csharp/verification/Developer',OUT/'fresh-copy-developer')
artifacts(run/'work','fresh-copy-artifacts')
for name in ['summary.json','input-inventory.json','verification.log']: copy(run/name,OUT/'fresh-copy'/name)
for name in ['fresh-copy.log','fresh-copy.stderr.log','fresh-copy-exit.json']: copy(TASK/name,OUT/'commands'/name)
originals={'core-library-implementation-guide-v1.md':'01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675','core-library-backlog-v1.json':'4abb87f20bc85beaa145c82714bb5d34cb923ef7b9b7b857e678839110dcd549','core-library-implementation-plan-v1.md':'41f1568583c9a04c783dd6db5fe80cd24ba8a6142a78983f910c4973e72bf7a5'}
for name,expected in originals.items(): require(sha(ROOT/'planning/logging/sources'/name)==expected,'Original source changed')
require(subprocess.run(['git','diff','--exit-code','467b9f2','--','contracts/logging','planning/logging/sources'],cwd=ROOT,capture_output=True).returncode==0,'Original contracts changed')
historical=0
for path in (ROOT/'planning/logging/evidence').glob('*windows*.json'):
    if path!=RECEIPT:
        for item in read(path).get('reports',[]):
            source=ROOT/item['path'];require(sha(source)==item['sha256'] and source.stat().st_size==item['bytes'],'Historical report changed');historical+=1
shipping=['implementations/csharp/src/CIS.Core/Logging/Scopes/CapturedScopeContext.cs','implementations/csharp/src/CIS.Core/Logging/Scopes/OperationScopeStore.cs']
record={'schemaVersion':1,'taskId':'WP04-S03-T02','recordedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'checkpointCommit':'467b9f2e58fa57f65e776fea3ca69f55292fe000','taskChangesCommitted':False,
 'originalTaskDescription':'Apply outer-to-inner precedence, enforce maximum scope depth 32, and expose a captured merged scope view for event creation.',
 'outcome':'Implement an immutable outer-to-inner captured scope view with bounded candidates, independent merged projection and preserved input loss',
 'nativeChecks':{'newUnitCases':41,'newIntegrationCases':4,'newArchitectureCases':1,'newCases':46,'boundMethods':17,'unit':2470,'integration':348,'architecture':393,'total':3211,'failed':0,'skipped':0},
 'compilerControls':{'startup':21,'compatibilitySamples':22,'actualCoreValues':12,'total':55,'allMatched':True},'standaloneProcesses':identity,
 'externalClaudeReview':{'reviewCompleted':True,'outsideSandbox':True,'readOnly':True,'initialVerdict':'PASS with two low advisories and optional coverage','finalVerdict':'PASS; no actionable issues','actionableFindingsResolved':True,'resolved':['Observed dictionary ordering qualified as .NET-specific; observer normalizes key list and negative changes membership','Candidate resource-cost mutants require future triage, not automatic equivalence','Later duplicate of an omitted key qualified'],'reviews':['claude-review.json','claude-final.json'],'productApproval':False},
 'verificationExits':{'directDeveloper':read(TASK/'developer-reviewed-exit.json')['exitCode'],**exits,'freshCopyDeveloper':read(TASK/'fresh-copy-exit.json')['exitCode'],'windowsDriver':read(TASK/'reviewed-process-exits.json')['windows'],'ubuntuDriver':read(TASK/'reviewed-process-exits.json')['linux']},
 'freshCopyVerification':replay,'canonicalSourceInventory':'planning/logging/evidence/wp04-s03-t02/fresh-copy/input-inventory.json','verifiedCanonicalInputCount':len(inputs),'postCaptureInventoriedEdits':[],
 'buildArtifactIdentity':{'directCoreSha256':identity['coreSha256'],'freshCopyCoreSha256':sha(run/'work/implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'),'scope':'Source/input and native verification, not byte reproducibility across relocated build/debug paths; exact separate artifacts retained.'},
 'sourceIdentity':'Main/reference/work input hashes verified before and after isolated fresh-copy execution. Final outputs and receipt appended afterward; completion-only metadata edits, if any, are recorded explicitly.',
 'changedShippingFiles':[{'path':path,'sha256':sha(ROOT/path)} for path in shipping],'originalSourceHashes':originals,'historicalReportReferencesVerified':historical,
 'supersededRuns':[{'run':'format-initial','reason':'Initial whitespace errors corrected by scoped formatter; raw failure retained.'},{'run':'pre-review-verification/process-windows/process-linux','reason':'Earlier3210 full run and platform children predate observer-order clarification and added omitted-key case; final reviewed run has3211 and current driver/host identities.'}],
 'evidenceGeneration':'Retained scripts perform direct/fresh retention and completion metadata updates. Helper success is recorded by the receipt and observed exec exits; final staged audit output remains outside the hashed report set and its actual SHA256 is recorded in the commit message to avoid self-reference.',
 'qualificationLimits':'Internal merged scope view and32/33depth guard. Windows native families/fresh-copy verification plus4 exact .NET10.0.12 children per Windows/Ubuntu, identical managed process inputs. Dictionary enumeration is qualified native behavior, not public or portable member order. Candidate cap is a resource bound. T03 full owning-flow disposal/T04shared scope story, event bytes/activity/public runtime integration, full Linux SDK/native families, macOS and release remain pending.',
 'pending':['WP04-S03-T03 full owning-flow disposal including inherited-frame and repeated-foreign cases','WP04-S03-T04 shared scope scenarios/story qualification','event attributes/operationID precedence, Activity and aggregate encoded-entry byte budget','public runtime/logger integration','Linux SDK/native families, macOS and release'],
 'taskComplete':True,'storyComplete':False,
 'reports':[{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]}
RECEIPT.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'retainedReports':len(record['reports']),'canonicalInputs':len(inputs),'historicalReferences':historical,'counts':counts,'freshCopy':replay['runRoot']}))
