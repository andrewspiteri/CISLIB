import datetime
import hashlib
import importlib.util
import json
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path.cwd().resolve()
TASK = ROOT / '.artifacts/csharp/tasks/wp04-s02-t04'
OUT = ROOT / 'planning/logging/evidence/wp04-s02-t04'
RECEIPT = ROOT / 'planning/logging/evidence/wp04-s02-t04-windows-linux.json'
COUNTS = {'unit': 2389, 'integration': 341, 'architecture': 390}
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8'))


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=True) + '\n', encoding='utf-8', newline='\n')


def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        require(source.read_bytes() == target.read_bytes(), 'Existing retained bytes differ: ' + str(target))
    else:
        shutil.copyfile(source, target)


def tree(source, target, suffixes=None):
    for path in sorted(source.rglob('*')):
        if path.is_file() and 'obj' not in path.relative_to(source).parts and (suffixes is None or path.suffix in suffixes):
            copy(path, target / path.relative_to(source))


def native(root):
    folder = root / '.artifacts/csharp/verification/Developer'
    counts = {}
    inventory = read(root / 'conformance/logging/case-inventory.json')['cases']
    groups = [row for row in inventory if 'EXCEPTION-T04-' in row['id']]
    require(len(groups) == 7 and sum(row['minimumExecutions'] for row in groups) == 310, 'Task bindings differ')
    for family, expected in COUNTS.items():
        value = ET.parse(folder / f'families/{family}/report.trx').find('t:ResultSummary/t:Counters', NS).attrib
        require(int(value['total']) == int(value['passed']) == expected and int(value['failed']) == int(value['notExecuted']) == 0, 'Native counts differ: ' + family)
        counts[family] = {key: int(value[key]) for key in ['total','passed','failed','notExecuted']}
        discovery = {row['id']: row['count'] for row in read(folder / f'families/{family}/discovery.json')['cases']}
        for group in groups:
            if group['family'] == family:
                require(discovery[group['id']] == group['minimumExecutions'], 'Native task discovery differs')
    for name, count in [('startup',21),('compatibility-samples',22),('log-value-consumers',12)]:
        control = read(folder / f'contracts/{name}/summary.json')
        require(control['exitCode'] == 0 and len(control['controls']) == count and all(row['matched'] for row in control['controls']), 'Compiler controls differ')
    require(read(folder / 'summary.json')['exitCode'] == read(folder / 'validation.json')['exitCode'] == 0, 'Developer validation failed')
    return counts


def bindings():
    catalog = read(ROOT / 'conformance/logging/exception-qualification-cases.json')
    require(catalog['caseCount'] == len(catalog['cases']) == 68, 'Catalog incomplete')
    require(len({row['id'] for row in catalog['cases']}) == 68, 'Duplicate cases')
    require(sum(row.get('requires') == 'utf16-code-unit-input' for row in catalog['cases']) == 12, 'Capabilities differ')
    require(sum(not row['expected']['valid'] for row in catalog['cases']) == 8, 'Invalid cases differ')
    mapping = read(ROOT / 'conformance/logging/exception-qualification-map.json')
    backlog = read(ROOT / mapping['originalSource'])
    story = next(row for row in backlog['stories'] if row['id'] == 'WP04-S02')
    original = {row['id']: row['description'] for row in story['acceptance_criteria']}
    require(len(mapping['criteria']) == len(original) == 4, 'Acceptance criteria differ')
    inventory = {row['id']: row for row in read(ROOT / 'conformance/logging/case-inventory.json')['cases']}
    for criterion in mapping['criteria']:
        require(criterion['sourceDescription'] == original[criterion['id']], 'Original criterion changed')
        for key in criterion['nativeCaseGroups']:
            group = inventory[key]
            discovery = read(ROOT / f".artifacts/csharp/verification/Developer/families/{group['family']}/discovery.json")['cases']
            require(next(row['count'] for row in discovery if row['id'] == key) == group['minimumExecutions'], 'Criterion binding not executed')
    require(any('encoded-entry' in row for row in mapping['pending']), 'Pending event scope lost')
    return catalog


