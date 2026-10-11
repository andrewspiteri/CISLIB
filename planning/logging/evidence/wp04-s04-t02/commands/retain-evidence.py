import datetime, hashlib, importlib.util, json, re, shutil, subprocess, sys
from pathlib import Path
import xml.etree.ElementTree as ET
ROOT=Path('C:/miscwork/portfolio/cislib')
TASK=ROOT/'.artifacts/csharp/tasks/wp04-s04-t02'
OUT=ROOT/'planning/logging/evidence/wp04-s04-t02'
RECEIPT=ROOT/'planning/logging/evidence/wp04-s04-t02-windows-linux.json'
BASE='4fe61b72af451155d3c78737010fab53d209ba03'
COUNTS={'unit':2649,'integration':426,'architecture':398}
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
METADATA=['docs/modules/logging/event-precedence.md','planning/logging/implementation-status.md','docs/cis/references/traceability-matrix.md']
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
    groups=[row for row in read(root/'conformance/logging/case-inventory.json')['cases'] if '-EVENT-T02-' in row['id']]
    require(len(groups)==7 and sum(row['minimumExecutions'] for row in groups)==88,'Task binding denominator differs')
    require(all(g['family'] in COUNTS for g in groups) and {f:sum(g['minimumExecutions'] for g in groups if g['family']==f) for f in COUNTS}=={'unit':60,'integration':28,'architecture':0},'Task family split differs')
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
    driver=ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/verify-event-precedence.py'
    spec=importlib.util.spec_from_file_location('event_projection_driver',driver)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.CATALOG=read(ROOT/'conformance/logging/event-precedence-cases.json')
    module.ROWS={row['id']:row for row in module.CATALOG['cases']}
    require(len(module.controls())==8 and len(module.ROWS)==28,'Comparator controls differ')
    host=TASK/'reviewed-process-inputs/host'
    pair=(sha(host/'CIS.Core.DiscoveryFixture.dll'),sha(host/'CIS.Core.dll'))
    for family in ['windows','linux']:
        folder=TASK/f'reviewed-process-{family}';summary=read(folder/'summary.json')
        require(read(TASK/f'reviewed-process-{family}.stdout.json')==summary,'Process command output not bound to summary')
        require(summary['schema']=='cis.event-precedence-process/v1' and summary['status']=='Passed' and summary['exitCode']==0 and summary['parentEnvironmentAndDirectoryUnchanged'],'Process verification failed')
        require(summary['runtimeVersion']=='10.0.12' and summary['rollForward']=='Disable' and summary['family']==family,'Runtime selection differs')
        require(summary['driverSha256']==sha(driver) and summary['inputsSha256']==sha(ROOT/'conformance/logging/event-precedence-cases.json') and summary['comparatorControls']==module.controls(),'Final driver identity/controls differ')
        require([row['id'] for row in summary['cases']]==list(module.ROWS),'Child denominator differs')
        for item in summary['cases']:
            actual=read(folder/(item['id']+'.stdout.json'));module.verify(item['id'],actual)
            observed=actual['observed']
            require(item['status']=='Passed' and item['childExitCode']==0,'Child failed')
            require(observed['inputsSha256']==sha(ROOT/'conformance/logging/event-precedence-cases.json') and observed['publicFacadeInitialized'] is False and observed['runtimeDescription']=='.NET 10.0.12' and observed['operatingSystem']==family,'Runtime identity differs')
            require((observed['fixtureSha256'],observed['coreSha256'])==pair and (item['fixtureSha256'],item['coreSha256'])==pair,'Managed identities differ')
        require(len(summary['reports'])==56 and {p.name for p in folder.iterdir()}=={'summary.json'}|{item['path'] for item in summary['reports']},'Process report/file denominator differs')
        for item in summary['reports']:
            path=folder/item['path'];require(sha(path)==item['sha256'] and path.stat().st_size==item['bytes'],'Child report bytes differ')
    require(pair[1]==sha(ROOT/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'),'Process Core differs from native build')
    require(pair[0]==sha(ROOT/'implementations/csharp/tests/fixtures/DiscoveryHost/bin/Release/net10.0/CIS.Core.DiscoveryFixture.dll'),'Process fixture differs from native build')
    ubuntu=(TASK/'ubuntu-os-release.txt').read_text(encoding='utf-8-sig')
    require('ID=ubuntu' in ubuntu and 'VERSION_ID="24.04"' in ubuntu,'Ubuntu distribution differs')
    return {'childrenPerPlatform':28,'runtimeVersion':'10.0.12','rollForward':'Disable','fixtureSha256':pair[0],'coreSha256':pair[1],'comparatorControlsPerPlatform':8}

def artifacts(root,target):
    copy(root/'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll',OUT/target/'CIS.Core.dll')
    copy(root/'.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg',OUT/target/'CIS.Core.0.1.0-dev.1.nupkg')
def index():
    return [{'path':p.relative_to(ROOT).as_posix(),'sha256':sha(p),'bytes':p.stat().st_size} for p in sorted(OUT.rglob('*')) if p.is_file()]

def reviews():
    for name in ['claude-source','claude-evidence','claude-evidence-final']:
        result=read(TASK/(name+'.json'))
        require((TASK/(name+'.exit.txt')).read_text().strip()=='0' and not result.get('is_error'),'Claude review did not complete')
    for name in ['claude-source','claude-evidence-final']:
        require(re.match(r'^(?:\*\*)?PASS\b',read(TASK/(name+'.json'))['result'].lstrip()),'Required review did not pass: '+name)
    return read(TASK/'claude-evidence-final.json')['result']

phase=sys.argv[1]
if phase=='fresh':
    for name in ['retain-evidence.py', 'finalize-metadata.py', 'audit-commit.py']: require(sha(TASK/name)==sha(OUT/'commands'/name),'Frozen helper differs: '+name)
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
require((TASK/'focused-corrected-unit.exit.txt').read_text().strip()=='0' and (TASK/'format-corrected.exit.txt').read_text().strip()=='0','Focused corrected check/formatter differs')
review_verdict=reviews()
if phase=='direct':
    tree(ROOT/'.artifacts/csharp/verification/Developer',OUT/'developer',{'.json','.log','.trx'})
    tree(ROOT/'.artifacts/csharp/verification/Release',OUT/'release',{'.json','.log','.trx'})
    artifacts(ROOT,'development-artifacts')
    for name in ['focused-corrected','reviewed-process-windows','reviewed-process-linux','reviewed-process-inputs']:
        tree(TASK/name,OUT/name)
    tree(TASK/'superseded-initial',OUT/'superseded-initial')
    tree(TASK/'superseded-helper-prose',OUT/'superseded-helper-prose')
    commands=['developer.stdout.log', 'developer.stderr.log', 'check-developer.stdout.json', 'check-developer.stderr.log', 'release.stdout.log', 'release.stderr.log', 'check-release.stdout.json', 'check-release.stderr.log', 'verification-exits.json', 'reviewed-process-windows.stdout.json', 'reviewed-process-windows.stderr.log', 'reviewed-process-linux.stdout.json', 'reviewed-process-linux.stderr.log', 'reviewed-process-exits.json', 'ubuntu-os-release.txt', 'ubuntu-os-release.stderr.log', 'cis-before-exits.json', 'cis-current-exits.json', 'retain-evidence.py', 'finalize-metadata.py', 'audit-commit.py', 'focused-exits.json', 'focused-unit.stdout.log', 'focused-unit.stderr.log', 'focused-integration.stdout.log', 'focused-integration.stderr.log', 'focused-corrected-unit.stdout.log', 'focused-corrected-unit.stderr.log', 'focused-corrected-unit.exit.txt', 'format.stdout.log', 'format.stderr.log', 'format.exit.txt', 'format-corrected.stdout.log', 'format-corrected.stderr.log', 'format-corrected.exit.txt', 'cis-status-before.agent.txt', 'cis-status-before.stderr.log', 'cis-find-projection.json', 'cis-find-projection.stderr.log', 'cis-find-exact-projection.json', 'cis-find-exact-projection.stderr.log', 'cis-find-exact-projection.exit.txt', 'cis-projection-context.json', 'cis-projection-context.stderr.log', 'cis-projection-context.exit.txt', 'cis-symbol-help.txt', 'cis-symbol-help.stderr.log', 'cis-build-current.agent.txt', 'cis-build-current.stderr.log', 'cis-status-current.agent.txt', 'cis-status-current.stderr.log', 'cis-docs-current.agent.txt', 'cis-docs-current.stderr.log']
    for stem in ['claude-source','claude-evidence','claude-evidence-final']:
        commands += [stem+suffix for suffix in ['.json','.stderr.log','.exit.txt','.prompt.txt']]
    for name in commands: copy(TASK/name,OUT/'commands'/name)
    sources=subprocess.run(['git','diff','--name-only','-z',BASE],cwd=ROOT,capture_output=True,check=True).stdout
    sources+=subprocess.run(['git','ls-files','--others','--exclude-standard','-z'],cwd=ROOT,capture_output=True,check=True).stdout
    paths={p.decode('utf-8') for p in sources.split(b'\0') if p}
    for path in paths:
        if not path.startswith('planning/logging/evidence/wp04-s04-t02'):
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
# Bind every executed source inventory to all three roots, not just its local root.
inventories=[
    read(ROOT/'.artifacts/csharp/verification/Developer/source-inputs.json'),
    read(run/'work/.artifacts/csharp/verification/Developer/source-inputs.json'),
    read(ROOT/'.artifacts/csharp/verification/Release/source-inputs.json'),
    inputs]
direct_index={item['path']:item for item in inventories[0]}
fresh_index={item['path']:item for item in inventories[1]}
require(inventories[0]==inputs,'Direct Developer and replay inventories differ')
require(set(fresh_index).issubset(direct_index),'Fresh native inventory adds an uncaptured source')
require(all(direct_index[p]==row for p,row in fresh_index.items()),'Common native inventory entries differ')
omitted=sorted(set(direct_index)-set(fresh_index))
require(len(omitted)==3101 and all(p.startswith('planning/logging/evidence/') and not p.startswith('planning/logging/evidence/wp04-s04-t02') and ({'bin','obj'} & set(Path(p).parts)) for p in omitted),'Native inventory exclusion is not the observed archival bin/obj subset')
inventory_difference={'directAndReplayEntries':len(inputs),'freshNativeEntries':len(fresh_index),'commonRowsExactlyMatch':True,'freshAddsNoUncapturedSources':True,'omittedFromFreshNativeInventory':[direct_index[p] for p in omitted],'reason':'Get-SourceInventory uses git-tracked inputs in main; in the Git-free work copy it excludes all bin/obj segments. These historical archived files remain physically present and are hashed against main/input/work below. No current implementation/test/contract/conformance input is omitted.'}
for inventory in inventories:
    require(len({item['path'] for item in inventory})==len(inventory),'Duplicate source inventory path')
    for item in inventory:
        for checked_root in [ROOT,run/'input',run/'work']:
            require(sha(checked_root/item['path'])==item['sha256'],'Executed inventory source differs: '+item['path'])

difference_path=OUT/'fresh-copy/native-inventory-difference.json'
require(not difference_path.exists(),'Inventory reconciliation already retained')
difference_path.parent.mkdir(parents=True,exist_ok=True)
difference_path.write_text(json.dumps(inventory_difference,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
tree(run/'work/.artifacts/csharp/verification/Developer',OUT/'fresh-copy-developer',{'.json','.log','.trx'})
artifacts(run/'work','fresh-copy-artifacts')
for name in ['summary.json','input-inventory.json','verification.log']: copy(run/name,OUT/'fresh-copy'/name)
for name in ['fresh-copy.stdout.log','fresh-copy.stderr.log','fresh-copy.exit.txt']: copy(TASK/name,OUT/'commands'/name)
originals={'core-library-implementation-guide-v1.md':'01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675','core-library-backlog-v1.json':'4abb87f20bc85beaa145c82714bb5d34cb923ef7b9b7b857e678839110dcd549','core-library-implementation-plan-v1.md':'41f1568583c9a04c783dd6db5fe80cd24ba8a6142a78983f910c4973e72bf7a5'}
for name,expected in originals.items(): require(sha(ROOT/'planning/logging/sources'/name)==expected,'Original supplied source changed')
require(subprocess.run(['git','diff','--exit-code',BASE,'--','planning/logging/sources','planning/logging/evidence',':(exclude)planning/logging/evidence/wp04-s04-t02*'],cwd=ROOT,capture_output=True).returncode==0,'Historical evidence changed')
require(subprocess.run(['git','diff','--exit-code',BASE,'--','contracts/logging'],cwd=ROOT,capture_output=True).returncode==0,'Historical contract source changed')
require(subprocess.run(['git','diff','--exit-code',BASE,'--','implementations/csharp/src/CIS.Core'],cwd=ROOT,capture_output=True).returncode==0,'Shipping Core changed')
shipping=[]
record={'schemaVersion':1,'taskId':'WP04-S04-T02','recordedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'checkpointCommit':BASE,'taskChangesCommitted':False,
 'originalTaskDescription':'Apply explicit-event-over-scope attribute and OperationId precedence, trim optional identifiers, and reserve identity and trace/span fields against free-form overrides.',
 'selectedContract':'Human-selected single CIS.Core/CIS.Logging, typed named payload, creator-owned scopes with detection limits, configurable entry truncation; original sources/historical evidence remain unchanged.',
 'outcome':'Actual existing owned projection qualified for whole-value ordinal replacement, null preservation, trimmed protected operation IDs and nested reserved spellings; no fake wire/correlation/queue.',
 'nativeChecks':{'newUnitCases':60,'newIntegrationCases':28,'newArchitectureCases':0,'newCases':88,'boundGroups':7,'unit':2649,'integration':426,'architecture':398,'total':3473,'failed':0,'skipped':0},
 'compilerControls':{'startup':21,'compatibilitySamples':22,'actualCoreValues':12,'total':55,'allMatched':True},
 'standaloneProcesses':identity,'ubuntuDistribution':{'report':'planning/logging/evidence/wp04-s04-t02/commands/ubuntu-os-release.txt','identityChecked':'ID=ubuntu;VERSION_ID=24.04'},
 'externalClaudeReview':{'reviewCompleted':True,'outsideSandbox':True,'readOnly':True,'reviews':['claude-source.json','claude-evidence.json','claude-evidence-final.json'],'sourceRequiredFindings':[],'resolvedFindings':['Evidence review R1: pre-review self corrections separated from actual Claude findings; source review has no required findings.','Evidence review R2: completion prose spacing corrected before helper freeze.'],'finalVerdict':review_verdict,'actionableFindingsResolved':True,'productApproval':False},
 'preReviewSelfCorrections':['Initial unit compile used a nonexistent Double factory; actual Number binding corrected and original compiler failure retained.','C1 control made interior because boundary NEL is permitted trim whitespace.'],
 'verificationExits':{'directDeveloper':0,'checkDeveloper':0,'releaseVerify':2,'checkRelease':2,'freshCopyDeveloper':0,'windowsDriver':0,'ubuntuDriver':0,'ubuntuIdentity':0},
 'activeEvidenceHelpers':['retain-evidence.py','finalize-metadata.py','audit-commit.py'],
 'nativeInventoryReconciliation':'planning/logging/evidence/wp04-s04-t02/fresh-copy/native-inventory-difference.json',
 'freshCopyVerification':replay,'canonicalSourceInventory':'planning/logging/evidence/wp04-s04-t02/fresh-copy/input-inventory.json','verifiedCanonicalInputCount':len(inputs),
 'sourceIdentity':'All actual final inputs verified unchanged in main/input/work. Direct and replay inventories match, fresh native is an exact common-row subset with exactly 3101 explicitly listed historical archived bin/obj exclusions. Every excluded archived row is also hashed in every root. No current implementation/test/contract/conformance input is omitted. Build artifacts separately retained; path-independent binary reproduction not claimed.',
 'postCaptureInventoriedEdits':[], 'changedShippingFiles':[],
 'supersededRuns':{'classification':'Initial source/compiler/focused evidence is preserved, not final candidate acceptance','reportsRoot':'planning/logging/evidence/wp04-s04-t02/superseded-initial','note':'Initial unit compile failed CS0117; actual 28 child integration passed. Corrected 60 focused unit and final full/fresh 88 new cases independently qualified.'},
 'taskComplete':True,'storyComplete':False,'storyCompletionScope':'T02 precedence/reserved input separation complete for owned facts; actual UTC/W3C pair capture, full original reserved-correlation vector, escaped byte encoder and full S04 qualification remain required.',
 'limits':['Owned development facts are not a complete v1 record or output admission/queue.','Actual W3C capture and complete original CAPTURE-RESERVED-CORRELATION vector are T03/T05; nested trace/span separation does not claim actual queued Activity evidence.','T03 UTC/W3C, T04 escaped entry-byte truncation and T05 full structural qualification remain pending.','First-encounter bounded candidate selection remains; explicit matching retained keys win, arbitrary new event keys are not prioritized over scopes.','Cross-flow ownership remains the selected caller rule with detection limits; no atomic concurrent caller snapshot promised.','Windows native/fresh-copy suites and Ubuntu child processes execute; full Linux SDK/release suite remains pending.','CIS graph has partial ownership/extraction coverage and is not product approval.'],
 'reports':index()}
require(not RECEIPT.exists(),'Receipt already exists')
RECEIPT.write_text(json.dumps(record,indent=2,ensure_ascii=True)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'phase':'fresh','retainedFiles':len(record['reports']),'capturedInputs':len(inputs),'taskComplete':True,'storyComplete':False}))
