import datetime
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT=Path('C:/miscwork/portfolio/cislib').resolve()
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s03-t01'
OUT=ROOT/'planning/logging/evidence/wp04-s03-t01'
RECEIPT=ROOT/'planning/logging/evidence/wp04-s03-t01-windows-linux.json'
COUNTS={'unit':2429,'integration':344,'architecture':392}
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def require(value,message):
    if not value: raise AssertionError(message)

def read(path): return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def write(path,value):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(value,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
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
    groups=[row for row in read(root/'conformance/logging/case-inventory.json')['cases'] if '-SCOPE-T01-' in row['id']]
    require(len(groups)==19 and sum(row['minimumExecutions'] for row in groups)==45,'Task bindings differ')
    counts={}
    for family,expected in COUNTS.items():
        value=ET.parse(folder/f'families/{family}/report.trx').find('t:ResultSummary/t:Counters',NS).attrib
        require(int(value['total'])==int(value['passed'])==expected and int(value['failed'])==int(value['notExecuted'])==0,'Native counts differ: '+family)
        counts[family]={key:int(value[key]) for key in ['total','passed','failed','notExecuted']}
        discovery={row['id']:row['count'] for row in read(folder/f'families/{family}/discovery.json')['cases']}
        for group in groups:
            if group['family']==family: require(discovery[group['id']]==group['minimumExecutions'],'Task discovery differs: '+group['id'])
    for name,count in [('startup',21),('compatibility-samples',22),('log-value-consumers',12)]:
        controls=read(folder/f'contracts/{name}/summary.json')
        require(controls['exitCode']==0 and len(controls['controls'])==count and all(row['matched'] for row in controls['controls']),'Compiler controls differ')
    require(read(folder/'summary.json')['exitCode']==read(folder/'validation.json')['exitCode']==0,'Developer evidence failed')
    return counts

def processes():
    driver=ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/verify-operation-scopes.py'
    spec=importlib.util.spec_from_file_location('scope_driver',driver)
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    require(len(module.controls())==3,'Comparator controls differ')
    host=TASK/'process-inputs/host/net10.0'
    identities=[]
    for family in ['windows','linux']:
        folder=TASK/f'process-{family}'
        summary=read(folder/'summary.json')
        require(summary['exitCode']==0 and summary['status']=='Passed' and summary['parentEnvironmentAndDirectoryUnchanged'],'Process failed')
        require(summary['driverSha256']==sha(driver) and len(summary['comparatorControls'])==3 and all(row['rejected'] for row in summary['comparatorControls']),'Process oracle controls differ')
        require([row['id'] for row in summary['cases']]==list(module.EXPECTED),'Process denominator differs')
        for case in summary['cases']:
            actual=read(folder/(case['id']+'.stdout.json'))
            module.verify(case['id'],actual)
            observed=actual['observed']
            require(case['status']=='Passed' and case['childExitCode']==0,'Child failed')
            require(observed['publicFacadeInitialized'] is False and observed['runtimeDescription']=='.NET 10.0.12' and observed['operatingSystem']==family,'Runtime identity differs')
            identities.append((observed['fixtureSha256'],observed['coreSha256']))
        for item in summary['reports']:
            path=folder/item['path']
            require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Child report bytes differ')
    pair=(sha(host/'CIS.Core.DiscoveryFixture.dll'),sha(host/'CIS.Core.dll'))
    require(set(identities)=={pair},'Managed identities differ')
    require(pair[1]==sha(ROOT/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'),'Process Core differs from full native build')
    return {'childrenPerPlatform':3,'runtimeVersion':'10.0.12','rollForward':'Disable','fixtureSha256':pair[0],'coreSha256':pair[1],'optimizationSafeComparatorControlsPerPlatform':3}

def artifacts(root,name):
    copy(root/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll',OUT/name/'CIS.Core.dll')
    copy(root/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg',OUT/name/'CIS.Core.0.1.0-dev.1.nupkg')

counts=native(ROOT)
identity=processes()
review=read(TASK/'claude-review-assessment.json')
require(review['reviewCompleted'] and review['actionableFindingsResolved'],'Advisory review incomplete')
require(read(TASK/'verification-exits.json')=={'checkDeveloper':0,'releaseVerify':2,'checkRelease':2},'Gate disposition differs')
if sys.argv[1]=='direct':
    tree(ROOT/'.artifacts/csharp/verification/Developer',OUT/'developer')
    tree(ROOT/'.artifacts/csharp/verification/Release',OUT/'release')
    artifacts(ROOT,'development-artifacts')
    for path in TASK.iterdir():
        if path.is_file() and path.suffix in ['.json','.log','.txt']: copy(path,OUT/'commands'/path.name)
    for name in ['focused-initial','focused-corrected','focused-architecture-corrected','focused-process','process-windows','process-linux']:
        tree(TASK/name,OUT/name)
    tree(TASK/'process-inputs',OUT/'process-inputs',{'.json','.dll','.pdb','.exe'})
    copy(TASK/'retain-evidence.py',OUT/'commands/retain-evidence.py')
    print(json.dumps({'retainedFiles':sum(path.is_file() for path in OUT.rglob('*')),'counts':counts,'processes':identity}))
    raise SystemExit(0)

replay=read(ROOT/'.artifacts/csharp/replay/latest.json')
require(replay['exitCode']==0 and replay['inputVerifiedBefore'] and replay['inputVerifiedAfter'],'Fresh-copy verification failed')
run=Path(replay['runRoot'])
inputs=read(run/'input-inventory.json')
for item in inputs:
    for root in [ROOT,run/'input',run/'work']:
        require(sha(root/item['path'])==item['sha256'],'Captured input changed: '+item['path'])
require(native(run/'work')==counts,'Fresh-copy native counts differ')
inspections=read(TASK/'binary-inspection.json')
require(len(inspections)==2 and inspections[0]['sha256']==identity['coreSha256'] and inspections[1]['sha256']==sha(run/'work/implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'),'Inspected artifact identity differs')
require(inspections[0]['methods']==inspections[1]['methods'],'Verified build signatures/IL/exception regions differ')
require(inspections[0]['pdbPath']!=inspections[1]['pdbPath'],'Expected recorded debug-path difference is missing')
tree(run/'work/.artifacts/csharp/verification/Developer',OUT/'fresh-copy-developer')
artifacts(run/'work','fresh-copy-artifacts')
for name in ['summary.json','input-inventory.json','verification.log']: copy(run/name,OUT/'fresh-copy'/name)
copy(TASK/'fresh-copy.log',OUT/'commands/fresh-copy.log')
originals={'core-library-implementation-guide-v1.md':'01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675','core-library-backlog-v1.json':'4abb87f20bc85beaa145c82714bb5d34cb923ef7b9b7b857e678839110dcd549','core-library-implementation-plan-v1.md':'41f1568583c9a04c783dd6db5fe80cd24ba8a6142a78983f910c4973e72bf7a5'}
for name,expected in originals.items(): require(sha(ROOT/'planning/logging/sources'/name)==expected,'Original source changed')
require(subprocess.run(['git','diff','--exit-code','1024d40','--','contracts/logging','planning/logging/sources'],cwd=ROOT,capture_output=True).returncode==0,'Preserved contracts changed')
historical=0
for path in (ROOT/'planning/logging/evidence').glob('*windows*.json'):
    if path!=RECEIPT:
        for item in read(path).get('reports',[]):
            source=ROOT/item['path']
            require(sha(source)==item['sha256'] and source.stat().st_size==item['bytes'],'Historical report changed')
            historical+=1
shipping=['implementations/csharp/src/CIS.Core/Logging/OperationContextData.cs']+['implementations/csharp/src/CIS.Core/Logging/Scopes/'+name+'.cs' for name in ['CapturedOperationContext','OperationScopeFrame','OperationScopeStore']]
reports=[{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]
record={'schemaVersion':1,'taskId':'WP04-S03-T01','recordedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'checkpointCommit':'1024d40784508f72d282fc99eb6bbda908e2380a','taskChangesCommitted':False,
 'originalTaskDescription':'Implement runtime-specific asynchronous scope storage and BeginScope using copied OperationContextData and the supported value model.',
 'outcome':'Implement the actual context input and internal per-store asynchronous scope foundation with copied bounded payloads and immutable parent frames',
 'nativeChecks':{'newUnitCases':40,'newIntegrationCases':3,'newArchitectureCases':2,'newCases':45,'boundMethods':19,'unit':2429,'integration':344,'architecture':392,'total':3165,'failed':0,'skipped':0},
 'compilerControls':{'existingStartup':21,'compatibilitySamples':22,'actualCoreValueControls':12,'total':55,'allMatched':True},
 'standaloneProcesses':identity,'externalClaudeReview':review,
 'buildArtifactComparison':{'directCoreSha256':inspections[0]['sha256'],'freshCopyCoreSha256':inspections[1]['sha256'],'byteIdentical':False,'managedMethodDefinitionsMatched':len(inspections[0]['methods']),'managedMethodBodiesMatched':sum(row['body'] is not None for row in inspections[0]['methods']),'methodSignaturesILAndExceptionRegionsEqual':True,'differentRecordedPdbPaths':True,'sameAssemblyAndInformationalVersions':True,'scope':'Fresh-copy source/native verification, not path-independent byte reproducibility. Distinct artifact identities are retained.','compilerPathDocumentation':'https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/main-compiler-option'},
 'verificationExits':{'directDeveloper':0,'checkDeveloper':0,'releaseVerify':2,'checkRelease':2,'freshCopyDeveloper':0,'windowsScopeDriver':0,'ubuntuScopeDriver':0},
 'freshCopyVerification':replay,'canonicalSourceInventory':'planning/logging/evidence/wp04-s03-t01/fresh-copy/input-inventory.json','verifiedCanonicalInputCount':len(inputs),'postCaptureInventoriedEdits':[],
 'sourceIdentity':'Main/reference/work SHA256 values verified before and after isolated fresh-copy Developer execution. Retained final results and this receipt are appended after capture; checkpoint is capture-time Git identity.',
 'historicalReportReferencesVerified':historical,'originalSourceHashes':originals,'changedShippingFiles':[{'path':path,'sha256':sha(ROOT/path)} for path in shipping],
 'supersededRuns':[{'run':'retention-initial','reason':'An extra helper assertion wrongly required path-independent DLL equality. Source/native verification had passed; actual debug paths and MVIDs differ, while all581 method signatures and563 bodies match. Corrected retention records both exact artifact hashes without making a byte-reproducibility claim.'},{'run':'focused-initial','reason':'Malformed surrogate attribute transport changed the input; native code units now constructed in test bodies; 2 failures retained.'},{'run':'format-initial','reason':'Switch arm formatting corrected; initial exit2 retained.'},{'run':'build-guards','reason':'Architecture project cannot access Core internals; compiled metadata reflection replaces direct internal typeof; CS0122 retained without broadening friendship.'}],
 'qualificationLimits':'Actual internal scope foundation only. Windows native families plus fresh-copy verification, three exact .NET10.0.12 roll-forward-disabled children per Windows/Ubuntu with identical managed bytes. Universal inherited-frame cross-flow disposal rejection remains explicitly unresolved for T03; no placeholder public runtime/logger APIs. Full Linux SDK/native families, macOS and release remain pending.',
 'pending':['WP04-S03-T02 outer-to-inner merge and complete bounds','WP04-S03-T03 full owning-flow disposal including inherited-frame and foreign-repeat cases','WP04-S03-T04 complete shared scope scenarios','public runtime/logger/event and byte-budget integration','Linux SDK/native families, macOS and release'],
 'storyComplete':False,'taskComplete':True,'reports':reports}
write(RECEIPT,record)
print(json.dumps({'retainedFiles':len(reports),'canonicalInputs':len(inputs),'historicalReferences':historical,'counts':counts,'freshCopy':replay['runRoot']}))