def processes():
    pairs = []
    catalog = bindings()
    host = TASK / 'post-review/process-inputs/host'
    inputs = TASK / 'post-review/process-inputs/exception-qualification-cases.json'
    require(sha(inputs) == sha(ROOT / 'conformance/logging/exception-qualification-cases.json'), 'Process inputs differ')
    for kind, filename, count in [('original','verify-exception-projection.py',6),('boundaries','verify-exception-boundaries.py',34),('content','verify-exception-content.py',20),('qualification','verify-exception-qualification.py',68)]:
        driver = ROOT / 'implementations/csharp/tests/fixtures/DiscoveryHost' / filename
        spec = importlib.util.spec_from_file_location('t04_' + kind, driver)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        modes = [row['id'] for row in catalog['cases']] if kind == 'qualification' else (['plain','thrown','inner','aggregate','mutation','null'] if kind == 'original' else module.cases())
        require(len(modes) == count, 'Driver denominator differs')
        for family in ['windows','linux']:
            folder = TASK / f'post-review/process-{family}' / ('qualification-corrected' if kind == 'qualification' else kind)
            summary = read(folder / 'summary.json')
            require(summary['exitCode'] == 0 and summary['status'] == 'Passed' and summary['parentEnvironmentAndDirectoryUnchanged'], 'Process failed')
            if kind != 'qualification':
                require(summary['driverSha256'] == sha(driver), 'Driver identity differs')
            else:
                require(summary['inputsSha256'] == sha(inputs) and summary['coreCaptureCases'] == 68 and summary['nativeUtf16CapabilityCases'] == 12 and summary['comparatorControlsPassed'] == 9, 'Shared process coverage differs')
            require([row['id'] for row in summary['cases']] == modes, 'Process cases differ')
            for case in summary['cases']:
                require(case['status'] == 'Passed' and case['childExitCode'] == 0, 'Child failed')
                actual = read(folder / (case['id'] + '.stdout.json'))
                if kind == 'qualification':
                    module.verify(next(row for row in catalog['cases'] if row['id'] == case['id']), actual, catalog['adapterObservations']['csharp'])
                else:
                    module.verify(case['id'], actual)
                observed = actual['observed']
                require(observed['publicFacadeInitialized'] is False and observed['runtimeDescription'] == '.NET 10.0.12' and observed['operatingSystem'] == family, 'Runtime identity differs')
                pairs.append((observed['fixtureSha256'], observed['coreSha256']))
            for item in summary['reports']:
                path = folder / item['path']
                require(sha(path) == item['sha256'] and path.stat().st_size == item['bytes'], 'Process report bytes differ')
    require(set(pairs) == {(sha(host / 'CIS.Core.DiscoveryFixture.dll'),sha(host / 'CIS.Core.dll'))}, 'Managed identities differ')
    require(sha(host / 'CIS.Core.dll') == sha(ROOT / 'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'), 'Core differs from native build')
    return {'fixtureSha256': pairs[0][0], 'coreSha256': pairs[0][1], 'inputsSha256': sha(inputs), 'childrenPerPlatform': 128}


def artifacts(root, name):
    copy(root / 'implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll', OUT / name / 'CIS.Core.dll')
    copy(root / '.artifacts/csharp/packages/CIS.Core.0.1.0-dev.1.nupkg', OUT / name / 'CIS.Core.0.1.0-dev.1.nupkg')


require(ROOT == Path('C:/miscwork/portfolio/cislib').resolve(), 'Unexpected repository')
counts = native(ROOT)
identity = processes()
review = read(TASK / 'claude-review-assessment.json')
require(review['reviewCompleted'] and review['actionableFindingsResolved'], 'Review incomplete')
require(read(TASK / 'final-reviewed-verification-exits.json') == {'checkDeveloper':0,'releaseVerify':2,'checkRelease':2}, 'Gate dispositions differ')
for family, expected in [('UnitTests',240),('IntegrationTests',68),('ArchitectureTests',2)]:
    report = TASK / f'focused-binding/{family}/report.trx'
    values = ET.parse(report).find('t:ResultSummary/t:Counters', NS).attrib
    require(int(values['total']) == int(values['passed']) == expected and int(values['failed']) == int(values['notExecuted']) == 0, 'Focused counts differ')
if sys.argv[1] == 'direct':
    tree(ROOT / '.artifacts/csharp/verification/Developer', OUT / 'developer', {'.json','.log','.trx','.dll'})
    artifacts(ROOT,'development-artifacts')
    for source in TASK.iterdir():
        if source.is_file() and source.suffix in ['.json','.log','.txt']:
            copy(source,OUT / 'commands' / source.name)
    for name in ['focused','focused-final','focused-reviewed','focused-binding','developer-before-trap-controls','developer-before-review-corrections','windows-qualification','linux-qualification','process-windows','process-linux','process-linux-corrected','process-inputs','post-review']:
        tree(TASK / name, OUT / ('final' if name == 'post-review' else 'pre-review/' + name), {'.json','.log','.trx','.dll','.txt','.pdb','.exe'})
    tree(ROOT / '.artifacts/csharp/verification/Release', OUT / 'release', {'.json','.log'})
    copy(TASK / 'retain-evidence.py', OUT / 'commands/retain-evidence.py')
    print(json.dumps({'retainedDirectFiles':sum(path.is_file() for path in OUT.rglob('*')),'native':counts,'identity':identity}))
    raise SystemExit(0)
replay = read(ROOT / '.artifacts/csharp/replay/latest.json')
require(replay['exitCode'] == 0 and replay['inputVerifiedBefore'] and replay['inputVerifiedAfter'], 'Source replay failed')
run = Path(replay['runRoot'])
inputs = read(run / 'input-inventory.json')
for item in inputs:
    for root in [ROOT, run/'input', run/'work']:
        require(sha(root / item['path']) == item['sha256'], 'Inventoried input changed: ' + item['path'])
require(native(run / 'work') == counts, 'Replay native coverage differs')
tree(run / 'work/.artifacts/csharp/verification/Developer', OUT / 'replay-developer', {'.json','.log','.trx','.dll'})
artifacts(run / 'work','replay-artifacts')
for name in ['summary.json','input-inventory.json','verification.log']:
    copy(run / name,OUT / 'replay' / name)
copy(TASK / 'replay.log',OUT / 'commands/replay.log')
originals = {'core-library-implementation-guide-v1.md':'01b4b234fd460088f6c94abb0fc151ca7089375edaf473bf90bc1215d780a675','core-library-backlog-v1.json':'4abb87f20bc85beaa145c82714bb5d34cb923ef7b9b7b857e678839110dcd549','core-library-implementation-plan-v1.md':'41f1568583c9a04c783dd6db5fe80cd24ba8a6142a78983f910c4973e72bf7a5'}
for name, expected in originals.items():
    require(sha(ROOT/'planning/logging/sources'/name) == expected, 'Original plan changed')
preserved = ['implementations/csharp/src/CIS.Core','contracts/logging','planning/logging/sources']
require(subprocess.run(['git','diff','--exit-code','4783207','--',*preserved],capture_output=True).returncode == 0,'Shipping/original contracts changed')
historical = 0
for path in (ROOT / 'planning/logging/evidence').glob('*windows*.json'):
    if path != RECEIPT:
        for item in read(path).get('reports',[]):
            source = ROOT / item['path']
            require(sha(source) == item['sha256'] and source.stat().st_size == item['bytes'],'Historical report changed')
            historical += 1
reports = [{'path':path.relative_to(ROOT).as_posix(),'sha256':sha(path),'bytes':path.stat().st_size} for path in sorted(OUT.rglob('*')) if path.is_file()]
record = {
 'schemaVersion':1,'taskId':'WP04-S02-T04','recordedUtc':datetime.datetime.now(datetime.timezone.utc).isoformat(),
 'checkpointCommit':'4783207','taskChangesCommitted':False,
 'outcome':'Qualify complete shared deterministic exception fixtures and all four WP04-S02 criteria for internal cooperative owned capture',
 'nativeChecks':{'newUnitCases':240,'newIntegrationCases':68,'newArchitectureCases':2,'newCases':310,'boundMethods':7,'unit':2389,'integration':341,'architecture':390,'total':3120,'failed':0,'skipped':0},
 'compilerControls':{'existingStartup':21,'compatibilitySamples':22,'actualCoreValueControls':12,'total':55,'allMatched':True},
 'sharedCases':{'total':68,'nativeUtf16CapabilityRows':12,'malformedRetainedFailures':8,'crossCultureExecutions':204,'nativeComparatorNegatives':16,'pythonComparatorNegatives':9,'armedExcludedGetterControls':14},
 'standaloneProcesses':identity,'qualifiedAcceptanceCriteria':[row['id'] for row in read(ROOT/'conformance/logging/exception-qualification-map.json')['criteria']],
 'acceptanceProgress':'All four source criteria qualified for internal owned exception capture with preserved earlier BCL mutation/weak-reference/compiled guards. Shipping event encoding/aggregate bytes/runtime remain later work.',
 'externalClaudeReview':review,'verificationExits':{'directDeveloper':0,'checkDeveloper':0,'releaseVerify':2,'checkRelease':2,'developerReplay':0,'windowsExceptionDrivers':0,'ubuntuExceptionDrivers':0},
 'developerReplay':replay,'canonicalSourceInventory':'planning/logging/evidence/wp04-s02-t04/replay/input-inventory.json','verifiedCanonicalInputCount':len(inputs),'postReplayInventoriedEdits':[],
 'sourceIdentity':'Main/reference/work inventoried SHA256 values verified before and after fresh copied-source execution using isolated dependency caches. Final replay evidence and this receipt appended after capture; checkpoint is capture-time Git identity, not a claim that task edits were committed then.',
 'historicalReportReferencesVerified':historical,'originalSourceHashes':originals,'changedShippingFiles':[],
 'qualificationLimits':'Internal cooperative exception projection only. Windows Developer native families and source replay; 128 exact .NET10.0.12 roll-forward-disabled children per Windows/Ubuntu with identical managed/input bytes. Development-only JSON observers. T04 cyclic base Data is behind throwing accessors; T03 retains readable BCL mutation/weak-reference evidence. Caller-supplied Message/StackTrace content is explicit input and getter cost/failure is cooperative. Event escapes/envelope/LF entry bytes, logger accounting/lifecycle, full Linux SDK/native families, macOS and release are pending.',
 'pending':['shipping event capture and aggregate encoded-entry byte budget','logger/runtime accounting and lifecycle','Linux SDK/native families, macOS and release'],
 'storyComplete':True,'storyCompletionScope':'Internal bounded immutable exception capture; event budget integration remains explicitly WP04-S04','taskComplete':True,'reports':reports
}
write(RECEIPT,record)
print(json.dumps({'retainedFiles':len(reports),'canonicalInputs':len(inputs),'historicalReferences':historical,'native':counts,'replay':replay['runRoot']}))